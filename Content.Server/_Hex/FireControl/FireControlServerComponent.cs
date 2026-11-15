namespace Content.Server._Hex.FireControl;

/// <summary>
/// A gunnery control server. Each powered server claims one grid and controls
/// every <see cref="FireControllableComponent"/> anchored to that grid.
/// </summary>
[RegisterComponent]
public sealed partial class FireControlServerComponent : Component
{
    /// <summary>
    /// The grid this server is currently controlling, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? ConnectedGrid;

    /// <summary>
    /// Weapons currently registered to this server.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Controlled = new();

    /// <summary>
    /// Consoles currently registered to this server.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Consoles = new();

    /// <summary>
    /// Total processing power available for weapons.
    /// </summary>
    [ViewVariables, DataField]
    public int ProcessingPower;

    /// <summary>
    /// Processing power currently used by registered weapons.
    /// </summary>
    [ViewVariables]
    public int UsedProcessingPower;

    /// <summary>
    /// Maximum number of consoles that may be linked when <see cref="EnforceMaxConsoles"/> is set.
    /// </summary>
    [ViewVariables, DataField]
    public int MaxConsoles = 1;

    /// <summary>
    /// Whether the console limit is enforced.
    /// </summary>
    [ViewVariables, DataField]
    public bool EnforceMaxConsoles;
}
