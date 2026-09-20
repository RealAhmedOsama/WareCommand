namespace Wms.Domain.Services;

public sealed class LocationConstraintViolationException(
    string code,
    string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
