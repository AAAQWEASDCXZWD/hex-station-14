using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Mining;

/// <summary>
/// Зберігає карту покладів руди на гріді астероїда для швидкого non-greedy сканування без перебору сутностей.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AsteroidOreDataComponent : Component
{
    [DataField("ores")]
    public Dictionary<Vector2i, HexOreType> Ores = new();
}
