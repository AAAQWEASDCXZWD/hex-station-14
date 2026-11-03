using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._NF.Shipyard.Components;
using Content.Shared._Hex.Shipyard.Components;
using Content.Shared._Hex.Shipyard.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Database;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;

    /// <summary>
    /// Safety caps so a malformed or extremely large grid cannot create an unbounded networked blueprint.
    /// </summary>
    private const int MaxBlueprintTiles = 30000;
    private const int MaxBlueprintEntities = 6000;

    private static readonly HashSet<string> NonVisibleBlueprintComponents = new(StringComparer.Ordinal)
    {
        "Marker",
        "SpawnPoint",
        "ContainerSpawnPoint",
        "RandomSpawner",
        "ConditionalSpawner",
        "TimedSpawner",
        "EntityTableSpawner",
        "RandomHumanoidSpawner",
        "AntagSpawner",
        "GhostRoleMobSpawner",
        "GhostRoleAntagSpawner",
        "StationDeedSpawner",
        "AtmosFixMarker",
        "WallReplacementMarker",
        "SolarPanelReplacementMarker",
        "ReinforcedWallReplacementMarker",
        "RecallMarker",
        "DamageMarker",
        "AdminFrozen",
        "AdminMinigun",
    };

    private void OnPrintBlueprintMessage(EntityUid uid, ShipyardConsoleComponent component, ShipyardConsolePrintBlueprintMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.TargetIdSlot.ContainerSlot?.ContainedEntity is not { Valid: true } targetId)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-idcard"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (!HasCaptainAccess(player, targetId))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-storage-captain-access-required"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-deed"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (!TryGetEntity(args.ShuttleUid, out var shuttleUid) || !shuttleUid.Value.Valid)
            return;

        if (!IsShuttleOwnedByDeed(deed, shuttleUid.Value))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-no-deed"));
            PlayDenySound(player, uid, component);
            return;
        }

        // Blueprints are only offered for shuttles currently parked in bluespace storage.
        if (!HasComp<ShuttleStoredInBluespaceComponent>(shuttleUid.Value))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-blueprint-not-stored"));
            PlayDenySound(player, uid, component);
            return;
        }

        if (!TryComp<MapGridComponent>(shuttleUid.Value, out var grid))
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-sale-invalid-ship"));
            PlayDenySound(player, uid, component);
            return;
        }

        GridBlueprintData? data = null;
        if (TryComp<ShuttleStoredInBluespaceComponent>(shuttleUid.Value, out var stored)
            && stored.BlueprintData is { } cached
            && (cached.Entities.Count > 0 || cached.Tiles.Count == 0))
        {
            // Prefer the snapshot captured before the shuttle was moved into paused storage.
            data = cached;
        }

        // Missing/empty entity data means the cached snapshot came from an older build or a partial
        // capture, so try the live grid once before giving up.
        data ??= BuildBlueprintData(shuttleUid.Value, grid);

        if (data == null)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-blueprint-too-large"));
            PlayDenySound(player, uid, component);
            return;
        }

        var blueprint = Spawn("NFShuttleBlueprint", Transform(uid).Coordinates);
        var blueprintComponent = EnsureComp<ShipBlueprintComponent>(blueprint);
        blueprintComponent.Data = data;
        blueprintComponent.ShuttleName = Name(shuttleUid.Value);
        Dirty(blueprint, blueprintComponent);

        _metaData.SetEntityName(blueprint, Loc.GetString("shipyard-shuttle-blueprint-name", ("name", blueprintComponent.ShuttleName)));

        ConsolePopup(player, Loc.GetString("shipyard-console-blueprint-printed", ("name", blueprintComponent.ShuttleName)));
        PlayConfirmSound(player, uid, component);

        _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Low,
            $"{ToPrettyString(player):actor} printed a blueprint of stored shuttle {ToPrettyString(shuttleUid.Value)} via {ToPrettyString(uid)}");
    }

    /// <summary>
    /// Snapshots the full grid layout: tiles and every prototype-backed entity on it.
    /// </summary>
    private GridBlueprintData? BuildBlueprintData(EntityUid gridUid, MapGridComponent grid)
    {
        var data = new GridBlueprintData
        {
            TileSize = grid.TileSize,
            Center = grid.LocalAABB.Center,
        };

        foreach (var tileRef in _map.GetAllTiles(gridUid, grid))
        {
            if (tileRef.Tile.IsEmpty)
                continue;

            if (data.Tiles.Count >= MaxBlueprintTiles)
                return null;

            var layer = GridHologramLayer.Floor;
            if (_tileDefinitions.TryGetDefinition(tileRef.Tile.TypeId, out var tileDefinition)
                && tileDefinition is ContentTileDefinition contentTile
                && contentTile.IsSubFloor)
            {
                layer = GridHologramLayer.SubFloor;
            }

            data.Tiles.Add(new GridBlueprintTile
            {
                Indices = tileRef.GridIndices,
                TileId = tileRef.Tile.TypeId,
                Layer = layer,
            });
        }

        if (!CollectBlueprintEntities(gridUid, data))
            return null;

        _sawmill.Info($"Built blueprint data for {ToPrettyString(gridUid)}: {data.Tiles.Count} tiles, {data.Entities.Count} entities.");
        return data;
    }

    /// <summary>
    /// Walks the grid's transform children instead of querying by <see cref="TransformComponent.GridUid"/>.
    /// Entities in detached chunks, containers, or paused storage maps can have stale grid ids, but they
    /// are still parented under the grid and must be part of the blueprint.
    /// </summary>
    private bool CollectBlueprintEntities(EntityUid gridUid, GridBlueprintData data)
    {
        var gridXform = Transform(gridUid);
        var added = new HashSet<EntityUid>();

        // Only direct children of the grid are part of the schematic. Entities nested inside
        // containers (cabinets, vendors, etc.) must not be projected separately.
        var rootChildren = gridXform.ChildEnumerator;
        while (rootChildren.MoveNext(out var child))
        {
            if (!TryAddBlueprintEntity(gridUid, gridXform, child, data, added))
                return false;
        }

        // Fallback for entities that are parented elsewhere but still belong directly to this grid.
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var entity, out var xform, out var meta))
        {
            if (entity == gridUid
                || xform.ParentUid != gridUid
                || meta.EntityPrototype == null
                || HasComp<MapGridComponent>(entity))
            {
                continue;
            }

            if (!TryAddBlueprintEntity(gridUid, gridXform, entity, data, added))
                return false;
        }

        return true;
    }

    private bool TryAddBlueprintEntity(
        EntityUid gridUid,
        TransformComponent gridXform,
        EntityUid entity,
        GridBlueprintData data,
        HashSet<EntityUid> added)
    {
        if (!added.Add(entity) || TerminatingOrDeleted(entity))
            return true;

        // Nested grids are separate grids and are not part of this blueprint.
        if (HasComp<MapGridComponent>(entity))
            return true;

        if (!TryComp<MetaDataComponent>(entity, out var meta) || meta.EntityPrototype == null)
            return true;

        if (!IsVisibleBlueprintPrototype(meta.EntityPrototype))
            return true;

        if (data.Entities.Count >= MaxBlueprintEntities)
            return false;

        var xform = Transform(entity);
        var local = _transform.WithEntityId(xform.Coordinates, gridUid);
        var rotation = (float) (_transform.GetWorldRotation(xform) - _transform.GetWorldRotation(gridXform)).Theta;

        // The client uses the prototype's actual sprite draw depth when it has layers. This
        // fallback keeps walls above floor-level objects for icon-only prototypes.
        var layer = meta.EntityPrototype.Components.TryGetValue("IconSmooth", out _)
            ? GridHologramLayer.Walls
            : GridHologramLayer.Objects;

        data.Entities.Add(new GridBlueprintEntity
        {
            Prototype = meta.EntityPrototype.ID,
            Position = local.Position,
            Rotation = rotation,
            Layer = layer,
        });

        return true;
    }

    /// <summary>
    /// Filters out markers, spawners, debug-only entities, and other prototypes that are not part of
    /// the visible ship a regular player would see.
    /// </summary>
    private static bool IsVisibleBlueprintPrototype(EntityPrototype prototype)
    {
        if (prototype.Categories.Any(category => category.ID == "Debug"))
            return false;

        foreach (var component in prototype.Components.Keys)
        {
            if (NonVisibleBlueprintComponents.Contains(component))
                return false;
        }

        // Fallback for prototypes that are named as spawn helpers but do not use one of the
        // component types above.
        return !prototype.ID.Contains("Spawner", StringComparison.OrdinalIgnoreCase)
            && !prototype.ID.Contains("SpawnPoint", StringComparison.OrdinalIgnoreCase)
            && !prototype.ID.Contains("Debug", StringComparison.OrdinalIgnoreCase);
    }
}
