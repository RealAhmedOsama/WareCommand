using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WarehouseWorkActivity : Entity
{
    private WarehouseWorkActivity()
    {
    }

    public WarehouseWorkActivity(
        int warehouseId,
        int warehouseWorkId,
        string workerUserId,
        WarehouseWorkActivityCategory category,
        DateTime startedAtUtc,
        DateTime? endedAtUtc,
        string? reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseWorkId);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        var start = NormalizeUtc(startedAtUtc);
        DateTime? end = endedAtUtc.HasValue ? NormalizeUtc(endedAtUtc.Value) : null;
        if (end.HasValue && end.Value < start)
        {
            throw new ArgumentException("An activity cannot end before it starts.", nameof(endedAtUtc));
        }

        WarehouseId = warehouseId;
        WarehouseWorkId = warehouseWorkId;
        WorkerUserId = Required(workerUserId, 450, nameof(workerUserId));
        Category = category;
        StartedAtUtc = start;
        EndedAtUtc = end;
        Reason = Optional(reason, 1_000);
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public int WarehouseWorkId { get; private set; }
    public string WorkerUserId { get; private set; } = string.Empty;
    public WarehouseWorkActivityCategory Category { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public WarehouseWork Work { get; private set; } = null!;

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }
}
