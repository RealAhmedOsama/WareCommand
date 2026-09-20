// Wms.Domain/Entities/Item.cs

using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public class Item : Entity
{
    private const int MaximumSkuLength = 50;
    private const int MaximumNameLength = 200;
    private const int MaximumDescriptionLength = 1_000;
    private const int MaximumUnitLength = 20;
    private const int MaximumShortTextLength = 100;
    private const int MaximumReferenceLength = 500;

    private readonly List<Barcode> _barcodes = new();
    private readonly List<ItemPackaging> _packagings = new();

    // EF Constructor
    private Item()
    {
    }

    public Item(
        string sku,
        string name,
        string unitOfMeasure,
        bool requiresLot = false,
        bool requiresSerial = false)
    {
        Sku = NormalizeRequired(sku, MaximumSkuLength, nameof(sku), uppercase: true);
        UnitOfMeasure = NormalizeRequired(unitOfMeasure, MaximumUnitLength, nameof(unitOfMeasure), uppercase: true);
        ApplyMasterDetails(
            new ItemMasterDetails(
                name,
                Description: string.Empty,
                PurchaseUnit: UnitOfMeasure,
                SalesUnit: UnitOfMeasure,
                RequiresLot: requiresLot,
                RequiresSerial: requiresSerial),
            stampUpdatedAt: false);
    }

    public string Sku { get; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string LocalizedDescription { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string? Brand { get; private set; }
    public ItemType Type { get; private set; } = ItemType.Stock;
    public ItemLifecycleStatus LifecycleStatus { get; private set; } = ItemLifecycleStatus.Active;
    public string? ImageReference { get; private set; }
    public string? DocumentReference { get; private set; }
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public string PurchaseUnit { get; private set; } = string.Empty;
    public string SalesUnit { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public bool RequiresLot { get; private set; }
    public bool RequiresSerial { get; private set; }
    public bool RequiresExpiry { get; private set; }
    public int ShelfLifeDays { get; private set; }
    public bool UseFefo { get; private set; }
    public bool QualityInspectionRequired { get; private set; }
    public bool IsHazardous { get; private set; }
    public bool TemperatureControlled { get; private set; }
    public bool SpecialHandlingRequired { get; private set; }
    public decimal? NetWeightKg { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? VolumeCubicMeters { get; private set; }
    public decimal? StandardCost { get; private set; }
    public decimal? SalesPrice { get; private set; }
    public string? CountryOfOrigin { get; private set; }
    public string? CustomsCode { get; private set; }
    public decimal? MinimumTemperatureCelsius { get; private set; }
    public decimal? MaximumTemperatureCelsius { get; private set; }
    public string? StorageProfile { get; private set; }
    public string? PutawayProfile { get; private set; }
    public string? DefaultSupplierCode { get; private set; }
    public ItemReorderPolicy ReorderPolicy { get; private set; } = ItemReorderPolicy.None;
    public decimal? MinimumStock { get; private set; }
    public decimal? MaximumStock { get; private set; }
    public decimal? SafetyStock { get; private set; }
    public int? LeadTimeDays { get; private set; }

    public IReadOnlyList<Barcode> Barcodes => _barcodes.AsReadOnly();
    public IReadOnlyList<ItemPackaging> Packagings => _packagings.AsReadOnly();

    public void AddBarcode(Barcode barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        if (_barcodes.Contains(barcode))
        {
            throw new InvalidOperationException($"Barcode {barcode} already exists for item {Sku}");
        }

        _barcodes.Add(barcode);
        SetUpdatedAt();
    }

    public void RemoveBarcode(Barcode barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        if (!_barcodes.Contains(barcode))
        {
            throw new InvalidOperationException($"Barcode {barcode} does not exist for item {Sku}");
        }

        _barcodes.Remove(barcode);
        SetUpdatedAt();
    }

    public void AddPackaging(ItemPackaging packaging)
    {
        ArgumentNullException.ThrowIfNull(packaging);
        if (_packagings.Any(existing =>
                string.Equals(existing.Code, packaging.Code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Packaging code {packaging.Code} already exists for item {Sku}");
        }

        _packagings.Add(packaging);
        SetUpdatedAt();
    }

    public void RemovePackaging(ItemPackaging packaging)
    {
        ArgumentNullException.ThrowIfNull(packaging);
        if (!_packagings.Remove(packaging))
        {
            throw new InvalidOperationException($"Packaging code {packaging.Code} does not exist for item {Sku}");
        }

        SetUpdatedAt();
    }

    public void UpdateDetails(string name, string description = "")
    {
        Name = NormalizeRequired(name, MaximumNameLength, nameof(name));
        Description = NormalizeOptional(description, MaximumDescriptionLength);
        SetUpdatedAt();
    }

    public void UpdateMasterData(ItemMasterDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        ApplyMasterDetails(details, stampUpdatedAt: true);
    }

    public void SetShelfLife(int days)
    {
        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Shelf life cannot be negative.");
        }

        ShelfLifeDays = days;
        if (days == 0)
        {
            RequiresExpiry = false;
            UseFefo = false;
        }

        SetUpdatedAt();
    }

    public void SetLocalizedText(string? localizedName, string? localizedDescription)
    {
        LocalizedName = NormalizeOptional(localizedName, MaximumNameLength);
        LocalizedDescription = NormalizeOptional(localizedDescription, MaximumDescriptionLength);
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        SetLifecycleStatus(ItemLifecycleStatus.Inactive);
    }

    public void Activate()
    {
        SetLifecycleStatus(ItemLifecycleStatus.Active);
    }

    public void Discontinue()
    {
        SetLifecycleStatus(ItemLifecycleStatus.Discontinued);
    }

    public void SetLifecycleStatus(ItemLifecycleStatus status)
    {
        LifecycleStatus = status;
        IsActive = status == ItemLifecycleStatus.Active;
        SetUpdatedAt();
    }

    private void ApplyMasterDetails(ItemMasterDetails details, bool stampUpdatedAt)
    {
        Name = NormalizeRequired(details.Name, MaximumNameLength, nameof(details.Name));
        LocalizedName = NormalizeOptional(details.LocalizedName, MaximumNameLength);
        Description = NormalizeOptional(details.Description, MaximumDescriptionLength);
        LocalizedDescription = NormalizeOptional(details.LocalizedDescription, MaximumDescriptionLength);
        Category = NormalizeOptional(details.Category, MaximumShortTextLength);
        Brand = NormalizeOptional(details.Brand, MaximumShortTextLength);
        Type = details.Type;
        LifecycleStatus = details.LifecycleStatus;
        IsActive = LifecycleStatus == ItemLifecycleStatus.Active;
        ImageReference = NormalizeOptional(details.ImageReference, MaximumReferenceLength);
        DocumentReference = NormalizeOptional(details.DocumentReference, MaximumReferenceLength);
        PurchaseUnit = NormalizeRequired(details.PurchaseUnit ?? UnitOfMeasure, MaximumUnitLength, nameof(details.PurchaseUnit), uppercase: true);
        SalesUnit = NormalizeRequired(details.SalesUnit ?? UnitOfMeasure, MaximumUnitLength, nameof(details.SalesUnit), uppercase: true);
        RequiresLot = details.RequiresLot;
        RequiresSerial = details.RequiresSerial;
        ShelfLifeDays = ValidateNonNegative(details.ShelfLifeDays, nameof(details.ShelfLifeDays));
        RequiresExpiry = details.RequiresExpiry;
        UseFefo = details.UseFefo;
        QualityInspectionRequired = details.QualityInspectionRequired;
        IsHazardous = details.IsHazardous;
        TemperatureControlled = details.TemperatureControlled;
        SpecialHandlingRequired = details.SpecialHandlingRequired;
        NetWeightKg = ValidateNonNegative(details.NetWeightKg, nameof(details.NetWeightKg));
        LengthCm = ValidateNonNegative(details.LengthCm, nameof(details.LengthCm));
        WidthCm = ValidateNonNegative(details.WidthCm, nameof(details.WidthCm));
        HeightCm = ValidateNonNegative(details.HeightCm, nameof(details.HeightCm));
        VolumeCubicMeters = ValidateNonNegative(details.VolumeCubicMeters, nameof(details.VolumeCubicMeters));
        if (!VolumeCubicMeters.HasValue && LengthCm.HasValue && WidthCm.HasValue && HeightCm.HasValue)
        {
            VolumeCubicMeters = LengthCm.Value * WidthCm.Value * HeightCm.Value / 1_000_000m;
        }

        StandardCost = ValidateNonNegative(details.StandardCost, nameof(details.StandardCost));
        SalesPrice = ValidateNonNegative(details.SalesPrice, nameof(details.SalesPrice));
        CountryOfOrigin = NormalizeOptional(details.CountryOfOrigin, MaximumShortTextLength);
        CustomsCode = NormalizeOptional(details.CustomsCode, MaximumShortTextLength);
        MinimumTemperatureCelsius = details.MinimumTemperatureCelsius;
        MaximumTemperatureCelsius = details.MaximumTemperatureCelsius;
        if (MinimumTemperatureCelsius.HasValue && MaximumTemperatureCelsius.HasValue &&
            MinimumTemperatureCelsius > MaximumTemperatureCelsius)
        {
            throw new ArgumentException("Minimum temperature cannot exceed maximum temperature.");
        }

        StorageProfile = NormalizeOptional(details.StorageProfile, MaximumShortTextLength);
        PutawayProfile = NormalizeOptional(details.PutawayProfile, MaximumShortTextLength);
        DefaultSupplierCode = NormalizeOptional(details.DefaultSupplierCode, MaximumShortTextLength);
        ReorderPolicy = details.ReorderPolicy;
        MinimumStock = ValidateNonNegative(details.MinimumStock, nameof(details.MinimumStock));
        MaximumStock = ValidateNonNegative(details.MaximumStock, nameof(details.MaximumStock));
        SafetyStock = ValidateNonNegative(details.SafetyStock, nameof(details.SafetyStock));
        if (MinimumStock.HasValue && MaximumStock.HasValue && MinimumStock > MaximumStock)
        {
            throw new ArgumentException("Minimum stock cannot exceed maximum stock.");
        }

        LeadTimeDays = details.LeadTimeDays is null
            ? null
            : ValidateNonNegative(details.LeadTimeDays.Value, nameof(details.LeadTimeDays));
        if (RequiresExpiry && ShelfLifeDays == 0)
        {
            throw new ArgumentException("An expiry-controlled item must define a positive shelf life.");
        }

        if (UseFefo && !RequiresExpiry)
        {
            throw new ArgumentException("FEFO requires expiry control.");
        }

        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static int ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    private static decimal? ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName,
        bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (uppercase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
