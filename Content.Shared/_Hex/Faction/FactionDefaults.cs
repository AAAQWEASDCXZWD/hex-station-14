namespace Content.Shared._Hex.Faction;

/// <summary>
///     Shared constants and allowed values for faction customization.
/// </summary>
public static class FactionDefaults
{
    /// <summary>
    ///     Item slot used for the ID card that confirms who is registering the faction.
    /// </summary>
    public const string IdSlot = "FactionComputer-id";

    public const int MaxNameLength = 32;
    public const int MaxDescriptionLength = 240;

    public const string DefaultColor = "#4ea1ff";
    public const string DefaultPhoto = "shield";

    /// <summary>
    ///     Emblem states from <see cref="EmblemRsi"/> that can be picked as a faction photo.
    /// </summary>
    public static readonly string[] Photos =
    {
        "shield", "star", "circle", "diamond", "hexagon", "triangle", "cross", "ring",
    };

    /// <summary>
    ///     Colors offered in the customization window.
    /// </summary>
    public static readonly string[] Colors =
    {
        "#4ea1ff", "#22c55e", "#ef4444", "#f59e0b", "#a855f7", "#14b8a6",
        "#e11d48", "#64748b", "#eab308", "#0ea5e9", "#8b5cf6", "#f97316",
    };

    public const string EmblemRsiPath = "/Textures/_Hex/Interface/Faction/emblems.rsi";

    public static bool IsValidPhoto(string photoId)
    {
        return Array.IndexOf(Photos, photoId) >= 0;
    }

    public static bool IsValidColor(string colorHex)
    {
        return Robust.Shared.Maths.Color.TryFromHex(colorHex) != null;
    }
}
