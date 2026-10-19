using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Examine;
using Content.Server._NF.Shipyard.Systems;
using Robust.Shared.Network;
using System.Linq;

namespace Content.Shared._NF.Shipyard;

public sealed partial class ShuttleDeedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleDeedComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShuttleAccessComponent, ExaminedEvent>(OnShuttleAccessExamined);
    }

    private void OnExamined(Entity<ShuttleDeedComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        if (!string.IsNullOrEmpty(comp.ShuttleName))
        {
            var fullName = ShipyardSystem.GetFullName(comp);
            if (!string.IsNullOrEmpty(comp.ShuttleOwner))
            {
                args.PushMarkup(Loc.GetString("shuttle-deed-examine-text-owner", ("shipname", fullName), ("owner", comp.ShuttleOwner)));
            }
            else
            {
                args.PushMarkup(Loc.GetString("shuttle-deed-examine-text", ("shipname", fullName)));
            }
        }
    }

    private void OnShuttleAccessExamined(Entity<ShuttleAccessComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        if (comp.Groups.Count == 0)
            return;

        var shuttleNames = string.Join(", ", comp.Groups.Values.Select(g => g.ShuttleName));
        args.PushMarkup(Loc.GetString("shuttle-access-examine-text", ("shuttles", shuttleNames)));
    }

    /// <summary>
    /// Checks whether the specified entity is the registered owner of the given shuttle.
    /// </summary>
    public bool IsShuttleOwner(EntityUid shuttleUid, EntityUid playerUid)
    {
        if (!TryComp<ShuttleDeedComponent>(shuttleUid, out var deed))
            return false;

        if (deed.OwnerEntity == playerUid)
            return true;

        var charName = Name(playerUid).Trim();
        return !string.IsNullOrEmpty(deed.ShuttleOwner) && deed.ShuttleOwner == charName;
    }

    /// <summary>
    /// Checks whether the specified NetUserId is the registered owner of the given shuttle.
    /// </summary>
    public bool IsShuttleOwner(EntityUid shuttleUid, NetUserId userId)
    {
        if (!TryComp<ShuttleDeedComponent>(shuttleUid, out var deed))
            return false;

        return deed.OwnerUserId == userId;
    }

    /// <summary>
    /// Attempts to retrieve owner information for a given shuttle.
    /// </summary>
    public bool TryGetShuttleOwner(
        EntityUid shuttleUid,
        out string? ownerName,
        out EntityUid? ownerEntity,
        out NetUserId? ownerUserId)
    {
        ownerName = null;
        ownerEntity = null;
        ownerUserId = null;

        if (!TryComp<ShuttleDeedComponent>(shuttleUid, out var deed))
            return false;

        ownerName = deed.ShuttleOwner;
        ownerEntity = deed.OwnerEntity;
        ownerUserId = deed.OwnerUserId;
        return true;
    }
}
