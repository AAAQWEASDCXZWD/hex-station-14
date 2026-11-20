using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[Serializable, NetSerializable]
public enum FactoryConveyorJunctionVisualLayers : byte
{
    Base,
    Indicator,
}

[Serializable, NetSerializable]
public enum FactoryConveyorJunctionVisuals : byte
{
    Direction,
}

[Serializable, NetSerializable]
public enum FactoryConveyorJunctionDirection : byte
{
    Forward,
    Right,
    Backward,
    Left,
}

/// <summary>
/// Component for multi-directional conveyor junctions (4-way intersection, T-junction).
/// Allows switching the active conveyor flow direction among a configured set of allowed angles.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryConveyorJunctionComponent : Component
{
    /// <summary>
    /// List of angles (relative to entity's facing rotation) this junction can convey items toward.
    /// Default is 4 cardinal directions (0, 90, 180, 270 degrees).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<Angle> AllowedAngles = new()
    {
        Angle.Zero,
        Angle.FromDegrees(90),
        Angle.FromDegrees(180),
        Angle.FromDegrees(270),
    };

    /// <summary>
    /// Currently selected angle index within <see cref="AllowedAngles"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentAngleIndex;

    /// <summary>
    /// Whether players can manually cycle the direction by clicking / interacting with the junction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AllowManualSwitch = true;

    [DataField]
    public ProtoId<SinkPortPrototype> NextDirectionPort = "FactoryConveyorNextDirection";

    [DataField]
    public ProtoId<SinkPortPrototype> ForwardPort = "FactoryConveyorDirectionForward";

    [DataField]
    public ProtoId<SinkPortPrototype> RightPort = "FactoryConveyorDirectionRight";

    [DataField]
    public ProtoId<SinkPortPrototype> BackwardPort = "FactoryConveyorDirectionBackward";

    [DataField]
    public ProtoId<SinkPortPrototype> LeftPort = "FactoryConveyorDirectionLeft";
}
