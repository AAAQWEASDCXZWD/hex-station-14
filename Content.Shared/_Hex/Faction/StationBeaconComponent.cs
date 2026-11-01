using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     A station beacon that claims a hex on the shuttle navigation map for a faction.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationBeaconComponent : Component
{
    /// <summary>
    ///     Whether this beacon is currently active and holding its hex.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Active;

    /// <summary>
    ///     Whether the beacon remembers a hex it belongs to. This is separate from <see cref="Active"/>
    ///     because a picked-up beacon still explodes if it is carried out of its hex.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HasClaim;

    /// <summary>
    ///     Axial hex coordinates this beacon belongs to. Only meaningful while <see cref="HasClaim"/> is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int HexQ;

    [DataField, AutoNetworkedField]
    public int HexR;

    /// <summary>
    ///     Faction this beacon is attached to.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string FactionId = string.Empty;

    [DataField, AutoNetworkedField]
    public string FactionName = string.Empty;

    /// <summary>
    ///     Name shown on the hex map for this beacon.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string BeaconName = string.Empty;

    /// <summary>
    ///     True when the beacon is attached to the hidden (factionless) faction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HiddenFaction;
}
