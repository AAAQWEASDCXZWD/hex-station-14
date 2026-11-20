using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class FactoryGasPort
{
    /// <summary>
    /// The name of the node in NodeContainerComponent that this port connects to.
    /// e.g. "inlet", "outlet", "coolant_in".
    /// </summary>
    [DataField("node", required: true)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Tile offset relative to the machine's local orientation where this pipe attaches.
    /// </summary>
    [DataField("offset")]
    public Vector2i Offset { get; set; } = Vector2i.Zero;

    [DataField("portType")]
    public FactoryPortType PortType { get; set; } = FactoryPortType.Input;

    /// <summary>
    /// Optional gas filter (e.g. Gas.Oxygen, Gas.Plasma). Null allows any gas.
    /// </summary>
    [DataField("gas")]
    public Gas? Gas { get; set; }

    /// <summary>
    /// Target pressure in kPa to intake/maintain.
    /// </summary>
    [DataField("targetPressure")]
    public float TargetPressure { get; set; } = 1000f;

    /// <summary>
    /// Maximum moles per second to transfer.
    /// </summary>
    [DataField("maxTransferRate")]
    public float MaxTransferRate { get; set; } = 20f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryGasPortComponent : Component
{
    [DataField("ports")]
    public List<FactoryGasPort> Ports = new();

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// Internal gas mixture stored in the machine buffer.
    /// </summary>
    [DataField]
    public GasMixture InternalAir = new(200f);
}
