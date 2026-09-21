using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Attachments;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/attachments")]
[Authorize]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class AttachmentsController(IAttachmentService attachmentService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = WmsPermissions.AttachmentsRead)]
    public async Task<IActionResult> List(
        [FromQuery, Required] string referenceType,
        [FromQuery, Required] string referenceId,
        [FromQuery, Range(1, int.MaxValue)] int warehouseId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await attachmentService.ListAsync(
            new AttachmentQuery(referenceType, referenceId, warehouseId),
            cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AttachmentsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        [FromForm] UploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || request.File is null)
        {
            if (request.File is null)
            {
                ModelState.AddModelError(nameof(request.File), "A file is required.");
            }

            return ValidationProblem(ModelState);
        }

        await using var content = request.File.OpenReadStream();
        return ToActionResult(await attachmentService.UploadAsync(
            new AttachmentUploadInput(
                request.ReferenceType,
                request.ReferenceId,
                request.WarehouseId,
                request.File.FileName,
                request.File.ContentType,
                request.File.Length,
                request.Classification,
                request.RetentionUntilUtc,
                request.ImmutableEvidence),
            content,
            cancellationToken));
    }

    [HttpGet("{attachmentId:int}/download")]
    [Authorize(Policy = WmsPermissions.AttachmentsRead)]
    public async Task<IActionResult> Download(
        int attachmentId,
        [FromQuery] bool preview = false,
        CancellationToken cancellationToken = default)
    {
        var result = await attachmentService.OpenDownloadAsync(
            attachmentId,
            preview,
            cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var download = result.Value;
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = string.Concat(
            download.IsPreview ? "inline" : "attachment",
            "; filename*=UTF-8''",
            Uri.EscapeDataString(download.FileName));
        Response.Headers.ContentLength = download.SizeBytes;
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox";
        return new FileStreamResult(download.Content, download.ContentType)
        {
            EnableRangeProcessing = true
        };
    }

    [HttpPost("{attachmentId:int}/evidence")]
    [Authorize(Policy = WmsPermissions.AttachmentsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkEvidence(
        int attachmentId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await attachmentService.MarkEvidenceAsync(attachmentId, cancellationToken));

    [HttpDelete("{attachmentId:int}")]
    [Authorize(Policy = WmsPermissions.AttachmentsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestDeletion(
        int attachmentId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await attachmentService.RequestDeletionAsync(attachmentId, cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }

    public sealed class UploadRequest
    {
        [Required, StringLength(80)]
        public string ReferenceType { get; init; } = string.Empty;

        [Required, StringLength(200)]
        public string ReferenceId { get; init; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int WarehouseId { get; init; }

        public AttachmentClassification Classification { get; init; } = AttachmentClassification.Operational;

        public DateTimeOffset? RetentionUntilUtc { get; init; }

        public bool ImmutableEvidence { get; init; }

        [Required]
        public IFormFile? File { get; init; }
    }
}
