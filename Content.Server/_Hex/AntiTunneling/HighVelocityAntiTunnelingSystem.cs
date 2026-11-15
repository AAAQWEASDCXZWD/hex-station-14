using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Hex.AntiTunneling;

/// <summary>
/// Prevents non-projectile dynamic entities (mobs, players, loose items) from tunneling
/// through walls when moving at high velocity — most commonly after being launched by
/// an explosion.
///
/// Mechanism
/// ---------
/// Every game tick, the system queries all awake dynamic physics bodies. Those whose
/// speed exceeds <see cref="SpeedThreshold"/> (meaning they could realistically skip
/// over a wall within a few physics frames) get a short look-ahead raycast.
///
/// If a wall (Impassable / BulletImpassable, anchored) is found within the entity's
/// projected travel distance for this tick, the system:
///   1. Repositions the entity to just before the wall surface.
///   2. Projects its velocity onto the wall plane (sliding, not dead-stop), so the
///      entity continues along the wall rather than being frozen.
///
/// Performance
/// -----------
/// At normal SS14 speeds (3–8 u/s) no entity ever reaches <see cref="SpeedThreshold"/>,
/// so the per-tick cost is effectively zero — only the outer query loop runs and exits
/// immediately after the speed check. Raycasts fire only during exceptional events
/// (explosions, force-launchers) where a handful of entities are briefly fast.
///
/// Projectiles are excluded: they are handled at spawn-time by
/// <see cref="ProjectileWallTraceSystem"/> (zero per-tick cost per projectile).
/// </summary>
public sealed class HighVelocityAntiTunnelingSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    /// <summary>
    /// Minimum speed (units/s) that triggers the look-ahead raycast.
    ///
    /// At 60 Hz, an entity at this speed moves 15/60 = 0.25 units/tick.
    /// SS14 walls are 1 unit thick → no tunneling below this speed.
    /// Tune down if you add thinner geometry; tune up to reduce overhead.
    /// </summary>
    private const float SpeedThreshold = 15f;

    /// <summary>
    /// How many ticks ahead to look. Buffer against variable frame timing.
    /// 3 × dt means: even if a tick is 3× longer than expected, we still catch it.
    /// </summary>
    private const float LookAheadFrames = 3f;

    /// <summary>
    /// Clearance kept between the corrected entity position and the wall surface.
    /// Prevents the entity from being placed exactly on the fixture boundary,
    /// which can cause repeated correction next tick.
    /// </summary>
    private const float SafetyBuffer = 0.05f;

    private const int WallMask = (int)(CollisionGroup.Impassable | CollisionGroup.BulletImpassable);

    // -------------------------------------------------------------------------
    //  Update
    // -------------------------------------------------------------------------

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PhysicsComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var physics, out var xform))
        {
            // Static / kinematic bodies do not move on their own — skip.
            if (physics.BodyType != BodyType.Dynamic)
                continue;

            // Sleeping bodies have zero velocity — nothing to check.
            if (!physics.Awake)
                continue;

            // Projectiles have dedicated spawn-time wall-trace; skip to avoid
            // the overhead of an extra HasComp check on every other entity.
            if (HasComp<ProjectileComponent>(uid))
                continue;

            var velocity = physics.LinearVelocity;
            var speed    = velocity.Length();

            if (speed < SpeedThreshold)
                continue;

            if (xform.MapID == MapId.Nullspace)
                continue;

            var direction     = velocity / speed;
            var frameDistance = speed * frameTime;
            var lookAhead     = frameDistance * LookAheadFrames;

            var origin = _xform.GetWorldPosition(xform);
            var ray    = new CollisionRay(origin, direction, WallMask);

            var results = _physics.IntersectRayWithPredicate(
                xform.MapID,
                ray,
                lookAhead,
                entity =>
                {
                    if (entity == uid)
                        return true;                 // skip self
                    if (TerminatingOrDeleted(entity))
                        return true;
                    if (!Transform(entity).Anchored)
                        return true;                 // only fixed geometry counts
                    return false;
                },
                returnOnFirstHit: true);

            if (!results.Any())
                continue;

            var hit = results.First();

            // Wall is farther than what we travel this tick — no correction needed.
            if (hit.Distance > frameDistance)
                continue;

            // ---------------------------------------------------------------
            // Correction
            // ---------------------------------------------------------------

            // 1. Reposition to just before the wall.
            var safePos = hit.HitPos - direction * SafetyBuffer;
            _xform.SetWorldPosition(uid, safePos);

            // 2. Project velocity onto the wall surface (sliding behaviour).
            //    wallNormal points away from the wall toward the entity.
            var wallNormal = -direction;
            // Penetrating component is the amount of velocity going INTO the wall
            // (clamped to ≤ 0 so we only remove inward motion, never outward).
            var penetrating  = MathF.Min(0f, Vector2.Dot(velocity, wallNormal));
            var newVelocity  = velocity - wallNormal * penetrating;

            _physics.SetLinearVelocity(uid, newVelocity, body: physics);
        }
    }
}
