using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.ReportingAssistant;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/reporting-assistant")]
[Authorize(Policy = WmsPermissions.ReportsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class ReportingAssistantController(
    IReportingAssistantService reportingAssistantService) : ControllerBase
{
    [HttpPost("query")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ReportingAssistantAnswer), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Query(
        [FromBody] ReportingAssistantHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await reportingAssistantService.ExecuteAsync(
            new ReportingAssistantQueryRequest(
                request.Question,
                request.Locale,
                request.WarehouseId,
                request.MaximumRows),
            cancellationToken);
        if (result.IsFailure)
        {
            return ProblemFor(result.FirstError!);
        }

        return Ok(result.Value);
    }

    private ObjectResult ProblemFor(ResultError error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency or ErrorType.Unexpected => StatusCodes.Status503ServiceUnavailable,
            ErrorType.Cancelled => StatusCodes.Status408RequestTimeout,
            _ => StatusCodes.Status400BadRequest
        };
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "Reporting assistant request failed.",
            Detail = error.Message,
            Type = "https://httpstatuses.com/" + statusCode
        };
        problem.Extensions["code"] = error.Code;
        return StatusCode(statusCode, problem);
    }
}

public sealed class ReportingAssistantHttpRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(ReportingAssistantPolicy.MaximumQuestionLength)]
    public string Question { get; init; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Locale { get; init; } = "en-US";

    public int? WarehouseId { get; init; }

    [Range(1, ReportingAssistantPolicy.MaximumRows)]
    public int MaximumRows { get; init; } = 50;
}
