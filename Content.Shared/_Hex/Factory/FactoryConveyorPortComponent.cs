using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class FactoryConveyorPort
{
    /// <summary>
    /// Tile offset relative to the machine's local orientation.
    /// (0, 1) is 1 tile forward, (0, -1) is 1 tile behind.
    /// </summary>
    [DataField("offset")]
    public Vector2i Offset { get; set; } = new(0, -1);

    /// <summary>
    /// Optional expected direction of conveyance into or out of this port.
    /// If null, any conveyor on that tile is accepted.
    /// </summary>
    [DataField("direction")]
    public Direction? Direction { get; set; }

    /// <summary>
    /// Whether this port accepts items or ejects them.
    /// </summary>
    [DataField("portType")]
    public FactoryPortType PortType { get; set; } = FactoryPortType.Input;

    /// <summary>
    /// Optional whitelist of entities accepted by this port.
    /// </summary>
    [DataField("filter")]
    public EntityWhitelist? Filter { get; set; }

    /// <summary>
    /// Minimum time in seconds between ingesting/ejecting items.
    /// </summary>
    [DataField("transferInterval")]
    public float TransferInterval { get; set; } = 0.25f;

    [NonSerialized]
    public float TransferAccumulator;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryConveyorPortComponent : Component
{
    [DataField("ports")]
    public List<FactoryConveyorPort> Ports = new();

    /// <summary>
    /// Global toggle for all conveyor ports on this machine.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
