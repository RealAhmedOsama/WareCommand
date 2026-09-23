using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.B2bDocuments;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/b2b")]
[Authorize(Policy = WmsPermissions.SettingsManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class B2bDocumentsController(IB2bDocumentService b2bDocumentService) : ControllerBase
{
    private static readonly string[] ImplementedLocalBehaviors =
    [
        "canonical-envelope-validation",
        "mapping-profile-and-trading-partner-persistence",
        "warehouse-scoped-document-state"
    ];

    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new
    {
        implementationStatus = "ContractOnly",
        configurationStatus = "Unconfigured",
        verificationStatus = "Unverified",
        implementedLocalBehaviors = ImplementedLocalBehaviors,
        implementedPartnerStandards = Array.Empty<string>(),
        implementedLocalDocumentModel = WmsB2bStandards.Canonical,
        implementedTransportModes = Array.Empty<string>(),
        livePartnerAcceptanceRequired = true,
        standards = new[] { WmsB2bStandards.Canonical, WmsB2bStandards.X12, WmsB2bStandards.Edifact },
        directions = new[] { WmsB2bDirections.Inbound, WmsB2bDirections.Outbound },
        transportModes = new[]
        {
            WmsB2bTransportModes.Sftp,
            WmsB2bTransportModes.FileDrop,
            WmsB2bTransportModes.Api,
            WmsB2bTransportModes.Webhook
        },
        documentTypes = new[]
        {
            WmsB2bDocumentTypes.ItemMaster,
            WmsB2bDocumentTypes.LocationMaster,
            WmsB2bDocumentTypes.PurchaseOrder,
            WmsB2bDocumentTypes.AdvanceShippingNotice,
            WmsB2bDocumentTypes.Receipt,
            WmsB2bDocumentTypes.SalesOrder,
            WmsB2bDocumentTypes.WarehouseOrder,
            WmsB2bDocumentTypes.InventoryStatus,
            WmsB2bDocumentTypes.ShipmentConfirmation,
            WmsB2bDocumentTypes.Return
        }
    });

    [HttpPost("mapping-profiles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMappingProfile(
        [FromBody] B2bMappingProfileRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await b2bDocumentService.SaveMappingProfileAsync(request, cancellationToken));

    [HttpPost("trading-partners")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTradingPartner(
        [FromBody] TradingPartnerCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await b2bDocumentService.CreateTradingPartnerAsync(request, cancellationToken));

    [HttpPost("documents")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        [FromBody] B2bDocumentSubmitRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await b2bDocumentService.SubmitAsync(request, cancellationToken));

    [HttpPost("acknowledgements")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(
        [FromBody] B2bAcknowledgementRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await b2bDocumentService.AcknowledgeAsync(request, cancellationToken));

    [HttpPost("documents/{documentId:long}/replay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Replay(
        long documentId,
        [FromBody] ReplayRequest? request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await b2bDocumentService.ReplayAsync(
            new B2bDocumentReplayRequest(documentId, request?.Reason),
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

    public sealed record ReplayRequest(string? Reason);
}
