using Content.Shared.Access;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Components;

/// <summary>
/// Attached to ID cards to store per-shuttle access groups.
/// Allows a single card to belong to multiple shuttles with distinct, independent access levels.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShuttleAccessComponent : Component
{
    /// <summary>
    /// Map of Shuttle NetEntity to the access group granted on that shuttle.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<NetEntity, ShuttleAccessGroup> Groups = new();
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class ShuttleAccessGroup
{
    [DataField]
    public NetEntity ShuttleUid;

    [DataField]
    public string ShuttleName = string.Empty;

    [DataField]
    public HashSet<ProtoId<AccessLevelPrototype>> Tags = new();

    public ShuttleAccessGroup() { }

    public ShuttleAccessGroup(NetEntity shuttleUid, string shuttleName, IEnumerable<ProtoId<AccessLevelPrototype>> tags)
    {
        ShuttleUid = shuttleUid;
        ShuttleName = shuttleName;
        Tags = new HashSet<ProtoId<AccessLevelPrototype>>(tags);
    }
}
