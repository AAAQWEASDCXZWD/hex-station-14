using Robust.Shared.GameStates;

namespace Content.Shared._Hex.Factory;

/// <summary>
/// When present on an entity, prevents automated factory systems (such as manipulators, inserters,
/// and automated conveyor ports) from inserting or extracting items from this entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FactoryAutomationBlockedComponent : Component
{
}
