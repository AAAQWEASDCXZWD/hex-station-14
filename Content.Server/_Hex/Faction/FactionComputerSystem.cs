using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared._Hex.Faction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server._Hex.Faction;

/// <summary>
///     Global faction registry and the logic behind faction terminals.
/// </summary>
/// <remarks>
///     Factions are stored on disk and survive between rounds. Terminals only store the faction id, so any
///     number of terminals can be linked to the same faction. The character <-> faction binding lives in the
///     character profile (like the bank balance), which means it also survives reconnects and respawns.
/// </remarks>
public sealed class FactionComputerSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private readonly Dictionary<string, Faction> _factions = new();
    private FactionRegistryStorage _storage = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("faction");
        _storage = new FactionRegistryStorage(_resources, _sawmill);
        LoadRegistry();

        SubscribeLocalEvent<FactionComputerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<FactionComputerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FactionComputerComponent, FactionCreateMessage>(OnCreate);
        SubscribeLocalEvent<FactionComputerComponent, FactionLinkMessage>(OnLink);
        SubscribeLocalEvent<FactionComputerComponent, FactionCustomizeMessage>(OnCustomize);
        SubscribeLocalEvent<FactionComputerComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<FactionComputerComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<FactionComputerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    /// <summary>
    ///     A faction from the global registry.
    /// </summary>
    public sealed class Faction
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string ColorHex = FactionDefaults.DefaultColor;
        public string PhotoId = FactionDefaults.DefaultPhoto;

        public NetUserId LeaderUserId;
        public EntityUid? LeaderCharacter;
        public string LeaderCharacterName = string.Empty;

        public readonly List<FactionMember> Members = new();
        public readonly HashSet<EntityUid> Terminals = new();
    }

    private void LoadRegistry()
    {
        _factions.Clear();

        foreach (var record in _storage.Load())
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Id) || _factions.ContainsKey(record.Id))
                continue;

            var faction = new Faction
            {
                Id = record.Id,
                Name = record.Name ?? string.Empty,
                Description = record.Description ?? string.Empty,
                ColorHex = FactionDefaults.IsValidColor(record.ColorHex) ? record.ColorHex : FactionDefaults.DefaultColor,
                PhotoId = FactionDefaults.IsValidPhoto(record.PhotoId) ? record.PhotoId : FactionDefaults.DefaultPhoto,
                LeaderUserId = new NetUserId(record.LeaderUserId),
                LeaderCharacterName = record.LeaderCharacterName ?? string.Empty,
            };

            foreach (var memberRecord in record.Members ?? new List<FactionMemberRecord>())
            {
                if (string.IsNullOrWhiteSpace(memberRecord.CharacterName))
                    continue;

                faction.Members.Add(new FactionMember
                {
                    UserId = new NetUserId(memberRecord.UserId),
                    CharacterName = memberRecord.CharacterName,
                    Role = memberRecord.Role,
                });
            }

            _factions[faction.Id] = faction;
        }

        _sawmill.Info($"Loaded {_factions.Count} faction(s) from disk.");
    }

    private void SaveRegistry()
    {
        var records = new List<FactionRecord>(_factions.Count);

        foreach (var faction in _factions.Values)
        {
            var record = new FactionRecord
            {
                Id = faction.Id,
                Name = faction.Name,
                Description = faction.Description,
                ColorHex = faction.ColorHex,
                PhotoId = faction.PhotoId,
                LeaderUserId = faction.LeaderUserId.UserId,
                LeaderCharacterName = faction.LeaderCharacterName,
            };

            foreach (var member in faction.Members)
            {
                record.Members.Add(new FactionMemberRecord
                {
                    UserId = member.UserId.UserId,
                    CharacterName = member.CharacterName,
                    Role = member.Role,
                });
            }

            records.Add(record);
        }

        _storage.Save(records);
    }

    private void OnStartup(EntityUid uid, FactionComputerComponent component, ComponentStartup args)
    {
        if (component.FactionId.Length == 0)
            return;

        if (_factions.TryGetValue(component.FactionId, out var faction))
            faction.Terminals.Add(uid);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        // Entity ids from the previous round are meaningless now; the registry itself is persistent.
        foreach (var faction in _factions.Values)
        {
            faction.Terminals.Clear();
            faction.LeaderCharacter = null;

            foreach (var member in faction.Members)
                member.Character = null;
        }
    }

    private void OnShutdown(EntityUid uid, FactionComputerComponent component, ComponentShutdown args)
    {
        if (component.FactionId.Length > 0 && _factions.TryGetValue(component.FactionId, out var faction))
            faction.Terminals.Remove(uid);
    }

    private void OnInserted(EntityUid uid, FactionComputerComponent component, EntInsertedIntoContainerMessage args)
    {
        OnContainerChanged(uid, component, args.Container.ID);
    }

    private void OnRemoved(EntityUid uid, FactionComputerComponent component, EntRemovedFromContainerMessage args)
    {
        OnContainerChanged(uid, component, args.Container.ID);
    }

    private void OnContainerChanged(EntityUid uid, FactionComputerComponent component, string containerId)
    {
        if (containerId != FactionDefaults.IdSlot)
            return;

        UpdateState(uid, component);
    }

    private void OnUiOpened(EntityUid uid, FactionComputerComponent component, BoundUIOpenedEvent args)
    {
        UpdateState(uid, component);
    }

    private void OnCreate(EntityUid uid, FactionComputerComponent component, FactionCreateMessage args)
    {
        if (component.FactionId.Length > 0)
            return;

        if (args.Actor is not { Valid: true } player)
            return;

        if (!HasIdCard(uid))
        {
            _popup.PopupEntity(Loc.GetString("faction-computer-id-required"), player, player);
            return;
        }

        if (!TryGetPlayerProfile(player, out var userId, out var slot, out var profile))
            return;

        if (profile.FactionId.Length > 0 && _factions.ContainsKey(profile.FactionId))
        {
            _popup.PopupEntity(Loc.GetString("faction-computer-already-in-faction"), player, player);
            return;
        }

        var name = args.Name.Trim();
        if (name.Length == 0 || name.Length > FactionDefaults.MaxNameLength)
            return;

        var faction = new Faction
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            LeaderUserId = userId,
            LeaderCharacter = player,
            LeaderCharacterName = profile.Name,
        };

        faction.Members.Add(new FactionMember
        {
            UserId = userId,
            Character = player,
            CharacterName = profile.Name,
            Role = FactionRole.Leader,
        });

        _factions[faction.Id] = faction;

        SaveFactionToProfile(userId, slot, profile, faction, FactionRole.Leader);
        ApplyFactionToCharacter(player, faction, FactionRole.Leader);
        LinkTerminal(uid, component, faction);
        SaveRegistry();
    }

    private void OnLink(EntityUid uid, FactionComputerComponent component, FactionLinkMessage args)
    {
        if (component.FactionId.Length > 0)
            return;

        if (args.Actor is not { Valid: true } player)
            return;

        if (!HasIdCard(uid))
        {
            _popup.PopupEntity(Loc.GetString("faction-computer-id-required"), player, player);
            return;
        }

        if (!_factions.TryGetValue(args.FactionId, out var faction))
            return;

        if (!TryGetPlayerProfile(player, out var userId, out var slot, out var profile))
            return;

        var member = FindMember(faction, userId, profile.Name);
        if (profile.FactionId != faction.Id && member == null)
        {
            _popup.PopupEntity(Loc.GetString("faction-computer-not-member"), player, player);
            return;
        }

        var role = member?.Role ?? profile.FactionRank;

        if (member == null)
        {
            member = new FactionMember
            {
                UserId = userId,
                Character = player,
                CharacterName = profile.Name,
                Role = role,
            };
            faction.Members.Add(member);
        }
        else
        {
            member.Character = player;
            member.CharacterName = profile.Name;
        }

        SaveFactionToProfile(userId, slot, profile, faction, role);
        ApplyFactionToCharacter(player, faction, role);
        LinkTerminal(uid, component, faction);
        SaveRegistry();
    }

    private void OnCustomize(EntityUid uid, FactionComputerComponent component, FactionCustomizeMessage args)
    {
        if (!_factions.TryGetValue(component.FactionId, out var faction))
            return;

        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryGetPlayerProfile(player, out _, out _, out var profile))
            return;

        if (profile.FactionId != faction.Id || profile.FactionRank != FactionRole.Leader)
        {
            _popup.PopupEntity(Loc.GetString("faction-computer-not-leader"), player, player);
            return;
        }

        var name = args.Name.Trim();
        if (name.Length == 0 || name.Length > FactionDefaults.MaxNameLength)
            return;

        var description = args.Description.Trim();
        if (description.Length > FactionDefaults.MaxDescriptionLength)
            description = description[..FactionDefaults.MaxDescriptionLength];

        faction.Name = name;
        faction.Description = description;
        faction.ColorHex = FactionDefaults.IsValidColor(args.ColorHex) ? args.ColorHex : faction.ColorHex;
        faction.PhotoId = FactionDefaults.IsValidPhoto(args.PhotoId) ? args.PhotoId : faction.PhotoId;
        faction.LeaderCharacterName = profile.Name;

        // Keep the per-character copy of the faction name in sync.
        foreach (var member in faction.Members)
        {
            if (member.Character is not { Valid: true } character ||
                !TryComp<FactionMemberComponent>(character, out var memberComponent))
            {
                continue;
            }

            memberComponent.FactionName = faction.Name;
            Dirty(character, memberComponent);
        }

        SaveRegistry();
        UpdateFactionNameInProfiles(faction);
        BroadcastFaction(faction);
    }

    /// <summary>
    ///     Keeps the cached faction name in the profiles of online members in sync after a rename.
    /// </summary>
    private void UpdateFactionNameInProfiles(Faction faction)
    {
        foreach (var member in faction.Members)
        {
            if (!_preferences.TryGetCachedPreferences(member.UserId, out var prefs))
                continue;

            // SetProfile changes the selected character slot, so only touch the currently selected one.
            var slot = prefs.SelectedCharacterIndex;
            if (!prefs.Characters.TryGetValue(slot, out var character) ||
                character is not HumanoidCharacterProfile profile ||
                profile.FactionId != faction.Id ||
                profile.FactionName == faction.Name)
            {
                continue;
            }

            var updated = profile.WithFaction(faction.Id, faction.Name, profile.FactionRank);
            _ = _preferences.SetProfile(member.UserId, slot, updated, validateFields: false);
        }
    }

    /// <summary>
    ///     Restores the character's faction from its persisted profile when it spawns or respawns.
    /// </summary>
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var profile = args.Profile;
        var character = args.Mob;

        if (profile.FactionId.Length == 0)
        {
            if (TryComp<FactionMemberComponent>(character, out _))
                RemComp<FactionMemberComponent>(character);

            return;
        }

        if (!_factions.TryGetValue(profile.FactionId, out var faction))
        {
            _sawmill.Warning(
                $"Character {profile.Name} references missing faction {profile.FactionId}; clearing the in-game binding.");

            if (TryComp<FactionMemberComponent>(character, out _))
                RemComp<FactionMemberComponent>(character);

            return;
        }

        var member = FindMember(faction, args.Player.UserId, profile.Name);
        var registryChanged = false;

        if (member == null)
        {
            member = new FactionMember
            {
                UserId = args.Player.UserId,
                CharacterName = profile.Name,
                Role = profile.FactionRank,
            };
            faction.Members.Add(member);
            registryChanged = true;
        }

        var role = member.Role;
        member.Character = character;
        member.CharacterName = profile.Name;

        if (role == FactionRole.Leader)
        {
            faction.LeaderCharacter = character;

            if (faction.LeaderCharacterName != profile.Name)
            {
                faction.LeaderCharacterName = profile.Name;
                registryChanged = true;
            }
        }

        if (registryChanged)
            SaveRegistry();

        ApplyFactionToCharacter(character, faction, role);

        // The registry is authoritative for the faction name and role; repair stale profile data.
        if ((profile.FactionName != faction.Name || profile.FactionRank != role) &&
            _preferences.TryGetCachedPreferences(args.Player.UserId, out var prefs) &&
            prefs.Characters.TryGetValue(prefs.SelectedCharacterIndex, out var selected) &&
            selected is HumanoidCharacterProfile selectedProfile &&
            selectedProfile.FactionId == profile.FactionId)
        {
            SaveFactionToProfile(args.Player.UserId, prefs.SelectedCharacterIndex, selectedProfile, faction, role);
        }
    }

    /// <summary>
    ///     Saves the faction binding to the character profile, the same way the bank balance is stored.
    /// </summary>
    private void SaveFactionToProfile(
        NetUserId userId,
        int slot,
        HumanoidCharacterProfile profile,
        Faction faction,
        FactionRole role)
    {
        var updated = profile.WithFaction(faction.Id, faction.Name, role);
        _ = _preferences.SetProfile(userId, slot, updated, validateFields: false);
    }

    /// <summary>
    ///     Binds the character entity to the faction as a separate parameter on the character itself.
    /// </summary>
    private void ApplyFactionToCharacter(EntityUid character, Faction faction, FactionRole role)
    {
        var memberComponent = EnsureComp<FactionMemberComponent>(character);
        memberComponent.FactionId = faction.Id;
        memberComponent.FactionName = faction.Name;
        memberComponent.Role = role;
        Dirty(character, memberComponent);
    }

    private void LinkTerminal(EntityUid uid, FactionComputerComponent component, Faction faction)
    {
        if (component.FactionId.Length > 0 && _factions.TryGetValue(component.FactionId, out var previous))
            previous.Terminals.Remove(uid);

        component.FactionId = faction.Id;
        faction.Terminals.Add(uid);
        Dirty(uid, component);

        BroadcastFaction(faction);
    }

    private void BroadcastFaction(Faction faction)
    {
        foreach (var terminal in faction.Terminals.ToArray())
        {
            if (TerminatingOrDeleted(terminal))
            {
                faction.Terminals.Remove(terminal);
                continue;
            }

            if (TryComp<FactionComputerComponent>(terminal, out var component))
                UpdateState(terminal, component);
        }
    }

    private bool TryGetPlayerProfile(
        EntityUid player,
        out NetUserId userId,
        out int slot,
        out HumanoidCharacterProfile profile)
    {
        userId = default;
        slot = -1;
        profile = default!;

        if (!_mind.TryGetMind(player, out _, out var mind) || mind.UserId == null)
            return false;

        userId = mind.UserId.Value;

        if (!_preferences.TryGetCachedPreferences(userId, out var prefs))
            return false;

        slot = prefs.SelectedCharacterIndex;

        if (!prefs.Characters.TryGetValue(slot, out var character) ||
            character is not HumanoidCharacterProfile humanoid)
        {
            return false;
        }

        profile = humanoid;
        return true;
    }

    private static FactionMember? FindMember(Faction faction, NetUserId userId, string characterName)
    {
        foreach (var member in faction.Members)
        {
            if (member.UserId == userId && member.CharacterName == characterName)
                return member;
        }

        return null;
    }

    private bool HasIdCard(EntityUid uid)
    {
        return _slots.TryGetSlot(uid, FactionDefaults.IdSlot, out var slot) && slot?.Item != null;
    }

    private void UpdateState(EntityUid uid, FactionComputerComponent component)
    {
        Faction? faction = null;

        if (component.FactionId.Length > 0)
        {
            if (_factions.TryGetValue(component.FactionId, out var linked))
            {
                faction = linked;
            }
            else
            {
                // Stale link: the registry no longer knows this faction.
                component.FactionId = string.Empty;
                Dirty(uid, component);
            }
        }

        var hasCard = _slots.TryGetSlot(uid, FactionDefaults.IdSlot, out var slot) && slot?.Item != null;
        var cardName = hasCard && slot?.Item is { } card ? Name(card) : null;

        var members = new List<FactionMemberInfo>();
        var availableFactions = new List<FactionListingInfo>();

        if (faction != null)
        {
            foreach (var member in faction.Members)
            {
                members.Add(new FactionMemberInfo(member.UserId, member.CharacterName, member.Role));
            }
        }
        else
        {
            foreach (var entry in _factions.Values)
            {
                availableFactions.Add(new FactionListingInfo(
                    entry.Id,
                    entry.Name,
                    entry.ColorHex,
                    entry.PhotoId,
                    entry.LeaderCharacterName,
                    entry.Members.Select(member => member.UserId).Distinct().ToList()));
            }
        }

        var state = new FactionComputerUiState(
            faction != null,
            faction?.Id ?? string.Empty,
            faction?.Name ?? string.Empty,
            faction?.Description ?? string.Empty,
            faction?.ColorHex ?? FactionDefaults.DefaultColor,
            faction?.PhotoId ?? FactionDefaults.DefaultPhoto,
            faction?.LeaderUserId ?? default,
            faction?.LeaderCharacterName ?? string.Empty,
            hasCard,
            cardName,
            members,
            availableFactions);

        _ui.SetUiState(uid, FactionComputerUiKey.Key, state);
    }
}
