using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Shared._Hex.Factory;

namespace Content.Server._Hex.Factory;

public sealed class FactoryGasPortSystem : EntitySystem
{
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryGasPortComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnAtmosUpdate(EntityUid uid, FactoryGasPortComponent component, ref AtmosDeviceUpdateEvent args)
    {
        if (!component.Enabled || !_power.IsPowered(uid))
            return;

        foreach (var port in component.Ports)
        {
            if (string.IsNullOrEmpty(port.Node))
                continue;

            if (!_nodeContainer.TryGetNode(uid, port.Node, out PipeNode? pipeNode))
                continue;

            if (port.PortType == FactoryPortType.Input)
            {
                if (component.InternalAir.Pressure >= port.TargetPressure)
                    continue;

                if (pipeNode.Air.TotalMoles <= 0.001f)
                    continue;

                var availableMoles = port.Gas == null
                    ? pipeNode.Air.TotalMoles
                    : pipeNode.Air.GetMoles(port.Gas.Value);

                if (availableMoles <= 0.001f)
                    continue;

                var molesToTransfer = MathF.Min(availableMoles, port.MaxTransferRate * args.dt);
                if (molesToTransfer <= 0f)
                    continue;

                var taken = pipeNode.Air.Remove(molesToTransfer);
                _atmosphere.Merge(component.InternalAir, taken);
            }
            else if (port.PortType == FactoryPortType.Output)
            {
                if (component.InternalAir.TotalMoles <= 0.001f)
                    continue;

                var molesToTransfer = MathF.Min(component.InternalAir.TotalMoles, port.MaxTransferRate * args.dt);
                if (molesToTransfer <= 0f)
                    continue;

                var taken = component.InternalAir.Remove(molesToTransfer);
                _atmosphere.Merge(pipeNode.Air, taken);
            }
        }
    }
}
