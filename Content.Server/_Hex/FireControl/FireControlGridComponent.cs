namespace Content.Server._Hex.FireControl;

/// <summary>
/// Added to a grid that is currently claimed by a fire control server.
/// </summary>
[RegisterComponent]
public sealed partial class FireControlGridComponent : Component
{
    /// <summary>
    /// The server controlling this grid, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? ControllingServer;
}
