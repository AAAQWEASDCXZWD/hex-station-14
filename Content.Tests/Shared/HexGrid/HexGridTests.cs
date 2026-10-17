using System;
using System.Linq;
using System.Numerics;
using Content.Shared.HexGrid;
using NUnit.Framework;

namespace Content.Tests.Shared.HexGrid;

[TestFixture]
public sealed class HexGridTests
{
    [Test]
    public void StandardAreaContainsSevenHexes()
    {
        var hexes = Content.Shared.HexGrid.HexGrid.GetHexesInRadius(Content.Shared.HexGrid.HexGrid.StandardHexRadius).ToArray();

        Assert.That(hexes, Has.Length.EqualTo(7));
        Assert.That(hexes, Does.Contain(new HexCoordinates(0, 0)));
    }

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(-1, 1)]
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(1, -1)]
    public void CenterRoundTripsToItsHex(int q, int r)
    {
        var hex = new HexCoordinates(q, r);

        var result = Content.Shared.HexGrid.HexGrid.GetCoordinates(Content.Shared.HexGrid.HexGrid.GetCenter(hex));

        Assert.That(result, Is.EqualTo(hex));
    }

    [Test]
    public void HexHasSixVerticesAtConfiguredRadius()
    {
        var center = Content.Shared.HexGrid.HexGrid.GetCenter(new HexCoordinates(0, 0));
        var vertices = Content.Shared.HexGrid.HexGrid.GetVertices(new HexCoordinates(0, 0)).ToArray();

        Assert.That(vertices, Has.Length.EqualTo(6));
        Assert.That(vertices, Is.All.Matches<Vector2>(point =>
            MathF.Abs(Vector2.Distance(point, center) - Content.Shared.HexGrid.HexGrid.HexRadius) < 0.001f));
    }
}
