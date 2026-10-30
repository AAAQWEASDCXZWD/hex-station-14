using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     Role of a character inside a faction.
/// </summary>
[NetSerializable, Serializable]
public enum FactionRole : byte
{
    Leader,
    Officer,
    Member,
}
