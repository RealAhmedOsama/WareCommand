using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Recommendations;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/recommendations")]
[Authorize]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class RecommendationsController(
    IRecommendationGovernanceService recommendationService) : ControllerBase
{
    [HttpPost("replenishment/generate")]
    [Authorize(Policy = WmsPermissions.InventoryRead)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateReplenishment(
        [FromBody] GenerateReplenishmentRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(request.WarehouseId, request.Limit),
            cancellationToken));

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ReportsRead)]
    public async Task<IActionResult> Search(
        [FromQuery] RecommendationType? type,
        [FromQuery] RecommendationStatus? status,
        [FromQuery] int? warehouseId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.SearchAsync(
            new RecommendationQuery(warehouseId, type, status, page, pageSize),
            cancellationToken));

    [HttpGet("{recommendationId}")]
    [Authorize(Policy = WmsPermissions.ReportsRead)]
    public async Task<IActionResult> Get(
        string recommendationId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.GetAsync(recommendationId, cancellationToken));

    [HttpGet("{recommendationId}/history")]
    [Authorize(Policy = WmsPermissions.ReportsRead)]
    public async Task<IActionResult> History(
        string recommendationId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.GetHistoryAsync(recommendationId, cancellationToken));

    [HttpPost("{recommendationId}/review")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        string recommendationId,
        [FromBody] RecommendationDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.ReviewAsync(
            recommendationId,
            request.ToCommand(),
            cancellationToken));

    [HttpPost("{recommendationId}/approve")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        string recommendationId,
        [FromBody] RecommendationDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.ApproveAsync(
            recommendationId,
            request.ToCommand(),
            cancellationToken));

    [HttpPost("{recommendationId}/reject")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        string recommendationId,
        [FromBody] RecommendationDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.RejectAsync(
            recommendationId,
            request.ToCommand(),
            cancellationToken));

    [HttpPost("{recommendationId}/expire")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Expire(
        string recommendationId,
        [FromBody] RecommendationExpiryRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.ExpireAsync(
            recommendationId,
            request.ExpectedRevision,
            request.IdempotencyKey,
            cancellationToken));

    [HttpPost("{recommendationId}/execute")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(
        string recommendationId,
        [FromBody] RecommendationDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await recommendationService.ExecuteAsync(
            recommendationId,
            request.ToCommand(),
            cancellationToken));

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

    public sealed class GenerateReplenishmentRequest
    {
        [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
        [Range(1, 200)] public int Limit { get; init; } = 50;
    }

    public class RecommendationDecisionRequest
    {
        [Range(1, long.MaxValue)] public long ExpectedRevision { get; init; }
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        [StringLength(1_000)] public string? Comment { get; init; }

        public RecommendationLifecycleCommand ToCommand() =>
            new(ExpectedRevision, IdempotencyKey, Comment);
    }

    public sealed class RecommendationExpiryRequest
    {
        [Range(1, long.MaxValue)] public long ExpectedRevision { get; init; }
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    }
}
