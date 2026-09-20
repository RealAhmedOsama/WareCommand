// Wms.Domain/Entities/Warehouse.cs

using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class Warehouse : Entity
{
    private readonly List<Location> _locations = new();
    private readonly List<WarehouseOperationalLocation> _operationalLocations = new();

    // EF Constructor
    private Warehouse()
    {
    }

    public Warehouse(
        string code,
        string name,
        string? arabicName = null,
        string? address = null,
        string? contactName = null,
        string? contactPhone = null,
        string? contactEmail = null,
        string timeZone = "UTC",
        bool allowNegativeStock = false,
        bool requireLocationForAdjustment = true,
        bool blockExpiredReceipt = true,
        int expiryWarningDays = 30)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        var normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.Length > 20)
            throw new ArgumentException("Code cannot exceed 20 characters", nameof(code));

        Code = normalizedCode;
        ApplyProfile(
            name,
            arabicName,
            address,
            contactName,
            contactPhone,
            contactEmail,
            timeZone,
            allowNegativeStock,
            requireLocationForAdjustment,
            blockExpiredReceipt,
            expiryWarningDays,
            stampUpdatedAt: false);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string ArabicName { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string ContactName { get; private set; } = string.Empty;
    public string ContactPhone { get; private set; } = string.Empty;
    public string ContactEmail { get; private set; } = string.Empty;
    public string TimeZone { get; private set; } = "UTC";
    public bool IsActive { get; private set; } = true;
    public bool WorkflowEnabled { get; private set; }
    public bool AllowNegativeStock { get; private set; }
    public bool RequireLocationForAdjustment { get; private set; } = true;
    public bool BlockExpiredReceipt { get; private set; } = true;
    public int ExpiryWarningDays { get; private set; } = 30;
    public IReadOnlyList<Location> Locations => _locations.AsReadOnly();
    public IReadOnlyList<WarehouseOperationalLocation> OperationalLocations =>
        _operationalLocations.AsReadOnly();
    public WarehouseNumberSequence? NumberSequence { get; private set; }

    public void UpdateDetails(string name, string address = "")
    {
        UpdateProfile(
            name,
            ArabicName,
            address,
            ContactName,
            ContactPhone,
            ContactEmail,
            TimeZone,
            AllowNegativeStock,
            RequireLocationForAdjustment,
            BlockExpiredReceipt,
            ExpiryWarningDays);
    }

    public void UpdateProfile(
        string name,
        string? arabicName,
        string? address,
        string? contactName,
        string? contactPhone,
        string? contactEmail,
        string timeZone,
        bool allowNegativeStock,
        bool requireLocationForAdjustment,
        bool blockExpiredReceipt,
        int expiryWarningDays)
    {
        ApplyProfile(
            name,
            arabicName,
            address,
            contactName,
            contactPhone,
            contactEmail,
            timeZone,
            allowNegativeStock,
            requireLocationForAdjustment,
            blockExpiredReceipt,
            expiryWarningDays,
            stampUpdatedAt: true);
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        WorkflowEnabled = false;
        SetUpdatedAt();
    }

    public void EnableWorkflows(IEnumerable<WarehouseOperationalLocationRole> configuredRoles)
    {
        ArgumentNullException.ThrowIfNull(configuredRoles);
        if (!IsActive)
            throw new InvalidOperationException("An inactive warehouse cannot enable workflows.");

        var configured = configuredRoles.ToHashSet();
        var missing = RequiredOperationalLocationRoles
            .Where(role => !configured.Contains(role))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Required operational locations are missing: {string.Join(", ", missing)}.");
        }

        WorkflowEnabled = true;
        SetUpdatedAt();
    }

    public void DisableWorkflows()
    {
        WorkflowEnabled = false;
        SetUpdatedAt();
    }

    public string GetDisplayName(string? locale)
    {
        return locale?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true &&
               !string.IsNullOrWhiteSpace(ArabicName)
            ? ArabicName
            : Name;
    }

    public Location AddLocation(string code, string name, int? parentLocationId = null)
    {
        var location = new Location(code, name, Id, parentLocationId);
        _locations.Add(location);
        SetUpdatedAt();
        return location;
    }

    public static IReadOnlyList<WarehouseOperationalLocationRole> RequiredOperationalLocationRoles { get; } =
    [
        WarehouseOperationalLocationRole.Receiving,
        WarehouseOperationalLocationRole.Staging,
        WarehouseOperationalLocationRole.Storage,
        WarehouseOperationalLocationRole.Packing,
        WarehouseOperationalLocationRole.Shipping,
        WarehouseOperationalLocationRole.Quarantine,
        WarehouseOperationalLocationRole.Damaged,
        WarehouseOperationalLocationRole.Returns,
        WarehouseOperationalLocationRole.Transit
    ];

    private void ApplyProfile(
        string name,
        string? arabicName,
        string? address,
        string? contactName,
        string? contactPhone,
        string? contactEmail,
        string timeZone,
        bool allowNegativeStock,
        bool requireLocationForAdjustment,
        bool blockExpiredReceipt,
        int expiryWarningDays,
        bool stampUpdatedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
        if (name.Trim().Length > 200)
            throw new ArgumentException("Name cannot exceed 200 characters", nameof(name));
        if (expiryWarningDays is < 0 or > 3_650)
            throw new ArgumentOutOfRangeException(nameof(expiryWarningDays));

        Name = name.Trim();
        ArabicName = Trim(arabicName, 200);
        Address = Trim(address, 500);
        ContactName = Trim(contactName, 200);
        ContactPhone = Trim(contactPhone, 50);
        ContactEmail = Trim(contactEmail, 320);
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim();
        AllowNegativeStock = allowNegativeStock;
        RequireLocationForAdjustment = requireLocationForAdjustment;
        BlockExpiredReceipt = blockExpiredReceipt;
        ExpiryWarningDays = expiryWarningDays;

        if (stampUpdatedAt)
            SetUpdatedAt();
    }

    private static string Trim(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
