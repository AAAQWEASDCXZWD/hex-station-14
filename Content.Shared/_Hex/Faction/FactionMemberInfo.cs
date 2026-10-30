using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     Networked view of a <see cref="FactionMember"/>.
/// </summary>
[NetSerializable, Serializable]
public sealed class FactionMemberInfo
{
    public readonly NetUserId UserId;
    public readonly string CharacterName;
    public readonly FactionRole Role;

    public FactionMemberInfo(NetUserId userId, string characterName, FactionRole role)
    {
        UserId = userId;
        CharacterName = characterName;
        Role = role;
    }
}
