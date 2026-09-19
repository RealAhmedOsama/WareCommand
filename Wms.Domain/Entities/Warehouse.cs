// Wms.Domain/Entities/Warehouse.cs

using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public class Warehouse : Entity
{
    private readonly List<Location> _locations = new();

    // EF Constructor
    private Warehouse()
    {
    }

    public Warehouse(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<Location> Locations => _locations.AsReadOnly();

    public void UpdateDetails(string name, string address = "")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        Name = name.Trim();
        Address = address?.Trim() ?? string.Empty;
        SetUpdatedAt();
    }

    public Location AddLocation(string code, string name, int? parentLocationId = null)
    {
        var location = new Location(code, name, Id, parentLocationId);
        _locations.Add(location);
        SetUpdatedAt();
        return location;
    }
}
