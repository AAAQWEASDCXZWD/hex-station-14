using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Shipyard.Events;

/// <summary>
/// Sent by the shipyard console UI when the player wants a blueprint of a stored shuttle.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsolePrintBlueprintMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity ShuttleUid;

    public ShipyardConsolePrintBlueprintMessage(NetEntity shuttleUid)
    {
        ShuttleUid = shuttleUid;
    }
}
