using Content.Server.Shuttles.Systems;

namespace Content.Server.Shuttles.Components;

/// <summary>
/// Makes the grid this anchor is on heavier and increases its friction while active.
/// Multiple active anchors stack multiplicatively.
/// </summary>
[RegisterComponent]
[Access(typeof(MassAnchorSystem))]
public sealed partial class MassAnchorComponent : Component
{
    [DataField]
    public bool SwitchedOn { get; set; } = true;

    /// <summary>
    /// Multiplier applied to the mass of the grid this anchor is on.
    /// </summary>
    [DataField]
    public float MassMultiplier { get; set; } = 4f / 3f;

    /// <summary>
    /// Multiplier applied to the grid's tile friction, making it stop more quickly.
    /// </summary>
    [DataField]
    public float FrictionMultiplier { get; set; } = 4f / 3f;

    /// <summary>
    /// How long it takes to switch the anchor on or off.
    /// </summary>
    [DataField]
    public TimeSpan ToggleDelay { get; set; } = TimeSpan.FromSeconds(10);
}
