using System.Linq;
using System.Numerics;
using Content.Client._Hex;
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
    private static readonly Color CurrentHexEdgeColor = Color.Yellow.WithAlpha(0.9f);
    private static readonly Color HexLabelColor = Color.White.WithAlpha(0.85f);

    private void DrawHexOverlay(DrawingHandleScreen handle)
    {
        if (ViewingMap == MapId.Nullspace)
            return;

        var matty = Matrix3Helpers.CreateInverseTransform(Offset, Angle.Zero);
        HexCoordinates? currentHex = null;
        if (_shuttleEntity is { } shuttle &&
            EntManager.TryGetComponent(shuttle, out TransformComponent? shuttleTransform) &&
            shuttleTransform.MapID == ViewingMap)
        {
            currentHex = HexGrid.GetCoordinates(_xformSystem.GetMapCoordinates(shuttle).Position);
        }

        var names = EntManager.System<HexGridNameSystem>();

        foreach (var hex in HexGrid.GetHexesInRadius(HexOverlayRadius))
        {
            var verts = HexGrid.GetVertices(hex)
                .Select(v => HexMapToScreen(v, matty))
                .ToArray();

            var isCurrentHex = hex == currentHex;
            var edgeColor = isCurrentHex ? CurrentHexEdgeColor : HexEdgeColor;
            for (var i = 0; i < 6; i++)
                DrawHexEdge(handle, verts[i], verts[(i + 1) % 6], edgeColor, isCurrentHex);

            var center = HexMapToScreen(HexGrid.GetCenter(hex), matty);
            DrawHexLabel(handle, center, verts, hex, names.GetName(hex));
        }

        // Маркер для точки 0:0 мапи
        var origin = HexMapToScreen(Vector2.Zero, matty);
        handle.DrawCircle(origin, 3f, CurrentHexEdgeColor);
    }

    private void DrawHexLabel(
        DrawingHandleScreen handle,
        Vector2 center,
        Vector2[] vertices,
        HexCoordinates hex,
        string name)
    {
        var coordinates = $"{hex.Q} {hex.R}";
        var coordinatesDimensions = handle.GetDimensions(_font, coordinates, 1f);
        var nameDimensions = string.IsNullOrWhiteSpace(name)
            ? Vector2.Zero
            : handle.GetDimensions(_font, name, 1f);
        var dimensions = new Vector2(
            MathF.Max(coordinatesDimensions.X, nameDimensions.X),
            coordinatesDimensions.Y + nameDimensions.Y);
        var radius = Vector2.Distance(center, vertices[0]);
        var maxTextDiagonal = radius * MathF.Sqrt(3f) * 0.8f;
        var scale = MathF.Min(1f, maxTextDiagonal / dimensions.Length());
        var top = center.Y - dimensions.Y * scale / 2f;
        handle.DrawString(
            _font,
            new Vector2(center.X - coordinatesDimensions.X * scale / 2f, top),
            coordinates,
            scale,
            HexLabelColor);

        if (!string.IsNullOrWhiteSpace(name))
        {
            handle.DrawString(
                _font,
                new Vector2(center.X - nameDimensions.X * scale / 2f, top + coordinatesDimensions.Y * scale),
                name,
                scale,
                HexLabelColor);
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
