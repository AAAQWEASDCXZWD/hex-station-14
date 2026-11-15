using Content.Shared.Shuttles.BUIStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.FireControl;

/// <summary>
/// State sent from the server to every client that has a fire control console open.
/// </summary>
[Serializable, NetSerializable]
public sealed class FireControlConsoleBoundInterfaceState : BoundUserInterfaceState
{
    public bool Connected;
    public FireControllableEntry[] FireControllables;
    public NavInterfaceState NavState;

    public FireControlConsoleBoundInterfaceState(
        bool connected,
        FireControllableEntry[] fireControllables,
        NavInterfaceState navState)
    {
        Connected = connected;
        FireControllables = fireControllables;
        NavState = navState;
    }
}

/// <summary>
/// Requests the server to re-scan the grid for a reachable fire control server and weapons.
/// </summary>
[Serializable, NetSerializable]
public sealed class FireControlConsoleRefreshServerMessage : BoundUserInterfaceMessage;

/// <summary>
/// Requests the selected weapons to fire at the given coordinates.
/// Sent repeatedly while the fire button is held so automatic weapons keep firing.
/// </summary>
[Serializable, NetSerializable]
public sealed class FireControlConsoleFireMessage : BoundUserInterfaceMessage
{
    public readonly List<NetEntity> Selected;
    public readonly NetCoordinates Coordinates;

    public FireControlConsoleFireMessage(List<NetEntity> selected, NetCoordinates coordinates)
    {
        Selected = selected;
        Coordinates = coordinates;
    }
}

/// <summary>
/// A single weapon entry shown in the gunnery console's weapon list.
/// </summary>
[Serializable, NetSerializable]
public struct FireControllableEntry
{
    /// <summary>
    /// The weapon entity.
    /// </summary>
    public NetEntity NetEntity;

    /// <summary>
    /// The weapon's current coordinates.
    /// </summary>
    public NetCoordinates Coordinates;

    /// <summary>
    /// Display name of the weapon.
    /// </summary>
    public string Name;

    /// <summary>
    /// Current ammunition count, if <see cref="HasAmmoCount"/> is true.
    /// </summary>
    public int AmmoCount;

    /// <summary>
    /// Whether <see cref="AmmoCount"/> contains a meaningful value.
    /// </summary>
    public bool HasAmmoCount;

    /// <summary>
    /// Whether the weapon can be reloaded manually.
    /// </summary>
    public bool HasManualReload;

    public FireControllableEntry(
        NetEntity entity,
        NetCoordinates coordinates,
        string name,
        int ammoCount = 0,
        bool hasAmmoCount = false,
        bool hasManualReload = false)
    {
        NetEntity = entity;
        Coordinates = coordinates;
        Name = name;
        AmmoCount = ammoCount;
        HasAmmoCount = hasAmmoCount;
        HasManualReload = hasManualReload;
    }
}
