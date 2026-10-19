using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Containers.ItemSlots;
using JetBrains.Annotations;

namespace Content.Shared._NF.Shipyard.Systems;

[UsedImplicitly]
public abstract class SharedShuttleIdCardConsoleSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private readonly ILogManager _log = default!;

    public const string Sawmill = "shuttleidconsole";
    protected ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _log.GetSawmill(Sawmill);

        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ShuttleIdCardConsoleComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(EntityUid uid, ShuttleIdCardConsoleComponent component, ComponentInit args)
    {
        _itemSlotsSystem.AddItemSlot(uid, ShuttleIdCardConsoleComponent.PrivilegedIdCardSlotId, component.PrivilegedIdSlot);
        _itemSlotsSystem.AddItemSlot(uid, ShuttleIdCardConsoleComponent.TargetIdCardSlotId, component.TargetIdSlot);
    }

    private void OnComponentRemove(EntityUid uid, ShuttleIdCardConsoleComponent component, ComponentRemove args)
    {
        _itemSlotsSystem.RemoveItemSlot(uid, component.PrivilegedIdSlot);
        _itemSlotsSystem.RemoveItemSlot(uid, component.TargetIdSlot);
    }
}
