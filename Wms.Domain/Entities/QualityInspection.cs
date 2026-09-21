using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class QualityInspection : Entity
{
    private readonly List<QualityInspectionTestResult> _results = [];
    private readonly List<QualityInspectionDisposition> _dispositions = [];

    private QualityInspection()
    {
    }

    public QualityInspection(
        string inspectionNumber,
        int receiptId,
        int receiptLineId,
        int warehouseId,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        decimal receivedBaseQuantity,
        decimal sampleBaseQuantity,
        int inventoryStatusId,
        string? sourceType = null,
        int? supplierId = null,
        string? supplierCodeSnapshot = null,
        int? qualityProfileId = null,
        string? qualityProfileCodeSnapshot = null,
        QualityRiskLevel riskLevel = QualityRiskLevel.Medium,
        int? lotId = null,
        string? lotNumberSnapshot = null,
        DateTime? expiryDateSnapshot = null,
        string? serialNumberSnapshot = null,
        int? licensePlateId = null,
        string? licensePlateNumberSnapshot = null,
        bool licensePlateIsSscc = false,
        string? createdByUserId = null)
    {
        if (receiptId <= 0 || receiptLineId <= 0 || warehouseId <= 0 || itemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receiptId));
        }

        if (receivedBaseQuantity <= 0m || sampleBaseQuantity <= 0m || sampleBaseQuantity > receivedBaseQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleBaseQuantity));
        }

        InspectionNumber = Required(inspectionNumber, 80, nameof(inspectionNumber));
        ReceiptId = receiptId;
        ReceiptLineId = receiptLineId;
        WarehouseId = warehouseId;
        ItemId = itemId;
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = Required(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        ReceivedBaseQuantity = receivedBaseQuantity;
        SampleBaseQuantity = sampleBaseQuantity;
        InventoryStatusId = inventoryStatusId > 0
            ? inventoryStatusId
            : throw new ArgumentOutOfRangeException(nameof(inventoryStatusId));
        SourceType = OptionalUpper(sourceType, 30);
        SupplierId = supplierId;
        SupplierCodeSnapshot = OptionalUpper(supplierCodeSnapshot, 50);
        QualityProfileId = qualityProfileId;
        QualityProfileCodeSnapshot = OptionalUpper(qualityProfileCodeSnapshot, 50);
        RiskLevel = riskLevel;
        LotId = lotId;
        LotNumberSnapshot = Optional(lotNumberSnapshot, 100);
        ExpiryDateSnapshot = expiryDateSnapshot.HasValue ? NormalizeUtc(expiryDateSnapshot.Value) : null;
        SerialNumberSnapshot = Optional(serialNumberSnapshot, 100);
        LicensePlateId = licensePlateId;
        LicensePlateNumberSnapshot = OptionalUpper(licensePlateNumberSnapshot, 100);
        LicensePlateIsSscc = licensePlateIsSscc;
        CreatedByUserId = Required(createdByUserId ?? "system", 450, nameof(createdByUserId));
    }

    public string InspectionNumber { get; private set; } = string.Empty;
    public int ReceiptId { get; private set; }
    public int ReceiptLineId { get; private set; }
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal SampleBaseQuantity { get; private set; }
    public decimal InspectedBaseQuantity { get; private set; }
    public decimal AcceptedBaseQuantity { get; private set; }
    public decimal RejectedBaseQuantity { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string? SourceType { get; private set; }
    public int? SupplierId { get; private set; }
    public string? SupplierCodeSnapshot { get; private set; }
    public int? QualityProfileId { get; private set; }
    public string? QualityProfileCodeSnapshot { get; private set; }
    public QualityRiskLevel RiskLevel { get; private set; }
    public int? LotId { get; private set; }
    public string? LotNumberSnapshot { get; private set; }
    public DateTime? ExpiryDateSnapshot { get; private set; }
    public string? SerialNumberSnapshot { get; private set; }
    public int? LicensePlateId { get; private set; }
    public string? LicensePlateNumberSnapshot { get; private set; }
    public bool LicensePlateIsSscc { get; private set; }
    public QualityInspectionStatus Status { get; private set; } = QualityInspectionStatus.Open;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? InspectorUserId { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public string? ClosureReason { get; private set; }
    public long Revision { get; private set; }

    public Receipt Receipt { get; private set; } = null!;
    public ReceiptLine ReceiptLine { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Supplier? Supplier { get; private set; }
    public QualityProfile? QualityProfile { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public IReadOnlyList<QualityInspectionTestResult> Results => _results.AsReadOnly();
    public IReadOnlyList<QualityInspectionDisposition> Dispositions => _dispositions.AsReadOnly();
    public bool IsClosed => Status is QualityInspectionStatus.Closed or QualityInspectionStatus.Cancelled;
    public decimal RemainingSampleQuantity => Math.Max(0m, SampleBaseQuantity - InspectedBaseQuantity);

    public void Start(string userId, DateTime startedAtUtc)
    {
        EnsureEditable();
        InspectorUserId = Required(userId, 450, nameof(userId));
        StartedAtUtc ??= NormalizeUtc(startedAtUtc);
        Status = QualityInspectionStatus.InProgress;
        Revision++;
        SetUpdatedAt(startedAtUtc);
    }

    public void AddResult(
        QualityInspectionTestResult result,
        decimal inspectedQuantity,
        string userId,
        DateTime recordedAtUtc)
    {
        EnsureEditable();
        ArgumentNullException.ThrowIfNull(result);
        if (inspectedQuantity <= 0m || inspectedQuantity > SampleBaseQuantity)
        {
            throw new InvalidOperationException("The inspected quantity must be positive and within the selected quality sample.");
        }

        if (result.InspectedQuantity != inspectedQuantity)
        {
            throw new InvalidOperationException("The quality test result quantity must match the inspection quantity.");
        }

        if (_results.Any(existing => existing.QualityProfileTestId == result.QualityProfileTestId))
        {
            throw new InvalidOperationException("A result already exists for this quality test. Use a controlled correction after closure.");
        }

        if (inspectedQuantity < InspectedBaseQuantity)
        {
            throw new InvalidOperationException("A later quality test cannot cover less quantity than the already inspected sample.");
        }

        Start(userId, recordedAtUtc);
        _results.Add(result);
        InspectedBaseQuantity = inspectedQuantity;
        Revision++;
        SetUpdatedAt(recordedAtUtc);
    }

    public void AddDisposition(QualityInspectionDisposition disposition, DateTime recordedAtUtc)
    {
        EnsureEditable();
        ArgumentNullException.ThrowIfNull(disposition);
        var alreadyDispositioned = _dispositions.Sum(value => value.Quantity);
        if (alreadyDispositioned + disposition.Quantity > SampleBaseQuantity)
        {
            throw new InvalidOperationException("Quality dispositions cannot exceed the selected sample quantity.");
        }

        _dispositions.Add(disposition);
        if (disposition.Type is QualityDispositionType.Pass or QualityDispositionType.PartialPass)
        {
            AcceptedBaseQuantity += disposition.Quantity;
        }
        else
        {
            RejectedBaseQuantity += disposition.Quantity;
        }
        if (Status == QualityInspectionStatus.Open)
        {
            Status = QualityInspectionStatus.InProgress;
        }

        Revision++;
        SetUpdatedAt(recordedAtUtc);
    }

    public void Close(
        string userId,
        DateTime closedAtUtc,
        string? closureReason = null,
        bool supervisorOverride = false)
    {
        EnsureEditable();
        var dispositioned = _dispositions.Sum(value => value.Quantity);
        if (dispositioned != SampleBaseQuantity)
        {
            if (!supervisorOverride)
            {
                throw new InvalidOperationException("A quality inspection must dispose the complete selected sample before closure.");
            }

            if (string.IsNullOrWhiteSpace(closureReason))
            {
                throw new InvalidOperationException("A supervisor override reason is required for an incomplete quality inspection.");
            }
        }

        var requiredTests = QualityProfile?.Tests.Where(test => test.IsRequired).ToArray() ?? [];
        var missingRequiredTest = requiredTests.Any(test =>
            !_results.Any(result => result.QualityProfileTestId == test.Id));
        if (missingRequiredTest && !supervisorOverride)
        {
            throw new InvalidOperationException("All required quality tests must have a result before closure.");
        }

        var hasFailure = _dispositions.Any(value => value.Type is
            QualityDispositionType.Fail or QualityDispositionType.Damage or QualityDispositionType.Scrap or QualityDispositionType.ReturnToVendor);
        var hasPass = _dispositions.Any(value => value.Type is QualityDispositionType.Pass or QualityDispositionType.PartialPass);
        Status = hasFailure && hasPass
            ? QualityInspectionStatus.PartiallyPassed
            : hasFailure
                ? QualityInspectionStatus.Failed
                : _dispositions.Any(value => value.Type == QualityDispositionType.Retest)
                    ? QualityInspectionStatus.Retest
                    : _dispositions.Any(value => value.Type == QualityDispositionType.Hold)
                        ? QualityInspectionStatus.Held
                        : QualityInspectionStatus.Passed;
        Status = QualityInspectionStatus.Closed;
        InspectorUserId = Required(userId, 450, nameof(userId));
        ClosedByUserId = InspectorUserId;
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        ClosureReason = Optional(closureReason, 1_000);
        Revision++;
        SetUpdatedAt(closedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        Status = QualityInspectionStatus.Cancelled;
        ClosedByUserId = Required(userId, 450, nameof(userId));
        ClosedAtUtc = NormalizeUtc(cancelledAtUtc);
        ClosureReason = Optional(reason, 1_000);
        Revision++;
        SetUpdatedAt(cancelledAtUtc);
    }

    private void EnsureEditable()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("A closed quality inspection cannot be edited without a controlled correction.");
        }
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
