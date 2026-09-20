namespace Wms.ASP.Health;

public sealed class WmsApplicationHealthState
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public void MarkStarting() => Volatile.Write(ref _ready, 0);

    public void MarkReady() => Volatile.Write(ref _ready, 1);

    public void MarkFailed() => Volatile.Write(ref _ready, 0);
}
