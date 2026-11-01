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

    private readonly Dictionary<HexCoordinates, (string Name, string? ColorHex)> _hexData = new();

    public override void Initialize()
    {
        _player.PlayerStatusChanged += OnPlayerStatusChanged;
        LoadPrototypeNames();
    }

    public override void Shutdown()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public void SetHex(HexCoordinates coordinates, string name, string? colorHex = null)
    {
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(colorHex))
            _hexData.Remove(coordinates);
        else
            _hexData[coordinates] = (name, colorHex);

        RaiseNetworkEvent(GetNamesUpdatedEvent());
    }

    public void SetName(HexCoordinates coordinates, string name)
    {
        var existingColor = _hexData.TryGetValue(coordinates, out var data) ? data.ColorHex : null;
        SetHex(coordinates, name, existingColor);
    }

    public void SetColor(HexCoordinates coordinates, string? colorHex)
    {
        var existingName = _hexData.TryGetValue(coordinates, out var data) ? data.Name : string.Empty;
        SetHex(coordinates, existingName, colorHex);
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
                _hexData[new HexCoordinates(hex.Q, hex.R)] = (hex.Name, null);
        }
    }

    private HexGridNamesUpdatedEvent GetNamesUpdatedEvent()
    {
        var names = _hexData
            .Select(pair => new HexGridName(pair.Key.Q, pair.Key.R, pair.Value.Name, pair.Value.ColorHex))
            .ToList();
        return new HexGridNamesUpdatedEvent(names);
    }
}
