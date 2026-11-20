using System.Linq;
using System.Numerics;
using Content.Client.Clickable;
using Content.Client.Construction;
using Content.Client.IconSmoothing;
using Content.Client.Interactable.Components;
using Content.Client.Power.Visualizers;
using Content.Shared._Hex.Shipyard.Components;
using Content.Shared._Hex.Shipyard.Events;
using Content.Shared.Examine;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Hex.Shipyard;

/// <summary>
/// Projects the grid stored in a <see cref="ShipBlueprintComponent"/> as a client-side hologram.
/// Supports progressive layer filtering (Tiles -> Wires -> Objects), high-fidelity holographic
/// rendering, and instant client-side switching via right-click (PKM) context menu.
/// </summary>
public sealed class ShipBlueprintHologramSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private const string GhostPrototype = "constructionghost";

    /// <summary>
    /// Soft translucent sci-fi holographic tint applied to sprites to clearly indicate a hologram
    /// projection while preserving all prototype textures, colors, and visual details.
    /// </summary>
    private static readonly Color HologramTint = new(0.92f, 0.96f, 1.0f, 0.82f);

    private static readonly VerbCategory LayerCategory = new(
        "ship-blueprint-verb-category-layers",
        "/Textures/Interface/VerbIcons/settings.svg.192dpi.png");

    private EntityUid? _activeHologram;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipBlueprintComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShipBlueprintComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShipBlueprintComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ShipBlueprintComponent, ExaminedEvent>(OnExamined);
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

    /// <summary>
    /// Adds right-click (PKM) verbs allowing players to configure visible blueprint layers:
    /// Tiles only, Tiles + Wires, or All Objects.
    /// </summary>
    private void OnGetVerbs(Entity<ShipBlueprintComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var comp = ent.Comp;
        var user = args.User;

        foreach (var mode in Enum.GetValues<BlueprintDisplayMode>())
        {
            var targetMode = mode;
            var modeName = GetModeLocaleName(targetMode);
            var isCurrent = comp.Mode == targetMode;
            var label = isCurrent ? $"[✓] {modeName}" : modeName;

            args.Verbs.Add(new Verb
            {
                Category = LayerCategory,
                Text = label,
                Disabled = isCurrent,
                ClientExclusive = true,
                Priority = (int) targetMode,
                Act = () =>
                {
                    SetDisplayMode(ent, targetMode, user);
                }
            });
        }
    }

    private void OnExamined(Entity<ShipBlueprintComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        var modeName = GetModeLocaleName(comp.Mode);

        var wireCount = comp.Data.Entities.Count(e => e.Layer == GridHologramLayer.SubFloor);
        var objectCount = comp.Data.Entities.Count - wireCount;

        args.PushMarkup(Loc.GetString("ship-blueprint-examine-info",
            ("name", string.IsNullOrEmpty(comp.ShuttleName) ? "Unknown" : comp.ShuttleName),
            ("mode", modeName),
            ("tiles", comp.Data.Tiles.Count),
            ("wires", wireCount),
            ("objects", objectCount)));

        args.PushMarkup(Loc.GetString("ship-blueprint-examine-tip"));
    }

    private string GetModeLocaleName(BlueprintDisplayMode mode)
    {
        return mode switch
        {
            BlueprintDisplayMode.Tiles => Loc.GetString("ship-blueprint-layer-tiles"),
            BlueprintDisplayMode.Wires => Loc.GetString("ship-blueprint-layer-wires"),
            BlueprintDisplayMode.Objects => Loc.GetString("ship-blueprint-layer-objects"),
            _ => mode.ToString(),
        };
    }

    /// <summary>
    /// Sets the active blueprint display mode, syncing with the server and updating the active projection.
    /// </summary>
    public void SetDisplayMode(Entity<ShipBlueprintComponent> blueprint, BlueprintDisplayMode mode, EntityUid? user = null)
    {
        blueprint.Comp.Mode = mode;
        RaiseNetworkEvent(new ShipBlueprintSetModeMessage(GetNetEntity(blueprint.Owner), mode));

        if (_activeHologram is { } active
            && !Deleted(active)
            && TryComp<GridHologramComponent>(active, out var hologram))
        {
            ApplyDisplayMode(hologram, mode);
        }

        if (user != null)
        {
            var modeName = GetModeLocaleName(mode);
            _popup.PopupEntity(Loc.GetString("ship-blueprint-mode-changed", ("mode", modeName)), blueprint.Owner, user.Value);
        }
    }

    /// <summary>
    /// Updates ghost entity visibility according to the selected progressive layer mode.
    /// Hierarchy:
    /// - Tiles: Floor tiles visible. Wires hidden. Objects hidden.
    /// - Wires: Floor tiles visible. Wires visible. Objects hidden.
    /// - Objects: Floor tiles visible. Wires visible. Objects visible (all layers shown).
    /// </summary>
    public void ApplyDisplayMode(GridHologramComponent hologram, BlueprintDisplayMode mode)
    {
        hologram.Mode = mode;

        var showWires = hologram.Visible && mode is BlueprintDisplayMode.Wires or BlueprintDisplayMode.Objects;
        var showObjects = hologram.Visible && mode is BlueprintDisplayMode.Objects;

        foreach (var ghost in hologram.WireGhosts)
        {
            if (!Deleted(ghost) && TryComp<SpriteComponent>(ghost, out var sprite))
                sprite.Visible = showWires;
        }

        foreach (var ghost in hologram.ObjectGhosts)
        {
            if (!Deleted(ghost) && TryComp<SpriteComponent>(ghost, out var sprite))
                sprite.Visible = showObjects;
        }
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
            var updates = hologram.Visible ? hologram.CachedTiles : hologram.EmptyTiles;
            _map.SetTiles(tileGridUid, tileGrid, updates);
        }

        ApplyDisplayMode(hologram, hologram.Mode);
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
            coordinates = _transform.ToCoordinates(mapCoordinates);
        }

        var direction = Transform(args.User).LocalRotation.GetCardinalDir();
        PlaceHologram(ent, coordinates, direction);
        args.Handled = true;
    }

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
    /// Projects the blueprint as a client-side holographic representation identical to its spawned state.
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

        // Create the client-side grid to render real ship tiles in fast hardware-accelerated chunks.
        var tileGrid = _mapManager.CreateGrid(mapId);
        var tileGridUid = tileGrid.Owner;
        _transform.SetWorldRotation(tileGridUid, worldRotation);
        _transform.SetWorldPosition(tileGridUid, worldPosition - worldRotation.RotateVec(data.Center));

        var tiles = new List<(Vector2i Indices, Tile Tile)>(data.Tiles.Count);
        var emptyTiles = new List<(Vector2i Indices, Tile Tile)>(data.Tiles.Count);
        foreach (var sourceTile in data.Tiles)
        {
            tiles.Add((sourceTile.Indices, new Tile(sourceTile.TileId)));
            emptyTiles.Add((sourceTile.Indices, Tile.Empty));
        }

        _map.SetTiles(tileGridUid, tileGrid, tiles);

        // Attach GridHologramComponent directly to the grid root for tracking and instant lifecycle management.
        var hologram = EnsureComp<GridHologramComponent>(tileGridUid);
        hologram.SourceBlueprint = blueprint.Owner;
        hologram.TileGrid = tileGridUid;
        hologram.CachedTiles.Clear();
        hologram.CachedTiles.AddRange(tiles);
        hologram.EmptyTiles.Clear();
        hologram.EmptyTiles.AddRange(emptyTiles);
        hologram.WireGhosts.Clear();
        hologram.ObjectGhosts.Clear();
        hologram.AllGhosts.Clear();
        hologram.Mode = blueprint.Comp.Mode;
        hologram.Visible = true;
        _activeHologram = tileGridUid;

        // Precompute cable positions so any cable entity without a recorded wire mask
        // can instantly calculate cardinal connections to neighbor cables.
        var cableTiles = new HashSet<Vector2i>();
        foreach (var entity in data.Entities)
        {
            if (entity.Layer == GridHologramLayer.SubFloor
                || (_prototype.TryIndex(entity.Prototype, out var p)
                    && (p.Components.ContainsKey("Cable") || p.Components.ContainsKey("CableVisualizer"))))
            {
                cableTiles.Add(new Vector2i((int) MathF.Floor(entity.Position.X), (int) MathF.Floor(entity.Position.Y)));
            }
        }

        foreach (var source in data.Entities)
        {
            if (!_prototype.TryIndex(source.Prototype, out EntityPrototype? prototype))
                continue;

            // Spawn the client-side ghost parented directly to tileGridUid at its exact grid coordinates.
            var ghost = Spawn(GhostPrototype, new EntityCoordinates(tileGridUid, source.Position));
            if (Deleted(ghost))
                continue;

            RemoveInteractionComponents(ghost);

            var ghostXform = Transform(ghost);
            var sprite = EnsureComp<SpriteComponent>(ghost);
            var hasPrototypeSprite = prototype.TryGetComponent(out SpriteComponent? prototypeSprite, _componentFactory);
            var hasSmooth = prototype.TryGetComponent(out IconSmoothComponent? smooth, _componentFactory);

            var isWire = source.Layer == GridHologramLayer.SubFloor
                || prototype.Components.ContainsKey("Cable")
                || prototype.Components.ContainsKey("CableVisualizer")
                || prototype.Components.ContainsKey("SubFloorHide")
                || prototype.Components.ContainsKey("PipeAppearance")
                || prototype.Components.ContainsKey("AtmosPipeLayers");

            if (isWire)
            {
                // Wire / Cable handling: restore directional connected states
                if (prototype.TryGetComponent(out CableVisualizerComponent? cableVis, _componentFactory))
                {
                    var mask = source.WireMask;
                    if (mask == 0)
                    {
                        var tilePos = new Vector2i((int) MathF.Floor(source.Position.X), (int) MathF.Floor(source.Position.Y));
                        if (cableTiles.Contains(tilePos + new Vector2i(0, 1))) mask |= (byte) WireVisDirFlags.North;
                        if (cableTiles.Contains(tilePos + new Vector2i(0, -1))) mask |= (byte) WireVisDirFlags.South;
                        if (cableTiles.Contains(tilePos + new Vector2i(1, 0))) mask |= (byte) WireVisDirFlags.East;
                        if (cableTiles.Contains(tilePos + new Vector2i(-1, 0))) mask |= (byte) WireVisDirFlags.West;
                    }

                    if (hasPrototypeSprite)
                        sprite.CopyFrom(prototypeSprite!);

                    if (!sprite.AllLayers.Any())
                        _sprite.AddBlankLayer((ghost, sprite), 0);

                    _sprite.LayerSetRsiState((ghost, sprite), 0, $"{cableVis.StatePrefix}{mask}");
                    if (cableVis.ExtraLayerPrefix != null)
                    {
                        _sprite.AddBlankLayer((ghost, sprite), 1);
                        _sprite.LayerSetRsiState((ghost, sprite), 1, $"{cableVis.ExtraLayerPrefix}{mask}");
                    }

                    _sprite.LayerSetVisible((ghost, sprite), 0, true);
                    sprite.DrawDepth = (int) DrawDepth.FloorObjects;
                }
                else if (hasPrototypeSprite)
                {
                    sprite.CopyFrom(prototypeSprite!);
                    sprite.DrawDepth = prototypeSprite!.DrawDepth;
                }
                else if (prototype.TryGetComponent(out IconComponent? icon, _componentFactory))
                {
                    _sprite.AddBlankLayer((ghost, sprite), 0);
                    _sprite.LayerSetSprite((ghost, sprite), 0, icon.Icon);
                    _sprite.LayerSetVisible((ghost, sprite), 0, true);
                    sprite.DrawDepth = (int) DrawDepth.FloorObjects;
                }

                ghostXform.LocalRotation = new Angle(source.Rotation);
                hologram.WireGhosts.Add(ghost);
            }
            else
            {
                // Structural / Object handling
                if (hasPrototypeSprite)
                {
                    sprite.CopyFrom(prototypeSprite!);
                    sprite.DrawDepth = prototypeSprite!.DrawDepth;
                }
                else
                {
                    sprite.DrawDepth = GetDrawDepth(source.Layer);
                }

                var canSmooth = hasSmooth
                    && hasPrototypeSprite
                    && smooth!.SmoothKey == "walls"
                    && !string.IsNullOrEmpty(smooth.StateBase)
                    && smooth.Mode is IconSmoothingMode.Corners or IconSmoothingMode.Diagonal;

                if (!sprite.AllLayers.Any())
                {
                    if (!(canSmooth && smooth!.Mode == IconSmoothingMode.Corners))
                    {
                        if (prototype.TryGetComponent(out IconComponent? icon, _componentFactory))
                        {
                            _sprite.AddBlankLayer((ghost, sprite), 0);
                            _sprite.LayerSetSprite((ghost, sprite), 0, icon.Icon);
                            _sprite.LayerSetVisible((ghost, sprite), 0, true);
                        }
                    }
                }

                if (hasSmooth && canSmooth)
                {
                    // Anchoring to tileGridUid connects walls seamlessly on this client-side grid!
                    _transform.AnchorEntity((ghost, ghostXform));
                    var smoothCopy = _serialization.CreateCopy(smooth!, notNullableOverride: true);
                    AddComp(ghost, smoothCopy);
                }
                else
                {
                    ghostXform.LocalRotation = new Angle(source.Rotation);
                }

                hologram.ObjectGhosts.Add(ghost);
            }

            SanitizeLayerDirections((ghost, sprite));
            ApplyHologramVisuals((ghost, sprite));
            hologram.AllGhosts.Add(ghost);
        }

        // Apply current display mode layers immediately
        ApplyDisplayMode(hologram, blueprint.Comp.Mode);
    }

    /// <summary>
    /// Applies the unshaded holographic visual style to a ghost sprite.
    /// Uses engine-batched unshaded shader to avoid unique shader instance allocations.
    /// </summary>
    private void ApplyHologramVisuals(Entity<SpriteComponent> ent)
    {
        ent.Comp.Color = HologramTint;
        for (var i = 0; i < ent.Comp.AllLayers.Count(); i++)
        {
            ent.Comp.LayerSetShader(i, "unshaded");
        }
    }

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

    private static int GetDrawDepth(GridHologramLayer layer)
    {
        return layer switch
        {
            GridHologramLayer.SubFloor => (int) DrawDepth.FloorObjects,
            GridHologramLayer.Floor => (int) DrawDepth.FloorTiles,
            GridHologramLayer.FloorObjects => (int) DrawDepth.FloorObjects,
            GridHologramLayer.Objects => (int) DrawDepth.Objects,
            GridHologramLayer.HighObjects => (int) DrawDepth.HighFloorObjects,
            GridHologramLayer.Walls => (int) DrawDepth.Walls,
            GridHologramLayer.Overlay => (int) DrawDepth.Overlays,
            _ => (int) DrawDepth.FloorTiles,
        };
    }

    private void RemoveInteractionComponents(EntityUid uid)
    {
        if (TryComp<ConstructionGhostComponent>(uid, out var constructionGhost))
            constructionGhost.GhostId = int.MinValue;
        EntityManager.RemoveComponentDeferred<ConstructionGhostComponent>(uid);
        EntityManager.RemoveComponentDeferred<ClickableComponent>(uid);
        EntityManager.RemoveComponentDeferred<InteractionOutlineComponent>(uid);
    }

    private void OnHologramShutdown(Entity<GridHologramComponent> ent, ref ComponentShutdown args)
    {
        foreach (var ghost in ent.Comp.AllGhosts)
        {
            if (!Deleted(ghost))
                QueueDel(ghost);
        }

        ent.Comp.WireGhosts.Clear();
        ent.Comp.ObjectGhosts.Clear();
        ent.Comp.AllGhosts.Clear();
        ent.Comp.CachedTiles.Clear();
        ent.Comp.EmptyTiles.Clear();

        if (ent.Comp.TileGrid is { } tileGrid && !Deleted(tileGrid))
        {
            ent.Comp.TileGrid = null;
            QueueDel(tileGrid);
        }

        if (_activeHologram == ent.Owner)
            _activeHologram = null;
    }
}
