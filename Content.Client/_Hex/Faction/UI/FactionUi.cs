using System;
using Content.Shared._Hex.Faction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Hex.Faction.UI;

/// <summary>
///     Small helpers shared by the faction windows.
/// </summary>
public static class FactionUi
{
    public static Texture? GetEmblem(IEntitySystemManager systemManager, string photoId)
    {
        if (!FactionDefaults.IsValidPhoto(photoId))
            photoId = FactionDefaults.DefaultPhoto;

        try
        {
            var sprite = systemManager.GetEntitySystem<SpriteSystem>();
            return sprite.Frame0(new SpriteSpecifier.Rsi(new ResPath(FactionDefaults.EmblemRsiPath), photoId));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Color GetColor(string hex)
    {
        return Color.TryFromHex(hex) ?? Color.FromHex(FactionDefaults.DefaultColor);
    }

    public static StyleBoxFlat CreateHeaderStyle(Color color)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = new Color(color.R, color.G, color.B, 0.22f),
            BorderColor = color,
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 10,
            ContentMarginBottomOverride = 10,
        };
    }

    public static StyleBoxFlat CreateSwatchStyle(bool selected)
    {
        return new StyleBoxFlat
        {
            BorderColor = selected ? Color.White : new Color(0f, 0f, 0f, 0f),
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 2,
            ContentMarginRightOverride = 2,
            ContentMarginTopOverride = 2,
            ContentMarginBottomOverride = 2,
        };
    }
}
