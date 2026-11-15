using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Hex.FireControl;

/// <summary>
/// A ship weapon that can be controlled by a fire control server.
/// </summary>
[RegisterComponent]
public sealed partial class FireControllableComponent : Component
{
    /// <summary>
    /// The server controlling this weapon, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? ControllingServer;

    /// <summary>
    /// When this weapon can next be fired.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextFire = TimeSpan.Zero;

    /// <summary>
    /// Minimum delay between two fire control shots, in seconds.
    /// The gun's own fire rate still applies on top of this.
    /// </summary>
    [DataField]
    public float FireCooldown = 0.2f;
}
