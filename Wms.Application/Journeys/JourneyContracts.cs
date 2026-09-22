using Wms.Application.Common;

namespace Wms.Application.Journeys;

public enum WarehouseJourneyKind
{
    Inbound,
    Outbound,
    Transfer,
    CustomerReturn,
    SupplierReturn,
    CycleCount,
    Replenishment,
    Disposition,
    Kitting
}

public enum JourneyOutcome
{
    Pass,
    Fail,
    Skipped
}

public sealed record JourneyStep(
    string Name,
    string ExpectedState,
    string ReferenceType,
    bool IsMutation,
    bool AllowsRetry = false);

public sealed record JourneyReconciliationRequirements(
    bool Documents,
    bool Work,
    bool Reservations,
    bool Balances,
    bool Ledger,
    bool Serials,
    bool LicensePlates,
    bool RequiresZeroErrors = true);

public sealed record WarehouseJourneySpecification(
    string JourneyId,
    WarehouseJourneyKind Kind,
    string Locale,
    bool IsHappyPath,
    bool IncludesPartialOrException,
    bool IncludesRetry,
    bool IncludesConcurrency,
    IReadOnlyList<JourneyStep> Steps,
    JourneyReconciliationRequirements Reconciliation);

public sealed record JourneyRunResult(
    string JourneyId,
    JourneyOutcome Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int StepCount,
    int ReconciliationErrorCount,
    IReadOnlyList<string> References,
    IReadOnlyList<string> Diagnostics);

public interface IWarehouseJourneyRunner
{
    Task<Result<JourneyRunResult>> RunAsync(
        WarehouseJourneySpecification specification,
        CancellationToken cancellationToken = default);
}

public static class JourneySpecificationPolicy
{
    public const int MaximumSteps = 40;
    public const int MaximumDiagnostics = 100;

    public static Result Validate(WarehouseJourneySpecification specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (string.IsNullOrWhiteSpace(specification.JourneyId) ||
            specification.JourneyId.Trim().Length > 100 ||
            specification.Steps is null ||
            specification.Steps.Count is < 2 or > MaximumSteps)
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.specification_invalid",
                $"A journey id and between 2 and {MaximumSteps} executable steps are required."));
        }

        if (!specification.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
            !specification.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.locale_invalid",
                "Journey contracts support English and Arabic locales."));
        }

        if (!specification.Steps[0].IsMutation && specification.Steps.Any(step => step.IsMutation))
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.setup_invalid",
                "The first journey step must establish the executable business state."));
        }

        if (specification.Steps.Any(step =>
                string.IsNullOrWhiteSpace(step.Name) ||
                string.IsNullOrWhiteSpace(step.ExpectedState) ||
                string.IsNullOrWhiteSpace(step.ReferenceType)))
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.step_invalid",
                "Every journey step requires a name, expected state, and reference type."));
        }

        var required = specification.Reconciliation;
        if (!required.Documents || !required.Work || !required.Reservations ||
            !required.Balances || !required.Ledger || !required.Serials ||
            !required.LicensePlates || !required.RequiresZeroErrors)
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.reconciliation_incomplete",
                "Journey specifications must require zero-error reconciliation across every tracked state."));
        }

        if (specification.IncludesRetry &&
            !specification.Steps.Any(step => step.AllowsRetry))
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.retry_missing",
                "A retry journey must identify at least one retryable step."));
        }

        return Result.Success();
    }

    public static Result ValidateRunResult(
        WarehouseJourneySpecification specification,
        JourneyRunResult result)
    {
        var specificationResult = Validate(specification);
        if (specificationResult.IsFailure)
        {
            return specificationResult;
        }

        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(specification.JourneyId, result.JourneyId, StringComparison.Ordinal) ||
            result.StepCount != specification.Steps.Count ||
            result.StartedAtUtc > result.CompletedAtUtc ||
            result.Diagnostics.Count > MaximumDiagnostics)
        {
            return Result.Failure(WmsErrors.Validation(
                "journey.result_invalid",
                "The journey result does not match its executable specification."));
        }

        if (result.Outcome == JourneyOutcome.Pass && result.ReconciliationErrorCount != 0)
        {
            return Result.Failure(WmsErrors.Conflict(
                "journey.reconciliation_failed",
                "A passing journey cannot contain unexplained reconciliation errors."));
        }

        return Result.Success();
    }
}

public static class JourneyCatalog
{
    public static IReadOnlyList<WarehouseJourneySpecification> CriticalJourneys { get; } =
    [
        Create(
            "inbound.po-asn-receipt-qc-putaway",
            WarehouseJourneyKind.Inbound,
            "en-US",
            true,
            false,
            false,
            false,
            [
                new("receive-po", "purchase-order-received", "purchase-order", true),
                new("check-in-asn", "asn-checked-in", "asn", true),
                new("complete-receipt", "receipt-completed", "receipt", true),
                new("pass-quality", "quality-passed", "inspection", true),
                new("complete-putaway", "putaway-completed", "work", true)
            ]),
        Create(
            "outbound-order-allocate-pick-pack-ship",
            WarehouseJourneyKind.Outbound,
            "en-US",
            true,
            false,
            true,
            true,
            [
                new("confirm-order", "order-confirmed", "sales-order", true),
                new("allocate", "allocation-created", "reservation", true, true),
                new("release", "work-released", "work", true),
                new("pick", "picked", "pick", true, true),
                new("pack", "packed", "pack", true),
                new("ship", "shipped", "shipment", true)
            ]),
        Create(
            "transfer-source-to-destination",
            WarehouseJourneyKind.Transfer,
            "ar-EG",
            false,
            true,
            true,
            true,
            [
                new("allocate-source", "transfer-allocated", "transfer", true),
                new("ship-source", "in-transit", "shipment", true),
                new("receive-destination", "transfer-received", "receipt", true),
                new("putaway-destination", "transfer-putaway", "work", true, true)
            ])
    ];

    private static WarehouseJourneySpecification Create(
        string id,
        WarehouseJourneyKind kind,
        string locale,
        bool happyPath,
        bool partialOrException,
        bool retry,
        bool concurrency,
        IReadOnlyList<JourneyStep> steps) =>
        new(
            id,
            kind,
            locale,
            happyPath,
            partialOrException,
            retry,
            concurrency,
            steps,
            new JourneyReconciliationRequirements(
                true,
                true,
                true,
                true,
                true,
                true,
                true));
}
