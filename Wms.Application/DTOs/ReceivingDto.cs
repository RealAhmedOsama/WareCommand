// Wms.Application/DTOs/ReceivingDto.cs

namespace Wms.Application.DTOs;

public record ReceiveItemDto(
    string ItemSku,
    string LocationCode,
    decimal Quantity,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? ReferenceNumber = null,
    string? Notes = null,
    DateTime? ExpiryDate = null,
    DateTime? ManufacturedDate = null,
    string? UnitOfMeasure = null,
    string? PackagingCode = null
);

public record PutawayDto(
    string ItemSku,
    string FromLocationCode,
    string ToLocationCode,
    decimal Quantity,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? Notes = null,
    string? UnitOfMeasure = null,
    string? PackagingCode = null
);

public record ReceiptResultDto(
    int MovementId,
    string ItemSku,
    string LocationCode,
    decimal Quantity,
    string? LotNumber,
    DateTime Timestamp
);
