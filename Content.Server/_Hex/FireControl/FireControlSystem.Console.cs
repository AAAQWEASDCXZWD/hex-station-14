using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Systems;
using Content.Shared._Hex.FireControl;
using Content.Shared.Database;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;

namespace Content.Server._Hex.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<FireControlConsoleComponent, PowerChangedEvent>(OnConsolePowerChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, MapInitEvent>(OnConsoleMapInit);
        SubscribeLocalEvent<FireControlConsoleComponent, EntParentChangedMessage>(OnConsoleParentChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleRefreshServerMessage>(OnRefreshServer);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleFireMessage>(OnFire);
        SubscribeLocalEvent<FireControlConsoleComponent, BoundUIOpenedEvent>(OnUIOpened);
    }

    private void OnConsolePowerChanged(EntityUid uid, FireControlConsoleComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered)
            TryRegisterConsole(uid, component);
    }

    private void OnConsoleMapInit(EntityUid uid, FireControlConsoleComponent component, MapInitEvent args)
    {
        TryRegisterConsole(uid, component);
    }

    private void OnConsoleParentChanged(EntityUid uid, FireControlConsoleComponent component, ref EntParentChangedMessage args)
    {
        if (component.ConnectedServer == null)
        {
            TryRegisterConsole(uid, component);
            return;
        }

        if (!TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) ||
            server.ConnectedGrid == null)
        {
            return;
        }

        if (_xform.GetGrid(uid) == server.ConnectedGrid)
            return;

        UnregisterConsole(uid, component);
        TryRegisterConsole(uid, component);
    }

    private void OnConsoleShutdown(EntityUid uid, FireControlConsoleComponent component, ComponentShutdown args)
    {
        UnregisterConsole(uid, component);
    }

    private void OnUIOpened(EntityUid uid, FireControlConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (component.ConnectedServer == null)
            TryRegisterConsole(uid, component);

        UpdateUi(uid, component);
    }

    private void OnRefreshServer(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleRefreshServerMessage args)
    {
        DoRefreshServer(uid, component);
    }

    private void DoRefreshServer(EntityUid uid, FireControlConsoleComponent component)
    {
        CleanupInvalidServerReferences();

        var consoleGrid = _xform.GetGrid(uid);
        if (consoleGrid != null)
            ForceServerReconnectionOnGrid(consoleGrid.Value);

        if (component.ConnectedServer != null &&
            (!Exists(component.ConnectedServer) ||
             !TryComp<FireControlServerComponent>(component.ConnectedServer, out _)))
        {
            component.ConnectedServer = null;
        }

        if (component.ConnectedServer == null)
            TryRegisterConsole(uid, component);

        if (component.ConnectedServer != null &&
            TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) &&
            server.ConnectedGrid != null)
        {
            RefreshControllables(server.ConnectedGrid.Value);
        }

        UpdateUi(uid, component);
    }

    private bool CanRegister((EntityUid? ServerUid, FireControlServerComponent? ServerComponent) gridServer)
    {
        var server = gridServer.ServerComponent;
        if (server == null)
            return false;

        if (!server.EnforceMaxConsoles)
            return true;

        return server.Consoles.Count < server.MaxConsoles;
    }

    private bool TryRegisterConsole(EntityUid console, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(console, ref component))
            return false;

        if (component.ConnectedServer != null)
        {
            if (Exists(component.ConnectedServer) &&
                TryComp<FireControlServerComponent>(component.ConnectedServer, out var current) &&
                current.Consoles.Contains(console))
            {
                return true;
            }

            component.ConnectedServer = null;
        }

        var gridServer = TryGetGridServer(console);
        var serverUid = gridServer.ServerUid;
        var server = gridServer.ServerComponent;

        if (serverUid == null || server == null)
            return false;

        if (!CanRegister(gridServer))
            return false;

        server.Consoles.Add(console);
        component.ConnectedServer = serverUid;
        UpdateUi(console, component);
        return true;
    }

    /// <summary>
    /// After a server connects, scan the grid for consoles that initialized before it.
    /// </summary>
    public void RefreshConsolesOnGrid(EntityUid grid, EntityUid server, FireControlServerComponent? component = null)
    {
        if (!Resolve(server, ref component))
            return;

        var query = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out var consoleComponent))
        {
            if (_xform.GetGrid(consoleUid) != grid)
                continue;

            if (consoleComponent.ConnectedServer == server && component.Consoles.Contains(consoleUid))
                continue;

            TryRegisterConsole(consoleUid, consoleComponent);
        }
    }

    private void UnregisterConsole(EntityUid console, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(console, ref component))
            return;

        if (component.ConnectedServer == null)
            return;

        if (Exists(component.ConnectedServer) &&
            TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            server.Consoles.Remove(console);
        }

        component.ConnectedServer = null;
        UpdateUi(console, component);
    }

    private void OnFire(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleFireMessage args)
    {
        if (component.ConnectedServer == null ||
            !TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) ||
            !server.Consoles.Contains(uid))
        {
            return;
        }

        var consoleTransform = Transform(uid);
        var grid = consoleTransform.GridUid;
        if (grid == null)
            return;

        FireWeapons(component.ConnectedServer.Value, args.Selected, args.Coordinates, server);

        if (args.Selected.Count == 0 ||
            (component.NextLog != null && component.NextLog >= _timing.CurTime))
        {
            return;
        }

        var firePosition = _xform.ToMapCoordinates(GetCoordinates(args.Coordinates)).Position;
        var user = args.Actor.IsValid() ? args.Actor : uid;

        _adminLogger.Add(
            LogType.ShipgunFired,
            LogImpact.High,
            $"{ToPrettyString(user):user} fired weaponry of grid {ToPrettyString(grid.Value):entity} at {firePosition}");

        component.NextLog = _timing.CurTime + component.LogSpacing;
    }

    /// <summary>
    /// Builds and sends the complete console state.
    /// </summary>
    private void UpdateUi(EntityUid uid, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        FireControlServerComponent? server = null;

        if (component.ConnectedServer != null &&
            (!Exists(component.ConnectedServer) ||
             !TryComp<FireControlServerComponent>(component.ConnectedServer, out server)))
        {
            component.ConnectedServer = null;
        }

        if (server != null && !server.Consoles.Contains(uid))
            component.ConnectedServer = null;

        var navState = _shuttleConsole.GetNavState(uid, _shuttleConsole.GetAllDocks());
        var controllables = new List<FireControllableEntry>();

        if (component.ConnectedServer != null && server != null)
        {
            foreach (var controllable in server.Controlled.OrderBy(entry => MetaData(entry).EntityName))
            {
                var (ammoCount, hasAmmoCount, hasManualReload) = GetWeaponAmmunitionInfo(controllable);

                controllables.Add(
                    new FireControllableEntry(
                        GetNetEntity(controllable),
                        GetNetCoordinates(Transform(controllable).Coordinates),
                        MetaData(controllable).EntityName,
                        ammoCount,
                        hasAmmoCount,
                        hasManualReload));
            }
        }

        var state = new FireControlConsoleBoundInterfaceState(
            component.ConnectedServer != null,
            controllables.ToArray(),
            navState);

        _ui.SetUiState(uid, FireControlConsoleUiKey.Key, state);
    }

    /// <summary>
    /// Resolves ammo information through the gun's ammo provider chain.
    /// </summary>
    private (int AmmoCount, bool HasAmmoCount, bool HasManualReload) GetWeaponAmmunitionInfo(EntityUid weapon)
    {
        var ammoEvent = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammoEvent);

        var hasManualReload =
            HasComp<MagazineAmmoProviderComponent>(weapon) ||
            HasComp<ChamberMagazineAmmoProviderComponent>(weapon) ||
            HasComp<BallisticAmmoProviderComponent>(weapon);

        if (ammoEvent.Capacity <= 0)
            return (0, false, hasManualReload);

        return (ammoEvent.Count, true, hasManualReload);
    }
}
