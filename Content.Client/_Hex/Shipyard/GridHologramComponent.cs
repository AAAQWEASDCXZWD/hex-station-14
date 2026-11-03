using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._Hex.Shipyard;

/// <summary>
/// Marks a client-side construction ghost as the root of a projected grid hologram and tracks
/// the ghost entities that make up the projection so they can be cleaned up together.
/// </summary>
[RegisterComponent]
public sealed partial class GridHologramComponent : Component
{
    public readonly List<EntityUid> Ghosts = new();

    /// <summary>
    /// Client-side grid that renders the hologram's tile layer in one pass.
    /// </summary>
    public EntityUid? TileGrid;

    /// <summary>
    /// Tile indices currently used by the hologram, kept for visibility toggling.
    /// </summary>
    public readonly List<Vector2i> TileIndices = new();

    public Tile HologramTile;

    public bool Visible = true;
}
