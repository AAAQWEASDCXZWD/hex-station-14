using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Factory;

[Serializable, NetSerializable]
public enum ManipulatorState : byte
{
    Idle,
    PickingUp,
    MovingToTarget,
    DroppingOff,
    Returning,
}

[Serializable, NetSerializable]
public enum ManipulatorVisualLayers : byte
{
    Base,
    Arm,
}

[Serializable, NetSerializable]
public enum ManipulatorVisuals : byte
{
    Holding,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryManipulatorComponent : Component
{
    /// <summary>
    /// Current FSM state.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public ManipulatorState State = ManipulatorState.Idle;

    /// <summary>
    /// Source tile offset relative to facing direction.
    /// Default (0, -1) = 1 tile behind the manipulator.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i SourceOffset = new(0, -1);

    /// <summary>
    /// Target tile offset relative to facing direction.
    /// Default (0, 1) = 1 tile in front of the manipulator.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i TargetOffset = new(0, 1);

    /// <summary>
    /// Duration of a swing in seconds.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SwingDuration = 0.6f;

    /// <summary>
    /// Progress of the current swing (0.0 to 1.0).
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float SwingProgress;

    /// <summary>
    /// Whitelist for what items this manipulator picks up.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Blacklist for what items this manipulator ignores.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Sleep interval when idle with no items found or target full.
    /// Factorio-like sleeping to save CPU.
    /// </summary>
    [DataField]
    public float SleepInterval = 0.5f;

    [ViewVariables]
    public float SleepAccumulator;

    [ViewVariables, AutoNetworkedField]
    public bool Powered = true;

    /// <summary>
    /// Name of the internal container holding the gripped item during swing.
    /// </summary>
    [DataField]
    public string HandContainerId = "manipulator_hand";

    /// <summary>
    /// Cached reference to the hand container. Set on MapInit.
    /// </summary>
    [ViewVariables]
    public ContainerSlot? HandContainer;
}
