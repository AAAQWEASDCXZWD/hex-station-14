using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Hex.Shipyard.Components;

/// <summary>
/// A blueprint of a shuttle grid. The grid is snapshotted when the blueprint is printed so the
/// client can project it without keeping the source grid in PVS.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipBlueprintComponent : Component
{
    /// <summary>
    /// The grid snapshot used to build the client-side hologram.
    /// </summary>
    [DataField, AutoNetworkedField]
    public GridBlueprintData Data = new();

    /// <summary>
    /// Name of the source shuttle, used for examine and printing messages.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ShuttleName = string.Empty;
}

/// <summary>
/// A compact, networked snapshot of a grid: every tile plus every entity that can be projected.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class GridBlueprintData
{
    [DataField]
    public List<GridBlueprintTile> Tiles = new();

    [DataField]
    public List<GridBlueprintEntity> Entities = new();

    /// <summary>
    /// Tile size of the source grid in world units.
    /// </summary>
    [DataField]
    public float TileSize = 1f;

    /// <summary>
    /// Centre of the source grid in grid-local coordinates. The projection is centred on this point
    /// so it appears around the clicked tile instead of at the grid's origin.
    /// </summary>
    [DataField]
    public Vector2 Center;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class GridBlueprintTile
{
    [DataField]
    public Vector2i Indices;

    [DataField]
    public int TileId;

    /// <summary>
    /// Layer this tile belongs to. See <see cref="GridHologramLayer"/> for the stacking order.
    /// </summary>
    [DataField]
    public GridHologramLayer Layer = GridHologramLayer.Floor;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class GridBlueprintEntity
{
    /// <summary>
    /// Entity prototype to project. The client uses the prototype's sprite layers, falling back to
    /// the prototype icon for structures whose sprite is built from neighbours at runtime.
    /// </summary>
    [DataField]
    public string Prototype = string.Empty;

    /// <summary>
    /// Grid-local position of the entity.
    /// </summary>
    [DataField]
    public Vector2 Position;

    /// <summary>
    /// Grid-local rotation of the entity, in radians.
    /// </summary>
    [DataField]
    public float Rotation;

    /// <summary>
    /// Fallback layer used when the prototype must be drawn from its icon.
    /// </summary>
    [DataField]
    public GridHologramLayer Layer = GridHologramLayer.Objects;
}

/// <summary>
/// Explicit stacking order for grid hologram parts.
/// </summary>
/// <remarks>
/// Lower values are drawn first and therefore appear underneath higher values. When more hologram
/// part types are added, extend this enum instead of inventing per-system draw depths so that every
/// part has one predictable order.
/// </remarks>
public enum GridHologramLayer : byte
{
    /// <summary>Under-floor structures such as cables and pipes.</summary>
    SubFloor = 0,

    /// <summary>The main tile grid.</summary>
    Floor = 1,

    /// <summary>Objects lying directly on the floor.</summary>
    FloorObjects = 2,

    /// <summary>Normal structures and furniture.</summary>
    Objects = 3,

    /// <summary>Tall structures that should draw above normal objects.</summary>
    HighObjects = 4,

    /// <summary>Walls and other full-cell structures.</summary>
    Walls = 5,

    /// <summary>Effects and labels that must draw above everything else.</summary>
    Overlay = 6,
}
