using System.Linq;
using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Hex.SpaceArtillery;
using Content.Shared.Explosion.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._Hex.AntiTunneling;

/// <summary>
/// Prevents projectile tunneling (passing through walls) for ALL gun-fired projectiles.
///
/// Problem
/// -------
/// RobustToolbox uses discrete physics. Fast projectiles skip over walls between ticks
/// without registering collision. <see cref="Content.Server._Hex.SpaceArtillery.SpaceArtillerySystem"/>
/// already handles ship-weapon projectiles; this system covers everything else.
///
/// Solution
/// --------
/// On every <see cref="AmmoShotEvent"/>, cast a ray from the muzzle in the projectile's
/// direction. If a BulletImpassable / Impassable fixture is found, schedule a "wall-stop"
/// at exactly the moment the projectile would arrive.
///
/// When triggered:
///   • Projectile still alive → delete it; if it was explosive, queue the explosion.
///   • Projectile already gone (normal physics hit) → no-op; that effect already fired.
///
/// Ship-weapon projectiles (<see cref="ShipWeaponProjectileComponent"/>) are skipped —
/// SpaceArtillerySystem handles those with its own wall-trace.
/// </summary>
public sealed class ProjectileWallTraceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const int WallTraceMask = (int)(CollisionGroup.Impassable | CollisionGroup.BulletImpassable);
    private const float DefaultMaxRange = 500f;

    /// <summary>Pending wall-stops keyed by projectile entity.</summary>
    private readonly Dictionary<EntityUid, WallHitEntry> _pendingHits = new();

    private record struct WallHitEntry(
        TimeSpan TriggerTime,
        MapCoordinates Position,
        // Null when the projectile has no ExplosiveComponent.
        string? ExplosionType,
        float TotalIntensity,
        float Slope,
        float MaxIntensity,
        int MaxTileBreak);

    // -------------------------------------------------------------------------
    //  Lifecycle
    // -------------------------------------------------------------------------

    public override void Initialize()
    {
        base.Initialize();

        // GunComponent is present on every entity that can fire — catches all shots.
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAmmoShot);

        // Cancel pending stop if the projectile died early via normal physics collision.
        SubscribeLocalEvent<ProjectileComponent, EntityTerminatingEvent>(OnProjectileTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingHits.Count == 0)
            return;

        var now = _timing.CurTime;
        List<EntityUid>? toFire = null;

        foreach (var (uid, entry) in _pendingHits)
        {
            if (now >= entry.TriggerTime)
                (toFire ??= new List<EntityUid>()).Add(uid);
        }

        if (toFire == null)
            return;

        foreach (var uid in toFire)
        {
            _pendingHits.Remove(uid, out var entry);

            // Already gone — the physics-collision path handled it.
            if (TerminatingOrDeleted(uid))
                continue;

            if (entry.ExplosionType != null)
            {
                _explosion.QueueExplosion(
                    entry.Position,
                    entry.ExplosionType,
                    entry.TotalIntensity,
                    entry.Slope,
                    entry.MaxIntensity,
                    cause: null,
                    maxTileBreak: entry.MaxTileBreak,
                    addLog: false);
            }

            QueueDel(uid);
        }
    }

    // -------------------------------------------------------------------------
    //  Handlers
    // -------------------------------------------------------------------------

    private void OnAmmoShot(EntityUid gunUid, GunComponent gun, AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
            return;

        var speed = gun.ProjectileSpeedModified > 0f
            ? gun.ProjectileSpeedModified
            : gun.ProjectileSpeed;

        foreach (var projUid in args.FiredProjectiles)
        {
            TraceProjectile(projUid, speed);
        }
    }

    private void OnProjectileTerminating(EntityUid uid, ProjectileComponent comp, ref EntityTerminatingEvent args)
    {
        _pendingHits.Remove(uid);
    }

    // -------------------------------------------------------------------------
    //  Core trace
    // -------------------------------------------------------------------------

    private void TraceProjectile(EntityUid projUid, float speed)
    {
        // Ship-weapon projectiles: handled by SpaceArtillerySystem.WallTrace.
        if (HasComp<ShipWeaponProjectileComponent>(projUid))
            return;

        if (!HasComp<ProjectileComponent>(projUid))
            return;

        if (!TryComp<PhysicsComponent>(projUid, out var physics))
            return;

        var velocity = physics.LinearVelocity;
        if (velocity.LengthSquared() <= float.Epsilon)
            return;

        var xform = Transform(projUid);
        if (xform.MapID == MapId.Nullspace)
            return;

        var origin = _xform.GetWorldPosition(xform);
        var direction = Vector2.Normalize(velocity);

        // Upper-bound on ray: despawn lifetime × speed, or a large fallback.
        float maxRange = DefaultMaxRange;
        if (TryComp<TimedDespawnComponent>(projUid, out var despawn))
            maxRange = despawn.Lifetime * speed;

        var ray = new CollisionRay(origin, direction, WallTraceMask);

        var results = _physics.IntersectRayWithPredicate(
            xform.MapID,
            ray,
            maxRange,
            entity =>
            {
                if (entity == projUid)
                    return true;                 // skip self
                if (TerminatingOrDeleted(entity))
                    return true;
                if (!Transform(entity).Anchored)
                    return true;                 // mobs/players handled by normal physics
                return false;
            },
            returnOnFirstHit: true);

        if (!results.Any())
            return;

        var hit = results.First();
        if (hit.Distance <= float.Epsilon)
            return;

        var hitPosition = new MapCoordinates(hit.HitPos, xform.MapID);
        var timeToHit   = TimeSpan.FromSeconds(hit.Distance / speed);

        // Collect optional explosion parameters.
        string? explosionType = null;
        float totalIntensity  = 0f;
        float slope           = 1f;
        float maxIntensity    = 4f;
        int   maxTileBreak    = int.MaxValue;

        if (TryComp<ExplosiveComponent>(projUid, out var explosive))
        {
            explosionType  = explosive.ExplosionType;    // ProtoId<T> → string implicit
            totalIntensity = explosive.TotalIntensity;
            slope          = explosive.IntensitySlope;
            maxIntensity   = explosive.MaxIntensity;
            maxTileBreak   = explosive.MaxTileBreak;
        }

        _pendingHits[projUid] = new WallHitEntry(
            TriggerTime:    _timing.CurTime + timeToHit,
            Position:       hitPosition,
            ExplosionType:  explosionType,
            TotalIntensity: totalIntensity,
            Slope:          slope,
            MaxIntensity:   maxIntensity,
            MaxTileBreak:   maxTileBreak);
    }
}
