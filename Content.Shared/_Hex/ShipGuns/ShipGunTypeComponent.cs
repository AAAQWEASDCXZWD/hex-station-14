using Robust.Shared.Serialization;

namespace Content.Shared._Hex.ShipGuns;

/// <summary>
/// Categorizes a ship gun by the way it deals damage. Used by consoles to group weapons.
/// </summary>
[RegisterComponent]
public sealed partial class ShipGunTypeComponent : Component
{
    [DataField("shipType")]
    public ShipGunType Type = ShipGunType.Ballistic;
}

/// <summary>
/// Types of ship guns.
/// </summary>
[Serializable, NetSerializable]
public enum ShipGunType
{
    Ballistic,
    Impulse,
    Guided,
}
