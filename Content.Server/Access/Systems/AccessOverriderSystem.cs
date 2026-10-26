using System.Linq;
using Content.Server.Popups;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using static Content.Shared.Access.Components.AccessOverriderComponent;

namespace Content.Server.Access.Systems;

[UsedImplicitly]
public sealed class AccessOverriderSystem : SharedAccessOverriderSystem
{
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleDeedSystem _shuttleDeed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AccessOverriderComponent, ComponentStartup>(UpdateUserInterface);
        SubscribeLocalEvent<AccessOverriderComponent, EntInsertedIntoContainerMessage>(UpdateUserInterface);
        SubscribeLocalEvent<AccessOverriderComponent, EntRemovedFromContainerMessage>(UpdateUserInterface);
        SubscribeLocalEvent<AccessOverriderComponent, AfterInteractEvent>(AfterInteractOn);
        SubscribeLocalEvent<AccessOverriderComponent, AccessOverriderDoAfterEvent>(OnDoAfter);

        Subs.BuiEvents<AccessOverriderComponent>(AccessOverriderUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(UpdateUserInterface);
            subs.Event<BoundUIClosedEvent>(OnClose);
            subs.Event<WriteToTargetAccessReaderIdMessage>(OnWriteToTargetAccessReaderIdMessage);
        });
    }

    private void AfterInteractOn(EntityUid uid, AccessOverriderComponent component, AfterInteractEvent args)
    {
        if (args.Target == null || !TryComp(args.Target, out AccessReaderComponent? accessReader))
            return;

        if (!_interactionSystem.InRangeUnobstructed(args.User, (EntityUid) args.Target))
            return;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.DoAfter, new AccessOverriderDoAfterEvent(), uid, target: args.Target, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfterSystem.TryStartDoAfter(doAfterEventArgs);
    }

    private void OnDoAfter(EntityUid uid, AccessOverriderComponent component, AccessOverriderDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Args.Target != null)
        {
            component.TargetAccessReaderId = args.Args.Target.Value;
            _userInterface.OpenUi(uid, AccessOverriderUiKey.Key, args.User);
            UpdateUserInterface(uid, component, args);
        }

        args.Handled = true;
    }

    private void OnClose(EntityUid uid, AccessOverriderComponent component, BoundUIClosedEvent args)
    {
        if (args.UiKey.Equals(AccessOverriderUiKey.Key))
        {
            component.TargetAccessReaderId = new();
        }
    }

    private void OnWriteToTargetAccessReaderIdMessage(EntityUid uid, AccessOverriderComponent component, WriteToTargetAccessReaderIdMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        TryWriteToTargetAccessReaderId(uid, args.AccessList, player, component);

        UpdateUserInterface(uid, component, args);
    }

    private void UpdateUserInterface(EntityUid uid, AccessOverriderComponent component, EntityEventArgs args)
    {
        if (!component.Initialized)
            return;

        var privilegedIdName = string.Empty;
        var targetLabel = Loc.GetString("access-overrider-window-no-target");
        var targetLabelColor = Color.Red;

        ProtoId<AccessLevelPrototype>[]? possibleAccess = null;
        ProtoId<AccessLevelPrototype>[]? currentAccess = null;
        ProtoId<AccessLevelPrototype>[]? missingAccess = null;

        if (component.TargetAccessReaderId is { Valid: true } accessReader)
        {
            targetLabel = Loc.GetString("access-overrider-window-target-label") + " " + Comp<MetaDataComponent>(component.TargetAccessReaderId).EntityName;
            targetLabelColor = Color.White;

            if (!_accessReader.GetMainAccessReader(accessReader, out var accessReaderEnt))
                return;

            var currentAccessHashsets = accessReaderEnt.Value.Comp.AccessLists;
            currentAccess = ConvertAccessHashSetsToList(currentAccessHashsets).ToArray();
        }

        if (component.PrivilegedIdSlot.Item is { Valid: true } idCard)
        {
            privilegedIdName = Comp<MetaDataComponent>(idCard).EntityName;

            if (component.TargetAccessReaderId is { Valid: true })
            {
                possibleAccess = GetAllowedModifyTags(idCard, component.TargetAccessReaderId, component).ToArray();
            }

            if (currentAccess != null && possibleAccess != null)
            {
                missingAccess = currentAccess.Except(possibleAccess).ToArray();
            }
        }

        AccessOverriderBoundUserInterfaceState newState;

        newState = new AccessOverriderBoundUserInterfaceState(
            component.PrivilegedIdSlot.HasItem,
            PrivilegedIdIsAuthorized(uid, component),
            currentAccess,
            possibleAccess,
            missingAccess,
            privilegedIdName,
            targetLabel,
            targetLabelColor);

        _userInterface.SetUiState(uid, AccessOverriderUiKey.Key, newState);
    }

    private List<ProtoId<AccessLevelPrototype>> ConvertAccessHashSetsToList(List<HashSet<ProtoId<AccessLevelPrototype>>> accessHashsets)
    {
        var accessList = new List<ProtoId<AccessLevelPrototype>>();

        if (accessHashsets.Count <= 0)
            return accessList;

        foreach (var hashSet in accessHashsets)
        {
            accessList.AddRange(hashSet);
        }

        return accessList;
    }

    /// <summary>
    /// Called whenever an access button is pressed, adding or removing that access requirement from the target access reader.
    /// </summary>
    private void TryWriteToTargetAccessReaderId(EntityUid uid,
        List<ProtoId<AccessLevelPrototype>> newAccessList,
        EntityUid player,
        AccessOverriderComponent? component = null)
    {
        if (!Resolve(uid, ref component) || component.TargetAccessReaderId is not { Valid: true })
            return;

        if (!PrivilegedIdIsAuthorized(uid, component))
            return;

        if (!_interactionSystem.InRangeUnobstructed(player, component.TargetAccessReaderId))
        {
            _popupSystem.PopupEntity(Loc.GetString("access-overrider-out-of-range"), player, player);

            return;
        }

        if (newAccessList.Count > 0 && !newAccessList.TrueForAll(x => component.AccessLevels.Contains(x)))
        {
            _sawmill.Warning($"User {ToPrettyString(uid)} tried to write unknown access tag.");
            return;
        }

        if (!_accessReader.GetMainAccessReader(component.TargetAccessReaderId, out var accessReaderEnt))
            return;

        var oldTags = ConvertAccessHashSetsToList(accessReaderEnt.Value.Comp.AccessLists);
        var privilegedId = component.PrivilegedIdSlot.Item;

        if (oldTags.SequenceEqual(newAccessList))
            return;

        var difference = newAccessList.Union(oldTags).Except(newAccessList.Intersect(oldTags)).ToHashSet();
        var privilegedPerms = GetAllowedModifyTags(privilegedId!.Value, component.TargetAccessReaderId, component, player);

        if (!difference.IsSubsetOf(privilegedPerms))
        {
            _sawmill.Warning($"User {ToPrettyString(uid)} tried to modify permissions they could not give/take!");

            return;
        }

        if (!oldTags.ToHashSet().IsSubsetOf(privilegedPerms))
        {
            _sawmill.Warning($"User {ToPrettyString(uid)} tried to modify permissions when they do not have sufficient access!");
            _popupSystem.PopupEntity(Loc.GetString("access-overrider-cannot-modify-access"), player, player);
            _audioSystem.PlayPvs(component.DenialSound, uid);

            return;
        }

        var addedTags = newAccessList.Except(oldTags).Select(tag => "+" + tag).ToList();
        var removedTags = oldTags.Except(newAccessList).Select(tag => "-" + tag).ToList();

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(player):player} has modified {ToPrettyString(accessReaderEnt.Value):entity} with the following allowed access level holders: [{string.Join(", ", addedTags.Union(removedTags))}] [{string.Join(", ", newAccessList)}]");

        _accessReader.SetAccesses(accessReaderEnt.Value, newAccessList);

        var ev = new OnAccessOverriderAccessUpdatedEvent(player);
        RaiseLocalEvent(component.TargetAccessReaderId, ref ev);
    }

    /// <summary>
    /// Returns true if there is an ID in <see cref="AccessOverriderComponent.PrivilegedIdSlot"/> and said ID satisfies the requirements of <see cref="AccessReaderComponent"/>.
    /// </summary>
    /// <remarks>
    /// Other code relies on the fact this returns false if privileged Id is null. Don't break that invariant.
    /// </remarks>
    private bool PrivilegedIdIsAuthorized(EntityUid uid, AccessOverriderComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return true;

        var privilegedId = component.PrivilegedIdSlot.Item;
        if (privilegedId == null)
            return false;

        if (component.TargetAccessReaderId is { Valid: true } target)
        {
            if (_accessReader.GetMainAccessReader(target, out var accessReader))
            {
                return _accessReader.IsAllowed(privilegedId.Value, target, accessReader);
            }
        }

        if (_accessReader.GetMainAccessReader(uid, out var selfAccessReader))
        {
            return _accessReader.IsAllowed(privilegedId.Value, uid, selfAccessReader);
        }

        return true;
    }

    private HashSet<ProtoId<AccessLevelPrototype>> GetAllowedModifyTags(
        EntityUid privilegedId,
        EntityUid? targetReader,
        AccessOverriderComponent component,
        EntityUid? actor = null)
    {
        var tags = _accessReader.FindAccessTags(privilegedId).ToHashSet();

        if (targetReader != null)
        {
            var targetGrid = _transform.GetGrid(targetReader.Value);
            if (targetGrid != null && TryComp<ShuttleDeedComponent>(targetGrid, out var deed))
            {
                // If the player/card is the registered owner of this shuttle, or has deed for this shuttle:
                if ((actor != null && _shuttleDeed.IsShuttleOwner(targetGrid.Value, actor.Value)) ||
                    _shuttleDeed.IsShuttleOwner(targetGrid.Value, privilegedId) ||
                    (TryComp<ShuttleDeedComponent>(privilegedId, out var cardDeed) && (cardDeed.ShuttleUid == targetGrid || cardDeed.OwnedShuttles.Contains(targetGrid.Value))) ||
                    (TryComp<IdCardComponent>(privilegedId, out var idCard) && !string.IsNullOrEmpty(idCard.FullName) && idCard.FullName == deed.ShuttleOwner))
                {
                    return component.AccessLevels.ToHashSet();
                }

                // If crew member with a ShuttleAccess group for this shuttle:
                var netGrid = GetNetEntity(targetGrid.Value);
                if (TryComp<ShuttleAccessComponent>(privilegedId, out var shuttleAccess) &&
                    shuttleAccess.Groups.TryGetValue(netGrid, out var group))
                {
                    tags.UnionWith(group.Tags);
                }
            }
        }

        return tags;
    }
}
