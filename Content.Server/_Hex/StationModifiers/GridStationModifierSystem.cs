using System.Linq;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._Hex.Faction;
using Content.Shared._Hex.StationModifiers.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Maths;

namespace Content.Server._Hex.StationModifiers;

/// <summary>
///     Manages station and grid-wide modifiers (health and weapon damage) for station grids.
///     Reinforces durability of walls, windows, and cannons while preserving health percentage
///     to prevent entities from breaking when modifiers are removed or scaled down.
/// </summary>
public sealed class GridStationModifierSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Reinforced entity tracking (walls, windows, cannons)
        SubscribeLocalEvent<StationReinforcedComponent, ComponentStartup>(OnReinforcedStartup);
        SubscribeLocalEvent<StationReinforcedComponent, MapInitEvent>(OnReinforcedMapInit);
        SubscribeLocalEvent<StationReinforcedComponent, ComponentShutdown>(OnReinforcedShutdown);
        SubscribeLocalEvent<StationReinforcedComponent, EntParentChangedMessage>(OnReinforcedParentChanged);

        // Weapon damage amplification
        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);

        // Grid cleanup
        SubscribeLocalEvent<GridStationModifierComponent, ComponentShutdown>(OnGridModifierShutdown);
    }

    #region Station Beacon Handling

    public void OnBeaconStartup(EntityUid beaconUid)
    {
        UpdateBeaconGrid(beaconUid, null);
    }

    public void OnBeaconShutdown(EntityUid beaconUid)
    {
        var gridUid = _transform.GetGrid(beaconUid);
        if (gridUid != null && TryComp<GridStationModifierComponent>(gridUid, out var gridMod))
        {
            gridMod.BeaconSources.Remove(beaconUid);
            UpdateGridModifiers(gridUid.Value, gridMod);
        }
    }

    public void UpdateBeaconGrid(EntityUid beaconUid, EntityUid? oldGrid = null)
    {
        var newGrid = _transform.GetGrid(beaconUid);

        if (oldGrid != null && oldGrid != newGrid && TryComp<GridStationModifierComponent>(oldGrid, out var oldMod))
        {
            oldMod.BeaconSources.Remove(beaconUid);
            UpdateGridModifiers(oldGrid.Value, oldMod);
        }

        if (newGrid != null)
        {
            var gridMod = EnsureComp<GridStationModifierComponent>(newGrid.Value);
            gridMod.BeaconSources.Add(beaconUid);
            UpdateGridModifiers(newGrid.Value, gridMod);
        }
    }

    #endregion

    #region Grid Modifier Management

    /// <summary>
    /// Recalculates grid modifiers and refreshes reinforced entities if the multiplier changed.
    /// </summary>
    public void UpdateGridModifiers(EntityUid gridUid, GridStationModifierComponent? gridMod = null)
    {
        if (!Resolve(gridUid, ref gridMod, false))
            return;

        var oldHealthMod = gridMod.HealthMultiplier;

        // Prune any deleted or terminating beacons
        gridMod.BeaconSources.RemoveWhere(uid => Deleted(uid) || Terminating(uid));

        // Tying 2X multiplier unconditionally to the hex station beacon presence
        var hasBeacon = gridMod.BeaconSources.Count > 0;
        gridMod.HealthMultiplier = hasBeacon ? 2.0f : 1.0f;
        gridMod.WeaponDamageMultiplier = hasBeacon ? 2.0f : 1.0f;

        Dirty(gridUid, gridMod);

        if (!MathHelper.CloseTo(oldHealthMod, gridMod.HealthMultiplier))
        {
            RefreshGridEntities(gridUid, gridMod);
        }
    }

    private void RefreshGridEntities(EntityUid gridUid, GridStationModifierComponent gridMod)
    {
        // Populate cache if empty (e.g. on round start or grid spawn)
        if (gridMod.ReinforcedEntities.Count == 0)
        {
            var query = EntityQueryEnumerator<StationReinforcedComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == gridUid)
                {
                    gridMod.ReinforcedEntities.Add(uid);
                }
            }
        }

        foreach (var entity in gridMod.ReinforcedEntities.ToArray())
        {
            if (Deleted(entity) || Terminating(entity))
            {
                gridMod.ReinforcedEntities.Remove(entity);
                continue;
            }

            if (TryComp<StationReinforcedComponent>(entity, out var reinforced))
            {
                ApplyMultiplier(entity, reinforced, gridMod.HealthMultiplier);
            }
        }
    }

    private void OnGridModifierShutdown(Entity<GridStationModifierComponent> ent, ref ComponentShutdown args)
    {
        foreach (var entity in ent.Comp.ReinforcedEntities)
        {
            if (TryComp<StationReinforcedComponent>(entity, out var reinforced))
            {
                ApplyMultiplier(entity, reinforced, 1.0f);
            }
        }
        ent.Comp.ReinforcedEntities.Clear();
    }

    #endregion

    #region Station Reinforced Entities

    private void OnReinforcedStartup(Entity<StationReinforcedComponent> ent, ref ComponentStartup args)
    {
        RegisterReinforcedEntity(ent.Owner, ent.Comp);
    }

    private void OnReinforcedMapInit(Entity<StationReinforcedComponent> ent, ref MapInitEvent args)
    {
        RegisterReinforcedEntity(ent.Owner, ent.Comp);
    }

    private void OnReinforcedShutdown(Entity<StationReinforcedComponent> ent, ref ComponentShutdown args)
    {
        var gridUid = _transform.GetGrid(ent.Owner);
        if (gridUid != null && TryComp<GridStationModifierComponent>(gridUid, out var gridMod))
        {
            gridMod.ReinforcedEntities.Remove(ent.Owner);
        }
    }

    private void OnReinforcedParentChanged(Entity<StationReinforcedComponent> ent, ref EntParentChangedMessage args)
    {
        var oldGrid = args.OldParent != null ? _transform.GetGrid(args.OldParent.Value) : null;
        if (oldGrid != null && TryComp<GridStationModifierComponent>(oldGrid, out var oldMod))
        {
            oldMod.ReinforcedEntities.Remove(ent.Owner);
        }

        RegisterReinforcedEntity(ent.Owner, ent.Comp);
    }

    private void RegisterReinforcedEntity(EntityUid uid, StationReinforcedComponent comp)
    {
        var gridUid = _transform.GetGrid(uid);
        if (gridUid != null)
        {
            var gridMod = EnsureComp<GridStationModifierComponent>(gridUid.Value);
            gridMod.ReinforcedEntities.Add(uid);
            ApplyMultiplier(uid, comp, gridMod.HealthMultiplier);
        }
        else
        {
            ApplyMultiplier(uid, comp, 1.0f);
        }
    }

    /// <summary>
    /// Applies a health/durability multiplier to a reinforced entity, adjusting max destruction
    /// thresholds and current damage such that the health percentage is preserved and shrinking
    /// the modifier never destroys the entity.
    /// </summary>
    public void ApplyMultiplier(EntityUid uid, StationReinforcedComponent comp, float targetMultiplier)
    {
        if (MathHelper.CloseTo(comp.AppliedMultiplier, targetMultiplier))
            return;

        if (!TryComp<DestructibleComponent>(uid, out var destructible) ||
            !TryComp<DamageableComponent>(uid, out var damageable))
        {
            return;
        }

        if (!comp.Initialized)
        {
            InitializeBaseThresholds(comp, destructible);
        }

        if (comp.BaseThresholds.Count == 0)
            return;

        var oldMultiplier = comp.AppliedMultiplier;
        if (oldMultiplier <= 0f)
            oldMultiplier = 1.0f;

        var ratio = targetMultiplier / oldMultiplier;
        if (ratio <= 0f)
            return;

        // Percentage Health Preservation:
        // Scaling down (ratio < 1):
        //   1. Scale down damage first so damage never exceeds the new lower threshold.
        //   2. Lower the thresholds.
        // Scaling up (ratio > 1):
        //   1. Increase the thresholds first.
        //   2. Scale up damage proportionally to preserve exact percentage.
        if (ratio < 1.0f)
        {
            ScaleDamage(uid, damageable, ratio, comp, destructible, targetMultiplier);
            UpdateThresholds(destructible, comp.BaseThresholds, targetMultiplier);
        }
        else
        {
            UpdateThresholds(destructible, comp.BaseThresholds, targetMultiplier);
            ScaleDamage(uid, damageable, ratio, comp, destructible, targetMultiplier);
        }

        comp.AppliedMultiplier = targetMultiplier;
        Dirty(uid, comp);
    }

    private void InitializeBaseThresholds(StationReinforcedComponent comp, DestructibleComponent destructible)
    {
        comp.BaseThresholds.Clear();
        for (var i = 0; i < destructible.Thresholds.Count; i++)
        {
            var trigger = destructible.Thresholds[i].Trigger;
            if (trigger is DamageTrigger dt)
                comp.BaseThresholds[i] = dt.Damage;
            else if (trigger is DamageGroupTrigger dgt)
                comp.BaseThresholds[i] = dgt.Damage;
            else if (trigger is DamageTypeTrigger dtt)
                comp.BaseThresholds[i] = dtt.Damage;
        }
        comp.Initialized = true;
    }

    private void UpdateThresholds(DestructibleComponent destructible, Dictionary<int, int> baseThresholds, float targetMultiplier)
    {
        foreach (var (index, baseDmg) in baseThresholds)
        {
            if (index < 0 || index >= destructible.Thresholds.Count)
                continue;

            var threshold = destructible.Thresholds[index];
            var newDmg = Math.Max(1, (int)Math.Round(baseDmg * targetMultiplier));

            if (threshold.Trigger is DamageTrigger dt)
                dt.Damage = newDmg;
            else if (threshold.Trigger is DamageGroupTrigger dgt)
                dgt.Damage = newDmg;
            else if (threshold.Trigger is DamageTypeTrigger dtt)
                dtt.Damage = newDmg;
        }
    }

    private void ScaleDamage(
        EntityUid uid,
        DamageableComponent damageable,
        float ratio,
        StationReinforcedComponent comp,
        DestructibleComponent destructible,
        float targetMultiplier)
    {
        if (damageable.TotalDamage <= FixedPoint2.Zero)
            return;

        var newTotal = damageable.TotalDamage * ratio;

        // Safety guarantee: If scaling down, ensure damage strictly cannot cross any untriggered destruction threshold
        if (ratio < 1.0f)
        {
            var lowestUntriggered = int.MaxValue;
            foreach (var (index, baseDmg) in comp.BaseThresholds)
            {
                if (index < 0 || index >= destructible.Thresholds.Count)
                    continue;

                var thresh = destructible.Thresholds[index];
                if (!thresh.Triggered)
                {
                    var targetThresholdValue = Math.Max(1, (int)Math.Round(baseDmg * targetMultiplier));
                    if (targetThresholdValue < lowestUntriggered)
                        lowestUntriggered = targetThresholdValue;
                }
            }

            if (lowestUntriggered != int.MaxValue && newTotal >= lowestUntriggered)
            {
                var maxSafeDamage = Math.Max(0, lowestUntriggered - 1);
                if (newTotal > maxSafeDamage)
                {
                    ratio = (float)maxSafeDamage / (float)damageable.TotalDamage;
                }
            }
        }

        var newDamage = new DamageSpecifier();
        foreach (var (type, val) in damageable.Damage.DamageDict)
        {
            newDamage.DamageDict[type] = FixedPoint2.Max(FixedPoint2.Zero, val * ratio);
        }

        _damageableSystem.SetDamage(uid, damageable, newDamage);
    }

    #endregion

    #region Weapon Damage Handling

    private void OnAmmoShot(Entity<GunComponent> ent, ref AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
            return;

        var gridUid = _transform.GetGrid(ent.Owner);
        if (gridUid == null || !TryComp<GridStationModifierComponent>(gridUid, out var modifier))
            return;

        if (MathHelper.CloseTo(modifier.WeaponDamageMultiplier, 1.0f))
            return;

        foreach (var projUid in args.FiredProjectiles)
        {
            if (TryComp<ProjectileComponent>(projUid, out var proj))
            {
                proj.Damage *= modifier.WeaponDamageMultiplier;
            }
        }
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        var gridUid = _transform.GetGrid(ent.Owner);
        if (gridUid == null && args.User.IsValid())
            gridUid = _transform.GetGrid(args.User);

        if (gridUid == null || !TryComp<GridStationModifierComponent>(gridUid, out var modifier))
            return;

        if (MathHelper.CloseTo(modifier.WeaponDamageMultiplier, 1.0f))
            return;

        var bonus = args.BaseDamage * (modifier.WeaponDamageMultiplier - 1.0f);
        args.BonusDamage += bonus;
    }

    #endregion
}
