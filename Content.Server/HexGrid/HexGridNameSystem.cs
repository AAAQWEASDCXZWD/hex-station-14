using System.Collections.Generic;
using System.Linq;
using Content.Shared.HexGrid;
using Content.Shared.HexGrid.Prototypes;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.HexGrid;

/// <summary>
/// Holds prototype-defined hex names and distributes runtime name changes to clients.
/// </summary>
public sealed class HexGridNameSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly Dictionary<HexCoordinates, string> _names = new();

    public override void Initialize()
    {
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
        LoadPrototypeNames();
    }

    public override void Shutdown()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public void SetName(HexCoordinates coordinates, string name)
    {
        _names[coordinates] = name;
        RaiseNetworkEvent(GetNamesUpdatedEvent());
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Connected)
            RaiseNetworkEvent(GetNamesUpdatedEvent(), args.Session);
    }

    private void LoadPrototypeNames()
    {
        foreach (var set in _prototype.EnumeratePrototypes<HexNameSetPrototype>())
        {
            foreach (var hex in set.Hexes)
                _names[new HexCoordinates(hex.Q, hex.R)] = hex.Name;
        }
    }

    private HexGridNamesUpdatedEvent GetNamesUpdatedEvent()
    {
        var names = _names
            .Select(pair => new HexGridName(pair.Key.Q, pair.Key.R, pair.Value))
            .ToList();
        return new HexGridNamesUpdatedEvent(names);
    }
}
