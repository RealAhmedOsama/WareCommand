using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Customers;
using Wms.Application.Identity;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = WmsPermissions.CustomersRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class CustomersController(
    ICustomerManagementService customerManagementService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeInactive = true,
        [FromQuery] CustomerSortField sortBy = CustomerSortField.Code,
        [FromQuery] bool descending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.ListAsync(
            new CustomerListQuery(searchTerm, includeInactive, sortBy, descending, page, pageSize),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.CustomersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] CustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = WmsPermissions.CustomersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] CustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.UpdateAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{id:int}/active")]
    [Authorize(Policy = WmsPermissions.CustomersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        [FromBody] CustomerActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.SetActiveAsync(
            id,
            request.IsActive,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = WmsPermissions.CustomersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.DeleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("import")]
    [Authorize(Policy = WmsPermissions.CustomersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        [FromBody] CustomerImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.ImportAsync(
            request.Csv,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeInactive = true,
        [FromQuery] CustomerSortField sortBy = CustomerSortField.Code,
        [FromQuery] bool descending = false,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.ExportAsync(
            new CustomerListQuery(searchTerm, includeInactive, sortBy, descending),
            cancellationToken);
        return result.IsSuccess
            ? File(System.Text.Encoding.UTF8.GetBytes(result.Value), "text/csv", "customers.csv")
            : ProblemFor(result.FirstError!);
    }

    [HttpGet("document-snapshot")]
    public async Task<IActionResult> DocumentSnapshot(
        [FromQuery] int customerId,
        [FromQuery] int? shipToAddressId = null,
        [FromQuery] string? shipToCode = null,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.GetDocumentSnapshotAsync(
            new CustomerDocumentSnapshotQuery(customerId, shipToAddressId, shipToCode),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("resolve-item")]
    public async Task<IActionResult> ResolveItem(
        [FromQuery] int customerId,
        [FromQuery] string? customerSku = null,
        [FromQuery] string? customerBarcode = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await customerManagementService.ResolveItemReferenceAsync(
            new CustomerItemReferenceResolutionQuery(
                customerId,
                customerSku,
                customerBarcode,
                includeInactive),
            cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : ProblemFor(result.FirstError!);

    private IActionResult ToActionResult(Result result) =>
        result.IsSuccess ? NoContent() : ProblemFor(result.FirstError!);

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

public sealed class CustomerRequest
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
    [StringLength(100)]
    public string? ExternalChannelIdentifier { get; init; }
    [StringLength(200)]
    public string? ContactName { get; init; }
    [EmailAddress]
    [StringLength(320)]
    public string? ContactEmail { get; init; }
    [StringLength(50)]
    public string? ContactPhone { get; init; }
    [StringLength(200)]
    public string? BillingAddressLine1 { get; init; }
    [StringLength(200)]
    public string? BillingAddressLine2 { get; init; }
    [StringLength(100)]
    public string? BillingCity { get; init; }
    [StringLength(100)]
    public string? BillingRegion { get; init; }
    [StringLength(30)]
    public string? BillingPostalCode { get; init; }
    [StringLength(2)]
    public string? BillingCountryCode { get; init; }
    [StringLength(50)]
    public string? DefaultCarrierCode { get; init; }
    [StringLength(80)]
    public string? DefaultCarrierServiceCode { get; init; }
    [Range(0, 999)]
    public int Priority { get; init; } = 100;
    [StringLength(100)]
    public string? PackagingProfile { get; init; }
    [StringLength(100)]
    public string? LabelProfile { get; init; }
    public bool AllowPartialShipment { get; init; }
    [StringLength(2_000)]
    public string? Notes { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<CustomerShipToAddressRequest>? ShipToAddresses { get; init; }
    public IReadOnlyList<CustomerItemReferenceRequest>? ItemReferences { get; init; }

    public CustomerInput ToInput() =>
        new(
            Code,
            LegalName,
            LocalizedName,
            TaxRegistrationNumber,
            ExternalErpIdentifier,
            ExternalChannelIdentifier,
            ContactName,
            ContactEmail,
            ContactPhone,
            BillingAddressLine1,
            BillingAddressLine2,
            BillingCity,
            BillingRegion,
            BillingPostalCode,
            BillingCountryCode,
            DefaultCarrierCode,
            DefaultCarrierServiceCode,
            Priority,
            PackagingProfile,
            LabelProfile,
            AllowPartialShipment,
            Notes,
            IsActive,
            ShipToAddresses?.Select(address => address.ToInput()).ToArray(),
            ItemReferences?.Select(reference => reference.ToInput()).ToArray());
}

public sealed class CustomerShipToAddressRequest
{
    public int? Id { get; init; }
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;
    [Required]
    [StringLength(200)]
    public string RecipientName { get; init; } = string.Empty;
    [StringLength(50)]
    public string? Phone { get; init; }
    [StringLength(2)]
    public string? CountryCode { get; init; }
    [StringLength(100)]
    public string? Region { get; init; }
    [StringLength(100)]
    public string? City { get; init; }
    [StringLength(30)]
    public string? PostalCode { get; init; }
    [Required]
    [StringLength(200)]
    public string AddressLine1 { get; init; } = string.Empty;
    [StringLength(200)]
    public string? AddressLine2 { get; init; }
    [StringLength(1_000)]
    public string? DeliveryInstructions { get; init; }
    public TimeSpan? DeliveryWindowStart { get; init; }
    public TimeSpan? DeliveryWindowEnd { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;

    public CustomerShipToAddressInput ToInput() =>
        new(
            Id,
            Code,
            RecipientName,
            Phone,
            CountryCode,
            Region,
            City,
            PostalCode,
            AddressLine1,
            AddressLine2,
            DeliveryInstructions,
            DeliveryWindowStart,
            DeliveryWindowEnd,
            IsDefault,
            IsActive);
}

public sealed class CustomerItemReferenceRequest
{
    public int? Id { get; init; }
    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }
    [Required]
    [StringLength(100)]
    public string CustomerSku { get; init; } = string.Empty;
    [StringLength(100)]
    public string? CustomerBarcode { get; init; }
    [StringLength(200)]
    public string? CustomerDescription { get; init; }
    public bool IsActive { get; init; } = true;

    public CustomerItemReferenceInput ToInput() =>
        new(Id, ItemId, CustomerSku, CustomerBarcode, CustomerDescription, IsActive);
}

public sealed class CustomerActivationRequest
{
    public bool IsActive { get; init; }
}

public sealed class CustomerImportRequest
{
    [Required]
    public string Csv { get; init; } = string.Empty;
}
