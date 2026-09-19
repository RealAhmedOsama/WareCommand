// Wms.Domain/Entities/Location.cs

using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public class Location : Entity
{
    private readonly List<Location> _childLocations = new();

    // EF Constructor
    private Location()
    {
    }

    public Location(string code, string name, int warehouseId, int? parentLocationId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        WarehouseId = warehouseId;
        ParentLocationId = parentLocationId;
    }

    public string Code { get; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? ParentLocationId { get; private set; }
    public bool IsPickable { get; private set; } = true;
    public bool IsReceivable { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public int Capacity { get; private set; } = 1000;

    // Navigation properties
    public Warehouse Warehouse { get; private set; } = null!;
    public Location? ParentLocation { get; private set; }
    public IReadOnlyList<Location> ChildLocations => _childLocations.AsReadOnly();

    public void UpdateDetails(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        Name = name.Trim();
        SetUpdatedAt();
    }

    public void SetCapacity(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be positive", nameof(capacity));

        Capacity = capacity;
        SetUpdatedAt();
    }

    public void SetPickable(bool isPickable)
    {
        IsPickable = isPickable;
        SetUpdatedAt();
    }

    public void SetReceivable(bool isReceivable)
    {
        IsReceivable = isReceivable;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    public string GetFullPath()
    {
        if (ParentLocation == null)
            return Code;

        return $"{ParentLocation.GetFullPath()}.{Code}";
    }
}
