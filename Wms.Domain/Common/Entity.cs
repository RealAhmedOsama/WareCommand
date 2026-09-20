// Wms.Domain/Common/Entity.cs

namespace Wms.Domain.Common;

public abstract class Entity
{
    public int Id { get; protected set; }
    // New entities are stamped by the persistence boundary with the injected
    // clock. Keeping the domain default deterministic prevents hidden system
    // time reads during construction.
    public DateTime CreatedAt { get; protected set; } = DateTime.UnixEpoch;
    public DateTime? UpdatedAt { get; protected set; }

    protected void SetUpdatedAt(DateTime? updatedAtUtc = null)
    {
        UpdatedAt = updatedAtUtc.HasValue
            ? NormalizeUtc(updatedAtUtc.Value)
            : DateTime.UnixEpoch;
    }

    internal static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id && Id != 0;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
