using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WaveProcessingHistory : Entity
{
    private WaveProcessingHistory()
    {
    }

    public WaveProcessingHistory(
        int waveId,
        WaveStepType step,
        int attempt,
        string idempotencyKey,
        DateTime startedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(waveId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);
        if (!Enum.IsDefined(step))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        WaveId = waveId;
        Step = step;
        Attempt = attempt;
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        StartedAtUtc = NormalizeUtc(startedAtUtc);
        Status = WaveStepStatus.Started;
    }

    public int WaveId { get; private set; }
    public WaveStepType Step { get; private set; }
    public int Attempt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public WaveStepStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public int ItemsExamined { get; private set; }
    public int ItemsSucceeded { get; private set; }
    public int ItemsFailed { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? DetailsJson { get; private set; }

    public Wave Wave { get; private set; } = null!;

    public void Complete(
        WaveStepStatus status,
        int itemsExamined,
        int itemsSucceeded,
        int itemsFailed,
        DateTime completedAtUtc,
        string? errorMessage = null,
        string? detailsJson = null)
    {
        if (status is not (WaveStepStatus.Succeeded or WaveStepStatus.PartiallySucceeded or WaveStepStatus.Failed or WaveStepStatus.Skipped))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ItemsExamined = Math.Max(0, itemsExamined);
        ItemsSucceeded = Math.Max(0, itemsSucceeded);
        ItemsFailed = Math.Max(0, itemsFailed);
        Status = status;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        ErrorMessage = Optional(errorMessage, 2_000);
        DetailsJson = Optional(detailsJson, 8_000);
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, nameof(value));

}
