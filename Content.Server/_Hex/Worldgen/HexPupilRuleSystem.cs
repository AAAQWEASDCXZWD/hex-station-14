using System.Linq;
using System.Numerics;
using Content.Server._NF.GameRule;
using Content.Server._NF.Worldgen.Components.Carvers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using HexMath = Content.Shared.HexGrid.HexGrid;

namespace Content.Server._Hex.Worldgen;

/// <summary>
/// Правило гри для пресету "Hex-Pupil".
/// Забезпечує спеціальну структуру сектора:
/// - Станція Фронтіру в центрі (0, 0);
/// - Два торгові пости на заданих пропорціях;
/// - Астероїдний шлейф по межі карти гексів (~1 гекс);
/// - Зміщене щільне скупчення астероїдів з периферійним обідком;
/// - Плато з малими астероїдами на решті простору карти.
/// </summary>
public sealed class HexPupilRuleSystem : GameRuleSystem<HexPupilRuleComponent>
{
    // =========================================================================
    // НАЛАШТУВАННЯ ПРЕСЕТУ HEX-PUPIL (РЕГУЛЮЮТЬСЯ ТУТ)
    // =========================================================================

    /// <summary>
    /// Масштаб 1 гекса з HexCoordinates.cs (HexRadius = 450f, відстань від центру до вершин).
    /// Діаметр гекса (від вершини до вершини) = 900м, ширина (між гранями) = ~779.4м.
    /// </summary>
    public static readonly float HexScale = HexMath.HexRadius; // 450f

    /// <summary>
    /// Масштаб 1 одиниці пропорцій у метрах мапи світу.
    /// При значенні 100f пропорція довжиною ~12 дає відстань ~1200м (середина простору мапи гексів).
    /// </summary>
    public const float ProportionUnit = 100f;

    /// <summary>
    /// 1. Пропорції першого торгового посту від центру станції Фронтіру (-10; -7).
    /// Координати в метрах світу: (-1000, -700).
    /// </summary>
    public static readonly Vector2 TradePost1Proportion = new(-10f, -7f);
    public static readonly Vector2 TradePost1Position = TradePost1Proportion * ProportionUnit;
    public const string TradePost1Name = "Trade Outpost Alpha";

    /// <summary>
    /// 2. Пропорції другого торгового посту від центру станції Фронтіру (11; -5).
    /// Координати в метрах світу: (1100, -500).
    /// </summary>
    public static readonly Vector2 TradePost2Proportion = new(11f, -5f);
    public static readonly Vector2 TradePost2Position = TradePost2Proportion * ProportionUnit;
    public const string TradePost2Name = "Trade Outpost Beta";

    /// <summary>
    /// Прототип торгового посту (TradeMall за замовчуванням).
    /// </summary>
    public const string TradePostProtoId = "TradeMall";

    /// <summary>
    /// 3. Співвідношення зміщення центру скупчення астероїдів (-1 по x, 10 по y).
    /// Координати центру скупчення в метрах світу: (-100, 1000).
    /// </summary>
    public static readonly Vector2 ClusterProportion = new(-1f, 10f);
    public static readonly Vector2 ClusterCenterOffset = ClusterProportion * ProportionUnit;

    /// <summary>
    /// Діаметр ядра скупчення астероїдів десь в 1 гекс (~900м діаметр, тобто радіус 450м).
    /// </summary>
    public static readonly float ClusterCoreRadius = HexScale; // 450f

    /// <summary>
    /// Зовнішній радіус периферії (обідка) скупчення астероїдів (HexScale * 1.5 = ~675м).
    /// Зона обідка розташована в межах від ClusterCoreRadius до ClusterPeripheryRadius.
    /// </summary>
    public static readonly float ClusterPeripheryRadius = HexScale * 1.5f; // ~675м

    /// <summary>
    /// Радіус межі карти гексів (StandardHexRadius = 3 гекси від центру).
    /// 3 гекси = 3 * HexScale * 2 (діаметр) / 2... але точніший розрахунок:
    /// центр гексагональної сітки з радіусом 3 кільця: відстань до зовнішнього краю
    /// приблизно = 3 * HexScale * sqrt(3) ≈ 3 * 450 * 1.732 ≈ 2338м.
    /// Використовуємо формулу: 3 гекси * HexScale * 1.732 (відстань між центрами по осі).
    /// </summary>
    public static readonly float HexMapBoundaryRadius = HexScale * 5.196f; // 3 * sqrt(3) * HexScale ≈ 2338м

    /// <summary>
    /// Товщина астероїдного шлейфу по межі карти (~1 гекс у ширину = HexScale * 2 ≈ 900м).
    /// Половина шлейфу (~450м) всередині меж карти гексів, інша половина (~450м) за її межами.
    /// </summary>
    public static readonly float TrailThickness = HexScale * 2f;              // ~900м
    public static readonly float TrailInnerRadius = HexMapBoundaryRadius - (TrailThickness * 0.5f); // ~1888м
    public static readonly float TrailOuterRadius = HexMapBoundaryRadius + (TrailThickness * 0.5f); // ~2788м

    /// <summary>
    /// Внутрішня буферна зона шлейфу: частина гексів, що не входять у сам шлейф,
    /// але мають помітно більше астероїдів за звичайне плато.
    /// Починається за 1 гекс всередині від межі шлейфу.
    /// </summary>
    public static readonly float TrailBufferInnerRadius = TrailInnerRadius - HexScale; // ~1438м

    /// <summary>
    /// Зовнішня межа плато для генерації малих астероїдів.
    /// Перекриває весь простір до краю шлейфу (встановлюється з запасом).
    /// </summary>
    public static readonly float PlateauOuterRadius = TrailOuterRadius + HexScale; // ~3238м

    /// <summary>
    /// Радіус мертвої зони навколо станції Frontier Outpost (0-250м: 0% спавну астероїдів для безпеки доків).
    /// </summary>
    public static readonly float FrontierDeadZoneRadius = 250f;

    /// <summary>
    /// Радіус безпечної перехідної зони навколо станції Frontier Outpost (250-400м).
    /// За межами цієї зони повністю працює плато з малими астероїдами.
    /// </summary>
    public static readonly float FrontierSafeRadius = 400f;

    /// <summary>
    /// ID конфігурації генерації світу для пресету.
    /// </summary>
    public const string WorldgenConfigId = "HexPupil";

    // =========================================================================

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PointOfInterestSystem _poi = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("hex.pupil");
        SynchronizeBiomePrototypes();
    }

    /// <summary>
    /// Синхронізує параметри біомів у прототипах із налаштуваннями констант у файлі.
    /// </summary>
    private void SynchronizeBiomePrototypes()
    {
        if (_proto.TryIndex<BiomePrototype>("HexPupilClusterCore", out var core))
        {
            core.CenterOffset = ClusterCenterOffset;
            core.DistanceRange = new Vector2(0f, ClusterCoreRadius);
        }

        if (_proto.TryIndex<BiomePrototype>("HexPupilClusterPeriphery", out var periphery))
        {
            periphery.CenterOffset = ClusterCenterOffset;
            periphery.DistanceRange = new Vector2(ClusterCoreRadius, ClusterPeripheryRadius);
        }

        if (_proto.TryIndex<BiomePrototype>("HexPupilBorderTrail", out var trail))
        {
            trail.CenterOffset = Vector2.Zero;
            trail.DistanceRange = new Vector2(TrailInnerRadius, TrailOuterRadius);
        }

        if (_proto.TryIndex<BiomePrototype>("HexPupilBorderTrailBuffer", out var trailBuffer))
        {
            trailBuffer.CenterOffset = Vector2.Zero;
            trailBuffer.DistanceRange = new Vector2(TrailBufferInnerRadius, TrailInnerRadius);
        }

        if (_proto.TryIndex<BiomePrototype>("HexPupilPlateau", out var plateau))
        {
            // Плато — фолбек без обмеження по дистанції (priority 0, ловить все що лишилось)
            plateau.CenterOffset = Vector2.Zero;
            plateau.DistanceRange = null;
        }
    }

    protected override void Started(EntityUid uid, HexPupilRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var mapUid = GameTicker.DefaultMap;
        if (!_map.MapExists(mapUid))
            return;

        // Синхронізуємо біоми перед генерацією
        SynchronizeBiomePrototypes();

        // 1. Налаштовуємо карвер мертвої зони на станції Frontier Outpost,
        // щоб він захищав лише саму станцію і не вирізав плато та зміщене скупчення.
        AdjustFrontierStationCarver();

        // 2. Спавнимо два торгові пости за заданими пропорціями.
        SpawnTradePosts(mapUid, component);
    }

    private void AdjustFrontierStationCarver()
    {
        var query = EntityQueryEnumerator<WorldGenDistanceCarverComponent, MetaDataComponent>();
        while (query.MoveNext(out var entityUid, out var carver, out var meta))
        {
            if (meta.EntityName.Contains("Frontier"))
            {
                carver.DistanceThresholds = new()
                {
                    new WorldGenDistanceThreshold { Prob = 0f, MaxDistance = FrontierDeadZoneRadius },
                    new WorldGenDistanceThreshold { Prob = 0.5f, MaxDistance = FrontierSafeRadius },
                };
                carver.SquaredDistanceThresholds = carver.DistanceThresholds
                    .OrderByDescending(x => x.MaxDistance)
                    .Select(x => new WorldGenDistanceThreshold { MaxDistance = x.MaxDistance * x.MaxDistance, Prob = x.Prob })
                    .ToList();
                _sawmill.Info($"Adjusted Frontier Outpost carver dead zone to {FrontierDeadZoneRadius}m on {entityUid}");
                break;
            }
        }
    }

    private void SpawnTradePosts(MapId mapUid, HexPupilRuleComponent component)
    {
        if (!_proto.TryIndex<PointOfInterestPrototype>(TradePostProtoId, out var tradeProto))
        {
            _sawmill.Error($"Could not find PointOfInterestPrototype '{TradePostProtoId}' to spawn trade outposts!");
            return;
        }

        // Спавн першого торгового посту (-10, -7)
        if (_poi.TrySpawnPoiGrid(mapUid, tradeProto, TradePost1Position, out var post1, overrideName: TradePost1Name) && post1 != null)
        {
            component.TradePosts.Add(post1.Value);
            _sawmill.Info($"Spawned {TradePost1Name} at {TradePost1Position} (Proportion: {TradePost1Proportion})");
        }

        // Спавн другого торгового посту (11, -5)
        if (_poi.TrySpawnPoiGrid(mapUid, tradeProto, TradePost2Position, out var post2, overrideName: TradePost2Name) && post2 != null)
        {
            component.TradePosts.Add(post2.Value);
            _sawmill.Info($"Spawned {TradePost2Name} at {TradePost2Position} (Proportion: {TradePost2Proportion})");
        }
    }
}
