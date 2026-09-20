namespace Wms.Domain.Services;

/// <summary>
/// A recoverable optimistic-concurrency conflict at a persistence boundary.
/// The message is safe for a user-facing retry response; resource details are
/// retained separately for structured logs and diagnostics.
/// </summary>
public sealed class ConcurrencyConflictException : InvalidOperationException
{
    public ConcurrencyConflictException(
        string resourceType,
        string resourceId,
        Exception? innerException = null)
        : base("The record changed while you were working. Reload it and try again.", innerException)
    {
        ResourceType = string.IsNullOrWhiteSpace(resourceType) ? "record" : resourceType;
        ResourceId = string.IsNullOrWhiteSpace(resourceId) ? "unknown" : resourceId;
        Code = "data.concurrency_conflict";
    }

    public string Code { get; }
    public string ResourceType { get; }
    public string ResourceId { get; }
}
