// Wms.Domain/Services/IStockMovementService.cs

using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Services;

// Keep the new selector after the existing CancellationToken parameter so
// existing positional callers remain source-compatible.
#pragma warning disable CA1068
public interface IStockMovementService
{
    Task<Movement> ReceiveAsync(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null, int? receiptId = null, int? receiptLineId = null);

    Task<Movement> ReverseReceiptAsync(
        int receiptId,
        int receiptLineId,
        Movement originalMovement,
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<Movement> PutawayAsync(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null);

    Task<Movement> PickAsync(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null, int? inventoryStatusId = null, bool recordLedger = true);

    Task<Movement> AdjustAsync(int itemId, int locationId, Quantity newQuantity, string userId,
        string reason, int? lotId = null, string? serialNumber = null,
        CancellationToken cancellationToken = default, int? licensePlateId = null);
}
#pragma warning restore CA1068
