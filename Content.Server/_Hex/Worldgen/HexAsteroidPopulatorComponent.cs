using Content.Shared._Hex.Mining;

namespace Content.Server._Hex.Worldgen;

public enum HexAsteroidType : byte
{
    Empty = 0,      // Пустишка: 100% камінь, 0% руди, 0% монстрів
    RichAll = 1,    // Тип 1: особливо багатий усіма рудами (~80% руди, ~15% камінь, ~5% вартові)
    RichSingle = 2  // Тип 2: моно-руда (~75% вибрана руда, ~18% камінь, ~5% рідкісні вкраплення інших руд, ~2% вартові)
}

/// <summary>
/// Компонент для генерації породи та руд на астероїдах Hex-Station.
/// </summary>
[RegisterComponent]
public sealed partial class HexAsteroidPopulatorComponent : Component
{
    [DataField("asteroidType")]
    public HexAsteroidType AsteroidType = HexAsteroidType.Empty;

    /// <summary>
    /// Конкретна фіксована руда для RichSingle, якщо задана в прототипі.
    /// Якщо не задана (None), обирається випадково зі списку руд.
    /// </summary>
    [DataField("primaryOre")]
    public HexOreType PrimaryOre = HexOreType.None;

    /// <summary>
    /// Опціональний оверрайд ваг руд для конкретного астероїда.
    /// Якщо не задано, використовуються налаштування біому або дефолтні ваги.
    /// </summary>
    [DataField("oreWeights")]
    public Dictionary<HexOreType, float>? OreWeights;

    /// <summary>
    /// Опціональний оверрайд шансу заповнення рудою (якщо null - з біому або 0.75).
    /// </summary>
    [DataField("fillChance")]
    public float? FillChance;
}
