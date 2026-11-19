using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Mining;

[Serializable, NetSerializable]
public enum HexOreType : byte
{
    None = 0,
    Coal,
    Tin,
    Quartz,
    Salt,
    Gold,
    Silver,
    Plasma,
    Uranium,
    Bananium,
    ArtifactFragment,
    Diamond,
    Bluespace
}

public static class HexOreColors
{
    public static Color GetColor(HexOreType ore) => ore switch
    {
        HexOreType.Coal => Color.FromHex("#FF6D00"),            // Гарячий бурштиновий
        HexOreType.Tin => Color.FromHex("#B0BEC5"),             // Металевий світло-сірий
        HexOreType.Quartz => Color.FromHex("#00E5FF"),          // Кристалічний ціан
        HexOreType.Salt => Color.FromHex("#FFFFFF"),            // Білий
        HexOreType.Gold => Color.FromHex("#FFD700"),            // Золотий
        HexOreType.Silver => Color.FromHex("#ECEFF1"),          // Срібний
        HexOreType.Plasma => Color.FromHex("#D500F9"),          // Неоново-фіолетовий
        HexOreType.Uranium => Color.FromHex("#00E676"),         // Радіоактивний неоново-зелений
        HexOreType.Bananium => Color.FromHex("#FFEA00"),        // Бананово-жовтий
        HexOreType.ArtifactFragment => Color.FromHex("#FF1744"),// Чужородний маджента/червоний
        HexOreType.Diamond => Color.FromHex("#80D8FF"),         // Сяючий діамантовий лід
        HexOreType.Bluespace => Color.FromHex("#2979FF"),       // Блюспейс синій
        _ => Color.White
    };
}
