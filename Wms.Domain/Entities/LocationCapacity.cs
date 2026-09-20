namespace Wms.Domain.Entities;

public sealed record LocationCapacitySnapshot(
    decimal Units,
    decimal WeightKg = 0,
    decimal VolumeCubicMeters = 0,
    int Pallets = 0,
    int Lpns = 0);

public sealed record LocationCapacityViolation(
    string Code,
    string Message);
