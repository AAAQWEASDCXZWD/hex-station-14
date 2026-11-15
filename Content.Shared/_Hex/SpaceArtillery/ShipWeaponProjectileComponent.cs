namespace Content.Shared._Hex.SpaceArtillery;

/// <summary>
/// Marks a projectile as being fired by a ship weapon.
/// Used to apply ship weapon specific effects such as screen shake on impact.
/// </summary>
[RegisterComponent]
public sealed partial class ShipWeaponProjectileComponent : Component;
