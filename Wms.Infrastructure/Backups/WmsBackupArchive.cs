using System.Security.Cryptography;
using System.Text;

namespace Wms.Infrastructure.Backups;

internal static class WmsBackupArchive
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("WARECOMMAND-BACKUP-V1\n");
    private const int InitializationVectorLength = 16;
    private const int AuthenticationTagLength = 32;

    public static byte[] LoadKey(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The backup encryption key file '{path}' does not exist.");
        }

        var raw = File.ReadAllBytes(path);
        if (raw.Length == 32)
        {
            return raw;
        }

        var text = Encoding.UTF8.GetString(raw).Trim();
        if (text.Length == 64 && text.All(Uri.IsHexDigit))
        {
            return Convert.FromHexString(text);
        }

        try
        {
            var base64 = Convert.FromBase64String(text);
            if (base64.Length == 32)
            {
                return base64;
            }
        }
        catch (FormatException)
        {
            // The normalized error below avoids echoing key material.
        }

        throw new InvalidOperationException(
            "The backup encryption key must be exactly 32 raw bytes, 64 hexadecimal characters, or a 32-byte Base64 value.");
    }

    public static async Task EncryptAsync(
        string inputPath,
        string outputPath,
        byte[] key,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var initializationVector = RandomNumberGenerator.GetBytes(InitializationVectorLength);
        await using (var input = File.OpenRead(inputPath))
        await using (var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await output.WriteAsync(Magic, cancellationToken);
            await output.WriteAsync(initializationVector, cancellationToken);

            using var aes = CreateAes(key, initializationVector);
            using var crypto = new CryptoStream(
                output,
                aes.CreateEncryptor(),
                CryptoStreamMode.Write,
                leaveOpen: true);
            await input.CopyToAsync(crypto, 128 * 1024, cancellationToken);
            crypto.FlushFinalBlock();
            await output.FlushAsync(cancellationToken);
        }

        var tag = await ComputeTagAsync(
            outputPath,
            key,
            Magic.Length + InitializationVectorLength,
            cancellationToken);
        await using var tagStream = new FileStream(
            outputPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            4 * 1024,
            FileOptions.Asynchronous);
        await tagStream.WriteAsync(tag, cancellationToken);
    }

    public static async Task<string> DecryptAsync(
        string inputPath,
        string outputDirectory,
        byte[] key,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        await using var input = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length <= Magic.Length + InitializationVectorLength + AuthenticationTagLength)
        {
            throw new InvalidOperationException("The backup artifact is truncated.");
        }

        var magic = new byte[Magic.Length];
        await input.ReadExactlyAsync(magic, cancellationToken);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidOperationException("The backup artifact format is not recognized.");
        }

        var initializationVector = new byte[InitializationVectorLength];
        await input.ReadExactlyAsync(initializationVector, cancellationToken);
        var ciphertextLength = input.Length - Magic.Length - InitializationVectorLength - AuthenticationTagLength;
        var expectedTag = new byte[AuthenticationTagLength];
        input.Position = input.Length - AuthenticationTagLength;
        await input.ReadExactlyAsync(expectedTag, cancellationToken);
        var actualTag = await ComputeTagAsync(
            inputPath,
            key,
            Magic.Length + InitializationVectorLength,
            cancellationToken,
            ciphertextLength);
        if (!CryptographicOperations.FixedTimeEquals(expectedTag, actualTag))
        {
            throw new InvalidOperationException("The backup artifact authentication check failed.");
        }

        Directory.CreateDirectory(outputDirectory);
        var zipPath = Path.Combine(outputDirectory, "backup.zip");
        input.Position = Magic.Length + InitializationVectorLength;
        await using var limited = new LimitedReadStream(input, ciphertextLength);
        await using var output = new FileStream(
            zipPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var aes = CreateAes(key, initializationVector);
        using var crypto = new CryptoStream(
            limited,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read,
            leaveOpen: true);
        await crypto.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return zipPath;
    }

    private static async Task<byte[]> ComputeTagAsync(
        string path,
        byte[] key,
        long offset,
        CancellationToken cancellationToken,
        long? length = null)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = offset;
        var remaining = length ?? stream.Length - offset;
        using var hmac = new HMACSHA256(CreateMacKey(key));
        var buffer = new byte[128 * 1024];
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("The backup artifact ended before its authentication data.");
            }

            hmac.TransformBlock(buffer, 0, read, buffer, 0);
            remaining -= read;
        }

        hmac.TransformFinalBlock([], 0, 0);
        return hmac.Hash!;
    }

    private static byte[] CreateMacKey(byte[] key)
    {
        var label = Encoding.UTF8.GetBytes("WareCommand backup HMAC v1");
        return SHA256.HashData([.. key, .. label]);
    }

    private static Aes CreateAes(byte[] key, byte[] initializationVector)
    {
        var aes = Aes.Create();
        aes.Key = key;
        aes.IV = initializationVector;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Backup encryption keys must be 32 bytes.", nameof(key));
        }
    }

    private sealed class LimitedReadStream(
        Stream inner,
        long remaining) : Stream
    {
        private long _remaining = remaining;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var requested = (int)Math.Min(count, _remaining);
            var read = inner.Read(buffer, offset, requested);
            _remaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var requested = buffer[..(int)Math.Min(buffer.Length, _remaining)];
            var read = inner.Read(requested);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var requested = buffer[..(int)Math.Min(buffer.Length, _remaining)];
            var read = await inner.ReadAsync(requested, cancellationToken);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
