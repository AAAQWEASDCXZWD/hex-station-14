using System.Linq;
using System.Numerics;
using Content.Client._Hex;
using Content.Shared.HexGrid;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private const int HexOverlayRadius = HexGrid.StandardHexRadius;
    private static readonly Color HexNavEdgeColor = Color.Cyan.WithAlpha(0.35f);
    private static readonly Color CurrentHexEdgeColor = Color.Yellow.WithAlpha(0.9f);
    private static readonly Color HexLabelColor = Color.White.WithAlpha(0.85f);

    /// <summary>
    /// Малює сітку гексів у NAV. Викликати з Draw після шаттла, до блипів.
    /// </summary>
    private void DrawHexOverlay(DrawingHandleScreen handle, Matrix3x2 worldToShuttle, Matrix3x2 shuttleToView)
    {
        var worldToView = Matrix3x2.Multiply(worldToShuttle, shuttleToView);
        var currentHex = HexGrid.GetCoordinates(_transform.ToMapCoordinates(_coordinates!.Value).Position);
        var names = EntManager.System<HexGridNameSystem>();

        foreach (var hex in HexGrid.GetHexesInRadius(HexOverlayRadius))
        {
            var verts = HexGrid.GetVertices(hex)
                .Select(v => Vector2.Transform(v, worldToView))
                .ToArray();

            var isCurrentHex = hex == currentHex;
            var edgeColor = isCurrentHex ? CurrentHexEdgeColor : HexNavEdgeColor;
            for (var i = 0; i < 6; i++)
                DrawHexEdge(handle, verts[i], verts[(i + 1) % 6], edgeColor, isCurrentHex);

            var center = Vector2.Transform(HexGrid.GetCenter(hex), worldToView);
            DrawHexLabel(handle, Font, center, verts, hex, names.GetName(hex), HexLabelColor);
        }
    }

    private static void DrawHexEdge(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, bool thick)
    {
        handle.DrawLine(start, end, color);
        if (!thick)
            return;

        var direction = end - start;
        var normal = new Vector2(-direction.Y, direction.X) / direction.Length();
        handle.DrawLine(start + normal, end + normal, color);
        handle.DrawLine(start - normal, end - normal, color);
    }

    private static void DrawHexLabel(
        DrawingHandleScreen handle,
        Font font,
        Vector2 center,
        Vector2[] vertices,
        HexCoordinates hex,
        string name,
        Color color)
    {
        var coordinates = $"{hex.Q} {hex.R}";
        var coordinatesDimensions = handle.GetDimensions(font, coordinates, 1f);
        var nameDimensions = string.IsNullOrWhiteSpace(name)
            ? Vector2.Zero
            : handle.GetDimensions(font, name, 1f);
        var dimensions = new Vector2(
            MathF.Max(coordinatesDimensions.X, nameDimensions.X),
            coordinatesDimensions.Y + nameDimensions.Y);
        var radius = Vector2.Distance(center, vertices[0]);
        var maxTextDiagonal = radius * MathF.Sqrt(3f) * 0.8f;
        var scale = MathF.Min(1f, maxTextDiagonal / dimensions.Length());
        var top = center.Y - dimensions.Y * scale / 2f;
        handle.DrawString(
            font,
            new Vector2(center.X - coordinatesDimensions.X * scale / 2f, top),
            coordinates,
            scale,
            color);

        if (!string.IsNullOrWhiteSpace(name))
        {
            handle.DrawString(
                font,
                new Vector2(center.X - nameDimensions.X * scale / 2f, top + coordinatesDimensions.Y * scale),
                name,
                scale,
                color);
        }
    }
}
