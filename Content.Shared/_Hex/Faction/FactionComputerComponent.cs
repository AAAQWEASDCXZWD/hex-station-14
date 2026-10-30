using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Faction;

/// <summary>
///     A faction terminal. Any number of terminals can be linked to the same faction; the terminal itself
///     only stores which faction it belongs to, all faction data lives in the global registry.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionComputerComponent : Component
{
    /// <summary>
    ///     Id of the faction this terminal is linked to, or an empty string while the terminal is unclaimed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string FactionId = string.Empty;
}
