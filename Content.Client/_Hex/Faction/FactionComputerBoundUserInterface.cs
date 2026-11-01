using System;
using Content.Client._Hex.Faction.UI;
using Content.Shared._Hex.Faction;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._Hex.Faction;

/// <summary>
///     Bound user interface of the faction terminal: the main window plus the three detail windows that
///     can be opened separately.
/// </summary>
public sealed class FactionComputerBoundUserInterface : BoundUserInterface
{
    private FactionComputerMenu? _menu;
    private FactionCustomizationWindow? _customizationWindow;
    private FactionMembersWindow? _membersWindow;
    private FactionProgressWindow? _progressWindow;
    private FactionComputerUiState? _state;

    public FactionComputerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<FactionComputerMenu>();
        _menu.OnCreateRequested += name => SendMessage(new FactionCreateMessage(name));
        _menu.OnLinkRequested += factionId => SendMessage(new FactionLinkMessage(factionId));
        _menu.OnIdCardPressed += () => SendMessage(new ItemSlotButtonPressedEvent(FactionDefaults.IdSlot));
        _menu.OnOpenCustomization += OpenCustomization;
        _menu.OnOpenMembers += OpenMembers;
        _menu.OnOpenProgress += OpenProgress;

        if (_state != null)
            _menu.UpdateState(_state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FactionComputerUiState factionState)
            return;

        _state = factionState;
        _menu?.UpdateState(factionState);
        _customizationWindow?.UpdateState(factionState);
        _membersWindow?.UpdateState(factionState);
    }

    private void OpenCustomization()
    {
        if (_state == null)
            return;

        if (_customizationWindow == null)
        {
            _customizationWindow = new FactionCustomizationWindow();
            _customizationWindow.OnSaveRequested += (name, description, color, photo, customPhotoData) =>
                SendMessage(new FactionCustomizeMessage(name, description, color, photo, customPhotoData));
        }

        _customizationWindow.UpdateState(_state);
        _customizationWindow.OpenCentered();
    }

    private void OpenMembers()
    {
        if (_state == null)
            return;

        _membersWindow ??= new FactionMembersWindow();
        _membersWindow.UpdateState(_state);
        _membersWindow.OpenCentered();
    }

    private void OpenProgress()
    {
        _progressWindow ??= new FactionProgressWindow();
        _progressWindow.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _customizationWindow?.Close();
            _membersWindow?.Close();
            _progressWindow?.Close();
            _customizationWindow = null;
            _membersWindow = null;
            _progressWindow = null;
        }

        base.Dispose(disposing);
    }
}
