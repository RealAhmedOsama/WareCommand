// Wms.Domain/ValueObjects/Quantity.cs

using System.Globalization;
using Wms.Domain.Common;

namespace Wms.Domain.ValueObjects;

public class Quantity : ValueObject, IComparable<Quantity>, IEquatable<Quantity>
{
    public static readonly Quantity Zero = new(0);

    public Quantity(decimal value)
    {
        if (value < 0)
            throw new ArgumentException("Quantity cannot be negative", nameof(value));

        Value = Math.Round(value, 4);
    }

    public decimal Value { get; }

    public int CompareTo(Quantity? other)
    {
        if (other is null) return 1;
        return Value.CompareTo(other.Value);
    }

    public bool Equals(Quantity? other)
    {
        return other is not null && Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is Quantity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString()
    {
        return Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static implicit operator decimal(Quantity quantity)
    {
        return quantity.Value;
    }

    public static implicit operator Quantity(decimal value)
    {
        return new Quantity(value);
    }

    public static Quantity operator +(Quantity left, Quantity right)
    {
        return new Quantity(left.Value + right.Value);
    }

    public static Quantity operator -(Quantity left, Quantity right)
    {
        var result = left.Value - right.Value;
        if (result < 0)
            throw new ArgumentException("Operation would result in negative quantity");
        return new Quantity(result);
    }

    public static Quantity operator *(Quantity left, decimal right)
    {
        if (right < 0)
            throw new ArgumentException("Multiplier cannot be negative", nameof(right));
        return new Quantity(left.Value * right);
    }

    public static bool operator ==(Quantity? left, Quantity? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(Quantity? left, Quantity? right)
    {
        return !(left == right);
    }

    public static bool operator >(Quantity left, Quantity right)
    {
        return left.Value > right.Value;
    }

    public static bool operator <(Quantity left, Quantity right)
    {
        return left.Value < right.Value;
    }

    public static bool operator >=(Quantity left, Quantity right)
    {
        return left.Value >= right.Value;
    }

    public static bool operator <=(Quantity left, Quantity right)
    {
        return left.Value <= right.Value;
    }
}
