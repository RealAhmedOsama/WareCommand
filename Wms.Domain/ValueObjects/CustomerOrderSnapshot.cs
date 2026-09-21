namespace Wms.Domain.ValueObjects;

/// <summary>
/// Scalar customer/destination values copied to a sales order before
/// confirmation. It deliberately has no persistence navigation.
/// </summary>
public sealed record CustomerOrderSnapshot(
    int CustomerId,
    string CustomerCode,
    string CustomerLegalName,
    string? CustomerLocalizedName,
    string? CustomerContactName,
    string? CustomerContactEmail,
    string? CustomerContactPhone,
    int? ShipToAddressId,
    string? ShipToCode,
    string? ShipToRecipientName,
    string? ShipToPhone,
    string? ShipToCountryCode,
    string? ShipToRegion,
    string? ShipToCity,
    string? ShipToPostalCode,
    string? ShipToAddressLine1,
    string? ShipToAddressLine2,
    string? ShipToDeliveryInstructions,
    string? DefaultCarrierCode,
    string? DefaultCarrierServiceCode,
    int Priority,
    string? PackagingProfile,
    string? LabelProfile,
    bool AllowPartialShipment);
