using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Shared._Hex.Factory;
using Content.Shared.Materials;
using Content.Shared.Power;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;

namespace Content.Server._Hex.Factory;

public sealed class FactoryMachineSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryMachineComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnPowerChanged(EntityUid uid, FactoryMachineComponent component, ref PowerChangedEvent args)
    {
        component.Powered = args.Powered;
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FactoryMachineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var machine, out var xform))
        {
            if (!machine.Enabled || !machine.Powered || !xform.Anchored)
                continue;

            if (!TryComp<MapGridComponent>(xform.GridUid, out _))
                continue;

            // Sleep when idle — no input available
            if (machine.SleepAccumulator > 0f)
            {
                machine.SleepAccumulator -= frameTime;
                continue;
            }

            // Check gas requirements
            if (machine.RequiredGas is { } reqGas && machine.RequiredGasMoles > 0f)
            {
                if (!TryComp<FactoryGasPortComponent>(uid, out var gasPort) ||
                    gasPort.InternalAir.GetMoles(reqGas) < machine.RequiredGasMoles)
                {
                    continue;
                }
            }

            // Check material / item input availability
            var hasMaterialInput = false;
            string? materialToConsume = null;

            if (TryComp<MaterialStorageComponent>(uid, out var materialStorage))
            {
                foreach (var (matId, amount) in materialStorage.Storage)
                {
                    if (amount >= 100)
                    {
                        hasMaterialInput = true;
                        materialToConsume = matId;
                        break;
                    }
                }
            }

            var hasItemInput = false;
            if (!hasMaterialInput &&
                _container.TryGetContainer(uid, machine.InputBufferId, out var inputContainer) &&
                inputContainer.ContainedEntities.Count > 0)
            {
                hasItemInput = true;
            }

            if (!hasMaterialInput && !hasItemInput)
            {
                machine.ProcessingProgress = 0f;
                machine.SleepAccumulator = machine.SleepInterval;
                continue;
            }

            // Progress cycle
            machine.ProcessingProgress += frameTime;
            if (machine.ProcessingProgress >= machine.ProcessingDuration)
            {
                machine.ProcessingProgress = 0f;

                // Consume gas
                if (machine.RequiredGas is { } gas && machine.RequiredGasMoles > 0f &&
                    TryComp<FactoryGasPortComponent>(uid, out var gasComp))
                {
                    gasComp.InternalAir.AdjustMoles(gas, -machine.RequiredGasMoles);
                }

                // Consume input
                if (hasMaterialInput && materialToConsume != null && materialStorage != null)
                {
                    _materialStorage.TryChangeMaterialAmount(uid, materialToConsume, -100, materialStorage);
                }
                else if (hasItemInput && _container.TryGetContainer(uid, machine.InputBufferId, out var inCont) &&
                         inCont.ContainedEntities.Count > 0)
                {
                    var item = inCont.ContainedEntities[0];
                    _container.Remove(item, inCont);
                    QueueDel(item);
                }

                // Produce output
                if (!string.IsNullOrEmpty(machine.ProducedPrototype))
                {
                    var output = Spawn(machine.ProducedPrototype, xform.Coordinates);
                    var outContainer = _container.EnsureContainer<Container>(uid, machine.OutputBufferId);
                    _container.Insert(output, outContainer);
                }
            }
        }
    }
}
