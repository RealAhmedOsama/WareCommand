using System.Threading;
using Wms.Application.Context;

namespace Wms.Infrastructure.Logging;

public sealed class WmsOperationContextAccessor : IWmsOperationContextAccessor
{
    private readonly AsyncLocal<WmsOperationContext?> _current = new();

    public WmsOperationContext? Current => _current.Value;

    public IDisposable Begin(WmsOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = _current.Value;
        _current.Value = context;
        return new Scope(() => _current.Value = previous);
    }

    private sealed class Scope(Action disposeAction) : IDisposable
    {
        private Action? _disposeAction = disposeAction;

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposeAction, null)?.Invoke();
        }
    }
}
