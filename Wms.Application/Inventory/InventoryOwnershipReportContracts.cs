using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record InventoryOwnershipReportQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    InventoryOwnerKind? OwnerKind = null,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Page = 1,
    int PageSize = 100);

public sealed record InventoryOwnershipBalanceReportRow(
    int WarehouseId,
    string WarehouseCode,
    int LocationId,
    string LocationCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    InventoryOwnerKind OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    string OwnerDisplayName,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime BalanceCreatedAtUtc,
    int AgeDays);

public sealed record InventoryOwnershipUsageReportRow(
    InventoryOwnerKind OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    InventoryTransactionType TransactionType,
    int TransactionCount,
    decimal QuantityDelta,
    decimal ReservedQuantityDelta);

public sealed record InventoryOwnershipStatementRow(
    int TransactionId,
    DateTime OccurredAtUtc,
    InventoryOwnerKind OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    InventoryTransactionType TransactionType,
    int WarehouseId,
    int LocationId,
    int ItemId,
    decimal QuantityDelta,
    decimal ReservedQuantityDelta,
    decimal QuantityBefore,
    decimal QuantityAfter,
    string? ReferenceType,
    string? ReferenceId,
    string? Reason,
    string ActorUserId);

public sealed record InventoryOwnershipReportDto(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<InventoryOwnershipBalanceReportRow> Balances,
    IReadOnlyList<InventoryOwnershipUsageReportRow> Usage,
    IReadOnlyList<InventoryOwnershipStatementRow> Statement,
    int Page,
    int PageSize);

public interface IInventoryOwnershipReportService
{
    Task<Result<InventoryOwnershipReportDto>> QueryAsync(
        InventoryOwnershipReportQuery query,
        CancellationToken cancellationToken = default);
}
