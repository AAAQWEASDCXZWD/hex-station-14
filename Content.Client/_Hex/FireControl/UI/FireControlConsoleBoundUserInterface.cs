using Content.Shared._Hex.FireControl;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Hex.FireControl.UI;

[UsedImplicitly]
public sealed class FireControlConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FireControlWindow? _window;

    public FireControlConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var window = this.CreateWindow<FireControlWindow>();
        _window = window;

        window.OnServerRefresh += () => SendMessage(new FireControlConsoleRefreshServerMessage());

        window.Radar.OnRadarClick += coordinates =>
        {
            var selected = window.GetSelectedWeapons();
            if (selected.Count == 0)
                return;

            SendMessage(new FireControlConsoleFireMessage(selected, EntMan.GetNetCoordinates(coordinates)));
        };

        window.Radar.SetConsole(Owner);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FireControlConsoleBoundInterfaceState castState)
            return;

        _window?.UpdateStatus(castState);
    }
}
