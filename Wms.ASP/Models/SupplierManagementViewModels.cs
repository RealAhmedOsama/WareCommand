using System.ComponentModel.DataAnnotations;
using Wms.Application.Identity;
using Wms.Application.Suppliers;

namespace Wms.ASP.Models;

public sealed class SupplierListViewModel
{
    public IReadOnlyList<SupplierDto> Suppliers { get; init; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; init; } = [];
    public string? SearchTerm { get; init; }
    public bool IncludeInactive { get; init; } = true;
    public int? PreferredWarehouseId { get; init; }
    public SupplierSortField SortBy { get; init; } = SupplierSortField.Code;
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class SupplierFormViewModel
{
    public int Id { get; init; }

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string LegalName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? LocalizedName { get; set; }

    [StringLength(100)]
    public string? TaxRegistrationNumber { get; set; }

    [StringLength(100)]
    public string? ExternalErpIdentifier { get; set; }

    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? StateOrProvince { get; set; }

    [StringLength(30)]
    public string? PostalCode { get; set; }

    [StringLength(2)]
    public string? CountryCode { get; set; }

    [StringLength(200)]
    public string? ContactName { get; set; }

    [EmailAddress]
    [StringLength(320)]
    public string? ContactEmail { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    public int? PreferredWarehouseId { get; set; }
    public int? PreferredDockLocationId { get; set; }

    [Range(0, 3650)]
    public int? DefaultLeadTimeDays { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? OverDeliveryTolerancePercent { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? UnderDeliveryTolerancePercent { get; set; }

    public bool RequiresLot { get; set; }
    public bool RequiresExpiry { get; set; }

    [StringLength(100)]
    public string? QualityProfile { get; set; }

    [StringLength(100)]
    public string? LabelRule { get; set; }

    [StringLength(3)]
    public string? DefaultCurrencyCode { get; set; }

    [StringLength(2_000)]
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ItemReferencesText { get; set; }
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];

    public static SupplierFormViewModel From(SupplierDto supplier) =>
        new()
        {
            Id = supplier.Id,
            Code = supplier.Code,
            LegalName = supplier.LegalName,
            LocalizedName = supplier.LocalizedName,
            TaxRegistrationNumber = supplier.TaxRegistrationNumber,
            ExternalErpIdentifier = supplier.ExternalErpIdentifier,
            AddressLine1 = supplier.AddressLine1,
            AddressLine2 = supplier.AddressLine2,
            City = supplier.City,
            StateOrProvince = supplier.StateOrProvince,
            PostalCode = supplier.PostalCode,
            CountryCode = supplier.CountryCode,
            ContactName = supplier.ContactName,
            ContactEmail = supplier.ContactEmail,
            ContactPhone = supplier.ContactPhone,
            PreferredWarehouseId = supplier.PreferredWarehouseId,
            PreferredDockLocationId = supplier.PreferredDockLocationId,
            DefaultLeadTimeDays = supplier.DefaultLeadTimeDays,
            OverDeliveryTolerancePercent = supplier.OverDeliveryTolerancePercent,
            UnderDeliveryTolerancePercent = supplier.UnderDeliveryTolerancePercent,
            RequiresLot = supplier.RequiresLot,
            RequiresExpiry = supplier.RequiresExpiry,
            QualityProfile = supplier.QualityProfile,
            LabelRule = supplier.LabelRule,
            DefaultCurrencyCode = supplier.DefaultCurrencyCode,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive,
            ItemReferencesText = string.Join(
                Environment.NewLine,
                supplier.ItemReferences.Select(reference => string.Join('|', [
                    reference.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reference.VendorSku,
                    reference.VendorBarcode ?? string.Empty,
                    reference.ItemPackagingId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    reference.VendorPackaging ?? string.Empty,
                    reference.UnitsPerPurchasePackage?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    reference.MinimumOrderQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reference.LeadTimeDays?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    reference.IsActive.ToString()
                ])))
        };
}

public sealed class SupplierImportViewModel
{
    public IFormFile? File { get; set; }
    public string? Csv { get; set; }
}
