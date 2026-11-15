namespace Content.Server._Hex.SpaceArtillery.Components;

/// <summary>
/// Power and behaviour data for a ship-mounted artillery piece.
/// </summary>
[RegisterComponent]
public sealed partial class SpaceArtilleryComponent : Component
{
    /// <summary>
    /// Baseline power drawn continuously while the gun is operational.
    /// </summary>
    [DataField("powerUsePassive"), ViewVariables(VVAccess.ReadWrite)]
    public int PowerUsePassive = 600;

    /// <summary>
    /// Maximum rate at which the gun's internal battery recharges from the powernet.
    /// </summary>
    [DataField("powerChargeRate"), ViewVariables(VVAccess.ReadWrite)]
    public int PowerChargeRate = 3000;

    /// <summary>
    /// Power drained from the internal battery per shot.
    /// </summary>
    [DataField("powerUseActive"), ViewVariables(VVAccess.ReadWrite)]
    public int PowerUseActive = 6000;
}
