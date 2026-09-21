using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ReturnCommand : Entity
{
    private ReturnCommand()
    {
    }

    public ReturnCommand(
        int returnAuthorizationId,
        string operation,
        string idempotencyKey,
        string requestHash,
        string userId,
        DateTime executedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnAuthorizationId);
        ReturnAuthorizationId = returnAuthorizationId;
        Operation = Required(operation, 100, nameof(operation));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        RequestHash = Required(requestHash, 64, nameof(requestHash));
        UserId = Required(userId, 450, nameof(userId));
        ExecutedAtUtc = DateTime.SpecifyKind(executedAtUtc, DateTimeKind.Utc);
    }

    public int ReturnAuthorizationId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public DateTime ExecutedAtUtc { get; private set; }

    public ReturnAuthorization ReturnAuthorization { get; private set; } = null!;

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
}
