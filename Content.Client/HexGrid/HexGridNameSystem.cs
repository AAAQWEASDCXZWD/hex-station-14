using System.Collections.Generic;
using Content.Shared.HexGrid;
using Content.Shared.HexGrid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Client._Hex;

/// <summary>
/// Supplies prototype and server-updated hex names to client UI.
/// </summary>
public sealed class HexGridNameSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly Dictionary<HexCoordinates, string> _names = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<HexGridNamesUpdatedEvent>(OnNamesUpdated);

        foreach (var set in _prototype.EnumeratePrototypes<HexNameSetPrototype>())
        {
            foreach (var hex in set.Hexes)
                _names[new HexCoordinates(hex.Q, hex.R)] = hex.Name;
        }
    }

    public string GetName(HexCoordinates coordinates)
    {
        return _names.GetValueOrDefault(coordinates, string.Empty);
    }

    private void OnNamesUpdated(HexGridNamesUpdatedEvent args, EntitySessionEventArgs session)
    {
        _names.Clear();
        foreach (var hex in args.Names)
            _names[new HexCoordinates(hex.Q, hex.R)] = hex.Name;
    }
}
