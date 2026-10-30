using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

[NetSerializable, Serializable]
public sealed class FactionComputerUiState : BoundUserInterfaceState
{
    public readonly bool Registered;
    public readonly string FactionId;
    public readonly string FactionName;
    public readonly string Description;
    public readonly string ColorHex;
    public readonly string PhotoId;
    public readonly NetUserId OwnerUserId;
    public readonly string OwnerCharacterName;
    public readonly bool HasIdCard;
    public readonly string? IdCardName;
    public readonly List<FactionMemberInfo> Members;
    public readonly List<FactionListingInfo> AvailableFactions;

    public FactionComputerUiState(
        bool registered,
        string factionId,
        string factionName,
        string description,
        string colorHex,
        string photoId,
        NetUserId ownerUserId,
        string ownerCharacterName,
        bool hasIdCard,
        string? idCardName,
        List<FactionMemberInfo> members,
        List<FactionListingInfo> availableFactions)
    {
        Registered = registered;
        FactionId = factionId;
        FactionName = factionName;
        Description = description;
        ColorHex = colorHex;
        PhotoId = photoId;
        OwnerUserId = ownerUserId;
        OwnerCharacterName = ownerCharacterName;
        HasIdCard = hasIdCard;
        IdCardName = idCardName;
        Members = members;
        AvailableFactions = availableFactions;
    }
}
