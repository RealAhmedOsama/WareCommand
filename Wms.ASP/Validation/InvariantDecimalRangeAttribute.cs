using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Wms.ASP.Validation;

[AttributeUsage(
    AttributeTargets.Field |
    AttributeTargets.Parameter |
    AttributeTargets.Property,
    AllowMultiple = false)]
public sealed class InvariantDecimalRangeAttribute : ValidationAttribute
{
    private readonly decimal _minimum;
    private readonly decimal _maximum;

    public InvariantDecimalRangeAttribute(string minimum, string maximum)
    {
        _minimum = decimal.Parse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture);
        _maximum = decimal.Parse(maximum, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (_minimum > _maximum)
        {
            throw new ArgumentException("The minimum decimal range must not exceed the maximum.");
        }
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not decimal decimalValue || decimalValue < _minimum || decimalValue > _maximum)
        {
            return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
        }

        return ValidationResult.Success;
    }

    public override string FormatErrorMessage(string name) =>
        string.IsNullOrWhiteSpace(ErrorMessage)
            ? string.Format(
                CultureInfo.CurrentCulture,
                "{0} must be between {1} and {2}.",
                name,
                _minimum.ToString(CultureInfo.InvariantCulture),
                _maximum.ToString(CultureInfo.InvariantCulture))
            : base.FormatErrorMessage(name);
}
