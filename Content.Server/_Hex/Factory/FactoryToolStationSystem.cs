using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Hex.Factory;
using Content.Shared.Conveyor;
using Content.Shared.Construction;
using Content.Shared.Construction.Steps;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Hex.Factory;

public sealed class FactoryToolStationSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ConstructionSystem _construction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryToolStationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FactoryToolStationComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FactoryToolStationComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(EntityUid uid, FactoryToolStationComponent component, MapInitEvent args)
    {
        var container = _container.EnsureContainer<ContainerSlot>(uid, component.ToolSlotId);
        if (container.ContainedEntity == null && component.InternalTool != null)
        {
            var spawnedTool = Spawn(component.InternalTool, Transform(uid).Coordinates);
            _container.Insert(spawnedTool, container);
        }

        UpdateAppearance(uid, component);
    }

    private void OnPowerChanged(EntityUid uid, FactoryToolStationComponent component, ref PowerChangedEvent args)
    {
        component.Powered = args.Powered;

        if (!component.Powered && component.Working && component.ActiveTarget is { } target)
        {
            ReleaseTarget(target, component.PreviousBodyType);
            component.Working = false;
            component.ActiveTarget = null;
        }

        UpdateAppearance(uid, component);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, FactoryToolStationComponent component, ComponentShutdown args)
    {
        if (component.Working && component.ActiveTarget is { } target && !TerminatingOrDeleted(target))
        {
            ReleaseTarget(target, component.PreviousBodyType);
            component.Working = false;
            component.ActiveTarget = null;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FactoryToolStationComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var station, out var xform))
        {
            if (!station.Powered || !xform.Anchored || xform.GridUid is not { } gridUid)
                continue;

            if (!_power.IsPowered(uid))
                continue;

            if (!TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            // Handle cooldown
            if (station.CooldownTimer > 0f)
            {
                station.CooldownTimer -= frameTime;
                continue;
            }

            var dir = xform.LocalRotation.GetCardinalDir();
            var currentTile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var targetTile = GetRotatedTile(currentTile, station.TargetOffset, dir);
            var targetCoords = _mapSystem.GridTileToLocal(gridUid, grid, targetTile);

            // Active working state
            if (station.Working)
            {
                if (station.ActiveTarget is not { } target ||
                    TerminatingOrDeleted(target) ||
                    !Transform(target).Coordinates.TryDistance(EntityManager, xform.Coordinates, out var dist) ||
                    dist > 2.5f)
                {
                    station.Working = false;
                    station.ActiveTarget = null;
                    UpdateAppearance(uid, station);
                    Dirty(uid, station);
                    continue;
                }

                station.OperationTimer -= frameTime;
                if (station.OperationTimer <= 0f)
                {
                    CompleteToolOperation(uid, station, target, targetCoords);
                }

                continue;
            }

            // Idle state - scanning
            if (station.SleepAccumulator > 0f)
            {
                station.SleepAccumulator -= frameTime;
                continue;
            }

            var candidate = FindCandidate(uid, station, targetCoords);
            if (candidate == null)
            {
                station.SleepAccumulator = station.SleepInterval;
                continue;
            }

            // Start working on candidate
            StartWorking(uid, station, candidate.Value);
        }
    }

    private EntityUid? FindCandidate(EntityUid machineUid, FactoryToolStationComponent comp, EntityCoordinates targetCoords)
    {
        var entities = _lookup.GetEntitiesIntersecting(targetCoords, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var entity in entities)
        {
            if (entity == machineUid || TerminatingOrDeleted(entity) || Transform(entity).Anchored)
                continue;

            if (HasComp<FactoryAutomationBlockedComponent>(entity))
                continue;

            if (IsValidTargetForTool(entity, comp.ToolQuality))
                return entity;
        }

        return null;
    }

    private bool IsValidTargetForTool(EntityUid entity, string toolQuality)
    {
        // 1. Check ConstructionComponent
        if (TryComp<ConstructionComponent>(entity, out var constr))
        {
            if (constr.EdgeIndex is { } edgeIndex)
            {
                var edge = _construction.GetCurrentEdge(entity, constr);
                if (edge != null)
                {
                    var step = _construction.GetCurrentStep(entity, constr);
                    if (step is ToolConstructionGraphStep toolStep && toolStep.Tool == toolQuality)
                        return true;
                }
            }
            else if (_construction.GetCurrentNode(entity, constr) is { } node)
            {
                foreach (var edge in node.Edges)
                {
                    if (edge.Steps.Count > 0 &&
                        edge.Steps[0] is ToolConstructionGraphStep toolStep &&
                        toolStep.Tool == toolQuality)
                    {
                        if (_construction.CheckConditions(entity, edge.Conditions))
                            return true;
                    }
                }
            }
        }

        // 2. Check WeldableComponent
        if (toolQuality == "Welding" && TryComp<WeldableComponent>(entity, out var weldable) && !weldable.IsWelded)
            return true;

        // 3. Check ToolRefinableComponent
        if (TryComp<ToolRefinableComponent>(entity, out var refinable) && refinable.QualityNeeded == toolQuality)
            return true;

        // 4. Check ToolOpenableComponent
        if (TryComp<ToolOpenableComponent>(entity, out var openable) && openable.OpenToolQualityNeeded?.Id == toolQuality)
            return true;

        return false;
    }

    private void StartWorking(EntityUid machineUid, FactoryToolStationComponent comp, EntityUid target)
    {
        // Clamp target in place so conveyor doesn't pull it away mid-operation
        if (TryComp<PhysicsComponent>(target, out var physics))
        {
            comp.PreviousBodyType = physics.BodyType;
            _physics.SetBodyType(target, BodyType.Static, body: physics);
        }

        comp.ActiveTarget = target;
        comp.Working = true;
        comp.OperationTimer = comp.OperationTime;

        if (comp.WorkSound != null)
            _audio.PlayPvs(comp.WorkSound, machineUid);

        UpdateAppearance(machineUid, comp);
        Dirty(machineUid, comp);
    }

    private void CompleteToolOperation(EntityUid machineUid, FactoryToolStationComponent comp, EntityUid target, EntityCoordinates targetCoords)
    {
        var toolEntity = GetToolEntity(machineUid, comp);

        // 1. Apply construction step
        if (TryComp<ConstructionComponent>(target, out var constr))
        {
            var interactEvent = new InteractUsingEvent(machineUid, toolEntity, target, targetCoords);
            var doAfterEvent = new ConstructionInteractDoAfterEvent(EntityManager, interactEvent);
            RaiseLocalEvent(target, doAfterEvent);
        }
        // 2. Apply refinable
        else if (TryComp<ToolRefinableComponent>(target, out var refinable) && refinable.QualityNeeded == comp.ToolQuality)
        {
            var refineDoAfter = new WelderRefineDoAfterEvent();
            RaiseLocalEvent(target, refineDoAfter);
        }
        // 3. General tool interaction (Weldable, Openable, etc.)
        else
        {
            _interaction.InteractUsing(
                machineUid,
                toolEntity,
                target,
                targetCoords,
                checkCanInteract: false,
                checkCanUse: false);
        }

        // Unclamp target and restore physics
        ReleaseTarget(target, comp.PreviousBodyType);

        comp.Working = false;
        comp.ActiveTarget = null;
        comp.CooldownTimer = comp.Cooldown;

        UpdateAppearance(machineUid, comp);
        Dirty(machineUid, comp);
    }

    private EntityUid GetToolEntity(EntityUid machineUid, FactoryToolStationComponent comp)
    {
        if (_container.TryGetContainer(machineUid, comp.ToolSlotId, out var container) &&
            container is ContainerSlot slot &&
            slot.ContainedEntity is { } tool)
        {
            return tool;
        }

        return machineUid;
    }

    private void ReleaseTarget(EntityUid target, BodyType previousBodyType)
    {
        if (TerminatingOrDeleted(target))
            return;

        if (TryComp<PhysicsComponent>(target, out var physics))
        {
            _physics.SetBodyType(target, previousBodyType, body: physics);
            _physics.WakeBody(target, body: physics);
            EnsureComp<ConveyedComponent>(target);
        }
    }

    private void UpdateAppearance(EntityUid uid, FactoryToolStationComponent comp)
    {
        _appearance.SetData(uid, FactoryToolStationVisuals.Powered, comp.Powered);
        _appearance.SetData(uid, FactoryToolStationVisuals.Working, comp.Working);
    }

    private static Vector2i GetRotatedTile(Vector2i originTile, Vector2i localOffset, Direction dir)
    {
        var forward = dir.ToIntVec();
        var right = new Vector2i(-forward.Y, forward.X);
        var offset = forward * localOffset.Y + right * localOffset.X;
        return originTile + offset;
    }
}
