using System.Linq;
using System.Numerics;
using Content.Server._Hex.SpaceArtillery.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Hex.FireControl;
using Content.Shared._Hex.ShipGuns;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Hex.FireControl;

/// <summary>
/// Connects gunnery control servers, consoles and weapons, and handles firing requests.
/// </summary>
public sealed partial class FireControlSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private RotateToFaceSystem _rotateToFace = default!;

    private EntityQuery<FireControlRotateComponent> _fireRotateQuery;
    private EntityQuery<GunComponent> _gunQuery;

    /// <summary>
    /// How often open consoles are refreshed. Firing itself is event driven.
    /// </summary>
    private const float UiUpdateInterval = 0.25f;

    /// <summary>
    /// How often the link graph is reconciled. Spawn order is not guaranteed:
    /// MapInit, anchoring and power events can all happen before an entity is
    /// parented to its grid, so this pass is the deterministic safety net.
    /// </summary>
    private const float ReconcileInterval = 0.5f;

    private float _uiUpdateAccumulator;
    private float _reconcileAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FireControlServerComponent, PowerChangedEvent>(OnServerPowerChanged);
        SubscribeLocalEvent<FireControlServerComponent, MapInitEvent>(OnServerMapInit);
        SubscribeLocalEvent<FireControlServerComponent, EntParentChangedMessage>(OnServerParentChanged);
        SubscribeLocalEvent<FireControlServerComponent, ComponentShutdown>(OnServerShutdown);
        SubscribeLocalEvent<FireControlServerComponent, ExaminedEvent>(OnServerExamined);
        SubscribeLocalEvent<FireControlServerComponent, EntityTerminatingEvent>(OnServerTerminating);

        // PowerChangedEvent стріляє тільки якщо є ApcPowerReceiver.
        // Weapons without an APC receiver register via MapInit / AnchorChanged / ParentChanged.
        SubscribeLocalEvent<FireControllableComponent, PowerChangedEvent>(OnControllablePowerChanged);
        SubscribeLocalEvent<FireControllableComponent, MapInitEvent>(OnControllableMapInit);
        SubscribeLocalEvent<FireControllableComponent, AnchorStateChangedEvent>(OnControllableAnchorChanged);
        SubscribeLocalEvent<FireControllableComponent, ComponentShutdown>(OnControllableShutdown);
        SubscribeLocalEvent<FireControllableComponent, EntParentChangedMessage>(OnControllableParentChanged);
        SubscribeLocalEvent<FireControllableComponent, ShotAttemptedEvent>(OnShotAttempted);

        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);

        InitializeConsole();

        _fireRotateQuery = GetEntityQuery<FireControlRotateComponent>();
        _gunQuery = GetEntityQuery<GunComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _reconcileAccumulator += frameTime;
        if (_reconcileAccumulator >= ReconcileInterval)
        {
            _reconcileAccumulator = 0f;
            ReconcileLinks();
        }

        _uiUpdateAccumulator += frameTime;
        if (_uiUpdateAccumulator < UiUpdateInterval)
            return;

        _uiUpdateAccumulator = 0f;

        var query = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key))
                continue;

            UpdateUi(uid, component);
        }
    }

    /// <summary>
    /// Rebuilds any link that was missed because of spawn ordering, power ordering
    /// or an entity being parented to its grid after MapInit. Linking is structural;
    /// power only gates whether the server can actually fire.
    /// </summary>
    private void ReconcileLinks()
    {
        // Servers first: they claim the grid and refresh their weapons/consoles.
        var serverQuery = EntityQueryEnumerator<FireControlServerComponent>();
        while (serverQuery.MoveNext(out var serverUid, out var server))
        {
            if (server.ConnectedGrid != null)
            {
                if (_xform.GetGrid(serverUid) == server.ConnectedGrid &&
                    Exists(server.ConnectedGrid) &&
                    TryComp<FireControlGridComponent>(server.ConnectedGrid, out var gridComponent) &&
                    gridComponent.ControllingServer == serverUid)
                {
                    continue;
                }

                Disconnect(serverUid, server);
            }

            TryConnect(serverUid, server);
        }

        // Weapons that are anchored but somehow not registered.
        var weaponQuery = EntityQueryEnumerator<FireControllableComponent>();
        while (weaponQuery.MoveNext(out var weaponUid, out var weapon))
        {
            if (weapon.ControllingServer != null)
            {
                if (Exists(weapon.ControllingServer) &&
                    TryComp<FireControlServerComponent>(weapon.ControllingServer, out var owner) &&
                    owner.Controlled.Contains(weaponUid))
                {
                    continue;
                }

                Unregister(weaponUid, weapon);
            }

            TryRegister(weaponUid, weapon);
        }

        // Consoles that are on a controlled grid but not attached yet.
        var consoleQuery = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (consoleQuery.MoveNext(out var consoleUid, out var console))
        {
            if (console.ConnectedServer != null)
            {
                if (Exists(console.ConnectedServer) &&
                    TryComp<FireControlServerComponent>(console.ConnectedServer, out var owner) &&
                    owner.Consoles.Contains(consoleUid))
                {
                    continue;
                }

                UnregisterConsole(consoleUid, console);
            }

            TryRegisterConsole(consoleUid, console);
        }
    }

    private void OnServerPowerChanged(EntityUid uid, FireControlServerComponent component, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            return; // Keep the structural link; firing checks server power separately.

        if (component.ConnectedGrid == null)
        {
            TryConnect(uid, component);
        }
        else
        {
            RefreshControllables(component.ConnectedGrid.Value);
            RefreshConsolesOnGrid(component.ConnectedGrid.Value, uid, component);
        }
    }

    private void OnServerMapInit(EntityUid uid, FireControlServerComponent component, MapInitEvent args)
    {
        TryConnect(uid, component);
    }

    private void OnServerParentChanged(EntityUid uid, FireControlServerComponent component, ref EntParentChangedMessage args)
    {
        if (component.ConnectedGrid == null)
        {
            TryConnect(uid, component);
            return;
        }

        if (_xform.GetGrid(uid) == component.ConnectedGrid)
            return;

        Disconnect(uid, component);
        TryConnect(uid, component);
    }

    private void OnServerShutdown(EntityUid uid, FireControlServerComponent component, ComponentShutdown args)
    {
        Disconnect(uid, component);
    }

    private void OnServerTerminating(EntityUid uid, FireControlServerComponent component, ref EntityTerminatingEvent args)
    {
        Disconnect(uid, component);
    }

    private void OnServerExamined(EntityUid uid, FireControlServerComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(
            Loc.GetString(
                "gunnery-server-examine-detail",
                ("usedProcessingPower", component.UsedProcessingPower),
                ("processingPower", component.ProcessingPower),
                ("valueColor", component.UsedProcessingPower <= component.ProcessingPower - 2 ? "green" : "yellow")));
    }

    /// <summary>
    /// Prevents ship weapons from firing unless a gunnery server is controlling them.
    /// </summary>
    private void OnShotAttempted(EntityUid uid, FireControllableComponent component, ref ShotAttemptedEvent args)
    {
        if (component.ControllingServer == null)
            args.Cancel();
    }

    private void OnControllablePowerChanged(EntityUid uid, FireControllableComponent component, ref PowerChangedEvent args)
    {
        // Linking is structural; whether the weapon is powered is checked when firing.
        if (args.Powered)
            TryRegister(uid, component);
    }

    private void OnControllableMapInit(EntityUid uid, FireControllableComponent component, MapInitEvent args)
    {
        TryRegister(uid, component);
    }

    private void OnControllableAnchorChanged(EntityUid uid, FireControllableComponent component, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            TryRegister(uid, component);
        else
            Unregister(uid, component);
    }

    private void OnControllableShutdown(EntityUid uid, FireControllableComponent component, ComponentShutdown args)
    {
        if (component.ControllingServer == null ||
            !TryComp<FireControlServerComponent>(component.ControllingServer, out var server))
        {
            return;
        }

        Unregister(uid, component);
        UpdateServerConsoles(server);
    }

    private void OnControllableParentChanged(EntityUid uid, FireControllableComponent component, ref EntParentChangedMessage args)
    {
        // EntParentChangedMessage fires during ComponentStartup, before MapInit and anchoring.
        // TryRegister checks Anchored itself, so just attempt it.
        if (component.ControllingServer == null)
        {
            // Not registered yet - try now.
            TryRegister(uid, component);
            return;
        }

        if (!TryComp<FireControlServerComponent>(component.ControllingServer, out var server) ||
            server.ConnectedGrid == null)
        {
            return;
        }

        if (_xform.GetGrid(uid) == server.ConnectedGrid)
            return;

        // The cannon moved to another grid - unregister and try the new one.
        Unregister(uid, component);
        UpdateServerConsoles(server);
        TryRegister(uid, component);
    }

    private void UpdateServerConsoles(FireControlServerComponent server)
    {
        foreach (var console in server.Consoles)
        {
            if (TryComp<FireControlConsoleComponent>(console, out var consoleComponent))
                UpdateUi(console, consoleComponent);
        }
    }

    private void Disconnect(EntityUid server, FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component))
            return;

        if (component.ConnectedGrid != null &&
            Exists(component.ConnectedGrid) &&
            TryComp<FireControlGridComponent>(component.ConnectedGrid, out var controlGrid) &&
            controlGrid.ControllingServer == server)
        {
            controlGrid.ControllingServer = null;
            RemComp<FireControlGridComponent>(component.ConnectedGrid.Value);
        }

        foreach (var controllable in component.Controlled.ToArray())
        {
            if (Exists(controllable))
                Unregister(controllable);
        }

        foreach (var console in component.Consoles.ToArray())
        {
            if (Exists(console))
                UnregisterConsole(console);
        }

        component.Controlled.Clear();
        component.Consoles.Clear();
        component.ConnectedGrid = null;
        component.UsedProcessingPower = 0;
    }

    /// <summary>
    /// Re-scans a grid and registers every anchored controllable weapon.
    /// </summary>
    public void RefreshControllables(EntityUid grid, FireControlGridComponent? component = null)
    {
        if (!Resolve(grid, ref component))
            return;

        if (component.ControllingServer == null)
            return;

        if (!Exists(component.ControllingServer) ||
            !TryComp<FireControlServerComponent>(component.ControllingServer, out var server))
        {
            component.ControllingServer = null;
            return;
        }

        // Drop stale back-references before rebuilding the set.
        foreach (var old in server.Controlled)
        {
            if (TryComp<FireControllableComponent>(old, out var oldComponent))
                oldComponent.ControllingServer = null;
        }

        server.Controlled.Clear();
        server.UsedProcessingPower = 0;

        var query = EntityQueryEnumerator<FireControllableComponent>();
        while (query.MoveNext(out var controllable, out var controlComponent))
        {
            if (_xform.GetGrid(controllable) == grid && Transform(controllable).Anchored)
                TryRegister(controllable, controlComponent);
        }

        UpdateServerConsoles(server);
    }

    private bool TryConnect(EntityUid server, FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component))
            return false;

        var grid = _xform.GetGrid(server);
        if (grid == null)
            return false;

        var controlGrid = EnsureComp<FireControlGridComponent>(grid.Value);

        if (controlGrid.ControllingServer != null)
        {
            if (!Exists(controlGrid.ControllingServer) ||
                !TryComp<FireControlServerComponent>(controlGrid.ControllingServer, out var current))
            {
                controlGrid.ControllingServer = null;
            }
            else if (_xform.GetGrid(controlGrid.ControllingServer.Value) != grid)
            {
                // The claiming server moved off this grid; release it.
                Disconnect(controlGrid.ControllingServer.Value, current);
                controlGrid = EnsureComp<FireControlGridComponent>(grid.Value);
            }
            else if (controlGrid.ControllingServer != server)
            {
                // A dedicated gunnery server takes priority over a console's integrated
                // server, but only while it actually has power. Conversely, a powered
                // console may take over from an unpowered dedicated server.
                var currentIsConsole = HasComp<FireControlConsoleComponent>(controlGrid.ControllingServer.Value);
                var newIsConsole = HasComp<FireControlConsoleComponent>(server);

                if (currentIsConsole && newIsConsole)
                {
                    // Keep the current console server unless it is unpowered and the
                    // incoming console is powered.
                    if (_power.IsPowered(controlGrid.ControllingServer.Value) || !_power.IsPowered(server))
                        return false;
                }
                else if (!currentIsConsole && !newIsConsole)
                {
                    return false;
                }
                else if (currentIsConsole)
                {
                    if (!_power.IsPowered(server))
                        return false;
                }
                else if (_power.IsPowered(controlGrid.ControllingServer.Value))
                {
                    return false;
                }

                Disconnect(controlGrid.ControllingServer.Value, current);
                controlGrid = EnsureComp<FireControlGridComponent>(grid.Value);
            }
        }

        controlGrid.ControllingServer = server;
        component.ConnectedGrid = grid;

        RefreshControllables(grid.Value, controlGrid);
        RefreshConsolesOnGrid(grid.Value, server, component);

        return true;
    }

    private void Unregister(EntityUid controllable, FireControllableComponent? component = null)
    {
        if (!Resolve(controllable, ref component))
            return;

        if (component.ControllingServer == null)
            return;

        if (TryComp<FireControlServerComponent>(component.ControllingServer, out var server) &&
            server.Controlled.Remove(controllable))
        {
            server.UsedProcessingPower = Math.Max(0, server.UsedProcessingPower - GetProcessingPowerCost(controllable, component));
        }

        component.ControllingServer = null;
    }

    private bool TryRegister(EntityUid controllable, FireControllableComponent? component = null)
    {
        if (!Resolve(controllable, ref component))
            return false;

        if (!Transform(controllable).Anchored)
            return false;

        var gridServer = TryGetGridServer(controllable);
        var serverUid = gridServer.ServerUid;
        var server = gridServer.ServerComponent;

        if (serverUid == null || server == null)
            return false;

        var cost = GetProcessingPowerCost(controllable, component);
        var remaining = GetRemainingProcessingPower(serverUid.Value, server);
        if (cost > remaining)
            return false;

        if (!server.Controlled.Add(controllable))
            return false;

        server.UsedProcessingPower += cost;
        component.ControllingServer = serverUid;
        return true;
    }

    public int GetRemainingProcessingPower(EntityUid server, FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component))
            return 0;

        return component.ProcessingPower - component.UsedProcessingPower;
    }

    public int GetProcessingPowerCost(EntityUid controllable, FireControllableComponent? component = null)
    {
        if (!Resolve(controllable, ref component))
            return 0;

        if (!TryComp<ShipGunClassComponent>(controllable, out var classComponent))
            return 0;

        return classComponent.Class switch
        {
            ShipGunClass.Superlight => 1,
            ShipGunClass.Light => 3,
            ShipGunClass.Medium => 6,
            ShipGunClass.Heavy => 9,
            ShipGunClass.Superheavy => 12,
            _ => 0,
        };
    }

    private (EntityUid? ServerUid, FireControlServerComponent? ServerComponent) TryGetGridServer(EntityUid uid)
    {
        var grid = _xform.GetGrid(uid);
        if (grid == null)
            return (null, null);

        if (TryComp<FireControlGridComponent>(grid.Value, out var controlGrid) &&
            controlGrid.ControllingServer != null)
        {
            if (Exists(controlGrid.ControllingServer) &&
                TryComp<FireControlServerComponent>(controlGrid.ControllingServer, out var server) &&
                _xform.GetGrid(controlGrid.ControllingServer.Value) == grid)
            {
                return (controlGrid.ControllingServer, server);
            }

            controlGrid.ControllingServer = null;
        }

        // Fallback: the grid component may be missing or stale (spawn ordering,
        // grid split, or a server that has not claimed the grid yet).
        EntityUid? fallbackUid = null;
        FireControlServerComponent? fallbackComponent = null;

        var query = EntityQueryEnumerator<FireControlServerComponent>();
        while (query.MoveNext(out var serverUid, out var serverComponent))
        {
            if (_xform.GetGrid(serverUid) != grid)
                continue;

            if (_power.IsPowered(serverUid))
                return (serverUid, serverComponent);

            fallbackUid ??= serverUid;
            fallbackComponent ??= serverComponent;
        }

        return (fallbackUid, fallbackComponent);
    }

    /// <summary>
    /// Drops references to servers that no longer exist.
    /// </summary>
    public void CleanupInvalidServerReferences()
    {
        var query = EntityQueryEnumerator<FireControlGridComponent>();
        while (query.MoveNext(out var gridUid, out var gridComponent))
        {
            if (gridComponent.ControllingServer == null)
                continue;

            if (Exists(gridComponent.ControllingServer) &&
                TryComp<FireControlServerComponent>(gridComponent.ControllingServer, out _))
            {
                continue;
            }

            gridComponent.ControllingServer = null;
            RemComp<FireControlGridComponent>(gridUid);
        }
    }

    /// <summary>
    /// Forces all servers on a grid to attempt to reconnect (called by the console Refresh button).
    /// </summary>
    public void ForceServerReconnectionOnGrid(EntityUid gridUid)
    {
        var query = EntityQueryEnumerator<FireControlServerComponent>();
        while (query.MoveNext(out var serverUid, out var serverComponent))
        {
            if (_xform.GetGrid(serverUid) != gridUid)
                continue;

            // Перевірка живлення прибрана — TryConnect дозволяє підключення без APC,
            // відключення відбувається тільки при явній втраті живлення (PowerChangedEvent).
            TryConnect(serverUid, serverComponent);
        }
    }

    public bool CanFireWeapons(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) ||
            HasComp<FTLComponent>(grid) ||
            HasComp<SpaceArtilleryDisabledGridComponent>(grid))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Fires every selected weapon that belongs to the given server.
    /// </summary>
    public void FireWeapons(
        EntityUid server,
        List<NetEntity> weapons,
        NetCoordinates coordinates,
        FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component))
            return;

        // The link survives power loss, but an unpowered server cannot fire.
        if (!_power.IsPowered(server))
            return;

        if (component.ConnectedGrid != null && !CanFireWeapons(component.ConnectedGrid.Value))
            return;

        var targetCoords = GetCoordinates(coordinates);

        foreach (var weapon in weapons)
        {
            var localWeapon = GetEntity(weapon);
            if (!Exists(localWeapon) || !component.Controlled.Contains(localWeapon))
                continue;

            // The weapon itself is used as the "user" so recoil impulses do not push the
            // console operator around, matching how ship weapons are normally fired.
            AttemptFire(localWeapon, localWeapon, targetCoords);
        }
    }

    /// <summary>
    /// Unregisters any controllables that no longer belong to the server's grid.
    /// </summary>
    public void UpdateAllControllables(EntityUid server, FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component) || component.ConnectedGrid == null)
            return;

        foreach (var controllable in component.Controlled.ToArray())
        {
            if (!TryComp<FireControllableComponent>(controllable, out var controlComponent))
                continue;

            if (_xform.GetGrid(controllable) != component.ConnectedGrid)
                Unregister(controllable, controlComponent);
        }

        UpdateServerConsoles(component);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        var query = EntityQueryEnumerator<FireControlServerComponent>();
        while (query.MoveNext(out var serverUid, out var server))
        {
            if (server.ConnectedGrid == args.Grid)
                UpdateAllControllables(serverUid, server);
        }
    }

    /// <summary>
    /// Attempts to aim and fire a single weapon at the given coordinates.
    /// </summary>
    public bool AttemptFire(
        EntityUid weapon,
        EntityUid user,
        EntityCoordinates coordinates,
        FireControllableComponent? component = null)
    {
        if (!Resolve(weapon, ref component))
            return false;

        if (!CanFire(weapon, component))
            return false;

        var weaponTransform = Transform(weapon);
        var weaponPosition = _xform.GetWorldPosition(weaponTransform);
        var targetMap = _xform.ToMapCoordinates(coordinates);
        if (targetMap.MapId != weaponTransform.MapID)
            return false;

        var targetPosition = targetMap.Position;
        var direction = targetPosition - weaponPosition;

        if (direction.LengthSquared() <= float.Epsilon)
            return false;

        direction = Vector2.Normalize(direction);

        // Do not shoot through the firing vessel's own hull or superstructure.
        if (!HasLineOfSight(weapon, weaponPosition, targetPosition, weaponTransform.MapID))
            return false;

        component.NextFire = _timing.CurTime + TimeSpan.FromSeconds(component.FireCooldown);

        if (_fireRotateQuery.HasComp(weapon))
        {
            var goalAngle = Angle.FromWorldVec(direction);
            _rotateToFace.TryRotateTo(weapon, goalAngle, 0f, Angle.FromDegrees(1), float.MaxValue, weaponTransform);
        }

        if (!_gunQuery.TryComp(weapon, out var gun))
            return false;

        _gun.AttemptShoot(user, weapon, gun, coordinates);
        return true;
    }

    /// <summary>
    /// Checks for obstacles on the firing weapon's own grid between the muzzle and
    /// the target. Other grids are ignored, so this only prevents shooting through
    /// the vessel the gun is mounted on.
    /// </summary>
    private bool HasLineOfSight(EntityUid weapon, Vector2 weaponPosition, Vector2 targetPosition, MapId mapId, float maxDistance = 500f)
    {
        var offset = targetPosition - weaponPosition;
        var distance = offset.Length();
        if (distance <= float.Epsilon)
            return false;

        var weaponGrid = Transform(weapon).GridUid;
        if (weaponGrid == null)
            return true;

        var weaponGridUid = weaponGrid.Value;
        var direction = Vector2.Normalize(offset);
        var ray = new CollisionRay(weaponPosition, direction, (int)(CollisionGroup.Opaque | CollisionGroup.Impassable | CollisionGroup.BulletImpassable));

        var results = _physics.IntersectRayWithPredicate(
            mapId,
            ray,
            Math.Min(distance, maxDistance),
            entity =>
            {
                if (entity == weapon || entity == weaponGridUid)
                    return true;

                if (TerminatingOrDeleted(entity))
                    return true;

                var xform = Transform(entity);
                if (xform.GridUid != weaponGridUid)
                    return true;

                // Only anchored structures on the firing vessel block line of sight.
                // Mobile entities (characters, players, mobs, dropped items, etc.) must never block ship cannons.
                if (!xform.Anchored)
                    return true;

                if (HasComp<MobStateComponent>(entity) || HasComp<ActorComponent>(entity))
                    return true;

                if (HasComp<FireControllableComponent>(entity) ||
                    HasComp<FireControlConsoleComponent>(entity) ||
                    HasComp<FireControlServerComponent>(entity))
                {
                    return true;
                }

                return false;
            },
            returnOnFirstHit: true);

        return !results.Any();
    }

    private bool CanFire(EntityUid weapon, FireControllableComponent component)
    {
        if (!_power.IsPowered(weapon))
            return false;

        if (component.ControllingServer == null)
            return false;

        if (component.NextFire > _timing.CurTime)
            return false;

        return true;
    }
}
