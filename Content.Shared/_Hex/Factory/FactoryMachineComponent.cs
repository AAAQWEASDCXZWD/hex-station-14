using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Factory;

/// <summary>
/// Core component for factory processing machines.
/// Coordinates automated inputs (manipulator, conveyor ports, gas pipe ports) and internal recipe processing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactoryMachineComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [ViewVariables, AutoNetworkedField]
    public bool Powered = true;

    /// <summary>
    /// Whether external manipulators are allowed to insert items into this machine.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AllowManipulatorInput = true;

    /// <summary>
    /// Whether external manipulators are allowed to extract items from this machine's output buffer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AllowManipulatorExtraction = true;

    /// <summary>
    /// Time in seconds required for one processing / smelting cycle.
    /// </summary>
    [DataField]
    public float ProcessingDuration = 2.0f;

    [ViewVariables]
    public float ProcessingProgress;

    /// <summary>
    /// Optional required gas per processing cycle.
    /// </summary>
    [DataField]
    public Gas? RequiredGas;

    /// <summary>
    /// Amount of gas in moles consumed per processing cycle.
    /// </summary>
    [DataField]
    public float RequiredGasMoles;

    /// <summary>
    /// ID of the container holding input items (if not using MaterialStorage).
    /// </summary>
    [DataField]
    public string InputBufferId = "factory_input_buffer";

    /// <summary>
    /// ID of the container holding output items before ejection.
    /// </summary>
    [DataField]
    public string OutputBufferId = "factory_output_buffer";

    /// <summary>
    /// Prototype ID of the item produced when smelting/processing raw materials (if configured).
    /// </summary>
    [DataField]
    public string? ProducedPrototype;

    /// <summary>
    /// How long the machine sleeps when no input is available. Saves CPU.
    /// </summary>
    [DataField]
    public float SleepInterval = 0.5f;

    [ViewVariables]
    public float SleepAccumulator;
}
