using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     Sent by the client to register a new faction on the terminal.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionCreateMessage : BoundUserInterfaceMessage
{
    public readonly string Name;

    public FactionCreateMessage(string name)
    {
        Name = name;
    }
}

/// <summary>
///     Sent by the client to link an unclaimed terminal to an existing faction from the registry.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionLinkMessage : BoundUserInterfaceMessage
{
    public readonly string FactionId;

    public FactionLinkMessage(string factionId)
    {
        FactionId = factionId;
    }
}

/// <summary>
///     Sent by the client to update the faction's profile.
/// </summary>
[Serializable, NetSerializable]
public sealed class FactionCustomizeMessage : BoundUserInterfaceMessage
{
    public readonly string Name;
    public readonly string Description;
    public readonly string ColorHex;
    public readonly string PhotoId;
    public readonly byte[]? CustomPhotoData;

    public FactionCustomizeMessage(string name, string description, string colorHex, string photoId, byte[]? customPhotoData = null)
    {
        Name = name;
        Description = description;
        ColorHex = colorHex;
        PhotoId = photoId;
        CustomPhotoData = customPhotoData;
    }
}
