using Robust.Shared.Maths;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Hex.Factory;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Hex.Factory;

public sealed class FactoryManipulatorSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly StorageSystem _storage = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private readonly HashSet<EntityUid> _lookupBuffer = new();
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        SubscribeLocalEvent<FactoryManipulatorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FactoryManipulatorComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, FactoryManipulatorComponent component, MapInitEvent args)
    {
        component.HandContainer = _container.EnsureContainer<ContainerSlot>(uid, component.HandContainerId);
    }

    private void OnPowerChanged(EntityUid uid, FactoryManipulatorComponent component, ref PowerChangedEvent args)
    {
        component.Powered = args.Powered;
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FactoryManipulatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var manipulator, out var xform))
        {
            if (!manipulator.Powered || !xform.Anchored || xform.GridUid is not { } gridUid)
                continue;

            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            if (manipulator.HandContainer is not { } handContainer)
                continue;

            switch (manipulator.State)
            {
                case ManipulatorState.Idle:
                {
                    if (manipulator.SleepAccumulator > 0f)
                    {
                        manipulator.SleepAccumulator -= frameTime;
                        continue;
                    }

                    // Check if hand already has an item unexpectedly
                    if (handContainer.ContainedEntity is not null)
                    {
                        manipulator.State = ManipulatorState.MovingToTarget;
                        manipulator.SwingProgress = 0f;
                        Dirty(uid, manipulator);
                        continue;
                    }

                    var currentTile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
                    var dir = xform.LocalRotation.GetCardinalDir();
                    var srcTile = GetRotatedTile(currentTile, manipulator.SourceOffset, dir);
                    var dstTile = GetRotatedTile(currentTile, manipulator.TargetOffset, dir);

                    var srcCoords = _mapSystem.GridTileToLocal(gridUid, grid, srcTile);
                    var dstCoords = _mapSystem.GridTileToLocal(gridUid, grid, dstTile);

                    var candidate = FindSourceItem(uid, manipulator, gridUid, grid, srcTile, srcCoords);
                    if (candidate == null)
                    {
                        manipulator.SleepAccumulator = manipulator.SleepInterval;
                        continue;
                    }

                    // Verify target can accept this item before picking it up
                    if (!CanTargetAccept(uid, candidate.Value, gridUid, grid, dstTile, dstCoords))
                    {
                        manipulator.SleepAccumulator = manipulator.SleepInterval;
                        continue;
                    }

                    // Pick up the candidate item
                    if (TryPickupItem(uid, manipulator, handContainer, candidate.Value))
                    {
                        manipulator.State = ManipulatorState.MovingToTarget;
                        manipulator.SwingProgress = 0f;
                        _appearance.SetData(uid, ManipulatorVisuals.Holding, true);
                        Dirty(uid, manipulator);
                    }
                    else
                    {
                        manipulator.SleepAccumulator = manipulator.SleepInterval;
                    }
                    break;
                }

                case ManipulatorState.MovingToTarget:
                {
                    if (handContainer.ContainedEntity is null)
                    {
                        manipulator.State = ManipulatorState.Returning;
                        manipulator.SwingProgress = 0f;
                        _appearance.SetData(uid, ManipulatorVisuals.Holding, false);
                        Dirty(uid, manipulator);
                        continue;
                    }

                    manipulator.SwingProgress += frameTime / Math.Max(0.05f, manipulator.SwingDuration);
                    if (manipulator.SwingProgress >= 1f)
                    {
                        manipulator.State = ManipulatorState.DroppingOff;
                        Dirty(uid, manipulator);
                    }
                    break;
                }

                case ManipulatorState.DroppingOff:
                {
                    if (handContainer.ContainedEntity is { } heldItem)
                    {
                        var currentTile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
                        var dir = xform.LocalRotation.GetCardinalDir();
                        var dstTile = GetRotatedTile(currentTile, manipulator.TargetOffset, dir);
                        var dstCoords = _mapSystem.GridTileToLocal(gridUid, grid, dstTile);

                        DropOffItem(uid, manipulator, handContainer, heldItem, gridUid, grid, dstTile, dstCoords);
                    }

                    manipulator.State = ManipulatorState.Returning;
                    manipulator.SwingProgress = 0f;
                    _appearance.SetData(uid, ManipulatorVisuals.Holding, false);
                    Dirty(uid, manipulator);
                    break;
                }

                case ManipulatorState.Returning:
                {
                    manipulator.SwingProgress += frameTime / Math.Max(0.05f, manipulator.SwingDuration);
                    if (manipulator.SwingProgress >= 1f)
                    {
                        manipulator.State = ManipulatorState.Idle;
                        manipulator.SwingProgress = 0f;
                        manipulator.SleepAccumulator = 0f;
                        Dirty(uid, manipulator);
                    }
                    break;
                }
            }
        }
    }

    private static Vector2i GetRotatedTile(Vector2i originTile, Vector2i localOffset, Direction dir)
    {
        var forward = dir.ToIntVec();
        var right = new Vector2i(-forward.Y, forward.X);
        var offset = forward * localOffset.Y + right * localOffset.X;
        return originTile + offset;
    }

    private EntityUid? FindSourceItem(EntityUid manipulatorUid, FactoryManipulatorComponent comp, EntityUid gridUid, MapGridComponent grid, Vector2i srcTile, EntityCoordinates srcCoords)
    {
        // 1. Check anchored entities at source tile (machines, storage, containers)
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, srcTile);
        while (anchoredEnumerator.MoveNext(out var anchored))
        {
            if (HasComp<FactoryAutomationBlockedComponent>(anchored))
                continue;

            // Check factory machine output buffer
            if (TryComp<FactoryMachineComponent>(anchored, out var machineComp))
            {
                if (!machineComp.AllowManipulatorExtraction)
                    continue;

                if (_container.TryGetContainer(anchored.Value, machineComp.OutputBufferId, out var outputContainer) &&
                    outputContainer.ContainedEntities.Count > 0)
                {
                    foreach (var item in outputContainer.ContainedEntities)
                    {
                        if (IsValidItem(comp, item))
                            return item;
                    }
                }
            }

            // Check standard storage / crate
            if (TryComp<StorageComponent>(anchored, out var storage))
            {
                foreach (var item in storage.StoredItems.Keys)
                {
                    if (IsValidItem(comp, item))
                        return item;
                }
            }
        }

        // 2. Check loose / conveyed items on the source tile
        _lookupBuffer.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, srcTile, _lookupBuffer, 0f, flags: LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var ent in _lookupBuffer)
        {
            if (ent == manipulatorUid)
                continue;

            if (_xformQuery.TryComp(ent, out var entXform) && entXform.Anchored)
                continue;

            if (IsValidItem(comp, ent))
                return ent;
        }

        return null;
    }

    private bool IsValidItem(FactoryManipulatorComponent comp, EntityUid item)
    {
        if (TerminatingOrDeleted(item))
            return false;

        if (comp.Whitelist != null && !_whitelist.IsWhitelistPass(comp.Whitelist, item))
            return false;

        if (comp.Blacklist != null && _whitelist.IsBlacklistPass(comp.Blacklist, item))
            return false;

        return HasComp<ItemComponent>(item) || HasComp<MaterialComponent>(item);
    }

    private bool CanTargetAccept(EntityUid manipulatorUid, EntityUid item, EntityUid gridUid, MapGridComponent grid, Vector2i dstTile, EntityCoordinates dstCoords)
    {
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, dstTile);
        var anyAnchored = false;

        while (anchoredEnumerator.MoveNext(out var target))
        {
            anyAnchored = true;

            // Automation blocked check
            if (HasComp<FactoryAutomationBlockedComponent>(target))
                return false;

            // 1. MaterialStorageComponent (All SS14 ore/material machines like OreProcessor, Sheetifier, Autolathe)
            if (TryComp<MaterialStorageComponent>(target, out var materialStorage))
            {
                if (TryComp<MaterialComponent>(item, out var material) &&
                    TryComp<PhysicalCompositionComponent>(item, out var composition))
                {
                    if (_whitelist.IsWhitelistFail(materialStorage.Whitelist, item))
                        continue;

                    var multiplier = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
                    var totalVolume = 0;
                    var canFit = true;

                    foreach (var (mat, vol) in composition.MaterialComposition)
                    {
                        if (!_materialStorage.CanChangeMaterialAmount(target.Value, mat, vol * multiplier, materialStorage))
                        {
                            canFit = false;
                            break;
                        }
                        totalVolume += vol * multiplier;
                    }

                    if (canFit && _materialStorage.CanTakeVolume(target.Value, totalVolume, materialStorage, localOnly: true))
                        return true;
                }
            }

            // 2. FactoryMachine input buffer
            if (TryComp<FactoryMachineComponent>(target, out var machineComp))
            {
                if (machineComp.AllowManipulatorInput)
                    return true;
            }

            // 3. Storage / Crate
            if (TryComp<StorageComponent>(target, out var storage))
            {
                if (_storage.CanInsert(target.Value, item, out _, storageComp: storage))
                    return true;
            }
        }

        // If no container/machine blocked it, target tile (floor or conveyor) can accept drop
        return true;
    }

    private bool TryPickupItem(EntityUid manipulatorUid, FactoryManipulatorComponent comp, ContainerSlot handContainer, EntityUid item)
    {
        if (TerminatingOrDeleted(item))
            return false;

        // If contained in a storage/container, remove it first
        if (_container.TryGetContainingContainer(item, out var parentContainer))
        {
            _container.Remove(item, parentContainer);
        }

        return _container.Insert(item, handContainer);
    }

    private void DropOffItem(EntityUid manipulatorUid, FactoryManipulatorComponent comp, ContainerSlot handContainer, EntityUid heldItem, EntityUid gridUid, MapGridComponent grid, Vector2i dstTile, EntityCoordinates dstCoords)
    {
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, dstTile);
        while (anchoredEnumerator.MoveNext(out var target))
        {
            if (HasComp<FactoryAutomationBlockedComponent>(target))
                continue;

            // 1. MaterialStorageComponent (Smelters, Ore Processors, Lathes)
            if (TryComp<MaterialStorageComponent>(target, out var materialStorage) &&
                HasComp<MaterialComponent>(heldItem))
            {
                // Remove from hand so TryInsertMaterialEntity can process and delete it
                _container.Remove(heldItem, handContainer);

                if (_materialStorage.TryInsertMaterialEntity(manipulatorUid, heldItem, target.Value, materialStorage))
                {
                    return; // Successfully absorbed into material storage
                }

                // If insertion failed, put back in hand or drop
                _container.Insert(heldItem, handContainer);
            }

            // 2. FactoryMachine input buffer
            if (TryComp<FactoryMachineComponent>(target, out var machineComp) && machineComp.AllowManipulatorInput)
            {
                var inputContainer = _container.EnsureContainer<Container>(target.Value, machineComp.InputBufferId);
                _container.Remove(heldItem, handContainer);
                if (_container.Insert(heldItem, inputContainer))
                    return;

                _container.Insert(heldItem, handContainer);
            }

            // 3. Storage
            if (TryComp<StorageComponent>(target, out var storage))
            {
                _container.Remove(heldItem, handContainer);
                if (_storage.Insert(target.Value, heldItem, out _, user: manipulatorUid, storageComp: storage))
                    return;

                _container.Insert(heldItem, handContainer);
            }
        }

        // 4. Default: Drop directly onto destination tile (conveyor or floor)
        _container.Remove(heldItem, handContainer);
        _transform.SetCoordinates(heldItem, dstCoords);

        if (TryComp<PhysicsComponent>(heldItem, out var physics))
        {
            _physics.WakeBody(heldItem, body: physics);
        }
    }
}
