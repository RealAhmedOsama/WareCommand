using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Retention;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/retention")]
[Authorize(Policy = WmsPermissions.SettingsManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class RetentionController(IRetentionService retentionService) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.GetCatalogAsync(cancellationToken));

    [HttpGet("policies")]
    public async Task<IActionResult> Policies(
        [FromQuery, Range(1, int.MaxValue)] int? warehouseId,
        [FromQuery, StringLength(50)] string? companyCode,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.ListPoliciesAsync(warehouseId, companyCode, cancellationToken));

    [HttpPut("policies")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePolicy(
        [FromBody] PolicyRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.SavePolicyAsync(
            new RetentionPolicyInput(
                request.Class,
                request.RetentionDays,
                request.WarehouseId,
                request.CompanyCode,
                request.ArchiveBeforePurge,
                request.ExportBeforePurge,
                request.Enabled),
            cancellationToken));

    [HttpGet("holds")]
    public async Task<IActionResult> Holds(
        [FromQuery] bool includeReleased = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.ListHoldsAsync(includeReleased, cancellationToken));

    [HttpPost("holds")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateHold(
        [FromBody] HoldRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.CreateHoldAsync(
            new RetentionHoldInput(
                request.Class,
                request.TargetType,
                request.TargetId,
                request.Reason,
                request.WarehouseId,
                request.CaseReference),
            cancellationToken));

    [HttpPost("holds/{holdId:long}/release")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseHold(
        long holdId,
        [FromBody] ReleaseHoldRequest? request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.ReleaseHoldAsync(
            holdId,
            request?.Reason,
            cancellationToken));

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewRequest? request,
        CancellationToken cancellationToken = default)
    {
        request ??= new PreviewRequest();
        return ToActionResult(await retentionService.PreviewAsync(
            new RetentionPreviewInput(request.AsOfUtc, request.WarehouseId, request.BatchSize, request.CompanyCode),
            cancellationToken));
    }

    [HttpPost("runs")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        [FromBody] RunRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.RunAsync(
            new RetentionRunInput(
                request.DryRun,
                request.AllowDestructive,
                request.BackupVerified,
                request.AuthorizationReference,
                request.AsOfUtc,
                request.WarehouseId,
                request.BatchSize,
                request.RunId,
                request.PreviewRunId,
                request.CompanyCode),
            cancellationToken));

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRun(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await retentionService.GetRunAsync(runId, cancellationToken));

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

    public sealed class PolicyRequest
    {
        public RetentionClass Class { get; init; }

        [Range(0, 365_000)]
        public int RetentionDays { get; init; }

        [Range(1, int.MaxValue)]
        public int? WarehouseId { get; init; }

        [StringLength(50)]
        public string? CompanyCode { get; init; }

        public bool ArchiveBeforePurge { get; init; } = true;

        public bool ExportBeforePurge { get; init; } = true;

        public bool Enabled { get; init; } = true;
    }

    public sealed class HoldRequest
    {
        public RetentionClass Class { get; init; }

        [Required, StringLength(100)]
        public string TargetType { get; init; } = string.Empty;

        [Required, StringLength(200)]
        public string TargetId { get; init; } = string.Empty;

        [Required, StringLength(2_000)]
        public string Reason { get; init; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int? WarehouseId { get; init; }

        [StringLength(200)]
        public string? CaseReference { get; init; }
    }

    public sealed class ReleaseHoldRequest
    {
        [StringLength(2_000)]
        public string? Reason { get; init; }
    }

    public sealed class PreviewRequest
    {
        public DateTimeOffset? AsOfUtc { get; init; }

        [Range(1, int.MaxValue)]
        public int? WarehouseId { get; init; }

        [Range(1, 1_000)]
        public int BatchSize { get; init; } = 100;

        [StringLength(50)]
        public string? CompanyCode { get; init; }
    }

    public sealed class RunRequest
    {
        public bool DryRun { get; init; } = true;

        public bool AllowDestructive { get; init; }

        public bool BackupVerified { get; init; }

        [StringLength(200)]
        public string? AuthorizationReference { get; init; }

        public DateTimeOffset? AsOfUtc { get; init; }

        [Range(1, int.MaxValue)]
        public int? WarehouseId { get; init; }

        [Range(1, 1_000)]
        public int BatchSize { get; init; } = 100;

        public Guid? RunId { get; init; }

        public Guid? PreviewRunId { get; init; }

        [StringLength(50)]
        public string? CompanyCode { get; init; }
    }
}
