using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Suppliers;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = WmsPermissions.SuppliersRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class SuppliersController(
    ISupplierManagementService supplierManagementService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SupplierPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeInactive = true,
        [FromQuery] int? preferredWarehouseId = null,
        [FromQuery] SupplierSortField sortBy = SupplierSortField.Code,
        [FromQuery] bool descending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ListAsync(
            new SupplierListQuery(
                searchTerm,
                includeInactive,
                preferredWarehouseId,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve(
        [FromQuery] int supplierId,
        [FromQuery] string? vendorSku,
        [FromQuery] string? vendorBarcode,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ResolveItemReferenceAsync(
            new SupplierItemReferenceResolutionQuery(
                supplierId,
                vendorSku,
                vendorBarcode,
                includeInactive),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] SupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await supplierManagementService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] SupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await supplierManagementService.UpdateAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:int}/active")]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] SupplierActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.SetActiveAsync(
            id,
            request.IsActive,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.DeleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("import")]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        [FromBody] SupplierImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ImportAsync(
            request.Csv,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("export")]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Export(
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeInactive = true,
        [FromQuery] int? preferredWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ExportAsync(
            new SupplierListQuery(
                searchTerm,
                includeInactive,
                preferredWarehouseId,
                PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "suppliers.csv");
    }

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ProblemFor(result.FirstError!);
    }

    private IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ProblemFor(result.FirstError!);
    }

    private ObjectResult ProblemFor(ResultError error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }
}

public sealed class SupplierRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string LegalName { get; init; } = string.Empty;

    [StringLength(200)]
    public string? LocalizedName { get; init; }

    [StringLength(100)]
    public string? TaxRegistrationNumber { get; init; }

    [StringLength(100)]
    public string? ExternalErpIdentifier { get; init; }

    [StringLength(200)]
    public string? AddressLine1 { get; init; }

    [StringLength(200)]
    public string? AddressLine2 { get; init; }

    [StringLength(100)]
    public string? City { get; init; }

    [StringLength(100)]
    public string? StateOrProvince { get; init; }

    [StringLength(30)]
    public string? PostalCode { get; init; }

    [StringLength(2)]
    public string? CountryCode { get; init; }

    [StringLength(200)]
    public string? ContactName { get; init; }

    [EmailAddress]
    [StringLength(320)]
    public string? ContactEmail { get; init; }

    [StringLength(50)]
    public string? ContactPhone { get; init; }

    public SupplierReceivingDefaultsRequest? ReceivingDefaults { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<SupplierItemReferenceRequest>? ItemReferences { get; init; }

    public SupplierInput ToInput() =>
        new(
            Code,
            LegalName,
            LocalizedName,
            TaxRegistrationNumber,
            ExternalErpIdentifier,
            AddressLine1,
            AddressLine2,
            City,
            StateOrProvince,
            PostalCode,
            CountryCode,
            ContactName,
            ContactEmail,
            ContactPhone,
            ReceivingDefaults?.ToInput(),
            Notes,
            IsActive,
            ItemReferences?.Select(reference => reference.ToInput()).ToArray());
}

public sealed class SupplierReceivingDefaultsRequest
{
    [Range(1, int.MaxValue)]
    public int? PreferredWarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int? PreferredDockLocationId { get; init; }

    [Range(0, 3650)]
    public int? DefaultLeadTimeDays { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? OverDeliveryTolerancePercent { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? UnderDeliveryTolerancePercent { get; init; }

    public bool RequiresLot { get; init; }
    public bool RequiresExpiry { get; init; }

    [StringLength(100)]
    public string? QualityProfile { get; init; }

    [StringLength(100)]
    public string? LabelRule { get; init; }

    [StringLength(3)]
    public string? DefaultCurrencyCode { get; init; }

    public SupplierReceivingDefaultsInput ToInput() =>
        new(
            PreferredWarehouseId,
            PreferredDockLocationId,
            DefaultLeadTimeDays,
            OverDeliveryTolerancePercent,
            UnderDeliveryTolerancePercent,
            RequiresLot,
            RequiresExpiry,
            QualityProfile,
            LabelRule,
            DefaultCurrencyCode);
}

public sealed class SupplierItemReferenceRequest
{
    public int? Id { get; init; }

    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Required]
    [StringLength(100)]
    public string VendorSku { get; init; } = string.Empty;

    [StringLength(100)]
    public string? VendorBarcode { get; init; }

    [Range(1, int.MaxValue)]
    public int? ItemPackagingId { get; init; }

    [StringLength(100)]
    public string? VendorPackaging { get; init; }

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal? UnitsPerPurchasePackage { get; init; }

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal MinimumOrderQuantity { get; init; } = 1;

    [Range(0, 3650)]
    public int? LeadTimeDays { get; init; }
    public bool IsActive { get; init; } = true;

    public SupplierItemReferenceInput ToInput() =>
        new(
            Id,
            ItemId,
            VendorSku,
            VendorBarcode,
            ItemPackagingId,
            VendorPackaging,
            UnitsPerPurchasePackage,
            MinimumOrderQuantity,
            LeadTimeDays,
            IsActive);
}

public sealed class SupplierActivationRequest
{
    public bool IsActive { get; init; }
}

public sealed class SupplierImportRequest
{
    [Required]
    public string Csv { get; init; } = string.Empty;
}
