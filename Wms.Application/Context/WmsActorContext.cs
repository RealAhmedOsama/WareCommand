namespace Wms.Application.Context;

public sealed record WmsActor(
    string? UserId,
    string? UserName,
    string? DisplayName);

public static class WmsActorContext
{
    private static readonly AsyncLocal<WmsActor?> CurrentActor = new();

    public static WmsActor? Current => CurrentActor.Value;

    public static IDisposable Begin(WmsActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var previous = CurrentActor.Value;
        CurrentActor.Value = actor;
        return new Scope(() => CurrentActor.Value = previous);
    }

    private sealed class Scope(Action disposeAction) : IDisposable
    {
        private Action? _disposeAction = disposeAction;

        public void Dispose() => Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
    }
}
