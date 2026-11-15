using System.Linq;
using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Hex.SpaceArtillery;
using Content.Shared.Explosion.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using SpaceArtilleryComponent = Content.Server._Hex.SpaceArtillery.Components.SpaceArtilleryComponent;

namespace Content.Server._Hex.SpaceArtillery;

/// <summary>
/// Wall-trace subsystem for ship artillery projectiles.
///
/// Problem being solved
/// --------------------
/// RobustToolbox uses discrete (non-CCD) physics. A fast projectile can skip
/// over a wall entirely between two physics ticks — it teleports from one side
/// to the other without registering a collision.
///
/// Solution
/// --------
/// Immediately after the GunSystem spawns a ship-weapon projectile we cast a
/// ray from the muzzle in the projectile's direction across ALL grids and maps.
/// If a BulletImpassable / Impassable fixture is found we schedule a "wall
/// detonation" at exactly the moment the projectile would reach that point.
///
/// When the scheduled time arrives:
///   • If the projectile still exists (no physics hit along the way) → we
///     remove it and queue the explosion ourselves at the wall position.
///   • If the projectile was already destroyed (hit a person/object via normal
///     physics) → we do nothing; that explosion already fired.
///
/// This gives fully deterministic wall collision that is independent of
/// projectile speed, while keeping normal physics collision working for
/// soft targets (mobs, players, windows, etc.).
/// </summary>
public sealed partial class SpaceArtillerySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    private const int WallTraceMask = (int)(CollisionGroup.Impassable | CollisionGroup.BulletImpassable);

    /// <summary>
    /// Pending wall-detonations keyed by the projectile entity.
    /// </summary>
    private readonly Dictionary<EntityUid, WallHitEntry> _pendingWallHits = new();

    private record struct WallHitEntry(
        TimeSpan TriggerTime,
        MapCoordinates Position,
        string ExplosionType,
        float TotalIntensity,
        float Slope,
        float MaxIntensity,
        int MaxTileBreak);

    // -------------------------------------------------------------------------
    //  Subscriptions wired from Initialize() in SpaceArtillerySystem.cs
    // -------------------------------------------------------------------------

    private void InitializeWallTrace()
    {
        // Cancel any pending detonation if the projectile was destroyed early
        // (normal physics hit or manual deletion).
        SubscribeLocalEvent<ShipWeaponProjectileComponent, EntityTerminatingEvent>(OnProjectileTerminating);
    }

    // -------------------------------------------------------------------------
    //  Update loop
    // -------------------------------------------------------------------------

    private void UpdateWallTrace(float frameTime)
    {
        if (_pendingWallHits.Count == 0)
            return;

        var now = _timing.CurTime;

        // Collect projectiles whose detonation time has arrived.
        List<EntityUid>? toFire = null;
        foreach (var (uid, entry) in _pendingWallHits)
        {
            if (now >= entry.TriggerTime)
                (toFire ??= new List<EntityUid>()).Add(uid);
        }

        if (toFire == null)
            return;

        foreach (var uid in toFire)
        {
            _pendingWallHits.Remove(uid, out var entry);

            // If the projectile is already gone (physics hit, manual delete,
            // etc.) the explosion already happened — nothing to do.
            if (TerminatingOrDeleted(uid))
                continue;

            // Trigger wall-impact explosion at the computed position, then
            // remove the now-obsolete projectile entity.
            _explosion.QueueExplosion(
                entry.Position,
                entry.ExplosionType,
                entry.TotalIntensity,
                entry.Slope,
                entry.MaxIntensity,
                cause: null,
                maxTileBreak: entry.MaxTileBreak,
                addLog: false);

            QueueDel(uid);
        }
    }

    // -------------------------------------------------------------------------
    //  Event handlers
    // -------------------------------------------------------------------------

    private void OnAmmoShotWallTrace(EntityUid gunUid, SpaceArtilleryComponent comp, AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
            return;

        if (!TryComp<GunComponent>(gunUid, out var gun))
            return;

        var speed = gun.ProjectileSpeedModified > 0f ? gun.ProjectileSpeedModified : gun.ProjectileSpeed;

        foreach (var projUid in args.FiredProjectiles)
        {
            TraceProjectileWall(projUid, speed);
        }
    }

    private void OnProjectileTerminating(EntityUid uid, ShipWeaponProjectileComponent comp, ref EntityTerminatingEvent args)
    {
        // The projectile died via physics collision or some other path —
        // cancel our scheduled wall detonation so we don't double-explode.
        _pendingWallHits.Remove(uid);
    }

    // -------------------------------------------------------------------------
    //  Core trace logic
    // -------------------------------------------------------------------------

    private void TraceProjectileWall(EntityUid projUid, float speed)
    {
        if (!HasComp<ShipWeaponProjectileComponent>(projUid))
            return;

        if (!TryComp<PhysicsComponent>(projUid, out var physics))
            return;

        // The velocity is in world-space map units per second.
        var velocity = physics.LinearVelocity;
        if (velocity.LengthSquared() <= float.Epsilon)
            return;

        if (!TryComp<ExplosiveComponent>(projUid, out var explosive))
            return;

        var xform = Transform(projUid);
        if (xform.MapID == MapId.Nullspace)
            return;

        var origin = _xform.GetWorldPosition(xform);
        var direction = Vector2.Normalize(velocity);

        // Max travel distance from TimedDespawn lifetime * speed.
        // Gives us a sensible upper bound for the raycast.
        float maxRange = 500f;
        if (TryComp<Robust.Shared.Spawners.TimedDespawnComponent>(projUid, out var despawn))
            maxRange = despawn.Lifetime * speed;

        // Cast a ray across ALL grids on this map, looking for the first
        // hard surface (wall, hull, structure) that is BulletImpassable.
        var ray = new CollisionRay(origin, direction, WallTraceMask);

        var results = _physics.IntersectRayWithPredicate(
            xform.MapID,
            ray,
            maxRange,
            entity =>
            {
                // Skip the projectile itself and its parent grid.
                if (entity == projUid)
                    return true;

                if (TerminatingOrDeleted(entity))
                    return true;

                var entityXform = Transform(entity);

                // We only want STATIC anchored structures (walls, hulls).
                // Mobile entities (mobs, players, loose items) are handled
                // by the normal physics TriggerOnCollide path.
                if (!entityXform.Anchored)
                    return true;

                // Skip fire-control equipment on the SAME grid as the gun —
                // the own-grid LoS check in FireControlSystem already guards
                // against shooting through your own ship. We don't want to
                // ghost-explode at a friendly cannon.
                // (Walls on own grid still block — that's intentional.)
                if (HasComp<Content.Server._Hex.FireControl.FireControllableComponent>(entity))
                    return true;

                return false;
            },
            returnOnFirstHit: true);

        if (!results.Any())
            return;

        var hit = results.First();

        // Distance is in map units (metres) from origin to contact point.
        // HitPos is already the contact point in map-space coordinates.
        var hitDistance = hit.Distance;
        if (hitDistance <= float.Epsilon)
            return;

        var hitPosition = new MapCoordinates(hit.HitPos, xform.MapID);
        var timeToHit = TimeSpan.FromSeconds(hitDistance / speed);

        _pendingWallHits[projUid] = new WallHitEntry(
            TriggerTime: _timing.CurTime + timeToHit,
            Position: hitPosition,
            ExplosionType: explosive.ExplosionType,
            TotalIntensity: explosive.TotalIntensity,
            Slope: explosive.IntensitySlope,
            MaxIntensity: explosive.MaxIntensity,
            MaxTileBreak: explosive.MaxTileBreak);
    }
}
