using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Server._NF.Station.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Utility;


namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem : SharedShipyardSystem
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;

    public MapId? ShipyardMap { get; private set; }
    private float _shuttleIndex;
    private const float ShuttleSpawnBuffer = 1f;
    private ISawmill _sawmill = default!;
    private bool _enabled;
    private float _baseSaleRate;

    public MapId? BluespaceStorageMap { get; private set; }
    private float _storageIndex;

    /// <summary>
    ///     Delay between forcing a shuttle into a client's PVS and asking that client for a photo. The client
    ///     retries until the shuttle is loaded, so this only has to give the PVS a head start.
    /// </summary>
    private static readonly TimeSpan PhotoRequestDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     How long to keep the shuttle force-sent to the client while waiting for the photo before giving up.
    /// </summary>
    private static readonly TimeSpan PhotoRequestTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     Upper bound for a shuttle photo sent by a client, to keep broken or malicious payloads out.
    /// </summary>
    private const int MaxShuttlePhotoSize = 512 * 1024;

    private int _photoRequestId;

    // The type of error from the attempted sale of a ship.
    public enum ShipyardSaleError
    {
        Success, // Ship can be sold.
        Undocked, // Ship is not docked with the station.
        OrganicsAboard, // Sapient intelligence is aboard, cannot sell, would delete the organics
        InvalidShip, // Ship is invalid
        MessageOverwritten, // Overwritten message.
    }

    public struct ShipyardSaleResult
    {
        public ShipyardSaleError Error; // Whether or not the ship can be sold.
        public string? OrganicName; // In case an organic is aboard, this will be set to the first that's aboard.
        public string? OverwrittenMessage; // The message to write if Error is MessageOverwritten.
    }

    public enum ShipyardStorageError
    {
        Success,
        InvalidShip,
        Undocked,
        OrganicsAboard,
        RequiresCaptainAccess,
        AlreadyStored,
    }

    public struct ShipyardStorageResult
    {
        public ShipyardStorageError Error;
        public string? OrganicName;
    }

    public enum ShipyardRecallError
    {
        Success,
        InvalidShip,
        NotStored,
        RequiresCaptainAccess,
    }

    public struct ShipyardRecallResult
    {
        public ShipyardRecallError Error;
        public bool Docked;
    }

    public override void Initialize()
    {
        base.Initialize();

        // FIXME: Load-bearing jank - game doesn't want to create a shipyard map at this point.
        _enabled = _configManager.GetCVar(NFCCVars.Shipyard);
        _configManager.OnValueChanged(NFCCVars.Shipyard, SetShipyardEnabled); // NOTE: run immediately set to false, see comment above

        _configManager.OnValueChanged(NFCCVars.ShipyardSellRate, SetShipyardSellRate, true);
        _sawmill = Logger.GetSawmill("shipyard");

        SubscribeLocalEvent<ShipyardConsoleComponent, ComponentStartup>(OnShipyardStartup);
        SubscribeLocalEvent<ShipyardConsoleComponent, BoundUIOpenedEvent>(OnConsoleUIOpened);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSellMessage>(OnSellMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsolePurchaseMessage>(OnPurchaseMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleStoreMessage>(OnStoreMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardShuttlePhotoMessage>(OnShuttlePhotoMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleRecallMessage>(OnRecallMessage);
        SubscribeLocalEvent<ShipyardConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<ShipyardConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<StationDeedSpawnerComponent, MapInitEvent>(OnInitDeedSpawner);
    }
    public override void Shutdown()
    {
        _configManager.UnsubValueChanged(NFCCVars.Shipyard, SetShipyardEnabled);
        _configManager.UnsubValueChanged(NFCCVars.ShipyardSellRate, SetShipyardSellRate);
    }
    private void OnShipyardStartup(EntityUid uid, ShipyardConsoleComponent component, ComponentStartup args)
    {
        if (!_enabled)
            return;
        InitializeConsole();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        CleanupShipyard();
        CleanupBluespaceStorage();
    }

    private void SetShipyardEnabled(bool value)
    {
        if (_enabled == value)
            return;

        _enabled = value;

        if (value)
            SetupShipyardIfNeeded();
        else
            CleanupShipyard();
    }

    private void SetShipyardSellRate(float value)
    {
        _baseSaleRate = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    /// Adds a ship to the shipyard, calculates its price, and ftl-docks it to the given station.
    /// </summary>
    /// <param name="stationUid">The ID of the station to dock the shuttle to</param>
    /// <param name="shuttlePath">The path to the shuttle file to load. Must be a grid file!</param>
    /// <param name="shuttleEntityUid">The EntityUid of the shuttle that was purchased</param>
    /// <param name="atmos">
    ///     An atmosphere prototype to fill the shuttle with. Will use the grid-defined atmosphere if <c>null</c>.
    ///     Ignored unless <see cref="NFCCVars.ShipyardCustomAtmos"/> is <c>true</c>.
    /// </param>
    /// <param name="dockToStation">
    ///     Whether to dock the shuttle to the station immediately. When <c>false</c> the shuttle stays alone on the
    ///     shipyard map so the client can photograph it without any station geometry in the background.
    /// </param>
    public bool TryPurchaseShuttle(EntityUid stationUid, ResPath shuttlePath, [NotNullWhen(true)] out EntityUid? shuttleEntityUid, ShuttleAtmospherePrototype? atmos = null, bool dockToStation = true)
    {
        if (!TryComp<StationDataComponent>(stationUid, out var stationData)
            || !TryAddShuttle(shuttlePath, out var shuttleGrid)
            || !TryComp<ShuttleComponent>(shuttleGrid, out var shuttleComponent))
        {
            shuttleEntityUid = null;
            return false;
        }

        var price = _pricing.AppraiseGrid(shuttleGrid.Value, null);
        var targetGrid = _station.GetLargestGrid((stationUid, stationData));


        if (targetGrid == null) //how are we even here with no station grid
        {
            QueueDel(shuttleGrid);
            shuttleEntityUid = null;
            return false;
        }

        if (_configManager.GetCVar(NFCCVars.ShipyardCustomAtmos) && atmos != null)
        {
            SetShuttleAtmosphere(shuttleGrid.Value, atmos);
        }

        _sawmill.Info($"Shuttle {shuttlePath} was purchased at {ToPrettyString(stationUid)} for {price:f2}");

        // If this vessel still needs a photo, the caller keeps it on the shipyard map until the photo is taken.
        if (dockToStation)
            _shuttle.TryFTLDock(shuttleGrid.Value, shuttleComponent, targetGrid.Value);

        shuttleEntityUid = shuttleGrid;
        return true;
    }

    /// <summary>
    /// Loads a shuttle into the ShipyardMap from a file path
    /// </summary>
    /// <param name="shuttlePath">The path to the grid file to load. Must be a grid file!</param>
    /// <returns>Returns the EntityUid of the shuttle</returns>
    private bool TryAddShuttle(ResPath shuttlePath, [NotNullWhen(true)] out EntityUid? shuttleGrid)
    {
        shuttleGrid = null;
        SetupShipyardIfNeeded();
        if (ShipyardMap == null)
            return false;

        if (!_mapLoader.TryLoadGrid(ShipyardMap.Value, shuttlePath, out var grid, offset: new Vector2(500f + _shuttleIndex, 1f)))
        {
            _sawmill.Error($"Unable to spawn shuttle {shuttlePath}");
            return false;
        }

        _shuttleIndex += grid.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;

        shuttleGrid = grid.Value.Owner;
        return true;
    }

    /// <summary>
    /// Checks a shuttle to make sure that it is docked to the given station, and that there are no lifeforms aboard. Then it teleports tagged items on top of the console, appraises the grid, outputs to the server log, and deletes the grid
    /// </summary>
    /// <param name="stationUid">The ID of the station that the shuttle is docked to</param>
    /// <param name="shuttleUid">The grid ID of the shuttle to be appraised and sold</param>
    /// <param name="consoleUid">The ID of the console being used to sell the ship</param>
    public ShipyardSaleResult TrySellShuttle(EntityUid stationUid, EntityUid shuttleUid, EntityUid consoleUid, out int bill)
    {
        ShipyardSaleResult result = new ShipyardSaleResult();
        bill = 0;

        if (!TryComp<StationDataComponent>(stationUid, out var stationGrid)
            || !HasComp<ShuttleComponent>(shuttleUid)
            || !TryComp(shuttleUid, out TransformComponent? xform)
            || ShipyardMap == null)
        {
            result.Error = ShipyardSaleError.InvalidShip;
            return result;
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationGrid));

        if (targetGrid == null)
        {
            result.Error = ShipyardSaleError.InvalidShip;
            return result;
        }

        if (!IsShuttleDocked(shuttleUid, targetGrid.Value))
        {
            _sawmill.Warning($"shuttle is not docked to that station");
            result.Error = ShipyardSaleError.Undocked;
            return result;
        }

        var mobQuery = GetEntityQuery<MobStateComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        var charName = FoundOrganics(shuttleUid, mobQuery, xformQuery);
        if (charName is not null)
        {
            _sawmill.Warning($"organics on board");
            result.Error = ShipyardSaleError.OrganicsAboard;
            result.OrganicName = charName;
            return result;
        }

        //just yeet and delete for now. Might want to split it into another function later to send back to the shipyard map first to pause for something
        //also superman 3 moment
        if (_station.GetOwningStation(shuttleUid) is { Valid: true } shuttleStationUid)
        {
            _station.DeleteStation(shuttleStationUid);
        }

        if (TryComp<ShipyardConsoleComponent>(consoleUid, out var comp))
        {
            CleanGrid(shuttleUid, consoleUid);
        }

        bill = (int)_pricing.AppraiseGrid(shuttleUid, LacksPreserveOnSaleComp);
        QueueDel(shuttleUid);
        _sawmill.Info($"Sold shuttle {shuttleUid} for {bill}");

        // Update all record UI (skip records, no new records)
        _shuttleRecordsSystem.RefreshStateForAll(true);

        result.Error = ShipyardSaleError.Success;
        return result;
    }

    private void CleanGrid(EntityUid grid, EntityUid destination)
    {
        var xform = Transform(grid);
        var enumerator = xform.ChildEnumerator;
        var entitiesToPreserve = new List<EntityUid>();

        while (enumerator.MoveNext(out var child))
        {
            FindEntitiesToPreserve(child, ref entitiesToPreserve);
        }
        foreach (var ent in entitiesToPreserve)
        {
            // Teleport this item and all its children to the floor (or space).
            _transform.SetCoordinates(ent, new EntityCoordinates(destination, 0, 0));
            _transform.AttachToGridOrMap(ent);
        }
    }

    // checks if something has the ShipyardPreserveOnSaleComponent and if it does, adds it to the list
    private void FindEntitiesToPreserve(EntityUid entity, ref List<EntityUid> output)
    {
        if (TryComp<ShipyardSellConditionComponent>(entity, out var comp) && comp.PreserveOnSale == true)
        {
            output.Add(entity);
            return;
        }
        else if (TryComp<ContainerManagerComponent>(entity, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var ent in container.ContainedEntities)
                {
                    FindEntitiesToPreserve(ent, ref output);
                }
            }
        }
    }

    // returns false if it has ShipyardPreserveOnSaleComponent, true otherwise
    private bool LacksPreserveOnSaleComp(EntityUid uid)
    {
        return !TryComp<ShipyardSellConditionComponent>(uid, out var comp) || comp.PreserveOnSale == false;
    }
    private void CleanupShipyard()
    {
        if (ShipyardMap == null || !_map.MapExists(ShipyardMap.Value))
        {
            ShipyardMap = null;
            return;
        }

        _map.DeleteMap(ShipyardMap.Value);
    }

    public void SetupShipyardIfNeeded()
    {
        if (ShipyardMap != null && _map.MapExists(ShipyardMap.Value))
            return;

        _map.CreateMap(out var shipyardMap);
        ShipyardMap = shipyardMap;

        _map.SetPaused(ShipyardMap.Value, false);
    }

    // <summary>
    // Tries to rename a shuttle deed and update the respective components.
    // Returns true if successful.
    //
    // Null name parts are promptly ignored.
    // </summary>
    public bool TryRenameShuttle(EntityUid uid, ShuttleDeedComponent? shuttleDeed, string? newName, string? newSuffix)
    {
        if (!Resolve(uid, ref shuttleDeed))
            return false;

        var shuttle = shuttleDeed.ShuttleUid;
        if (shuttle != null
             && _station.GetOwningStation(shuttle.Value) is { Valid: true } shuttleStation)
        {
            shuttleDeed.ShuttleName = newName;
            shuttleDeed.ShuttleNameSuffix = newSuffix;
            Dirty(uid, shuttleDeed);

            var fullName = GetFullName(shuttleDeed);
            _station.RenameStation(shuttleStation, fullName, loud: false);
            _metaData.SetEntityName(shuttle.Value, fullName);
            _metaData.SetEntityName(shuttleStation, fullName);
        }
        else
        {
            _sawmill.Error($"Could not rename shuttle {ToPrettyString(shuttle):entity} to {newName}");
            return false;
        }

        //TODO: move this to an event that others hook into.
        if (TryGetNetEntity(shuttleDeed.ShuttleUid, out var shuttleNetEntity) &&
            _shuttleRecordsSystem.TryGetRecord(shuttleNetEntity.Value, out var record))
        {
            record.Name = newName ?? "";
            record.Suffix = newSuffix ?? "";
            _shuttleRecordsSystem.TryUpdateRecord(record);
        }

        return true;
    }

    /// <summary>
    /// Returns the full name of the shuttle component in the form of [prefix] [name] [suffix].
    /// </summary>
    public static string GetFullName(ShuttleDeedComponent comp)
    {
        string?[] parts = { comp.ShuttleName, comp.ShuttleNameSuffix };
        return string.Join(' ', parts.Where(it => it != null));
    }

    public bool IsShuttleDocked(EntityUid shuttleUid, EntityUid targetGrid)
    {
        var gridDocks = _docking.GetDocks(targetGrid);
        var shuttleDocks = _docking.GetDocks(shuttleUid);

        foreach (var shuttleDock in shuttleDocks)
        {
            foreach (var gridDock in gridDocks)
            {
                if (shuttleDock.Comp.DockedWith == gridDock.Owner)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void SetupBluespaceStorageIfNeeded()
    {
        if (BluespaceStorageMap != null && _map.MapExists(BluespaceStorageMap.Value))
            return;

        _map.CreateMap(out var mapId);
        BluespaceStorageMap = mapId;
        _map.SetPaused(BluespaceStorageMap.Value, true);
    }

    private void CleanupBluespaceStorage()
    {
        if (BluespaceStorageMap == null || !_map.MapExists(BluespaceStorageMap.Value))
        {
            BluespaceStorageMap = null;
            return;
        }

        _map.DeleteMap(BluespaceStorageMap.Value);
        BluespaceStorageMap = null;
    }

    public bool HasCaptainAccess(EntityUid targetId)
    {
        var tags = _access.FindAccessTags(targetId);
        return tags.Contains("Captain") || tags.Contains("Command");
    }

    public bool HasCaptainAccess(EntityUid player, EntityUid? targetId)
    {
        if (targetId != null && HasCaptainAccess(targetId.Value))
            return true;
        return HasCaptainAccess(player);
    }

    /// <summary>
    /// Checks whether the given shuttle belongs to the deed stored on the console's ID card.
    /// Shuttles referenced by UI messages must always be validated with this before use.
    /// </summary>
    public bool IsShuttleOwnedByDeed(ShuttleDeedComponent deed, EntityUid shuttleUid)
    {
        if (deed.ShuttleUid == shuttleUid)
            return true;

        return deed.OwnedShuttles.Contains(shuttleUid);
    }

    public ShipyardStorageResult TryStoreShuttle(EntityUid stationUid, EntityUid shuttleUid, EntityUid consoleUid, EntityUid playerUid, EntityUid targetId)
    {
        ShipyardStorageResult result = new();

        if (!HasCaptainAccess(playerUid, targetId))
        {
            result.Error = ShipyardStorageError.RequiresCaptainAccess;
            return result;
        }

        if (!TryComp<StationDataComponent>(stationUid, out var stationData)
            || !HasComp<ShuttleComponent>(shuttleUid)
            || !HasComp<TransformComponent>(shuttleUid))
        {
            result.Error = ShipyardStorageError.InvalidShip;
            return result;
        }

        if (HasComp<ShuttleStoredInBluespaceComponent>(shuttleUid))
        {
            result.Error = ShipyardStorageError.AlreadyStored;
            return result;
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
        {
            result.Error = ShipyardStorageError.InvalidShip;
            return result;
        }

        if (!IsShuttleDocked(shuttleUid, targetGrid.Value))
        {
            result.Error = ShipyardStorageError.Undocked;
            return result;
        }

        var mobQuery = GetEntityQuery<MobStateComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        var charName = FoundOrganics(shuttleUid, mobQuery, xformQuery);
        if (charName != null)
        {
            result.Error = ShipyardStorageError.OrganicsAboard;
            result.OrganicName = charName;
            return result;
        }

        // Undock all docks on shuttle
        var shuttleDocks = _docking.GetDocks(shuttleUid);
        foreach (var dock in shuttleDocks)
        {
            if (dock.Comp.DockedWith != null)
            {
                _docking.Undock(dock);
            }
        }

        if (TryComp<PhysicsComponent>(shuttleUid, out var physics))
        {
            _physics.SetLinearVelocity(shuttleUid, Vector2.Zero, body: physics);
            _physics.SetAngularVelocity(shuttleUid, 0f, body: physics);
        }

        try
        {
            SetupBluespaceStorageIfNeeded();
            var storageMapUid = _map.GetMap(BluespaceStorageMap!.Value);
            _storageIndex += 1000f;
            _transform.SetCoordinates(shuttleUid, new EntityCoordinates(storageMapUid, new Vector2(_storageIndex, 0)));
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to move shuttle {shuttleUid} to bluespace storage map: {ex}");
            result.Error = ShipyardStorageError.InvalidShip;
            return result;
        }

        var stored = EnsureComp<ShuttleStoredInBluespaceComponent>(shuttleUid);
        stored.StoredTime = _timing.CurTime;
        stored.StationUid = stationUid;
        stored.StoredBy = Name(playerUid);

        if (TryComp<ShuttleDeedComponent>(shuttleUid, out var deedShuttle))
        {
            deedShuttle.IsStored = true;
            Dirty(shuttleUid, deedShuttle);
        }

        if (TryComp<ShuttleDeedComponent>(targetId, out var deedCard))
        {
            deedCard.IsStored = true;
            Dirty(targetId, deedCard);
        }

        _sawmill.Info($"Stored shuttle {shuttleUid} in bluespace storage map {BluespaceStorageMap.Value}");
        result.Error = ShipyardStorageError.Success;
        return result;
    }

    public ShipyardRecallResult TryRecallShuttle(EntityUid stationUid, EntityUid shuttleUid, EntityUid consoleUid, EntityUid playerUid, EntityUid targetId)
    {
        ShipyardRecallResult result = new();

        if (!HasCaptainAccess(playerUid, targetId))
        {
            result.Error = ShipyardRecallError.RequiresCaptainAccess;
            return result;
        }

        if (!TryComp<StationDataComponent>(stationUid, out var stationData)
            || !HasComp<ShuttleComponent>(shuttleUid)
            || !HasComp<TransformComponent>(shuttleUid))
        {
            result.Error = ShipyardRecallError.InvalidShip;
            return result;
        }

        if (!HasComp<ShuttleStoredInBluespaceComponent>(shuttleUid))
        {
            result.Error = ShipyardRecallError.NotStored;
            return result;
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
        {
            result.Error = ShipyardRecallError.InvalidShip;
            return result;
        }

        if (!TryComp<ShuttleComponent>(shuttleUid, out var shuttleComponent))
        {
            result.Error = ShipyardRecallError.InvalidShip;
            return result;
        }

        var docked = _shuttle.TryFTLDock(shuttleUid, shuttleComponent, targetGrid.Value);

        RemComp<ShuttleStoredInBluespaceComponent>(shuttleUid);

        if (TryComp<ShuttleDeedComponent>(shuttleUid, out var deedShuttle))
        {
            deedShuttle.IsStored = false;
            Dirty(shuttleUid, deedShuttle);
        }

        if (TryComp<ShuttleDeedComponent>(targetId, out var deedCard))
        {
            deedCard.IsStored = false;
            Dirty(targetId, deedCard);
        }

        _sawmill.Info($"Recalled shuttle {shuttleUid} from bluespace storage. Docked: {docked}");
        result.Error = ShipyardRecallError.Success;
        result.Docked = docked;
        return result;
    }
}
