using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._Hex.Mining;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Hex.Mining;

public sealed class ShuttleOreScannerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private readonly Dictionary<NetUserId, TimeSpan> _nextRequestPerUser = new();
    private static readonly TimeSpan MinRequestPeriod = TimeSpan.FromSeconds(1.0);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestOreBlipsEvent>(OnRequestOreBlips);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _nextRequestPerUser.Clear();
    }

    private void OnRequestOreBlips(RequestOreBlipsEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.ConsoleUid, out var consoleUid) || !Exists(consoleUid))
            return;

        if (_nextRequestPerUser.TryGetValue(args.SenderSession.UserId, out var requestTime) && _timing.RealTime < requestTime)
            return;

        _nextRequestPerUser[args.SenderSession.UserId] = _timing.RealTime + MinRequestPeriod;

        var xform = Transform(consoleUid.Value);
        if (xform.GridUid is not { } shuttleGridUid)
            return;

        // Перевіряємо чи є на цьому шатлі працюючий сканер руди
        if (!TryGetActiveScanner(shuttleGridUid, out var scannerComp))
            return;

        var shuttlePos = _xform.GetWorldPosition(consoleUid.Value);
        var range = scannerComp.Range;
        var queryBox = Box2.CenteredAround(shuttlePos, new Vector2(range * 2f, range * 2f));

        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(xform.MapID, queryBox, ref grids, approx: true, includeMap: false);

        var blips = new Dictionary<NetEntity, List<OreBlipData>>();

        foreach (var grid in grids)
        {
            if (grid.Owner == shuttleGridUid)
                continue;

            if (!TryComp<AsteroidOreDataComponent>(grid.Owner, out var oreData) || oreData.Ores.Count == 0)
                continue;

            var gridWorldPos = _xform.GetWorldPosition(grid.Owner);
            if (Vector2.Distance(gridWorldPos, shuttlePos) > range + 50f)
                continue;

            var list = new List<OreBlipData>(oreData.Ores.Count);
            foreach (var (tile, ore) in oreData.Ores)
            {
                list.Add(new OreBlipData((short)tile.X, (short)tile.Y, ore));
            }

            blips[GetNetEntity(grid.Owner)] = list;
        }

        RaiseNetworkEvent(new GiveOreBlipsEvent(blips), args.SenderSession);
    }

    private bool TryGetActiveScanner(EntityUid shuttleGridUid, out ShuttleOreScannerComponent scanner)
    {
        scanner = default!;
        var query = EntityQueryEnumerator<ShuttleOreScannerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scannerComponent, out var xform))
        {
            if (xform.GridUid != shuttleGridUid)
                continue;

            // Якщо є живлення - перевіряємо чи воно увімкнене
            if (TryComp<ApcPowerReceiverComponent>(uid, out var power) && !power.Powered)
                continue;

            if (!xform.Anchored)
                continue;

            scanner = scannerComponent;
            return true;
        }

        return false;
    }
}
