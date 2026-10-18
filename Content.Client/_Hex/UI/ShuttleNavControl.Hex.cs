using System.Linq;
using System.Numerics;
using Content.Shared.HexGrid;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private const int HexOverlayRadius = HexGrid.StandardHexRadius;
    private static readonly Color HexNavEdgeColor = Color.Cyan.WithAlpha(0.35f);

    /// <summary>
    /// Малює сітку гексів у NAV. Викликати з Draw після шаттла, до блипів.
    /// </summary>
    private void DrawHexOverlay(DrawingHandleScreen handle, Matrix3x2 worldToShuttle, Matrix3x2 shuttleToView)
    {
        var worldToView = Matrix3x2.Multiply(worldToShuttle, shuttleToView);

        foreach (var hex in HexGrid.GetHexesInRadius(HexOverlayRadius))
        {
            var verts = HexGrid.GetVertices(hex)
                .Select(v => Vector2.Transform(v, worldToView))
                .ToArray();

            for (var i = 0; i < 6; i++)
            {
                handle.DrawLine(verts[i], verts[(i + 1) % 6], HexNavEdgeColor);
            }
        }
    }
}
