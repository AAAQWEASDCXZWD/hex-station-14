using Robust.Shared.GameStates;

namespace Content.Shared._Hex.StationModifiers.Components;

/// <summary>
/// Marks an entity (such as a wall, window, or cannon) to receive station reinforcement bonuses
/// (health/durability multiplication with health percentage preservation) when located on a buffed grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationReinforcedComponent : Component
{
    /// <summary>
    /// Current multiplier applied to this entity's health/destruction thresholds.
    /// Default is 1.0 (unmodified).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AppliedMultiplier = 1.0f;

    /// <summary>
    /// Cached base damage values for destruction thresholds, keyed by threshold index.
    /// Used to calculate scaled thresholds without cumulative rounding drift.
    /// </summary>
    [DataField]
    public Dictionary<int, int> BaseThresholds = new();

    /// <summary>
    /// Whether base thresholds have been recorded from the entity's DestructibleComponent.
    /// </summary>
    [DataField]
    public bool Initialized;
}
