using System.Collections.Generic;
using Content.Shared.HexGrid;
using Content.Shared.HexGrid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._Hex;

/// <summary>
/// Supplies prototype and server-updated hex names and faction colors to client UI.
/// </summary>
public sealed class HexGridNameSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly Dictionary<HexCoordinates, (string Name, string? ColorHex)> _hexData = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<HexGridNamesUpdatedEvent>(OnNamesUpdated);

        foreach (var set in _prototype.EnumeratePrototypes<HexNameSetPrototype>())
        {
            foreach (var hex in set.Hexes)
                _hexData[new HexCoordinates(hex.Q, hex.R)] = (hex.Name, null);
        }
    }

    public string GetName(HexCoordinates coordinates)
    {
        return _hexData.TryGetValue(coordinates, out var data) ? data.Name : string.Empty;
    }

    public string? GetColorHex(HexCoordinates coordinates)
    {
        return _hexData.TryGetValue(coordinates, out var data) ? data.ColorHex : null;
    }

    public Color? GetColor(HexCoordinates coordinates)
    {
        var hex = GetColorHex(coordinates);
        if (string.IsNullOrEmpty(hex))
            return null;
        return Color.TryFromHex(hex);
    }

    private void OnNamesUpdated(HexGridNamesUpdatedEvent args, EntitySessionEventArgs session)
    {
        _hexData.Clear();
        foreach (var hex in args.Names)
            _hexData[new HexCoordinates(hex.Q, hex.R)] = (hex.Name, hex.ColorHex);
    }
}
