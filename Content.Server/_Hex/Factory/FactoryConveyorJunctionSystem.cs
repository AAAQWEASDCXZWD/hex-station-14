using Content.Server.DeviceLinking.Systems;
using Content.Shared._Hex.Factory;
using Content.Shared.Conveyor;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Server._Hex.Factory;

public sealed class FactoryConveyorJunctionSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _signalSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private static readonly SoundSpecifier SwitchSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactoryConveyorJunctionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<FactoryConveyorJunctionComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<FactoryConveyorJunctionComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<FactoryConveyorJunctionComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<FactoryConveyorJunctionComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
    }

    private void OnStartup(EntityUid uid, FactoryConveyorJunctionComponent component, ComponentStartup args)
    {
        _signalSystem.EnsureSinkPorts(
            uid,
            component.NextDirectionPort,
            component.ForwardPort,
            component.RightPort,
            component.BackwardPort,
            component.LeftPort);

        ApplyDirection(uid, component, false);
    }

    private void OnSignalReceived(EntityUid uid, FactoryConveyorJunctionComponent component, ref SignalReceivedEvent args)
    {
        if (component.AllowedAngles.Count == 0)
            return;

        if (args.Port == component.NextDirectionPort)
        {
            CycleDirection(uid, component);
            return;
        }

        if (args.Port == component.ForwardPort)
        {
            SetClosestDirection(uid, component, Angle.Zero);
            return;
        }

        if (args.Port == component.RightPort)
        {
            SetClosestDirection(uid, component, Angle.FromDegrees(90));
            return;
        }

        if (args.Port == component.BackwardPort)
        {
            SetClosestDirection(uid, component, Angle.FromDegrees(180));
            return;
        }

        if (args.Port == component.LeftPort)
        {
            SetClosestDirection(uid, component, Angle.FromDegrees(270));
            return;
        }
    }

    private void OnActivateInWorld(EntityUid uid, FactoryConveyorJunctionComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !component.AllowManualSwitch)
            return;

        args.Handled = true;
        CycleDirection(uid, component, args.User);
    }

    private void OnInteractHand(EntityUid uid, FactoryConveyorJunctionComponent component, InteractHandEvent args)
    {
        if (args.Handled || !component.AllowManualSwitch)
            return;

        args.Handled = true;
        CycleDirection(uid, component, args.User);
    }

    private void OnGetVerbs(EntityUid uid, FactoryConveyorJunctionComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !component.AllowManualSwitch)
            return;

        // Verb: Cycle next direction
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("factory-conveyor-junction-verb-cycle"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Act = () => CycleDirection(uid, component, args.User),
            Priority = 2,
        });

        // Verbs for direct direction selection
        for (var i = 0; i < component.AllowedAngles.Count; i++)
        {
            var idx = i;
            var angle = component.AllowedAngles[i];
            var dirName = GetDirectionLoc(angle);

            args.Verbs.Add(new ActivationVerb
            {
                Text = Loc.GetString("factory-conveyor-junction-verb-set", ("direction", dirName)),
                Disabled = component.CurrentAngleIndex == idx,
                Act = () => SetDirectionIndex(uid, idx, component, args.User),
                Priority = 1,
            });
        }
    }

    public void CycleDirection(EntityUid uid, FactoryConveyorJunctionComponent component, EntityUid? user = null)
    {
        if (component.AllowedAngles.Count == 0)
            return;

        var nextIndex = (component.CurrentAngleIndex + 1) % component.AllowedAngles.Count;
        SetDirectionIndex(uid, nextIndex, component, user);
    }

    public void SetDirectionIndex(EntityUid uid, int index, FactoryConveyorJunctionComponent component, EntityUid? user = null)
    {
        if (index < 0 || index >= component.AllowedAngles.Count)
            return;

        component.CurrentAngleIndex = index;
        ApplyDirection(uid, component, true, user);
    }

    private void SetClosestDirection(EntityUid uid, FactoryConveyorJunctionComponent component, Angle targetAngle)
    {
        if (component.AllowedAngles.Count == 0)
            return;

        var bestIndex = 0;
        var minDiff = float.MaxValue;

        for (var i = 0; i < component.AllowedAngles.Count; i++)
        {
            var diff = MathF.Abs((float) Angle.ShortestDistance(component.AllowedAngles[i], targetAngle).Degrees);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIndex = i;
            }
        }

        SetDirectionIndex(uid, bestIndex, component);
    }

    private void ApplyDirection(EntityUid uid, FactoryConveyorJunctionComponent component, bool playSound, EntityUid? user = null)
    {
        if (component.CurrentAngleIndex < 0 || component.CurrentAngleIndex >= component.AllowedAngles.Count)
            component.CurrentAngleIndex = 0;

        var currentAngle = component.AllowedAngles[component.CurrentAngleIndex];

        if (TryComp<ConveyorComponent>(uid, out var conveyor))
        {
            conveyor.Angle = currentAngle;
            Dirty(uid, conveyor);
        }

        Dirty(uid, component);

        // Update visuals
        var visualDir = GetVisualDirection(currentAngle);
        _appearance.SetData(uid, FactoryConveyorJunctionVisuals.Direction, visualDir);

        if (playSound)
        {
            _audio.PlayPvs(SwitchSound, uid);
        }

        if (user != null)
        {
            var dirName = GetDirectionLoc(currentAngle);
            _popup.PopupEntity(Loc.GetString("factory-conveyor-junction-switched", ("direction", dirName)), uid, user.Value);
        }

        // Awaken conveyed entities on this tile
        AwakenConveyedEntities(uid);
    }

    private static FactoryConveyorJunctionDirection GetVisualDirection(Angle angle)
    {
        var degrees = (float) angle.Reduced().Degrees;
        if (degrees < 0f)
            degrees += 360f;

        return degrees switch
        {
            >= 45f and < 135f => FactoryConveyorJunctionDirection.Right,
            >= 135f and < 225f => FactoryConveyorJunctionDirection.Backward,
            >= 225f and < 315f => FactoryConveyorJunctionDirection.Left,
            _ => FactoryConveyorJunctionDirection.Forward,
        };
    }

    private string GetDirectionLoc(Angle angle)
    {
        var dir = GetVisualDirection(angle);
        return dir switch
        {
            FactoryConveyorJunctionDirection.Right => Loc.GetString("factory-conveyor-junction-dir-right"),
            FactoryConveyorJunctionDirection.Backward => Loc.GetString("factory-conveyor-junction-dir-backward"),
            FactoryConveyorJunctionDirection.Left => Loc.GetString("factory-conveyor-junction-dir-left"),
            _ => Loc.GetString("factory-conveyor-junction-dir-forward"),
        };
    }

    private void AwakenConveyedEntities(EntityUid conveyorUid)
    {
        if (!TryComp<TransformComponent>(conveyorUid, out var xform) || xform.GridUid is not { } gridUid)
            return;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tileIndices = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var intersecting = new HashSet<EntityUid>();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tileIndices, intersecting, 0f, flags: LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate);

        foreach (var ent in intersecting)
        {
            if (TryComp<PhysicsComponent>(ent, out var physics) && physics.BodyType != BodyType.Static)
            {
                _physics.WakeBody(ent, body: physics);
            }
        }
    }
}
