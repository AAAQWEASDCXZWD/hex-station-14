using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Systems;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Shipyard.Systems;

public sealed class ShuttleIdCardConsoleSystem : SharedShuttleIdCardConsoleSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    // Track selected shuttle per console entity
    private readonly Dictionary<EntityUid, NetEntity> _selectedShuttles = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);

        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ShuttleIdCardSelectShuttleMessage>(OnSelectShuttle);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ShuttleIdCardWriteAccessMessage>(OnWriteAccess);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ShuttleIdCardRemoveGroupMessage>(OnRemoveGroup);
    }

    private void OnStartup(EntityUid uid, ShuttleIdCardConsoleComponent component, ComponentStartup args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnConsoleUiOpened(EntityUid uid, ShuttleIdCardConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnItemSlotChanged(EntityUid uid, ShuttleIdCardConsoleComponent component, ContainerModifiedMessage args)
    {
        if (!component.Initialized)
            return;

        if (args.Container.ID != component.PrivilegedIdSlot.ID &&
            args.Container.ID != component.TargetIdSlot.ID)
            return;

        UpdateUserInterface(uid, component);
    }

    private void OnSelectShuttle(EntityUid uid, ShuttleIdCardConsoleComponent component, ShuttleIdCardSelectShuttleMessage args)
    {
        _selectedShuttles[uid] = args.ShuttleUid;
        UpdateUserInterface(uid, component);
    }

    private void OnWriteAccess(EntityUid uid, ShuttleIdCardConsoleComponent component, ShuttleIdCardWriteAccessMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId ||
            component.TargetIdSlot.Item is not { Valid: true } targetId)
            return;

        if (!TryComp<IdCardComponent>(targetId, out _))
            return;

        var availableShuttles = GetAvailableShuttles(privilegedId);
        var targetEntry = availableShuttles.FirstOrDefault(s => s.ShuttleUid == args.ShuttleUid);
        if (targetEntry == null)
        {
            _popup.PopupEntity(Loc.GetString("shuttle-id-card-console-permission-denied"), uid, player);
            return;
        }

        var allowedToGrant = GetAllowedAccessForShuttle(privilegedId, targetEntry);

        // Verify that all tags to be written are allowed to be granted by this privileged card
        var finalTags = new HashSet<ProtoId<AccessLevelPrototype>>();
        foreach (var tag in args.AccessList)
        {
            if (component.AccessLevels.Contains(tag) && allowedToGrant.Contains(tag))
            {
                finalTags.Add(tag);
            }
        }

        var shuttleAccess = EnsureComp<ShuttleAccessComponent>(targetId);
        shuttleAccess.Groups[args.ShuttleUid] = new ShuttleAccessGroup(args.ShuttleUid, targetEntry.ShuttleName, finalTags);
        Dirty(targetId, shuttleAccess);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg"), uid);
        _popup.PopupEntity(Loc.GetString("shuttle-id-card-console-access-saved"), uid, player);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} modified shuttle access for {ToPrettyString(targetId):target} on shuttle {targetEntry.ShuttleName}: [{string.Join(", ", finalTags)}]");

        UpdateUserInterface(uid, component);
    }

    private void OnRemoveGroup(EntityUid uid, ShuttleIdCardConsoleComponent component, ShuttleIdCardRemoveGroupMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (component.PrivilegedIdSlot.Item is not { Valid: true } privilegedId ||
            component.TargetIdSlot.Item is not { Valid: true } targetId)
            return;

        var availableShuttles = GetAvailableShuttles(privilegedId);
        var targetEntry = availableShuttles.FirstOrDefault(s => s.ShuttleUid == args.ShuttleUid);
        if (targetEntry == null)
            return;

        if (TryComp<ShuttleAccessComponent>(targetId, out var shuttleAccess))
        {
            shuttleAccess.Groups.Remove(args.ShuttleUid);
            Dirty(targetId, shuttleAccess);
        }

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg"), uid);
        _popup.PopupEntity(Loc.GetString("shuttle-id-card-console-group-removed"), uid, player);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):player} removed shuttle access group {targetEntry.ShuttleName} from {ToPrettyString(targetId):target}");

        UpdateUserInterface(uid, component);
    }

    public void UpdateUserInterface(EntityUid uid, ShuttleIdCardConsoleComponent component)
    {
        var privilegedItem = component.PrivilegedIdSlot.Item;
        var targetItem = component.TargetIdSlot.Item;

        var isPrivilegedPresent = privilegedItem.HasValue;
        var privilegedName = isPrivilegedPresent ? Name(privilegedItem!.Value) : string.Empty;

        var availableShuttles = isPrivilegedPresent ? GetAvailableShuttles(privilegedItem!.Value) : new List<ShuttleAccessShuttleEntry>();
        var isPrivilegedAuthorized = availableShuttles.Count > 0;

        NetEntity? selectedShuttle = null;
        if (isPrivilegedAuthorized)
        {
            if (_selectedShuttles.TryGetValue(uid, out var curSelected) && availableShuttles.Any(s => s.ShuttleUid == curSelected))
            {
                selectedShuttle = curSelected;
            }
            else
            {
                selectedShuttle = availableShuttles[0].ShuttleUid;
                _selectedShuttles[uid] = selectedShuttle.Value;
            }
        }
        else
        {
            _selectedShuttles.Remove(uid);
        }

        var allowedModify = new List<ProtoId<AccessLevelPrototype>>();
        if (selectedShuttle != null && isPrivilegedPresent)
        {
            var entry = availableShuttles.First(s => s.ShuttleUid == selectedShuttle.Value);
            allowedModify = GetAllowedAccessForShuttle(privilegedItem!.Value, entry, component.AccessLevels);
        }

        var isTargetPresent = targetItem.HasValue;
        var targetName = isTargetPresent ? Name(targetItem!.Value) : string.Empty;
        string? targetFullName = null;
        string? targetJobTitle = null;
        var targetAccessList = new List<ProtoId<AccessLevelPrototype>>();
        var targetOtherGroups = new List<ShuttleAccessGroupEntry>();

        if (isTargetPresent && TryComp<IdCardComponent>(targetItem!.Value, out var targetIdComp))
        {
            targetFullName = targetIdComp.FullName;
            targetJobTitle = targetIdComp.LocalizedJobTitle;

            if (TryComp<ShuttleAccessComponent>(targetItem.Value, out var targetShuttleAccess))
            {
                if (selectedShuttle != null && targetShuttleAccess.Groups.TryGetValue(selectedShuttle.Value, out var currentGroup))
                {
                    targetAccessList = currentGroup.Tags.ToList();
                }

                foreach (var (shuttleNet, group) in targetShuttleAccess.Groups)
                {
                    targetOtherGroups.Add(new ShuttleAccessGroupEntry(shuttleNet, group.ShuttleName, group.Tags.Count));
                }
            }
        }

        var state = new ShuttleIdCardConsoleBoundUserInterfaceState(
            isPrivilegedPresent,
            privilegedName,
            isPrivilegedAuthorized,
            isTargetPresent,
            targetName,
            targetFullName,
            targetJobTitle,
            selectedShuttle,
            availableShuttles,
            allowedModify,
            targetAccessList,
            targetOtherGroups);

        _userInterface.SetUiState(uid, ShuttleIdCardConsoleUiKey.Key, state);
    }

    private List<ShuttleAccessShuttleEntry> GetAvailableShuttles(EntityUid cardUid)
    {
        var result = new List<ShuttleAccessShuttleEntry>();
        var seen = new HashSet<NetEntity>();

        // 1. Check Deed on this card (Owner)
        if (TryComp<ShuttleDeedComponent>(cardUid, out var deed) &&
            deed.ShuttleUid is { Valid: true } shuttleUid &&
            !Deleted(shuttleUid))
        {
            var netUid = GetNetEntity(shuttleUid);
            var fullName = ShipyardSystem.GetFullName(deed);
            result.Add(new ShuttleAccessShuttleEntry(netUid, fullName, isOwner: true));
            seen.Add(netUid);
        }

        // 2. Check ShuttleAccessComponent groups
        if (TryComp<ShuttleAccessComponent>(cardUid, out var access))
        {
            foreach (var (netUid, group) in access.Groups)
            {
                if (seen.Contains(netUid))
                    continue;

                var entity = GetEntity(netUid);
                if (Deleted(entity))
                    continue;

                // Check if this card is actually the owner of that shuttle
                var isOwner = TryComp<ShuttleDeedComponent>(entity, out var gridDeed) &&
                              gridDeed.ShuttleUid == entity &&
                              (gridDeed.OwnerEntity == cardUid || gridDeed.ShuttleOwner == Name(cardUid));

                result.Add(new ShuttleAccessShuttleEntry(netUid, group.ShuttleName, isOwner));
                seen.Add(netUid);
            }
        }

        return result;
    }

    private List<ProtoId<AccessLevelPrototype>> GetAllowedAccessForShuttle(
        EntityUid cardUid,
        ShuttleAccessShuttleEntry entry,
        List<ProtoId<AccessLevelPrototype>>? consoleAccessLevels = null)
    {
        // If owner: can grant all access levels available on this console
        if (entry.IsOwner)
        {
            return consoleAccessLevels ?? _prototype.EnumeratePrototypes<AccessLevelPrototype>()
                .Select(p => new ProtoId<AccessLevelPrototype>(p.ID))
                .ToList();
        }

        // If not owner: can only grant accesses this card currently holds for this shuttle
        if (TryComp<ShuttleAccessComponent>(cardUid, out var access) &&
            access.Groups.TryGetValue(entry.ShuttleUid, out var group))
        {
            return group.Tags.ToList();
        }

        return new List<ProtoId<AccessLevelPrototype>>();
    }
}
