using System.Linq;
using System.Numerics;
using Content.Client.Clickable;
using Content.Client.Construction;
using Content.Client.IconSmoothing;
using Content.Client.Interactable.Components;
using Content.Shared._Hex.Shipyard.Components;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Hex.Shipyard;

/// <summary>
/// Projects the grid stored in a <see cref="ShipBlueprintComponent"/> as a client-side hologram.
/// </summary>
/// <remarks>
/// The projection is built out of client-only ghost entities. The grid itself is represented by one
/// hologram tile per source tile, while every prototype-backed object on the grid gets its own ghost
/// sprite. The root can be parented to a grid or directly to a map, so the blueprint can be projected
/// in open space as well.
/// </remarks>
public sealed class ShipBlueprintHologramSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private const string GhostPrototype = "constructionghost";
    private const string HologramTileDefinition = "HologramFloor";

    private EntityUid? _activeHologram;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipBlueprintComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShipBlueprintComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<GridHologramComponent, ComponentShutdown>(OnHologramShutdown);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ToggleShipBlueprintHologram,
                InputCmdHandler.FromDelegate(_ => ToggleHologram()))
            .Register<ShipBlueprintHologramSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<ShipBlueprintHologramSystem>();
    }

    private void ToggleHologram()
    {
        if (_activeHologram is not { } root
            || Deleted(root)
            || !TryComp<GridHologramComponent>(root, out var hologram))
        {
            return;
        }

        hologram.Visible = !hologram.Visible;

        if (hologram.TileGrid is { } tileGridUid
            && !Deleted(tileGridUid)
            && TryComp<MapGridComponent>(tileGridUid, out var tileGrid))
        {
            var tile = hologram.Visible ? hologram.HologramTile : Tile.Empty;
            var updates = new List<(Vector2i Indices, Tile Tile)>(hologram.TileIndices.Count);
            foreach (var index in hologram.TileIndices)
                updates.Add((index, tile));

            _map.SetTiles(tileGridUid, tileGrid, updates);
        }

        foreach (var ghost in hologram.Ghosts)
        {
            if (!Deleted(ghost) && TryComp<SpriteComponent>(ghost, out var sprite))
                sprite.Visible = hologram.Visible;
        }
    }

    private void OnUseInHand(Entity<ShipBlueprintComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var mapCoordinates = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mapCoordinates.MapId == MapId.Nullspace)
        {
            args.Handled = true;
            return;
        }

        EntityCoordinates coordinates;
        if (_mapManager.TryFindGridAt(mapCoordinates, out var gridUid, out var grid))
        {
            var tile = _map.CoordinatesToTile(gridUid, grid, mapCoordinates);
            coordinates = _map.GridTileToLocal(gridUid, grid, tile);
        }
        else
        {
            // Open space: the projection root can sit directly on the map.
            coordinates = _transform.ToCoordinates(mapCoordinates);
        }

        var direction = Transform(args.User).LocalRotation.GetCardinalDir();

        PlaceHologram(ent, coordinates, direction);
        args.Handled = true;
    }

    /// <summary>
    /// Clicking with the blueprint projects it at the clicked location, snapping to a tile when the
    /// click lands on a grid.
    /// </summary>
    private void OnAfterInteract(Entity<ShipBlueprintComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !args.ClickLocation.IsValid(EntityManager))
            return;

        EntityCoordinates coordinates;
        if (_transform.GetGrid(args.ClickLocation) is { } grid
            && TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            var tile = _map.CoordinatesToTile(grid, mapGrid, args.ClickLocation);
            coordinates = _map.GridTileToLocal(grid, mapGrid, tile);
        }
        else
        {
            coordinates = _transform.ToCoordinates(_transform.ToMapCoordinates(args.ClickLocation));
        }

        var direction = Transform(args.User).LocalRotation.GetCardinalDir();

        PlaceHologram(ent, coordinates, direction);
        args.Handled = true;
    }

    /// <summary>
    /// Replaces the current hologram with a projection of the blueprint at the given map location.
    /// </summary>
    public void PlaceHologram(Entity<ShipBlueprintComponent> blueprint, EntityCoordinates coordinates, Direction direction)
    {
        if (!coordinates.IsValid(EntityManager))
            return;

        var data = blueprint.Comp.Data;
        if (data.Tiles.Count == 0 && data.Entities.Count == 0)
        {
            Log.Warning("Ship blueprint has no tile or entity data.");
            return;
        }

        Log.Info($"Projecting ship blueprint with {data.Tiles.Count} tiles and {data.Entities.Count} entities.");

        if (!_tileDefinitions.TryGetDefinition(HologramTileDefinition, out var definition)
            || definition is not ContentTileDefinition hologramTileDefinition)
        {
            Log.Error($"Missing {HologramTileDefinition} tile definition.");
            return;
        }

        var mapId = _transform.GetMapId(coordinates);
        if (mapId == MapId.Nullspace)
            return;

        var worldPosition = _transform.ToMapCoordinates(coordinates).Position;
        var localRotation = direction.ToAngle();
        var parentRotation = _transform.GetGrid(coordinates) is { } targetGrid
            ? _transform.GetWorldRotation(targetGrid)
            : Angle.Zero;
        var worldRotation = parentRotation + localRotation;

        if (_activeHologram is { } old && !Deleted(old))
            QueueDel(old);

        // One client-side grid renders the entire tile layer in chunks. Spawning a separate sprite
        // entity per tile is what was tanking the framerate on large shuttles.
        var tileGrid = _mapManager.CreateGrid(mapId);
        var tileGridUid = tileGrid.Owner;
        _transform.SetWorldRotation(tileGridUid, worldRotation);
        _transform.SetWorldPosition(tileGridUid, worldPosition - worldRotation.RotateVec(data.Center));

        var hologramTile = new Tile(hologramTileDefinition.TileId);
        var tiles = new List<(Vector2i Indices, Tile Tile)>(data.Tiles.Count);
        var tileIndices = new List<Vector2i>(data.Tiles.Count);
        foreach (var sourceTile in data.Tiles)
        {
            tiles.Add((sourceTile.Indices, hologramTile));
            tileIndices.Add(sourceTile.Indices);
        }

        _map.SetTiles(tileGridUid, tileGrid, tiles);

        // The root remains the client-side object/cleanup carrier for the entity ghosts.
        var root = Spawn(GhostPrototype, coordinates);
        _activeHologram = root;

        var rootXform = Transform(root);
        rootXform.LocalRotation = localRotation;

        var hologram = EnsureComp<GridHologramComponent>(root);
        hologram.Ghosts.Clear();
        hologram.TileGrid = tileGridUid;
        hologram.TileIndices.Clear();
        hologram.TileIndices.AddRange(tileIndices);
        hologram.HologramTile = hologramTile;

        RemoveInteractionComponents(root);

        foreach (var source in data.Entities)
        {
            if (!_prototype.TryIndex(source.Prototype, out EntityPrototype? prototype))
                continue;

            var ghost = SpawnGhost(root, source.Position - data.Center, GhostPrototype);
            if (ghost == null)
                continue;

            var ghostXform = Transform(ghost.Value);
            var ghostGrid = _transform.GetGrid(ghost.Value);
            var hasSmooth = prototype.TryGetComponent(out IconSmoothComponent? smooth, _componentFactory);

            var sprite = EnsureComp<SpriteComponent>(ghost.Value);
            var hasPrototypeSprite = prototype.TryGetComponent(out SpriteComponent? prototypeSprite, _componentFactory);

            if (hasPrototypeSprite)
            {
                // Copy even a layerless sprite: it still carries the base RSI that IconSmooth needs.
                sprite.CopyFrom(prototypeSprite!);
                sprite.DrawDepth = prototypeSprite!.DrawDepth;
            }
            else
            {
                sprite.DrawDepth = GetDrawDepth(source.Layer);
            }

            var canSmooth = hasSmooth
                && hasPrototypeSprite
                && ghostGrid is { } smoothGrid
                && TryComp<MapGridComponent>(smoothGrid, out _)
                && smooth!.SmoothKey == "walls"
                && !string.IsNullOrEmpty(smooth.StateBase)
                && smooth.Mode is IconSmoothingMode.Corners or IconSmoothingMode.Diagonal;

            if (!sprite.AllLayers.Any())
            {
                if (canSmooth && smooth!.Mode == IconSmoothingMode.Corners)
                {
                    // IconSmooth will create the corner layers using the copied base RSI.
                }
                else
                {
                    if (!prototype.TryGetComponent(out IconComponent? icon, _componentFactory))
                    {
                        QueueDel(ghost.Value);
                        continue;
                    }

                    // Many structural prototypes (walls, windows, etc.) only get their layers from
                    // IconSmooth at runtime. The prototype icon is a reliable static stand-in when
                    // the ghost cannot participate in smoothing (e.g. it is projected in open space).
                    _sprite.AddBlankLayer((ghost.Value, sprite), 0);
                    _sprite.LayerSetSprite((ghost.Value, sprite), 0, icon.Icon);
                    _sprite.LayerSetVisible((ghost.Value, sprite), 0, true);
                }
            }

            sprite.Color = Color.White;
            sprite.Visible = true;

            if (hasSmooth && canSmooth)
            {
                // Anchor the ghost to the target grid and let the real IconSmooth system choose the
                // connection states, so adjacent wall holograms line up like real walls.
                var ghostWorldPosition = _transform.GetWorldPosition(ghostXform);
                var ghostWorldRotation = _transform.GetWorldRotation(ghostXform);
                _transform.AnchorEntity((ghost.Value, ghostXform));
                _transform.SetWorldPosition(ghost.Value, ghostWorldPosition);
                _transform.SetWorldRotation(ghost.Value, ghostWorldRotation);

                var smoothCopy = _serialization.CreateCopy(smooth!, notNullableOverride: true);
                AddComp(ghost.Value, smoothCopy);

                // Safety net for a prototype whose smoothing data cannot produce layers.
                if (!sprite.AllLayers.Any())
                {
                    if (!prototype.TryGetComponent(out IconComponent? icon, _componentFactory))
                    {
                        QueueDel(ghost.Value);
                        continue;
                    }

                    _sprite.AddBlankLayer((ghost.Value, sprite), 0);
                    _sprite.LayerSetSprite((ghost.Value, sprite), 0, icon.Icon);
                    _sprite.LayerSetVisible((ghost.Value, sprite), 0, true);
                }
            }
            else
            {
                ghostXform.LocalRotation = new Angle(source.Rotation);
            }

            SanitizeLayerDirections((ghost.Value, sprite));
            ApplyHologram((ghost.Value, sprite));
            hologram.Ghosts.Add(ghost.Value);
        }
    }

    /// <summary>
    /// Guards against one-direction RSI states receiving a directional layer offset, which would
    /// make <see cref="SpriteSystem"/> index past the state's frame array while rendering.
    /// </summary>
    private void SanitizeLayerDirections(Entity<SpriteComponent> sprite)
    {
        foreach (var layer in sprite.Comp.AllLayers.OfType<SpriteComponent.Layer>())
        {
            if (layer.ActualRsi is not { } rsi
                || !rsi.TryGetState(layer.State, out var state)
                || state.RsiDirections != Robust.Shared.Graphics.RSI.RsiDirectionType.Dir1
                || layer.DirOffset == SpriteComponent.DirectionOffset.None)
            {
                continue;
            }

            _sprite.LayerSetDirOffset(layer, SpriteComponent.DirectionOffset.None);
        }
    }

    /// <summary>
    /// Maps the schema's explicit hologram layer order to an engine draw depth.
    /// </summary>
    private static int GetDrawDepth(GridHologramLayer layer)
    {
        return layer switch
        {
            GridHologramLayer.SubFloor => (int) DrawDepth.LowFloors,
            GridHologramLayer.Floor => (int) DrawDepth.FloorTiles,
            GridHologramLayer.FloorObjects => (int) DrawDepth.FloorObjects,
            GridHologramLayer.Objects => (int) DrawDepth.Objects,
            GridHologramLayer.HighObjects => (int) DrawDepth.HighFloorObjects,
            GridHologramLayer.Walls => (int) DrawDepth.Walls,
            GridHologramLayer.Overlay => (int) DrawDepth.Overlays,
            _ => (int) DrawDepth.FloorTiles,
        };
    }

    private EntityUid? SpawnGhost(EntityUid root, Vector2 localPosition, string prototype)
    {
        var ghost = Spawn(prototype, new EntityCoordinates(root, localPosition));
        if (Deleted(ghost))
            return null;

        RemoveInteractionComponents(ghost);
        return ghost;
    }

    private void RemoveInteractionComponents(EntityUid uid)
    {
        // The construction ghost prototype is reused purely as a client-side sprite carrier. Strip the
        // construction behaviour so holograms cannot be "built" or handled by the construction system.
        if (TryComp<ConstructionGhostComponent>(uid, out var constructionGhost))
            constructionGhost.GhostId = int.MinValue;
        EntityManager.RemoveComponentDeferred<ConstructionGhostComponent>(uid);
        EntityManager.RemoveComponentDeferred<ClickableComponent>(uid);
        EntityManager.RemoveComponentDeferred<InteractionOutlineComponent>(uid);
    }

    private void OnHologramShutdown(Entity<GridHologramComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.TileGrid is { } tileGrid && !Deleted(tileGrid))
            QueueDel(tileGrid);

        foreach (var ghost in ent.Comp.Ghosts)
        {
            if (!Deleted(ghost))
                QueueDel(ghost);
        }

        ent.Comp.Ghosts.Clear();
        ent.Comp.TileIndices.Clear();
        ent.Comp.TileGrid = null;

        if (_activeHologram == ent.Owner)
            _activeHologram = null;
    }

    private void ApplyHologram(Entity<SpriteComponent> ent)
    {
        var instance = _prototype.Index<ShaderPrototype>("Hologram").InstanceUnique();
        var texHeight = ent.Comp.AllLayers.Select(l => (float) l.PixelSize.Y).DefaultIfEmpty(1f).Max();
        if (texHeight <= 0f)
            texHeight = 1f;

        // The projection is built from the two blueprint inks: red for the outline and blue for the fill.
        instance.SetParameter("color1", new Vector3(1f, 0.23f, 0.23f));
        instance.SetParameter("color2", new Vector3(0.23f, 0.42f, 1f));
        instance.SetParameter("alpha", 0.6f);
        instance.SetParameter("intensity", 1.5f);
        instance.SetParameter("texHeight", texHeight);
        instance.SetParameter("t", 0f);

        ent.Comp.PostShader = instance;
        ent.Comp.RaiseShaderEvent = true;
    }
}
