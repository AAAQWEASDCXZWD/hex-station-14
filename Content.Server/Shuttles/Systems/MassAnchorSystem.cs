using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Shared.Construction.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Verbs;
using Robust.Server.Physics;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// Makes grids heavier and increases their friction while one or more mass anchors are active.
/// </summary>
public sealed class MassAnchorSystem : EntitySystem
{
    [Dependency] private readonly ShuttleSystem _shuttleSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MassAnchorComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<MassAnchorComponent, AnchorStateChangedEvent>(OnAnchorStateChange);

        SubscribeLocalEvent<MassAnchorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MassAnchorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<GridMassComponent, TileFrictionEvent>(OnGridTileFriction);

        SubscribeLocalEvent<MassAnchorComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<MassAnchorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerb);
        SubscribeLocalEvent<MassAnchorComponent, MassAnchorToggleDoAfterEvent>(OnToggleDoAfter);
    }

    private void OnMapInit(Entity<MassAnchorComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.SwitchedOn)
            return;

        SetStatus(ent, true);
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        UpdateGridMass(args.Grid);

        foreach (var grid in args.NewGrids)
            UpdateGridMass(grid);
    }

    private void OnGridTileFriction(Entity<GridMassComponent> ent, ref TileFrictionEvent args)
    {
        args.Modifier *= ent.Comp.FrictionMultiplier;
    }

    private void OnShutdown(Entity<MassAnchorComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<TransformComponent>(ent, out var xform) || xform.GridUid is not { } grid)
            return;

        ent.Comp.SwitchedOn = false;
        UpdateGridMass(grid, ent.Owner);
    }

    private void OnUnanchorAttempt(Entity<MassAnchorComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (!ent.Comp.SwitchedOn)
            return;

        _popupSystem.PopupEntity(
            Loc.GetString("mass-anchor-unanchoring-failed"),
            ent,
            args.User,
            PopupType.Medium);

        args.Cancel();
    }

    private void OnAnchorStateChange(Entity<MassAnchorComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
        {
            if (ent.Comp.SwitchedOn)
                SetStatus(ent, false);

            return;
        }

        if (ent.Comp.SwitchedOn)
            SetStatus(ent, true);
    }

    /// <summary>
    /// Handles activation through the right-click menu.
    /// </summary>
    private void OnActivate(Entity<MassAnchorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        StartToggle(ent, args.User);
    }

    /// <summary>
    /// Adds the verb used by both the right-click menu and alt-click.
    /// </summary>
    private void OnGetAlternativeVerb(Entity<MassAnchorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanComplexInteract || !args.CanAccess)
            return;

        var user = args.User;
        var activating = !ent.Comp.SwitchedOn;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(activating
                ? "mass-anchor-verb-activate"
                : "mass-anchor-verb-deactivate"),
            Act = () => StartToggle(ent, user),
            Priority = 1,
        });
    }

    private void StartToggle(Entity<MassAnchorComponent> ent, EntityUid user)
    {
        var xform = Transform(ent);

        if (!xform.Anchored || !xform.GridUid.HasValue)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("mass-anchor-not-anchored"),
                ent,
                user,
                PopupType.Medium);

            return;
        }

        var enabled = !ent.Comp.SwitchedOn;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            ent.Comp.ToggleDelay,
            new MassAnchorToggleDoAfterEvent(enabled),
            ent,
            target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        _popupSystem.PopupEntity(
            Loc.GetString(enabled
                ? "mass-anchor-activating"
                : "mass-anchor-deactivating"),
            ent,
            user,
            PopupType.Medium);
    }

    private void OnToggleDoAfter(Entity<MassAnchorComponent> ent, ref MassAnchorToggleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var xform = Transform(ent);
        if (!xform.Anchored || !xform.GridUid.HasValue)
            return;

        SetStatus(ent, args.Enabled);
    }

    private void SetStatus(Entity<MassAnchorComponent> ent, bool enabled)
    {
        var transform = Transform(ent);
        var grid = transform.GridUid;
        if (!grid.HasValue || enabled && !transform.Anchored)
            return;

        ent.Comp.SwitchedOn = enabled;
        UpdateGridMass(grid.Value);
    }

    /// <summary>
    /// Recalculates the grid's mass and friction from all active mass anchors on it.
    /// </summary>
    private void UpdateGridMass(EntityUid grid, EntityUid? ignored = null)
    {
        if (TerminatingOrDeleted(grid))
            return;

        var massMultiplier = 1f;
        var frictionMultiplier = 1f;

        var anchors = EntityQueryEnumerator<MassAnchorComponent, TransformComponent>();
        while (anchors.MoveNext(out var uid, out var anchor, out var xform))
        {
            if (uid == ignored ||
                TerminatingOrDeleted(uid) ||
                xform.GridUid != grid ||
                !xform.Anchored ||
                !anchor.SwitchedOn)
            {
                continue;
            }

            massMultiplier *= anchor.MassMultiplier;
            frictionMultiplier *= anchor.FrictionMultiplier;
        }

        _shuttleSystem.SetGridMassMultiplier(grid, massMultiplier, frictionMultiplier);
    }
}
