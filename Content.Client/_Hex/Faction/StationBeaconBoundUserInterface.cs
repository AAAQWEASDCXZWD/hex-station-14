using System;
using Content.Client._Hex.Faction.UI;
using Content.Shared._Hex.Faction;
using Robust.Client.UserInterface;

namespace Content.Client._Hex.Faction;

public sealed class StationBeaconBoundUserInterface : BoundUserInterface
{
    private StationBeaconWindow? _window;

    public StationBeaconBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<StationBeaconWindow>();
        _window.OnUpdateRequested += label => SendMessage(new StationBeaconUpdateMessage(label));

        if (EntMan.TryGetComponent<StationBeaconComponent>(Owner, out var beacon))
        {
            _window.UpdateState(new StationBeaconUiState(
                beacon.BeaconName,
                beacon.FactionName,
                beacon.Active,
                beacon.HasClaim,
                beacon.HexQ,
                beacon.HexR));
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is StationBeaconUiState beaconState)
            _window?.UpdateState(beaconState);
    }
}
