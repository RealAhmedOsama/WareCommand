namespace Wms.Domain.Enums;

public static class InventoryStatusCodes
{
    public const string Available = "AVAILABLE";
    public const string Reserved = "RESERVED";
    public const string QualityPending = "QC_PENDING";
    public const string Quarantine = "QUARANTINE";
    public const string Hold = "HOLD";
    public const string Damaged = "DAMAGED";
    public const string Expired = "EXPIRED";
    public const string ReturnPending = "RETURN_PENDING";
    public const string ScrapPending = "SCRAP_PENDING";
}

public static class InventoryStatusSystemIds
{
    public const int Available = 1;
    public const int Reserved = 2;
    public const int QualityPending = 3;
    public const int Quarantine = 4;
    public const int Hold = 5;
    public const int Damaged = 6;
    public const int Expired = 7;
    public const int ReturnPending = 8;
    public const int ScrapPending = 9;
}

public enum InventoryStatusMovementLeg
{
    Outbound = 1,
    Inbound = 2
}

public sealed record InventoryStatusSeedDefinition(
    int Id,
    string Code,
    string Name,
    string LocalizedName,
    bool IsAvailable,
    bool IsAllocatable,
    bool IsPickable,
    bool IsShippable,
    bool IsCountable,
    LocationType? DefaultLocationType = null,
    bool ForceForLocationType = false);

public sealed record InventoryStatusTransitionSeedDefinition(
    int Id,
    int FromStatusId,
    int ToStatusId,
    bool RequiresReason = true);

public static class InventoryStatusCatalog
{
    public static IReadOnlyList<InventoryStatusSeedDefinition> SystemStatuses { get; } =
    [
        new(
            InventoryStatusSystemIds.Available,
            InventoryStatusCodes.Available,
            "Available",
            "متاح",
            IsAvailable: true,
            IsAllocatable: true,
            IsPickable: true,
            IsShippable: true,
            IsCountable: true),
        new(
            InventoryStatusSystemIds.Reserved,
            InventoryStatusCodes.Reserved,
            "Reserved",
            "محجوز",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: true,
            IsShippable: true,
            IsCountable: true),
        new(
            InventoryStatusSystemIds.QualityPending,
            InventoryStatusCodes.QualityPending,
            "QC Pending",
            "قيد الفحص",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true),
        new(
            InventoryStatusSystemIds.Quarantine,
            InventoryStatusCodes.Quarantine,
            "Quarantine",
            "عزل",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true,
            DefaultLocationType: LocationType.Quarantine,
            ForceForLocationType: true),
        new(
            InventoryStatusSystemIds.Hold,
            InventoryStatusCodes.Hold,
            "Hold",
            "تعليق",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true),
        new(
            InventoryStatusSystemIds.Damaged,
            InventoryStatusCodes.Damaged,
            "Damaged",
            "تالف",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true,
            DefaultLocationType: LocationType.Damaged,
            ForceForLocationType: true),
        new(
            InventoryStatusSystemIds.Expired,
            InventoryStatusCodes.Expired,
            "Expired",
            "منتهي",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true),
        new(
            InventoryStatusSystemIds.ReturnPending,
            InventoryStatusCodes.ReturnPending,
            "Return Pending",
            "مرتجع قيد المعالجة",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true,
            DefaultLocationType: LocationType.Returns,
            ForceForLocationType: true),
        new(
            InventoryStatusSystemIds.ScrapPending,
            InventoryStatusCodes.ScrapPending,
            "Scrap Pending",
            "قيد الإهلاك",
            IsAvailable: false,
            IsAllocatable: false,
            IsPickable: false,
            IsShippable: false,
            IsCountable: true)
    ];

    public static IReadOnlyList<InventoryStatusTransitionSeedDefinition> SystemTransitions { get; } =
    [
        new(1, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.Reserved),
        new(2, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.QualityPending),
        new(3, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.Quarantine),
        new(4, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.Hold),
        new(5, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.Damaged),
        new(6, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.Expired),
        new(7, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.ReturnPending),
        new(8, InventoryStatusSystemIds.Available, InventoryStatusSystemIds.ScrapPending),
        new(9, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.Available),
        new(10, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.Hold),
        new(11, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.Quarantine),
        new(12, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.Damaged),
        new(13, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.Expired),
        new(14, InventoryStatusSystemIds.Reserved, InventoryStatusSystemIds.ScrapPending),
        new(15, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.Available),
        new(16, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.Quarantine),
        new(17, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.Hold),
        new(18, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.Damaged),
        new(19, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.Expired),
        new(20, InventoryStatusSystemIds.QualityPending, InventoryStatusSystemIds.ScrapPending),
        new(21, InventoryStatusSystemIds.Quarantine, InventoryStatusSystemIds.Available),
        new(22, InventoryStatusSystemIds.Quarantine, InventoryStatusSystemIds.Hold),
        new(23, InventoryStatusSystemIds.Quarantine, InventoryStatusSystemIds.Damaged),
        new(24, InventoryStatusSystemIds.Quarantine, InventoryStatusSystemIds.Expired),
        new(25, InventoryStatusSystemIds.Quarantine, InventoryStatusSystemIds.ScrapPending),
        new(26, InventoryStatusSystemIds.Hold, InventoryStatusSystemIds.Available),
        new(27, InventoryStatusSystemIds.Hold, InventoryStatusSystemIds.Quarantine),
        new(28, InventoryStatusSystemIds.Hold, InventoryStatusSystemIds.Damaged),
        new(29, InventoryStatusSystemIds.Hold, InventoryStatusSystemIds.Expired),
        new(30, InventoryStatusSystemIds.Hold, InventoryStatusSystemIds.ScrapPending),
        new(31, InventoryStatusSystemIds.Damaged, InventoryStatusSystemIds.ScrapPending),
        new(32, InventoryStatusSystemIds.Expired, InventoryStatusSystemIds.ScrapPending),
        new(33, InventoryStatusSystemIds.ReturnPending, InventoryStatusSystemIds.Available),
        new(34, InventoryStatusSystemIds.ReturnPending, InventoryStatusSystemIds.Quarantine),
        new(35, InventoryStatusSystemIds.ReturnPending, InventoryStatusSystemIds.Hold),
        new(36, InventoryStatusSystemIds.ReturnPending, InventoryStatusSystemIds.Damaged),
        new(37, InventoryStatusSystemIds.ReturnPending, InventoryStatusSystemIds.ScrapPending)
    ];
}
