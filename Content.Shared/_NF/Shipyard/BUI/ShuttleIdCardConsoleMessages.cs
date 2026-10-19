using Content.Shared.Access;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.BUI;

[Serializable, NetSerializable]
public enum ShuttleIdCardConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ShuttleIdCardSelectShuttleMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity ShuttleUid;

    public ShuttleIdCardSelectShuttleMessage(NetEntity shuttleUid)
    {
        ShuttleUid = shuttleUid;
    }
}

[Serializable, NetSerializable]
public sealed class ShuttleIdCardWriteAccessMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity ShuttleUid;
    public readonly List<ProtoId<AccessLevelPrototype>> AccessList;

    public ShuttleIdCardWriteAccessMessage(NetEntity shuttleUid, List<ProtoId<AccessLevelPrototype>> accessList)
    {
        ShuttleUid = shuttleUid;
        AccessList = accessList;
    }
}

[Serializable, NetSerializable]
public sealed class ShuttleIdCardRemoveGroupMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity ShuttleUid;

    public ShuttleIdCardRemoveGroupMessage(NetEntity shuttleUid)
    {
        ShuttleUid = shuttleUid;
    }
}

[Serializable, NetSerializable]
public sealed class ShuttleAccessShuttleEntry
{
    public readonly NetEntity ShuttleUid;
    public readonly string ShuttleName;
    public readonly bool IsOwner;

    public ShuttleAccessShuttleEntry(NetEntity shuttleUid, string shuttleName, bool isOwner)
    {
        ShuttleUid = shuttleUid;
        ShuttleName = shuttleName;
        IsOwner = isOwner;
    }
}

[Serializable, NetSerializable]
public sealed class ShuttleAccessGroupEntry
{
    public readonly NetEntity ShuttleUid;
    public readonly string ShuttleName;
    public readonly int AccessCount;

    public ShuttleAccessGroupEntry(NetEntity shuttleUid, string shuttleName, int accessCount)
    {
        ShuttleUid = shuttleUid;
        ShuttleName = shuttleName;
        AccessCount = accessCount;
    }
}

[Serializable, NetSerializable]
public sealed class ShuttleIdCardConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool IsPrivilegedIdPresent;
    public readonly string PrivilegedIdName;
    public readonly bool IsPrivilegedIdAuthorized;

    public readonly bool IsTargetIdPresent;
    public readonly string TargetIdName;
    public readonly string? TargetIdFullName;
    public readonly string? TargetIdJobTitle;

    public readonly NetEntity? SelectedShuttle;
    public readonly List<ShuttleAccessShuttleEntry> AvailableShuttles;
    public readonly List<ProtoId<AccessLevelPrototype>> AllowedModifyAccessList;
    public readonly List<ProtoId<AccessLevelPrototype>> TargetAccessList;
    public readonly List<ShuttleAccessGroupEntry> TargetOtherGroups;

    public ShuttleIdCardConsoleBoundUserInterfaceState(
        bool isPrivilegedIdPresent,
        string privilegedIdName,
        bool isPrivilegedIdAuthorized,
        bool isTargetIdPresent,
        string targetIdName,
        string? targetIdFullName,
        string? targetIdJobTitle,
        NetEntity? selectedShuttle,
        List<ShuttleAccessShuttleEntry> availableShuttles,
        List<ProtoId<AccessLevelPrototype>> allowedModifyAccessList,
        List<ProtoId<AccessLevelPrototype>> targetAccessList,
        List<ShuttleAccessGroupEntry> targetOtherGroups)
    {
        IsPrivilegedIdPresent = isPrivilegedIdPresent;
        PrivilegedIdName = privilegedIdName;
        IsPrivilegedIdAuthorized = isPrivilegedIdAuthorized;
        IsTargetIdPresent = isTargetIdPresent;
        TargetIdName = targetIdName;
        TargetIdFullName = targetIdFullName;
        TargetIdJobTitle = targetIdJobTitle;
        SelectedShuttle = selectedShuttle;
        AvailableShuttles = availableShuttles;
        AllowedModifyAccessList = allowedModifyAccessList;
        TargetAccessList = targetAccessList;
        TargetOtherGroups = targetOtherGroups;
    }
}
