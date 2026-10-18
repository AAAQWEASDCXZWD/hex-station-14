using System.Linq;
using System.Numerics;
using Content.Shared.HexGrid;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    /// <summary>
    /// Скільки гексів від 0:0 малювати в кожен напрямок. 0 означає лише центральний гекс.
    /// </summary>
    private const int HexOverlayRadius = HexGrid.StandardHexRadius;
    private static readonly Color HexEdgeColor = Color.Cyan.WithAlpha(0.35f);
    private static readonly Color HexOriginColor = Color.Yellow;

    private void DrawHexOverlay(DrawingHandleScreen handle)
    {
        if (ViewingMap == MapId.Nullspace)
            return;

        var matty = Matrix3Helpers.CreateInverseTransform(Offset, Angle.Zero);

        foreach (var hex in HexGrid.GetHexesInRadius(HexOverlayRadius))
        {
            var verts = HexGrid.GetVertices(hex)
                .Select(v => HexMapToScreen(v, matty))
                .ToArray();

            for (var i = 0; i < 6; i++)
            {
                handle.DrawLine(verts[i], verts[(i + 1) % 6], HexEdgeColor);
            }
        }

        // Маркер для точки 0:0 мапи
        var origin = HexMapToScreen(Vector2.Zero, matty);
        handle.DrawCircle(origin, 3f, HexOriginColor);
    }

    /// <summary>
    /// Переводить координату мапи в піксельні координати контролу тим самим шляхом,
    /// що й решта об'єктів у Draw.
    /// </summary>
    private Vector2 HexMapToScreen(Vector2 mapPos, Matrix3x2 matty)
    {
        var rel = Vector2.Transform(mapPos, matty);
        rel = rel with { Y = -rel.Y };
        return ScalePosition(rel);
    }
}
