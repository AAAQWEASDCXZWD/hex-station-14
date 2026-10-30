using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     Short description of a faction from the global registry, used when linking a terminal.
/// </summary>
[NetSerializable, Serializable]
public sealed class FactionListingInfo
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly string ColorHex;
    public readonly string PhotoId;
    public readonly string LeaderName;
    public readonly List<NetUserId> MemberUserIds;
    public readonly List<FactionMemberInfo> Members;

    public FactionListingInfo(string id, string name, string description, string colorHex, string photoId, string leaderName, List<NetUserId> memberUserIds, List<FactionMemberInfo> members)
    {
        Id = id;
        Name = name;
        Description = description;
        ColorHex = colorHex;
        PhotoId = photoId;
        LeaderName = leaderName;
        MemberUserIds = memberUserIds;
        Members = members;
    }
}
