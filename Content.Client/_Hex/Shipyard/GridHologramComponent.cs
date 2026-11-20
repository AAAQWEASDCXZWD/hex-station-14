using Content.Shared._Hex.Shipyard.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._Hex.Shipyard;

/// <summary>
/// Marks a client-side entity as the root of a projected grid hologram and tracks
/// the ghost entities that make up each layer so they can be filtered, toggled, and cleaned up together.
/// </summary>
[RegisterComponent]
public sealed partial class GridHologramComponent : Component
{
    /// <summary>
    /// The blueprint entity that created this projection.
    /// </summary>
    public EntityUid? SourceBlueprint;

    /// <summary>
    /// Client-side grid that renders the hologram's tile layer in one pass.
    /// </summary>
    public EntityUid? TileGrid;

    /// <summary>
    /// Ghost entities for subfloor wires, cables, and pipes.
    /// </summary>
    public readonly List<EntityUid> WireGhosts = new();

    /// <summary>
    /// Ghost entities for objects, walls, fixtures, and machinery.
    /// </summary>
    public readonly List<EntityUid> ObjectGhosts = new();

    /// <summary>
    /// All ghosts spawned for this projection, tracked for cleanup.
    /// </summary>
    public readonly List<EntityUid> AllGhosts = new();

    /// <summary>
    /// Full tile snapshot to populate the client grid with actual ship tiles.
    /// </summary>
    public readonly List<(Vector2i Indices, Tile Tile)> CachedTiles = new();

    /// <summary>
    /// Empty tile list used when toggling off grid tile rendering.
    /// </summary>
    public readonly List<(Vector2i Indices, Tile Tile)> EmptyTiles = new();

    /// <summary>
    /// Current display mode determining which layers are visible.
    /// </summary>
    public BlueprintDisplayMode Mode = BlueprintDisplayMode.Objects;

    /// <summary>
    /// Global visibility toggle state.
    /// </summary>
    public bool Visible = true;
}
