namespace Content.Shared.Shuttles.Components;

/// <summary>
/// Scales a grid's mass and tile friction. Used by things like mass anchors to make a grid heavier and harder to move.
/// </summary>
[RegisterComponent]
public sealed partial class GridMassComponent : Component
{
    /// <summary>
    /// Multiplier applied to the density of every fixture on the grid.
    /// A value of 1 means no change.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float MassMultiplier = 1f;

    /// <summary>
    /// Multiplier applied to the grid's tile friction, making it stop more quickly.
    /// A value of 1 means no change.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float FrictionMultiplier = 1f;
}
