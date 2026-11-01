using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

[Serializable, NetSerializable]
public enum StationBeaconUiKey : byte
{
    Key,
}

/// <summary>
///     Current state of a faction station beacon, sent to the beacon configuration window.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationBeaconUiState : BoundUserInterfaceState
{
    public readonly string Label;
    public readonly string FactionName;
    public readonly bool Active;
    public readonly bool HasClaim;
    public readonly int HexQ;
    public readonly int HexR;

    public StationBeaconUiState(string label, string factionName, bool active, bool hasClaim, int hexQ, int hexR)
    {
        Label = label;
        FactionName = factionName;
        Active = active;
        HasClaim = hasClaim;
        HexQ = hexQ;
        HexR = hexR;
    }
}

/// <summary>
///     Sent by the client when the beacon label is updated.
/// </summary>
[Serializable, NetSerializable]
public sealed class StationBeaconUpdateMessage : BoundUserInterfaceMessage
{
    public readonly string Label;

    public StationBeaconUpdateMessage(string label)
    {
        Label = label;
    }
}
