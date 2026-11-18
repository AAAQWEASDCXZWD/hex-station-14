using Robust.Shared.GameStates;

namespace Content.Shared._Hex.StationModifiers.Components;

/// <summary>
/// Tracks station/grid level modifiers (health bonuses, weapon damage bonuses) on a grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GridStationModifierComponent : Component
{
    /// <summary>
    /// Multiplier applied to max durability and current health of reinforced entities on this grid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HealthMultiplier = 1.0f;

    /// <summary>
    /// Multiplier applied to damage dealt by weapons fired or used on this grid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float WeaponDamageMultiplier = 1.0f;

    /// <summary>
    /// Tracked beacon sources on this grid that provide the unconditional 2X multiplier.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> BeaconSources = new();

    /// <summary>
    /// Cached set of reinforced entities currently on this grid.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> ReinforcedEntities = new();
}
