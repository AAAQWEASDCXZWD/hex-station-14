using System;
using System.Collections.Generic;
using System.Numerics;

namespace Content.Shared.HexGrid;

/// <summary>
/// An axial coordinate in the global, pointy-top hex grid.
/// </summary>
public readonly record struct HexCoordinates(int Q, int R)
{
    /// <summary>
    /// Hex distance from the grid origin.
    /// </summary>
    public int DistanceFromOrigin => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(Q + R)) / 2;

    public override string ToString() => $"({Q}, {R})";
}

/// <summary>
/// Converts between map positions and the project's mathematical hex grid.
/// The grid is pointy-top, centred at map coordinate (0, 0), and each hex has a 30 metre circumradius.
/// </summary>
public static class HexGrid
{
    /// <summary>
    /// Distance from a hex centre to each of its vertices, in map coordinates.
    /// </summary>
    public const float HexRadius = 30f;

    /// <summary>
    /// The central hex and its six direct neighbours.
    /// </summary>
    public const int StandardHexRadius = 1;

    private const float Sqrt3 = 1.7320508075688772f;

    /// <summary>
    /// Gets the axial hex containing a map position.
    /// </summary>
    public static HexCoordinates GetCoordinates(Vector2 position)
    {
        var q = (Sqrt3 / 3f * position.X - position.Y / 3f) / HexRadius;
        var r = (2f / 3f * position.Y) / HexRadius;
        return RoundAxial(q, r);
    }

    /// <summary>
    /// Gets the centre of an axial hex in map coordinates.
    /// </summary>
    public static Vector2 GetCenter(HexCoordinates coordinates)
    {
        var x = HexRadius * Sqrt3 * (coordinates.Q + coordinates.R / 2f);
        var y = HexRadius * 1.5f * coordinates.R;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Enumerates every hex whose distance from the origin is at most <paramref name="radius"/>.
    /// </summary>
    public static IEnumerable<HexCoordinates> GetHexesInRadius(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        for (var q = -radius; q <= radius; q++)
        {
            var minR = Math.Max(-radius, -q - radius);
            var maxR = Math.Min(radius, -q + radius);

            for (var r = minR; r <= maxR; r++)
            {
                yield return new HexCoordinates(q, r);
            }
        }
    }

    /// <summary>
    /// Gets the six vertex positions of a hex, clockwise from its upper-right vertex.
    /// </summary>
    public static IEnumerable<Vector2> GetVertices(HexCoordinates coordinates)
    {
        var center = GetCenter(coordinates);
        for (var i = 0; i < 6; i++)
        {
            var angle = MathF.PI / 6f + i * MathF.PI / 3f;
            yield return center + HexRadius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }

    private static HexCoordinates RoundAxial(float q, float r)
    {
        var x = q;
        var z = r;
        var y = -x - z;

        var roundedX = MathF.Round(x);
        var roundedY = MathF.Round(y);
        var roundedZ = MathF.Round(z);

        var xDifference = MathF.Abs(roundedX - x);
        var yDifference = MathF.Abs(roundedY - y);
        var zDifference = MathF.Abs(roundedZ - z);

        if (xDifference > yDifference && xDifference > zDifference)
            roundedX = -roundedY - roundedZ;
        else if (zDifference > yDifference)
            roundedZ = -roundedX - roundedY;

        return new HexCoordinates((int) roundedX, (int) roundedZ);
    }
}
