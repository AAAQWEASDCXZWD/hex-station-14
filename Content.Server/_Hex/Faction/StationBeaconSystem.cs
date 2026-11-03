using System.Linq;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Hex.Faction;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Destructible;
using Content.Shared.HexGrid;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using HexMath = Content.Shared.HexGrid.HexGrid;

namespace Content.Server._Hex.Faction;

/// <summary>
///     Handles station beacons that claim a hex on the shuttle navigation map for a faction.
/// </summary>
public sealed class StationBeaconSystem : EntitySystem
{
    /// <summary>
    ///     How often claimed beacons are checked for leaving their hex. Move events are handled immediately,
    ///     but this also catches movement caused by a parent entity or a moving grid.
    /// </summary>
    private const float BoundaryCheckInterval = 0.25f;

    private const float ExplosionIntensity = 15f;
    private const float ExplosionSlope = 5f;
    private const float ExplosionMaxTileIntensity = 5f;

    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly FactionComputerSystem _factions = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    ///     Active beacons currently holding each hex.
    /// </summary>
    private readonly Dictionary<HexCoordinates, HashSet<EntityUid>> _hexClaims = new();

    /// <summary>
    ///     Every beacon with a remembered hex, including beacons that are currently picked up.
    /// </summary>
    private readonly HashSet<EntityUid> _claimedBeacons = new();

    /// <summary>
    ///     Beacons whose map was paused (for example, a shuttle parked in Bluespace storage).
    /// </summary>
    private readonly HashSet<EntityUid> _pausedBeacons = new();

    /// <summary>
    ///     Original grid name and IFF state, so a grid can be restored when its last beacon stops being active.
    /// </summary>
    private readonly Dictionary<EntityUid, GridPresentation> _gridPresentations = new();

    private float _checkAccumulator;

    private readonly record struct GridPresentation(string Name, bool HadIff, Color IffColor, bool IffReadOnly);

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _log.GetSawmill("station-beacon");

        SubscribeLocalEvent<StationBeaconComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<StationBeaconComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<StationBeaconComponent, EntityPausedEvent>(OnPaused);
        SubscribeLocalEvent<StationBeaconComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<StationBeaconComponent, UserAnchoredEvent>(OnUserAnchored);
        SubscribeLocalEvent<StationBeaconComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<StationBeaconComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
        SubscribeLocalEvent<StationBeaconComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StationBeaconComponent, StationBeaconUpdateMessage>(OnUpdateMessage);
        SubscribeLocalEvent<StationBeaconComponent, MoveEvent>(OnMoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_claimedBeacons.Count == 0)
            return;

        _checkAccumulator += frameTime;
        if (_checkAccumulator < BoundaryCheckInterval)
            return;

        _checkAccumulator -= BoundaryCheckInterval;

        // Deleting a beacon modifies the set, so take a snapshot first.
        foreach (var uid in _claimedBeacons.ToArray())
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp<StationBeaconComponent>(uid, out var comp) ||
                !comp.HasClaim)
            {
                _claimedBeacons.Remove(uid);
                _pausedBeacons.Remove(uid);
                continue;
            }

            // Paused maps (Bluespace storage, paused shuttles, etc.) move grids without the beacon actually
            // leaving its sector. Do not explode there; when the map resumes, re-anchor the claim to the
            // grid's new location instead.
            var xform = Transform(uid);
            if (IsPausedOrStored(uid, xform))
            {
                _pausedBeacons.Add(uid);
                continue;
            }

            if (_pausedBeacons.Remove(uid))
            {
                RelocateClaim(uid, comp, xform);
                continue;
            }

            if (IsInsideClaim(uid, comp, Transform(uid)))
                continue;

            _sawmill.Info($"Station beacon {uid} left its claimed hex ({comp.HexQ},{comp.HexR}), exploding.");
            Explode(uid, comp);
        }
    }

    private void OnStartup(EntityUid uid, StationBeaconComponent comp, ComponentStartup args)
    {
        // Beacons from older saves may only have Active set.
        if (!comp.HasClaim && comp.Active)
        {
            comp.HasClaim = true;
            Dirty(uid, comp);
        }

        if (!comp.HasClaim)
            return;

        _claimedBeacons.Add(uid);

        if (!comp.Active)
            return;

        var hex = new HexCoordinates(comp.HexQ, comp.HexR);
        AddActiveClaim(uid, comp, hex);
    }

    private void OnShutdown(EntityUid uid, StationBeaconComponent comp, ComponentShutdown args)
    {
        // A beacon can be deleted by something other than its own boundary check.
        ReleaseClaim(uid, comp);
    }

    private void OnPaused(EntityUid uid, StationBeaconComponent comp, ref EntityPausedEvent args)
    {
        if (comp.HasClaim)
            _pausedBeacons.Add(uid);
    }

    private void OnMoved(EntityUid uid, StationBeaconComponent comp, ref MoveEvent args)
    {
        if (!comp.HasClaim || TerminatingOrDeleted(uid))
            return;

        // Parent changes can pass through transient coordinates (e.g. containers), so let the periodic
        // check validate the final position instead of exploding during the transfer.
        if (args.ParentChanged)
            return;

        if (IsPausedOrStored(uid, args.Component))
        {
            _pausedBeacons.Add(uid);
            return;
        }

        if (IsInsideClaim(uid, comp, args.Component))
            return;

        _sawmill.Info($"Station beacon {uid} left its claimed hex ({comp.HexQ},{comp.HexR}), exploding.");
        Explode(uid, comp);
    }

    private void OnAnchorChanged(EntityUid uid, StationBeaconComponent comp, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            TryActivate(uid, comp, null);
            return;
        }

        // Picked up. Stop holding the hex, but remember where it was so that carrying it out explodes it.
        if (comp.HasClaim)
            ReleaseActiveClaim(uid, comp);

        comp.Active = false;
        Dirty(uid, comp);
    }

    private void OnUserAnchored(EntityUid uid, StationBeaconComponent comp, UserAnchoredEvent args)
    {
        // AnchorStateChangedEvent has no actor, so it can only assign the hidden faction. Re-run activation
        // with the actual user so that a faction member's beacon is bound to their faction.
        TryActivate(uid, comp, args.User);
    }

    /// <summary>
    ///     Attempts to activate the beacon and claim its current hex. If the hex is already held by a beacon
    ///     of a different faction, the beacon stays inactive and must be restarted manually.
    /// </summary>
    public void TryActivate(EntityUid uid, StationBeaconComponent comp, EntityUid? actor)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var hex = HexMath.GetCoordinates(_transform.GetMapCoordinates(uid).Position);

        _sawmill.Info($"Station beacon {uid} trying to activate at {hex}");

        // A beacon that was moved while unanchored must not be allowed to claim a new hex.
        if (comp.HasClaim && (hex.Q != comp.HexQ || hex.R != comp.HexR))
        {
            _sawmill.Info($"Station beacon {uid} was moved to {hex} outside its claimed hex ({comp.HexQ},{comp.HexR}), exploding.");
            Explode(uid, comp);
            return;
        }

        // Beacons outside the standard (limited) hexagonal map do not work and do not count.
        if (hex.DistanceFromOrigin > HexMath.StandardHexRadius)
        {
            DeactivateBeacon(uid, comp);
            _sawmill.Info($"Station beacon at {hex} is outside the standard hex map; staying inactive.");
            return;
        }

        // Attach to the user's faction. An anchor-state event has no actor, so it initially assigns the
        // hidden faction; a later UserAnchoredEvent or manual restart can upgrade that to the real faction.
        var hiddenFaction = _factions.GetOrCreateHiddenFaction();
        var factionChanged = false;

        if (actor is { Valid: true } player)
        {
            var resolved = _factions.ResolvePlayerFaction(player);
            var resolvedHidden = resolved.FactionId == hiddenFaction.Id;

            if (comp.FactionId.Length == 0 || (comp.HiddenFaction && !resolvedHidden))
            {
                factionChanged = comp.FactionId != resolved.FactionId;

                comp.FactionId = resolved.FactionId;
                comp.FactionName = resolved.FactionName;
                comp.HiddenFaction = resolvedHidden;

                if (string.IsNullOrWhiteSpace(comp.BeaconName) ||
                    (comp.BeaconName == "Station Beacon" && !resolvedHidden))
                {
                    comp.BeaconName = resolvedHidden ? "Station Beacon" : resolved.FactionName;
                }

                Dirty(uid, comp);
            }
        }
        else if (comp.FactionId.Length == 0)
        {
            comp.FactionId = hiddenFaction.Id;
            comp.FactionName = hiddenFaction.Name;
            comp.HiddenFaction = true;
            comp.BeaconName = string.IsNullOrWhiteSpace(comp.BeaconName)
                ? "Station Beacon"
                : comp.BeaconName;

            Dirty(uid, comp);
        }

        // If the beacon was already active under the previous faction, release that claim before re-evaluating.
        if (factionChanged && comp.Active)
            ReleaseActiveClaim(uid, comp);

        if (!comp.HasClaim)
        {
            comp.HasClaim = true;
            comp.HexQ = hex.Q;
            comp.HexR = hex.R;
            _claimedBeacons.Add(uid);
            Dirty(uid, comp);
        }

        // Multiple beacons of the same faction may share a hex, but another faction may not.
        if (IsHeldByAnotherFaction(hex, comp.FactionId, uid))
        {
            DeactivateBeacon(uid, comp);
            _sawmill.Info($"Station beacon at {hex} inactive: hex occupied by another faction.");
            return;
        }

        AddActiveClaim(uid, comp, hex);

        _sawmill.Info($"Station beacon {uid} activated at {hex} for faction {comp.FactionId}.");
    }

    private bool IsPausedOrStored(EntityUid uid, TransformComponent xform)
    {
        if (Paused(uid))
            return true;

        return xform.GridUid != null &&
               HasComp<ShuttleStoredInBluespaceComponent>(xform.GridUid.Value);
    }

    /// <summary>
    ///     Moves an existing claim to the beacon's current sector after its map resumed from a paused state.
    /// </summary>
    private void RelocateClaim(EntityUid uid, StationBeaconComponent comp, TransformComponent xform)
    {
        var hex = HexMath.GetCoordinates(_transform.GetMapCoordinates(uid, xform).Position);
        if (hex.Q == comp.HexQ && hex.R == comp.HexR)
            return;

        var oldHex = new HexCoordinates(comp.HexQ, comp.HexR);
        if (_hexClaims.TryGetValue(oldHex, out var claims) && claims.Remove(uid) && claims.Count == 0)
            _hexClaims.Remove(oldHex);

        comp.HexQ = hex.Q;
        comp.HexR = hex.R;
        Dirty(uid, comp);

        _sawmill.Info($"Station beacon {uid} resumed from a paused map; claim moved to {hex}.");

        if (comp.Active && !IsHeldByAnotherFaction(hex, comp.FactionId, uid))
        {
            AddActiveClaim(uid, comp, hex);
        }
        else
        {
            comp.Active = false;
            Dirty(uid, comp);
            SendUiState(uid, comp);
        }
    }

    private bool IsInsideClaim(EntityUid uid, StationBeaconComponent comp, TransformComponent xform)
    {
        var mapCoordinates = _transform.GetMapCoordinates(uid, xform);

        // Entities in nullspace (e.g. inside an admin storage map) are not in a hex at all.
        if (mapCoordinates.MapId == MapId.Nullspace)
            return true;

        var hex = HexMath.GetCoordinates(mapCoordinates.Position);
        return hex.Q == comp.HexQ && hex.R == comp.HexR;
    }

    private bool IsHeldByAnotherFaction(HexCoordinates hex, string factionId, EntityUid self)
    {
        if (!_hexClaims.TryGetValue(hex, out var claims))
            return false;

        foreach (var uid in claims)
        {
            if (uid == self || TerminatingOrDeleted(uid))
                continue;

            if (TryComp<StationBeaconComponent>(uid, out var other) &&
                other.Active &&
                other.FactionId != factionId)
            {
                return true;
            }
        }

        return false;
    }

    private void AddActiveClaim(EntityUid uid, StationBeaconComponent comp, HexCoordinates hex)
    {
        if (!_hexClaims.TryGetValue(hex, out var claims))
        {
            claims = new HashSet<EntityUid>();
            _hexClaims[hex] = claims;
        }

        claims.Add(uid);
        _claimedBeacons.Add(uid);
        comp.Active = true;
        Dirty(uid, comp);

        ApplyGridPresentation(uid, comp);
        SendUiState(uid, comp);
    }

    private void ReleaseActiveClaim(EntityUid uid, StationBeaconComponent comp)
    {
        comp.Active = false;

        var hex = new HexCoordinates(comp.HexQ, comp.HexR);
        if (_hexClaims.TryGetValue(hex, out var claims) && claims.Remove(uid) && claims.Count == 0)
            _hexClaims.Remove(hex);

        RefreshGridPresentation(uid);
        SendUiState(uid, comp);
    }

    private void DeactivateBeacon(EntityUid uid, StationBeaconComponent comp)
    {
        if (comp.Active)
            ReleaseActiveClaim(uid, comp);
        else
        {
            comp.Active = false;
            Dirty(uid, comp);
            SendUiState(uid, comp);
        }
    }

    private void ReleaseClaim(EntityUid uid, StationBeaconComponent comp)
    {
        if (comp.HasClaim)
            ReleaseActiveClaim(uid, comp);

        _claimedBeacons.Remove(uid);
        _pausedBeacons.Remove(uid);
        comp.Active = false;
        comp.HasClaim = false;

        RefreshGridPresentation(uid);
    }

    private void Explode(EntityUid uid, StationBeaconComponent comp)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var mapCoordinates = _transform.GetMapCoordinates(uid);

        ReleaseClaim(uid, comp);
        comp.FactionId = string.Empty;
        comp.FactionName = string.Empty;
        comp.BeaconName = string.Empty;
        comp.HiddenFaction = false;
        comp.HexQ = 0;
        comp.HexR = 0;

        _explosion.QueueExplosion(
            mapCoordinates,
            ExplosionSystem.DefaultExplosionPrototypeId,
            ExplosionIntensity,
            ExplosionSlope,
            ExplosionMaxTileIntensity,
            uid);

        QueueDel(uid);
    }

    private void OnDestroyed(EntityUid uid, StationBeaconComponent comp, DestructionEventArgs args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        _sawmill.Info($"Station beacon {uid} destroyed in hex ({comp.HexQ},{comp.HexR}), exploding.");

        var mapCoordinates = _transform.GetMapCoordinates(uid);
        ReleaseClaim(uid, comp);

        _explosion.QueueExplosion(
            mapCoordinates,
            ExplosionSystem.DefaultExplosionPrototypeId,
            ExplosionIntensity,
            ExplosionSlope,
            ExplosionMaxTileIntensity,
            uid);
    }

    private void OnGetAltVerbs(Entity<StationBeaconComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("station-beacon-restart"),
            Act = () => TryActivate(ent, ent.Comp, user),
            Icon = null,
        });
    }

    private void OnUiOpened(EntityUid uid, StationBeaconComponent comp, BoundUIOpenedEvent args)
    {
        if (args.UiKey is not StationBeaconUiKey.Key)
            return;

        SendUiState(uid, comp);
    }

    private void OnUpdateMessage(EntityUid uid, StationBeaconComponent comp, StationBeaconUpdateMessage args)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var label = args.Label.Trim();
        if (label.Length > FactionDefaults.MaxNameLength)
            label = label[..FactionDefaults.MaxNameLength];

        if (label.Length == 0)
        {
            label = comp.FactionName.Length > 0
                ? comp.FactionName
                : "Station Beacon";
        }

        comp.BeaconName = label;
        Dirty(uid, comp);

        EntityUid? actor = args.Actor is { Valid: true } ? args.Actor : null;
        TryActivate(uid, comp, actor);

        if (!TerminatingOrDeleted(uid))
            SendUiState(uid, comp);
    }

    private void ApplyGridPresentation(EntityUid uid, StationBeaconComponent comp)
    {
        var gridUid = Transform(uid).GridUid;
        if (gridUid == null)
            return;

        var grid = gridUid.Value;
        if (TerminatingOrDeleted(grid))
            return;

        if (!_gridPresentations.TryGetValue(grid, out var original))
        {
            var hadIff = TryComp<IFFComponent>(grid, out var existingIff);
            original = new GridPresentation(
                MetaData(grid).EntityName,
                hadIff,
                existingIff?.Color ?? default,
                existingIff?.ReadOnly ?? false);
            _gridPresentations[grid] = original;
        }

        var label = string.IsNullOrWhiteSpace(comp.BeaconName) ? comp.FactionName : comp.BeaconName;
        if (string.IsNullOrWhiteSpace(label))
            return;

        _metaData.SetEntityName(grid, label);

        var iff = EnsureComp<IFFComponent>(grid);
        var color = GetFactionColor(comp);

        // Existing POI grids may be read-only; temporarily unlock so the beacon's faction color can be applied.
        _shuttle.SetIFFReadOnly(grid, false, iff);
        _shuttle.SetIFFColor(grid, color, iff);
        _shuttle.SetIFFReadOnly(grid, true, iff);

        _sawmill.Info($"Station beacon {uid} marked grid {grid} as '{label}'.");
    }

    private void RefreshGridPresentation(EntityUid uid)
    {
        var gridUid = Transform(uid).GridUid;
        if (gridUid == null || !_gridPresentations.TryGetValue(gridUid.Value, out var original))
            return;

        var grid = gridUid.Value;
        if (TerminatingOrDeleted(grid))
        {
            _gridPresentations.Remove(grid);
            return;
        }

        if (TryFindActiveBeaconOnGrid(grid, out var otherUid, out var otherComp))
        {
            ApplyGridPresentation(otherUid, otherComp);
            return;
        }

        _metaData.SetEntityName(grid, original.Name);

        if (original.HadIff)
        {
            if (TryComp<IFFComponent>(grid, out var iff))
            {
                _shuttle.SetIFFReadOnly(grid, false, iff);
                _shuttle.SetIFFColor(grid, original.IffColor, iff);
                _shuttle.SetIFFReadOnly(grid, original.IffReadOnly, iff);
            }
        }
        else
        {
            RemComp<IFFComponent>(grid);
        }

        _gridPresentations.Remove(grid);
    }

    private bool TryFindActiveBeaconOnGrid(EntityUid grid, out EntityUid uid, out StationBeaconComponent comp)
    {
        foreach (var candidate in _claimedBeacons.ToArray())
        {
            if (TerminatingOrDeleted(candidate) ||
                !TryComp<StationBeaconComponent>(candidate, out var candidateComp) ||
                !candidateComp.Active)
            {
                continue;
            }

            if (Transform(candidate).GridUid != grid)
                continue;

            uid = candidate;
            comp = candidateComp;
            return true;
        }

        uid = default;
        comp = default!;
        return false;
    }

    private Color GetFactionColor(StationBeaconComponent comp)
    {
        var colorHex = _factions.TryGetFactionColor(comp.FactionId);
        return colorHex != null && Color.TryFromHex(colorHex) is { } color
            ? color
            : Color.Orange;
    }

    private void SendUiState(EntityUid uid, StationBeaconComponent comp)
    {
        if (!_ui.HasUi(uid, StationBeaconUiKey.Key))
            return;

        _ui.SetUiState(uid, StationBeaconUiKey.Key, new StationBeaconUiState(
            comp.BeaconName,
            comp.FactionName,
            comp.Active,
            comp.HasClaim,
            comp.HexQ,
            comp.HexR));
    }

    public int CountBeacons(string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return 0;

        var count = 0;
        foreach (var claims in _hexClaims.Values)
        {
            foreach (var uid in claims)
            {
                if (TerminatingOrDeleted(uid))
                    continue;

                if (TryComp<StationBeaconComponent>(uid, out var comp) &&
                    comp.Active &&
                    comp.FactionId == factionId)
                {
                    count++;
                }
            }
        }
        return count;
    }

    public static bool IsStandardHex(HexCoordinates hex)
    {
        return hex.DistanceFromOrigin <= HexMath.StandardHexRadius;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _hexClaims.Clear();
        _claimedBeacons.Clear();
        _pausedBeacons.Clear();
        _gridPresentations.Clear();
    }
}
