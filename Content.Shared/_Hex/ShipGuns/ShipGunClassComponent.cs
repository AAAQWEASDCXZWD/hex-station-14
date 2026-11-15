using Robust.Shared.Serialization;

namespace Content.Shared._Hex.ShipGuns;

/// <summary>
/// Categorizes a ship gun by class. The class determines how much processing power
/// a fire control server needs to control the gun.
/// </summary>
[RegisterComponent]
public sealed partial class ShipGunClassComponent : Component
{
    [DataField("shipClass")]
    public ShipGunClass Class = ShipGunClass.Medium;
}

/// <summary>
/// Classes of ship guns, ordered from lightest to heaviest.
/// </summary>
[Serializable, NetSerializable]
public enum ShipGunClass
{
    Superlight,
    Light,
    Medium,
    Heavy,
    Superheavy,
}
