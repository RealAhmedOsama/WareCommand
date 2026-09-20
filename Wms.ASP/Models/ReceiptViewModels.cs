using System.Globalization;
using Wms.Application.Identity;
using Wms.Application.Receiving;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class ReceiptListViewModel
{
    public IReadOnlyList<ReceiptDto> Receipts { get; init; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; init; } = [];
    public string? SearchTerm { get; init; }
    public int? WarehouseId { get; init; }
    public int? SupplierId { get; init; }
    public ReceiptStatus? Status { get; init; }
    public bool IncludeCancelled { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class ReceiptFormViewModel
{
    public int WarehouseId { get; set; }
    public int ReceivingLocationId { get; set; }
    public int? SupplierId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int? AdvanceShippingNoticeId { get; set; }
    public int? DockLocationId { get; set; }
    public string SourceType { get; set; } = "MANUAL";
    public string? SourceReference { get; set; }
    public string? ExternalReference { get; set; }
    public string? SessionReference { get; set; }
    public string? Notes { get; set; }
    public string LinesText { get; set; } = string.Empty;
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];

    public ReceiptInput ToInput()
    {
        var lines = LinesText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLine)
            .ToArray();
        return new ReceiptInput(
            WarehouseId,
            ReceivingLocationId,
            SupplierId,
            PurchaseOrderId,
            AdvanceShippingNoticeId,
            DockLocationId,
            SourceType,
            SourceReference,
            ExternalReference,
            SessionReference,
            Notes,
            lines);
    }

    private static ReceiptLineInput ParseLine(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2 || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
        {
            throw new ArgumentException(
                "Each receipt line must use ITEM_SKU|QUANTITY|UOM|PACKAGING|PO_ID|PO_LINE_ID|ASN_ID|ASN_LINE_ID|LOT|EXPIRY|SERIAL|LPN_ID|ACCEPTED|REJECTED|DAMAGED|QUARANTINED|NOTES.");
        }

        int? IntAt(int index) => parts.Length > index && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        decimal? DecimalAt(int index) => parts.Length > index && decimal.TryParse(parts[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        DateTime? DateAt(int index) => parts.Length > index && DateTime.TryParse(
            parts[index], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
        string? At(int index) => parts.Length > index && !string.IsNullOrWhiteSpace(parts[index]) ? parts[index].Trim() : null;

        return new ReceiptLineInput(
            parts[0].Trim(),
            quantity,
            At(2),
            At(3),
            IntAt(4),
            IntAt(5),
            IntAt(6),
            IntAt(7),
            At(8),
            DateAt(9),
            At(10),
            IntAt(11),
            DecimalAt(12),
            DecimalAt(13),
            DecimalAt(14),
            DecimalAt(15),
            At(16));
    }
}
