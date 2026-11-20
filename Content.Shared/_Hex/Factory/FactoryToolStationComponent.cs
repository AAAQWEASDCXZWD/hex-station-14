using Content.Shared.Tools;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[Serializable, NetSerializable]
public enum FactoryToolStationVisualLayers : byte
{
    Base,
    Screen,
    Tool,
    Working,
}

[Serializable, NetSerializable]
public enum FactoryToolStationVisuals : byte
{
    Powered,
    Working,
}

/// <summary>
/// Component for automated stationary tool manipulator stations (Welder, Screwdriver, Wirecutter, Crowbar, Wrench).
/// Automatically applies the designated tool quality to valid target entities positioned on the tile in front of it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryToolStationComponent : Component
{
    /// <summary>
    /// The tool quality this station applies (e.g. Welding, Screwing, Cutting, Prying, Anchoring).
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> ToolQuality = "Welding";

    /// <summary>
    /// Optional prototype ID of the internal tool entity spawned inside this station's tool container.
    /// </summary>
    [DataField]
    public EntProtoId? InternalTool;

    /// <summary>
    /// Tile offset relative to the machine's local orientation.
    /// (0, 1) is 1 tile forward (in front of the tool station).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i TargetOffset = new(0, 1);

    /// <summary>
    /// Time in seconds required to perform one tool operation on an item.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OperationTime = 1.2f;

    /// <summary>
    /// Cooldown in seconds after finishing an operation before scanning for the next item.
    /// Prevents immediate re-acquisition and allows conveyors to move items away.
    /// </summary>
    [DataField]
    public float Cooldown = 0.5f;

    /// <summary>
    /// Sleep interval when idle with no valid candidates found.
    /// </summary>
    [DataField]
    public float SleepInterval = 0.4f;

    [ViewVariables]
    public float SleepAccumulator;

    [ViewVariables]
    public float OperationTimer;

    [ViewVariables]
    public float CooldownTimer;

    /// <summary>
    /// Entity currently being clamped and operated on.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActiveTarget;

    /// <summary>
    /// Saved physics body type of the active target so it can be restored after unclamping.
    /// </summary>
    [ViewVariables]
    public BodyType PreviousBodyType = BodyType.Dynamic;

    /// <summary>
    /// Container slot holding the internal tool entity.
    /// </summary>
    [DataField]
    public string ToolSlotId = "tool_station_slot";

    [ViewVariables, AutoNetworkedField]
    public bool Powered = true;

    [ViewVariables, AutoNetworkedField]
    public bool Working;

    /// <summary>
    /// Sound played when the station begins or performs its tool work.
    /// </summary>
    [DataField]
    public SoundSpecifier? WorkSound;
}
