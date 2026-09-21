using System.Text;
using Wms.Application.Common;

namespace Wms.Application.Attachments;

/// <summary>
/// Validates upload metadata and the leading file signature. Client MIME
/// values are treated as claims only; the signature and safe extension must
/// agree before a storage adapter receives the file.
/// </summary>
public static class AttachmentContentValidator
{
    public const int MaximumFileNameLength = 180;
    public const int SignatureProbeLength = 512;

    private static readonly Dictionary<string, IReadOnlySet<string>> AllowedExtensionsByType =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = new HashSet<string>([".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase),
            ["image/png"] = new HashSet<string>([".png"], StringComparer.OrdinalIgnoreCase),
            ["image/gif"] = new HashSet<string>([".gif"], StringComparer.OrdinalIgnoreCase),
            ["image/webp"] = new HashSet<string>([".webp"], StringComparer.OrdinalIgnoreCase),
            ["application/pdf"] = new HashSet<string>([".pdf"], StringComparer.OrdinalIgnoreCase),
            ["text/plain"] = new HashSet<string>([".txt"], StringComparer.OrdinalIgnoreCase),
            ["text/csv"] = new HashSet<string>([".csv"], StringComparer.OrdinalIgnoreCase)
        };

    public static Result Validate(
        string? fileName,
        string? contentType,
        long declaredLength,
        long actualLength,
        ReadOnlySpan<byte> signature,
        long maximumFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Failure("attachments.file_name_required", "A file name is required.", "FileName");
        }

        var normalizedFileName = fileName.Trim();
        if (normalizedFileName.Length > MaximumFileNameLength ||
            normalizedFileName != Path.GetFileName(normalizedFileName) ||
            normalizedFileName.Contains("..", StringComparison.Ordinal) ||
            normalizedFileName.Any(character =>
                char.IsControl(character) || character is '/' or '\\' ||
                Path.GetInvalidFileNameChars().Contains(character)))
        {
            return Failure(
                "attachments.file_name_invalid",
                "The file name contains a path, traversal marker, control character, or unsupported character.",
                "FileName");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (!AllowedExtensionsByType.TryGetValue(normalizedContentType, out var extensions))
        {
            return Failure(
                "attachments.content_type_unsupported",
                "The file type is not supported.",
                "ContentType");
        }

        var extension = Path.GetExtension(normalizedFileName);
        if (string.IsNullOrWhiteSpace(extension) || !extensions.Contains(extension))
        {
            return Failure(
                "attachments.extension_mismatch",
                "The file extension does not match the declared content type.",
                "FileName");
        }

        if (declaredLength <= 0 || declaredLength > maximumFileSizeBytes ||
            actualLength <= 0 || actualLength > maximumFileSizeBytes ||
            declaredLength != actualLength)
        {
            return Failure(
                "attachments.file_size_invalid",
                $"The file must be between 1 byte and {maximumFileSizeBytes} bytes.",
                "File");
        }

        if (!HasExpectedSignature(normalizedContentType, signature))
        {
            return Failure(
                "attachments.content_signature_mismatch",
                "The file content does not match its declared type.",
                "File");
        }

        return Result.Success();
    }

    public static string NormalizeContentType(string? contentType)
    {
        var separator = contentType?.IndexOf(';') ?? -1;
        var value = separator >= 0 ? contentType![..separator] : contentType;
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public static bool IsPreviewAvailable(string contentType) =>
        NormalizeContentType(contentType) is "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/pdf";

    private static bool HasExpectedSignature(string contentType, ReadOnlySpan<byte> signature)
    {
        switch (contentType)
        {
            case "image/jpeg":
                return signature.Length >= 3 &&
                       signature[0] == 0xFF && signature[1] == 0xD8 && signature[2] == 0xFF;
            case "image/png":
                return signature.Length >= 8 &&
                       signature[..8].SequenceEqual(
                           new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            case "image/gif":
                return signature.Length >= 6 &&
                       (signature[..6].SequenceEqual("GIF87a"u8) || signature[..6].SequenceEqual("GIF89a"u8));
            case "image/webp":
                return signature.Length >= 12 &&
                       signature[..4].SequenceEqual("RIFF"u8) &&
                       signature[8..12].SequenceEqual("WEBP"u8);
            case "application/pdf":
                return signature.Length >= 5 && signature[..5].SequenceEqual("%PDF-"u8);
            case "text/plain":
            case "text/csv":
                return !signature.Contains((byte)0) && IsReasonableText(signature);
            default:
                return false;
        }
    }

    private static bool IsReasonableText(ReadOnlySpan<byte> signature)
    {
        try
        {
            var text = Encoding.UTF8.GetString(signature);
            return text.All(character => !char.IsControl(character) || character is '\r' or '\n' or '\t');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Result Failure(string code, string message, string field) =>
        Result.Failure(WmsErrors.Validation(
            code,
            message,
            new Dictionary<string, string[]> { [field] = [message] }));
}
