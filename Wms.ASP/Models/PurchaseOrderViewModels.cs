using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Wms.Application.Identity;
using Wms.Application.Purchasing;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class PurchaseOrderListViewModel
{
    public IReadOnlyList<PurchaseOrderDto> PurchaseOrders { get; init; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; init; } = [];
    public string? SearchTerm { get; init; }
    public int? WarehouseId { get; init; }
    public int? SupplierId { get; init; }
    public PurchaseOrderStatus? Status { get; init; }
    public bool IncludeCancelled { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class PurchaseOrderFormViewModel
{
    public int Id { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; set; }

    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    public DateTime? ExpectedReceiptDate { get; set; }

    [StringLength(100)]
    public string? ExternalReference { get; set; }

    [StringLength(30)]
    public string SourceType { get; set; } = "MANUAL";

    [StringLength(200)]
    public string? SourceReference { get; set; }

    [StringLength(3)]
    public string? CurrencyCode { get; set; }

    [StringLength(2_000)]
    public string? Notes { get; set; }

    [Required]
    public string LinesText { get; set; } = string.Empty;

    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
    public IReadOnlyList<PurchaseOrderSupplierOption> Suppliers { get; set; } = [];

    public static PurchaseOrderFormViewModel From(PurchaseOrderDto order) => new()
    {
        Id = order.Id,
        WarehouseId = order.WarehouseId,
        SupplierId = order.SupplierId,
        OrderDate = order.OrderDate.ToDateTime(TimeOnly.MinValue),
        ExpectedReceiptDate = order.ExpectedReceiptDate?.ToDateTime(TimeOnly.MinValue),
        ExternalReference = order.ExternalReference,
        SourceType = order.SourceType,
        SourceReference = order.SourceReference,
        CurrencyCode = order.CurrencyCode,
        Notes = order.Notes,
        LinesText = string.Join(
            Environment.NewLine,
            order.Lines.Select(line => string.Join('|', [
                line.ItemSku,
                line.OrderedQuantity.ToString(CultureInfo.InvariantCulture),
                line.OrderedUnitOfMeasure,
                line.OverDeliveryTolerancePercent.ToString(CultureInfo.InvariantCulture),
                line.UnderDeliveryTolerancePercent.ToString(CultureInfo.InvariantCulture),
                line.SupplierItemReference ?? string.Empty,
                line.Notes ?? string.Empty
            ])))
    };

    public PurchaseOrderInput ToInput()
    {
        var lines = LinesText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((line, index) => ParseLine(line, index + 1))
            .ToArray();

        return new PurchaseOrderInput(
            WarehouseId,
            SupplierId,
            DateOnly.FromDateTime(OrderDate.Date),
            ExpectedReceiptDate.HasValue ? DateOnly.FromDateTime(ExpectedReceiptDate.Value.Date) : null,
            ExternalReference,
            SourceType,
            SourceReference,
            CurrencyCode,
            Notes,
            lines);
    }

    private static PurchaseOrderLineInput ParseLine(string value, int row)
    {
        var fields = value.Split('|');
        if (fields.Length < 2 || string.IsNullOrWhiteSpace(fields[0]))
        {
            throw new ArgumentException(
                $"Purchase-order line {row} must be ITEM_SKU|QUANTITY|UOM|OVER_TOLERANCE|UNDER_TOLERANCE|SUPPLIER_ITEM_REFERENCE|NOTES.");
        }

        if (!decimal.TryParse(fields[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
        {
            throw new ArgumentException($"Purchase-order line {row}: quantity must be a decimal number.");
        }

        return new PurchaseOrderLineInput(
            fields[0],
            quantity,
            Optional(fields, 2),
            OptionalDecimal(fields, 3, row),
            OptionalDecimal(fields, 4, row),
            Optional(fields, 5),
            Optional(fields, 6));
    }

    private static string? Optional(string[] fields, int index) =>
        fields.Length > index && !string.IsNullOrWhiteSpace(fields[index])
            ? fields[index].Trim()
            : null;

    private static decimal? OptionalDecimal(string[] fields, int index, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : decimal.TryParse(fields[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"Purchase-order line {row}: tolerance must be a decimal number.");
}

public sealed record PurchaseOrderSupplierOption(int Id, string Code, string Name, bool IsActive);

public sealed class PurchaseOrderImportViewModel
{
    public IFormFile? File { get; set; }
    public string? Csv { get; set; }
}
