// Wms.Domain/Entities/Item.cs

using Wms.Domain.Common;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public class Item : Entity
{
    private readonly List<Barcode> _barcodes = new();

    // EF Constructor
    private Item()
    {
    }

    public Item(string sku, string name, string unitOfMeasure, bool requiresLot = false, bool requiresSerial = false)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required", nameof(sku));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
            throw new ArgumentException("Unit of measure is required", nameof(unitOfMeasure));

        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        UnitOfMeasure = unitOfMeasure.Trim();
        RequiresLot = requiresLot;
        RequiresSerial = requiresSerial;
    }

    public string Sku { get; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public bool RequiresLot { get; private set; }
    public bool RequiresSerial { get; private set; }
    public int ShelfLifeDays { get; private set; }
    public IReadOnlyList<Barcode> Barcodes => _barcodes.AsReadOnly();

    public void AddBarcode(Barcode barcode)
    {
        if (_barcodes.Contains(barcode))
            throw new InvalidOperationException($"Barcode {barcode} already exists for item {Sku}");

        _barcodes.Add(barcode);
        SetUpdatedAt();
    }

    public void RemoveBarcode(Barcode barcode)
    {
        if (!_barcodes.Contains(barcode))
            throw new InvalidOperationException($"Barcode {barcode} does not exist for item {Sku}");

        _barcodes.Remove(barcode);
        SetUpdatedAt();
    }

    public void UpdateDetails(string name, string description = "")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        SetUpdatedAt();
    }

    public void SetShelfLife(int days)
    {
        if (days < 0)
            throw new ArgumentException("Shelf life cannot be negative", nameof(days));

        ShelfLifeDays = days;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }
}
