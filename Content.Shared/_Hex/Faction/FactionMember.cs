using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     A character that belongs to a faction. Kept in the global faction registry on the server.
/// </summary>
public sealed class FactionMember
{
    public NetUserId UserId;
    public EntityUid? Character;
    public string CharacterName = string.Empty;
    public FactionRole Role = FactionRole.Member;
}
