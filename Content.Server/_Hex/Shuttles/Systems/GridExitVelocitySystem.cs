using Content.Server.Shuttles.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Hex.Shuttles.Systems;

/// <summary>
///     Hex: When an entity leaves a moving shuttle grid onto the map, transfer the grid's
///     linear velocity to the entity so it doesn't instantly stop relative to the shuttle.
/// </summary>
public sealed class GridExitVelocitySystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Listen for any entity whose parent changes
        SubscribeLocalEvent<PhysicsComponent, EntParentChangedMessage>(OnEntParentChanged);
    }

    private void OnEntParentChanged(Entity<PhysicsComponent> ent, ref EntParentChangedMessage args)
    {
        // We only care about dynamic bodies (not static grids, walls, etc.)
        if (ent.Comp.BodyType != BodyType.Dynamic)
            return;

        // The entity must be moving to a non-grid parent (i.e., directly onto the map / space)
        // args.Transform.GridUid is the new grid uid after reparenting
        if (args.Transform.GridUid != null)
            return;

        // Ignore nullspace (e.g. deletion or being put into an off-map container)
        if (args.Transform.MapUid == null)
            return;

        var oldParent = args.OldParent;
        if (oldParent == null)
            return;

        // Find the shuttle grid: either oldParent was the shuttle grid itself,
        // or oldParent was an entity on a shuttle grid (e.g. chair, container).
        EntityUid? shuttleUid = null;
        if (HasComp<ShuttleComponent>(oldParent.Value))
        {
            shuttleUid = oldParent.Value;
        }
        else if (TryComp<TransformComponent>(oldParent.Value, out var oldXform) &&
                 oldXform.GridUid is { } oldGrid &&
                 HasComp<ShuttleComponent>(oldGrid))
        {
            shuttleUid = oldGrid;
        }

        if (shuttleUid == null)
            return;

        // Get the shuttle's physics to read its velocity
        if (!TryComp<PhysicsComponent>(shuttleUid.Value, out var gridBody))
            return;

        var gridVelocity = gridBody.LinearVelocity;

        // If the grid is stationary there's nothing to inherit
        if (gridVelocity.LengthSquared() < 0.01f)
            return;

        // Check if the entity's current velocity already contains the grid's velocity.
        // If not (e.g. ent.Comp.LinearVelocity is ~0 or hasn't inherited grid velocity),
        // apply the grid velocity plus any relative movement velocity.
        var relVel = ent.Comp.LinearVelocity - gridVelocity;
        if (relVel.LengthSquared() > ent.Comp.LinearVelocity.LengthSquared())
        {
            _physics.SetLinearVelocity(ent, gridVelocity + ent.Comp.LinearVelocity, body: ent.Comp);
        }
    }
}
