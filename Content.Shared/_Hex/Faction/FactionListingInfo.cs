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
    public readonly string ColorHex;
    public readonly string PhotoId;
    public readonly string LeaderName;
    public readonly List<NetUserId> MemberUserIds;

    public FactionListingInfo(string id, string name, string colorHex, string photoId, string leaderName, List<NetUserId> memberUserIds)
    {
        Id = id;
        Name = name;
        ColorHex = colorHex;
        PhotoId = photoId;
        LeaderName = leaderName;
        MemberUserIds = memberUserIds;
    }
}
