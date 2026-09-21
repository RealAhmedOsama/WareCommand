using Wms.Application.Administration;
using Wms.Application.Auditing;

namespace Wms.ASP.Models;

public sealed class AdministrationIndexViewModel
{
    public int? WarehouseId { get; init; }

    public IReadOnlyList<AdministrationModuleDto> Modules { get; init; } = [];

    public AdministrationReadinessDto? Readiness { get; init; }

    public AuditPage? History { get; init; }

    public string? HistoryError { get; init; }
}
