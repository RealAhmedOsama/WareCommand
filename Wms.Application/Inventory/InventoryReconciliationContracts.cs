using Wms.Application.Common;

namespace Wms.Application.Inventory;

public enum InventoryReconciliationSeverity
{
    Warning = 1,
    Critical = 2
}

public enum InventoryReconciliationCheck
{
    LedgerBalance = 1,
    InventoryInvariant = 2,
    ReservationAllocation = 3,
    SerialPlacement = 4,
    LicensePlateContent = 5,
    ReferenceIntegrity = 6
}

/// <summary>
/// Read-only reconciliation scope. Date filters constrain row-level audit
/// checks; current ledger-to-balance state always uses the complete ledger so
/// a partial event window cannot be mistaken for a current-state total.
/// </summary>
public sealed record InventoryReconciliationQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    bool Deep = true,
    int BatchSize = 500,
    int MaxIssues = 2_000);

public sealed record InventoryReconciliationIssueDto(
    InventoryReconciliationSeverity Severity,
    InventoryReconciliationCheck Check,
    string Code,
    string Message,
    string SuggestedAction,
    string? ReferenceType = null,
    string? ReferenceId = null,
    int? WarehouseId = null,
    int? ItemId = null,
    int? LocationId = null,
    decimal? ExpectedQuantity = null,
    decimal? ActualQuantity = null);

public sealed record InventoryReconciliationReportDto(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool Deep,
    bool IsClean,
    bool IssuesTruncated,
    int IssueCount,
    int CriticalIssueCount,
    int WarningIssueCount,
    long BatchesRead,
    long BalancesScanned,
    long TransactionsScanned,
    long ReservationsScanned,
    long AllocationsScanned,
    long SerialNumbersScanned,
    long LicensePlatesScanned,
    long LicensePlateContentsScanned,
    IReadOnlyList<InventoryReconciliationIssueDto> Issues);

public interface IInventoryReconciliationService
{
    Task<Result<InventoryReconciliationReportDto>> ReconcileAsync(
        InventoryReconciliationQuery query,
        CancellationToken cancellationToken = default);
}
