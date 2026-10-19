using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._NF.Shipyard.UI;

public sealed class ShuttleIdCardConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private ShuttleIdCardConsoleWindow? _window;

    public ShuttleIdCardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        List<ProtoId<AccessLevelPrototype>> accessLevels;
        if (EntMan.TryGetComponent<ShuttleIdCardConsoleComponent>(Owner, out var consoleComp))
        {
            accessLevels = consoleComp.AccessLevels;
        }
        else
        {
            accessLevels = new List<ProtoId<AccessLevelPrototype>>();
        }

        _window = new ShuttleIdCardConsoleWindow(this, _prototypeManager, accessLevels)
        {
            Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName
        };

        _window.PrivilegedIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(ShuttleIdCardConsoleComponent.PrivilegedIdCardSlotId));

        _window.TargetIdButton.OnPressed += _ =>
            SendMessage(new ItemSlotButtonPressedEvent(ShuttleIdCardConsoleComponent.TargetIdCardSlotId));

        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ShuttleIdCardConsoleBoundUserInterfaceState castState)
        {
            _window?.UpdateState(castState);
        }
    }

    public void SelectShuttle(NetEntity shuttleUid)
    {
        SendMessage(new ShuttleIdCardSelectShuttleMessage(shuttleUid));
    }

    public void SubmitAccess(NetEntity shuttleUid, List<ProtoId<AccessLevelPrototype>> accessList)
    {
        SendMessage(new ShuttleIdCardWriteAccessMessage(shuttleUid, accessList));
    }

    public void RemoveGroup(NetEntity shuttleUid)
    {
        SendMessage(new ShuttleIdCardRemoveGroupMessage(shuttleUid));
    }
}
