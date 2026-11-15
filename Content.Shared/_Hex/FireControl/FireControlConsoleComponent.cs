namespace Content.Shared._Hex.FireControl;

/// <summary>
/// Component for consoles that provide the user interface for a fire control server.
/// </summary>
[RegisterComponent]
public sealed partial class FireControlConsoleComponent : Component
{
    /// <summary>
    /// The fire control server this console is currently linked to.
    /// </summary>
    [ViewVariables]
    public EntityUid? ConnectedServer;

    /// <summary>
    /// When this console last wrote an admin log entry for firing.
    /// Used to avoid flooding the log while the fire button is held.
    /// </summary>
    [ViewVariables]
    public TimeSpan? NextLog;

    /// <summary>
    /// Minimum time between two admin log entries from the same console.
    /// </summary>
    [DataField]
    public TimeSpan LogSpacing = TimeSpan.FromSeconds(1);
}
