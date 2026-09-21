using Wms.Application.Identity;
using Wms.Application.Reporting;
using Wms.Application.UseCases.Reports;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class MovementReportViewModel
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? WarehouseId { get; set; }
    public string? SearchTerm { get; set; }
    public string? ItemSku { get; set; }
    public string? LocationCode { get; set; }
    public MovementType? MovementType { get; set; }
    public string? UserId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? LicensePlateNumber { get; set; }
    public MovementReportSort Sort { get; set; } = MovementReportSort.Timestamp;
    public bool Descending { get; set; } = true;
    public string? DisplayUnitOfMeasure { get; set; }
    public MovementReportGroupBy GroupBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
    public IReadOnlyList<MovementReportDto> Rows { get; set; } = [];
    public IReadOnlyList<ReportGroupRowDto> Groups { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public ReportMetadata? Metadata { get; set; }
}
