using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.SerialNumbers;
using Wms.Application.UseCases.Receiving;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/serial-numbers")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class SerialNumbersController(
    ISerialNumberService serialNumberService,
    ICurrentUser currentUser,
    IReceiveItemUseCase receiveItemUseCase) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SerialDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? itemSku,
        [FromQuery] string? number,
        [FromQuery] SerialStatus? status,
        [FromQuery] bool includeMigrationConflicts = false,
        CancellationToken cancellationToken = default)
    {
        var result = await serialNumberService.SearchAsync(
            new SerialSearchQuery(itemSku, number, status, includeMigrationConflicts),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{serialId:int}/traceability")]
    [ProducesResponseType(typeof(SerialTraceabilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Traceability(
        int serialId,
        CancellationToken cancellationToken = default)
    {
        var result = await serialNumberService.GetTraceabilityAsync(serialId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("bulk-receive")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkReceive(
        [FromBody] SerialBulkReceiveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var accepted = new List<string>();
        var rejected = new List<SerialBulkReceiveError>();
        foreach (var serialNumber in request.SerialNumbers
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await receiveItemUseCase.ExecuteAsync(
                new ReceiveItemDto(
                    request.ItemSku,
                    request.LocationCode,
                    1m,
                    request.LotNumber,
                    serialNumber,
                    ReferenceNumber: request.ReferenceNumber,
                    Notes: request.Notes,
                    ExpiryDate: request.ExpiryDate,
                    ManufacturedDate: request.ManufacturedDate),
                currentUser.RequireUserId(),
                cancellationToken);
            if (result.IsSuccess)
            {
                accepted.Add(serialNumber);
            }
            else
            {
                rejected.Add(new SerialBulkReceiveError(serialNumber, result.ErrorCode, result.Error));
            }
        }

        return Ok(new SerialBulkReceiveResult(accepted, rejected));
    }

    [HttpPost("{serialId:int}/status")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(
        int serialId,
        [FromBody] SerialStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await serialNumberService.ChangeStatusAsync(
            serialId,
            request.Status,
            request.Reason,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

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
            ErrorType.Conflict or ErrorType.Concurrency => StatusCodes.Status409Conflict,
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
}

public sealed class SerialStatusChangeRequest
{
    [Required]
    public SerialStatus Status { get; init; }

    [Required]
    [StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class SerialBulkReceiveRequest
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LocationCode { get; init; } = string.Empty;

    [StringLength(100)]
    public string? LotNumber { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(1_000)]
    public IReadOnlyList<string> SerialNumbers { get; init; } = [];

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    public DateTime? ExpiryDate { get; init; }

    public DateTime? ManufacturedDate { get; init; }
}

public sealed record SerialBulkReceiveError(string SerialNumber, string Code, string Message);

public sealed record SerialBulkReceiveResult(
    IReadOnlyList<string> Accepted,
    IReadOnlyList<SerialBulkReceiveError> Rejected);
