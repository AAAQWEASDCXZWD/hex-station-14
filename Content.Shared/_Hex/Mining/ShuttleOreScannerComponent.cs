using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Mining;

/// <summary>
/// Компонент шатлового сканера руди.
/// Встановлюється на шатл і дозволяє консолям пілота виявляти руду на навколишніх астероїдах.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShuttleOreScannerComponent : Component
{
    /// <summary>
    /// Радіус виявлення руди у метрах (блоках). За замовчуванням 300.
    /// </summary>
    [DataField("range")]
    public float Range = 300f;
}
