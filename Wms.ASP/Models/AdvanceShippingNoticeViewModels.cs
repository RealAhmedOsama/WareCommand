using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class AdvanceShippingNoticeListViewModel
{
    public IReadOnlyList<AdvanceShippingNoticeDto> AdvanceShippingNotices { get; init; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; init; } = [];
    public string? SearchTerm { get; init; }
    public int? WarehouseId { get; init; }
    public int? SupplierId { get; init; }
    public AdvanceShippingNoticeStatus? Status { get; init; }
    public bool IncludeCancelled { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class AdvanceShippingNoticeFormViewModel
{
    public int Id { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    [StringLength(200)]
    public string? CarrierName { get; set; }

    public DateTime? ExpectedArrivalFromUtc { get; set; }
    public DateTime? ExpectedArrivalToUtc { get; set; }

    [StringLength(100)]
    public string? VehicleNumber { get; set; }

    [StringLength(100)]
    public string? TrailerNumber { get; set; }

    [StringLength(100)]
    public string? ContainerNumber { get; set; }

    [StringLength(100)]
    public string? TrackingReference { get; set; }

    [StringLength(100)]
    public string? ExternalReference { get; set; }

    [StringLength(30)]
    public string SourceType { get; set; } = "MANUAL";

    [StringLength(200)]
    public string? SourceReference { get; set; }

    [StringLength(8_000)]
    public string? SourcePayload { get; set; }

    [Range(1, int.MaxValue)]
    public int? DockLocationId { get; set; }

    [StringLength(2_000)]
    public string? Notes { get; set; }

    public bool AllowMultiplePurchaseOrders { get; set; } = true;

    [Required]
    public string LinesText { get; set; } = string.Empty;

    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
    public IReadOnlyList<AdvanceShippingNoticeSupplierOption> Suppliers { get; set; } = [];

    public static AdvanceShippingNoticeFormViewModel From(AdvanceShippingNoticeDto notice) => new()
    {
        Id = notice.Id,
        WarehouseId = notice.WarehouseId,
        SupplierId = notice.SupplierId,
        CarrierName = notice.CarrierName,
        ExpectedArrivalFromUtc = notice.ExpectedArrivalFromUtc?.ToLocalTime(),
        ExpectedArrivalToUtc = notice.ExpectedArrivalToUtc?.ToLocalTime(),
        VehicleNumber = notice.VehicleNumber,
        TrailerNumber = notice.TrailerNumber,
        ContainerNumber = notice.ContainerNumber,
        TrackingReference = notice.TrackingReference,
        ExternalReference = notice.ExternalReference,
        SourceType = notice.SourceType,
        SourceReference = notice.SourceReference,
        SourcePayload = notice.SourcePayload,
        DockLocationId = notice.DockLocationId,
        Notes = notice.Notes,
        LinesText = string.Join(
            Environment.NewLine,
            notice.Lines.Select(line => string.Join('|', [
                line.ItemSku,
                line.ExpectedQuantity.ToString(CultureInfo.InvariantCulture),
                line.EnteredUnitOfMeasure,
                line.ItemPackagingCode ?? string.Empty,
                line.PurchaseOrderId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                line.PurchaseOrderLineId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                line.OverDeliveryTolerancePercent.ToString(CultureInfo.InvariantCulture),
                line.UnderDeliveryTolerancePercent.ToString(CultureInfo.InvariantCulture),
                line.PreAdvisedLotNumber ?? string.Empty,
                line.PreAdvisedExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                line.PreAdvisedSerialNumber ?? string.Empty,
                line.ExpectedLicensePlateNumber ?? string.Empty,
                line.ExpectedLicensePlateIsSscc.ToString(CultureInfo.InvariantCulture),
                line.Notes ?? string.Empty
            ]))),
        AllowMultiplePurchaseOrders = true
    };

    public AdvanceShippingNoticeInput ToInput()
    {
        var lines = LinesText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((line, index) => ParseLine(line, index + 1))
            .ToArray();

        return new AdvanceShippingNoticeInput(
            WarehouseId,
            SupplierId,
            CarrierName,
            NormalizeUtc(ExpectedArrivalFromUtc),
            NormalizeUtc(ExpectedArrivalToUtc),
            VehicleNumber,
            TrailerNumber,
            ContainerNumber,
            TrackingReference,
            ExternalReference,
            SourceType,
            SourceReference,
            SourcePayload,
            DockLocationId,
            Notes,
            AllowMultiplePurchaseOrders,
            lines);
    }

    private static AdvanceShippingNoticeLineInput ParseLine(string value, int row)
    {
        var fields = value.Split('|');
        if (fields.Length < 2 || string.IsNullOrWhiteSpace(fields[0]))
        {
            throw new ArgumentException(
                $"ASN line {row} must be ITEM_SKU|QUANTITY|UOM|PACKAGING|PO_ID|PO_LINE_ID|OVER_TOLERANCE|UNDER_TOLERANCE|LOT|EXPIRY|SERIAL|LPN|IS_SSCC|NOTES.");
        }

        if (!decimal.TryParse(fields[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
        {
            throw new ArgumentException($"ASN line {row}: expected quantity must be a decimal number.");
        }

        return new AdvanceShippingNoticeLineInput(
            fields[0],
            quantity,
            Optional(fields, 2),
            Optional(fields, 3),
            OptionalInt(fields, 4, row),
            OptionalInt(fields, 5, row),
            OptionalDecimal(fields, 6, row),
            OptionalDecimal(fields, 7, row),
            Optional(fields, 8),
            OptionalDate(fields, 9, row),
            Optional(fields, 10),
            Optional(fields, 11),
            OptionalBool(fields, 12, row),
            Optional(fields, 13));
    }

    private static string? Optional(string[] fields, int index) =>
        fields.Length > index && !string.IsNullOrWhiteSpace(fields[index])
            ? fields[index].Trim()
            : null;

    private static int? OptionalInt(string[] fields, int index, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : throw new ArgumentException($"ASN line {row}: ID values must be positive integers.");

    private static decimal? OptionalDecimal(string[] fields, int index, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : decimal.TryParse(fields[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN line {row}: tolerance must be a decimal number.");

    private static DateTime? OptionalDate(string[] fields, int index, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : DateTime.TryParseExact(fields[index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN line {row}: expiry must use yyyy-MM-dd.");

    private static bool OptionalBool(string[] fields, int index, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? false
            : bool.TryParse(fields[index], out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN line {row}: SSCC flag must be true or false.");

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
}

public sealed record AdvanceShippingNoticeSupplierOption(int Id, string Code, string Name, bool IsActive);

public sealed class AdvanceShippingNoticeImportViewModel
{
    public IFormFile? File { get; set; }
    public string? Csv { get; set; }
}
