using System.ComponentModel.DataAnnotations;
using Wms.Application.Auditing;
using Wms.Application.Identity;

namespace Wms.ASP.Models;

public sealed class AuditLogViewModel
{
    [DataType(DataType.Date)]
    [Display(Name = "From date")]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "To date")]
    public DateTime? ToDate { get; set; }

    [Display(Name = "Actor user ID")]
    public string? UserId { get; set; }

    [Display(Name = "Warehouse")]
    public int? WarehouseId { get; set; }

    [Display(Name = "Action")]
    public string? Action { get; set; }

    [Display(Name = "Entity type")]
    public string? EntityType { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;

    public IReadOnlyList<AuditEntryDto> Entries { get; set; } = [];

    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];

    public IReadOnlyList<string> Actions { get; set; } = WmsAuditActions.Catalog;

    public IReadOnlyList<string> EntityTypes { get; set; } =
    [
        WmsAuditEntityTypes.Authentication,
        WmsAuditEntityTypes.User,
        WmsAuditEntityTypes.AccessAssignment,
        WmsAuditEntityTypes.Item,
        WmsAuditEntityTypes.Location,
        WmsAuditEntityTypes.Stock,
        WmsAuditEntityTypes.Movement,
        WmsAuditEntityTypes.Count,
        WmsAuditEntityTypes.Transfer,
        WmsAuditEntityTypes.Allocation,
        WmsAuditEntityTypes.Package,
        WmsAuditEntityTypes.Shipment,
        WmsAuditEntityTypes.Return,
        WmsAuditEntityTypes.Settings,
        WmsAuditEntityTypes.Integration
    ];

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}
