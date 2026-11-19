using Content.Shared._Hex.Mining;
using Content.Shared.Mining.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Hex.Mining;

public sealed class AsteroidOreDataSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, EntityTerminatingEvent>(OnOreTerminating);
    }

    private void OnOreTerminating(EntityUid uid, OreVeinComponent component, ref EntityTerminatingEvent args)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid)
            return;

        if (!TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<AsteroidOreDataComponent>(gridUid, out var oreData))
            return;

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        oreData.Ores.Remove(indices);
    }
}
