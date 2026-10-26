using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

/// <summary>
///     Sent by the client when the user requests storing the shuttle in bluespace storage.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardConsoleStoreMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? ShuttleUid;

    public ShipyardConsoleStoreMessage(NetEntity? shuttleUid = null)
    {
        ShuttleUid = shuttleUid;
    }
}

/// <summary>
///     Sent by the client with the photo it captured of a shuttle after the server requested one.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipyardShuttlePhotoMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity ShuttleUid;
    public readonly byte[] PhotoData;

    public ShipyardShuttlePhotoMessage(NetEntity shuttleUid, byte[] photoData)
    {
        ShuttleUid = shuttleUid;
        PhotoData = photoData;
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
