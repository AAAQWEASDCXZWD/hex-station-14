using Content.Shared._Hex.Mining;
using Robust.Shared.Timing;

namespace Content.Client._Hex.Mining;

public sealed class ShuttleOreScannerClientSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _lastRequestTime = TimeSpan.Zero;
    private static readonly TimeSpan RequestThrottle = TimeSpan.FromSeconds(1.5);

    public Dictionary<NetEntity, List<OreBlipData>> CachedBlips { get; private set; } = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GiveOreBlipsEvent>(OnGiveOreBlips);
    }

    private void OnGiveOreBlips(GiveOreBlipsEvent ev)
    {
        CachedBlips = ev.Blips;
    }

    public void RequestOreBlips(EntityUid consoleUid)
    {
        if (_timing.CurTime - _lastRequestTime < RequestThrottle)
            return;

        _lastRequestTime = _timing.CurTime;
        RaiseNetworkEvent(new RequestOreBlipsEvent(GetNetEntity(consoleUid)));
    }
}
