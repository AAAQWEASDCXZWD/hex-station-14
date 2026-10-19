using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///     Sent by the client when the user requests storing the shuttle in bluespace storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleStoreMessage : BoundUserInterfaceMessage
{
    public ShipyardConsoleStoreMessage()
    {
    }
}

/// <summary>
///     Sent by the client when the user requests recalling the shuttle from bluespace storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleRecallMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? ShuttleUid;

    public ShipyardConsoleRecallMessage(NetEntity? shuttleUid = null)
    {
        ShuttleUid = shuttleUid;
    }
}
