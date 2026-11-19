using Content.Shared._Hex.Mining;

namespace Content.Server._Hex.Worldgen;

/// <summary>
/// Компонент для чанка біому (spaceBiome -> chunkComponents).
/// Дозволяє налаштовувати шанси та ваги появи монолітних руд у цьому біомі.
/// </summary>
[RegisterComponent]
public sealed partial class HexMonolithicOreWeightsComponent : Component
{
    /// <summary>
    /// Ваги для кожної руди при випадковому виборі типу монолітного астероїда в цьому біомі.
    /// </summary>
    [DataField("oreWeights")]
    public Dictionary<HexOreType, float> OreWeights = new()
    {
        { HexOreType.Tin, 1.0f },
        { HexOreType.Coal, 0.9f },
        { HexOreType.Quartz, 0.8f },
        { HexOreType.Salt, 0.7f },
        { HexOreType.Silver, 0.5f },
        { HexOreType.Gold, 0.45f },
        { HexOreType.Plasma, 0.4f },
        { HexOreType.Uranium, 0.35f },
        { HexOreType.Bananium, 0.15f },
        { HexOreType.ArtifactFragment, 0.15f },
        { HexOreType.Diamond, 0.1f },
        { HexOreType.Bluespace, 0.1f },
    };

    /// <summary>
    /// Відсоток заповнення головною моно-рудою (за замовчуванням 0.75 = 75%).
    /// </summary>
    [DataField("fillChance")]
    public float FillChance = 0.75f;

    /// <summary>
    /// Відсоток рідкісних вкраплень інших руд (за замовчуванням 0.05 = 5%).
    /// </summary>
    [DataField("inclusionsChance")]
    public float InclusionsChance = 0.05f;

    /// <summary>
    /// Відсоток чистої породи/каменю (за замовчуванням 0.18 = 18%).
    /// </summary>
    [DataField("rockChance")]
    public float RockChance = 0.18f;

    /// <summary>
    /// Відсоток монстрів/вартових (за замовчуванням 0.02 = 2%).
    /// </summary>
    [DataField("guardianChance")]
    public float GuardianChance = 0.02f;
}
