using Content.Shared._Hex.Shipyard.Components;
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

/// <summary>
/// Sent by the client to the server when the user changes the blueprint's display layer mode via right-click (PKM).
/// </summary>
[Serializable, NetSerializable]
public sealed class ShipBlueprintSetModeMessage : EntityEventArgs
{
    public readonly NetEntity BlueprintUid;
    public readonly BlueprintDisplayMode Mode;

    public ShipBlueprintSetModeMessage(NetEntity blueprintUid, BlueprintDisplayMode mode)
    {
        BlueprintUid = blueprintUid;
        Mode = mode;
    }
}
