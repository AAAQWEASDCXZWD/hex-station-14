using System.Numerics;
using Content.Server._Hex.FireControl;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Hex.ShipGuns;
using Content.Shared._Hex.SpaceArtillery;
using Content.Shared.Camera;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Player;
using SpaceArtilleryComponent = Content.Server._Hex.SpaceArtillery.Components.SpaceArtilleryComponent;

namespace Content.Server._Hex.SpaceArtillery;

/// <summary>
/// Shared power handling and impact effects for ship and station artillery.
/// Firing itself is driven by <see cref="FireControlSystem"/> or an external signal.
/// </summary>
public sealed partial class SpaceArtillerySystem : EntitySystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private FireControlSystem _fireControl = default!;

    private const float ImpactDamageForMaxKick = 1000f;
    private const float MaxImpactKick = 35f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpaceArtilleryComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<SpaceArtilleryComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<SpaceArtilleryComponent, ChargeChangedEvent>(OnBatteryChargeChanged);
        SubscribeLocalEvent<ShipWeaponProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<ShipGunClassComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAmmoShot(EntityUid uid, SpaceArtilleryComponent component, AmmoShotEvent args)
    {
        if (args.FiredProjectiles.Count == 0)
            return;

        if (TryComp<BatteryComponent>(uid, out var battery))
            _battery.UseCharge(uid, component.PowerUseActive, battery);

        OnAmmoShotWallTrace(uid, component, args);
    }

    private void OnPowerChanged(EntityUid uid, SpaceArtilleryComponent component, ref PowerChangedEvent args)
    {
        if (!TryComp<BatterySelfRechargerComponent>(uid, out var charger))
            return;

        charger.AutoRecharge = true;
        charger.AutoRechargeRate = args.Powered
            ? component.PowerChargeRate
            : -component.PowerUsePassive;
    }

    private void OnBatteryChargeChanged(EntityUid uid, SpaceArtilleryComponent component, ref ChargeChangedEvent args)
    {
        if (!TryComp<ApcPowerReceiverComponent>(uid, out var receiver) ||
            !TryComp<BatteryComponent>(uid, out var battery))
        {
            return;
        }

        receiver.Load = battery.CurrentCharge >= battery.MaxCharge * 0.99f
            ? component.PowerUsePassive
            : component.PowerUsePassive + component.PowerChargeRate;
    }

    /// <summary>
    /// Shakes the camera of everyone on the hit grid proportionally to the impact damage.
    /// </summary>
    private void OnProjectileHit(EntityUid uid, ShipWeaponProjectileComponent component, ref ProjectileHitEvent args)
    {
        var grid = Transform(args.Target).GridUid;
        if (grid == null)
            return;

        var players = Filter.Empty();
        players.AddInGrid(grid.Value);

        var projectilePosition = _xform.GetWorldPosition(uid);
        var kick = (float)args.Damage.GetTotal() / ImpactDamageForMaxKick * MaxImpactKick;

        foreach (var player in players.Recipients)
        {
            if (player.AttachedEntity is not { } playerEntity)
                continue;

            var direction = projectilePosition - _xform.GetWorldPosition(playerEntity);
            if (direction.LengthSquared() <= float.Epsilon)
                continue;

            _recoil.KickCamera(playerEntity, direction.Normalized() * kick);
        }
    }

    private void OnExamined(EntityUid uid, ShipGunClassComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryComp<FireControllableComponent>(uid, out var controllable))
            return;

        args.PushMarkup(
            Loc.GetString(
                "ship-gun-class-component-examine-detail",
                ("processingPower", _fireControl.GetProcessingPowerCost(uid, controllable))));
    }
}
