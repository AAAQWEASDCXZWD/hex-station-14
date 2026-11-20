using System.Numerics;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Shared._Hex.Factory;
using Content.Shared.Conveyor;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Hex.Factory;

public sealed class FactoryConveyorPortSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FactoryConveyorPortComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var conveyorPorts, out var xform))
        {
            if (!conveyorPorts.Enabled || !xform.Anchored || xform.GridUid is not { } gridUid)
                continue;

            if (!_power.IsPowered(uid))
                continue;

            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var originTile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);

            foreach (var port in conveyorPorts.Ports)
            {
                port.TransferAccumulator -= frameTime;
                if (port.TransferAccumulator > 0f)
                    continue;

                port.TransferAccumulator = port.TransferInterval;

                var portTile = GetRotatedTile(originTile, port.Offset, xform.LocalRotation);
                var portCoords = _mapSystem.GridTileToLocal(gridUid, grid, portTile);

                if (port.PortType == FactoryPortType.Input)
                {
                    ProcessInputPort(uid, port, gridUid, grid, portTile, portCoords);
                }
                else if (port.PortType == FactoryPortType.Output)
                {
                    ProcessOutputPort(uid, port, gridUid, grid, portTile, portCoords);
                }
            }
        }
    }

    private static Vector2i GetRotatedTile(Vector2i originTile, Vector2i localOffset, Angle rotation)
    {
        var rotated = rotation.RotateVec(new Vector2(localOffset.X, localOffset.Y));
        return originTile + new Vector2i((int) MathF.Round(rotated.X), (int) MathF.Round(rotated.Y));
    }

    private void ProcessInputPort(EntityUid machineUid, FactoryConveyorPort port, EntityUid gridUid, MapGridComponent grid, Vector2i portTile, EntityCoordinates portCoords)
    {
        var itemsOnTile = _lookup.GetEntitiesIntersecting(portCoords, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var item in itemsOnTile)
        {
            if (item == machineUid || TerminatingOrDeleted(item) || (_xformQuery.TryComp(item, out var itemXform) && itemXform.Anchored))
                continue;

            if (port.Filter != null && !_whitelist.IsWhitelistPass(port.Filter, item))
                continue;

            if (!HasComp<ItemComponent>(item) && !HasComp<MaterialComponent>(item))
                continue;

            // 1. If machine has MaterialStorageComponent
            if (TryComp<MaterialStorageComponent>(machineUid, out var materialStorage) &&
                HasComp<MaterialComponent>(item))
            {
                if (_materialStorage.TryInsertMaterialEntity(machineUid, item, machineUid, materialStorage))
                    return; // Ingested
            }

            // 2. If machine has FactoryMachineComponent buffer
            if (TryComp<FactoryMachineComponent>(machineUid, out var machineComp))
            {
                var inputContainer = _container.EnsureContainer<Container>(machineUid, machineComp.InputBufferId);
                if (_container.Insert(item, inputContainer))
                    return; // Ingested
            }
        }
    }

    private void ProcessOutputPort(EntityUid machineUid, FactoryConveyorPort port, EntityUid gridUid, MapGridComponent grid, Vector2i portTile, EntityCoordinates portCoords)
    {
        if (!TryComp<FactoryMachineComponent>(machineUid, out var machineComp))
            return;

        if (!_container.TryGetContainer(machineUid, machineComp.OutputBufferId, out var outputContainer) ||
            outputContainer.ContainedEntities.Count == 0)
        {
            return;
        }

        var item = outputContainer.ContainedEntities[0];
        _container.Remove(item, outputContainer);
        _transform.SetCoordinates(item, portCoords);

        if (TryComp<PhysicsComponent>(item, out var physics))
        {
            _physics.WakeBody(item, body: physics);
        }

        // Check if there is a conveyor on the port tile to immediately convey it
        var anchoredEnumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, portTile);
        while (anchoredEnumerator.MoveNext(out var anchored))
        {
            if (HasComp<ConveyorComponent>(anchored))
            {
                EnsureComp<ConveyedComponent>(item);
                break;
            }
        }
    }
}
