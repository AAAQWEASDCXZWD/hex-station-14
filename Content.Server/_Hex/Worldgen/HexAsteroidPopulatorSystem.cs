using Content.Server.Worldgen.Systems;
using Content.Shared._Hex.Mining;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Hex.Worldgen;

public sealed class HexAsteroidPopulatorSystem : BaseWorldSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private static readonly Dictionary<HexOreType, float> DefaultOreWeights = new()
    {
        { HexOreType.Tin, 1.0f },
        { HexOreType.Coal, 0.9f },
        { HexOreType.Quartz, 0.8f },
        { HexOreType.Salt, 0.7f },
        { HexOreType.Silver, 0.5f },
        { HexOreType.Gold, 0.45f },
        { HexOreType.Plasma, 0.4f },
        { HexOreType.Uranium, 0.35f },
        { HexOreType.Bananium, 0.15f },
        { HexOreType.ArtifactFragment, 0.15f },
        { HexOreType.Diamond, 0.1f },
        { HexOreType.Bluespace, 0.1f },
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<HexAsteroidPopulatorComponent, LocalStructureLoadedEvent>(OnStructureLoaded);
    }

    private void OnStructureLoaded(EntityUid uid, HexAsteroidPopulatorComponent component, LocalStructureLoadedEvent args)
    {
        if (!TryComp<MapGridComponent>(uid, out var grid))
            return;

        var oreData = EnsureComp<AsteroidOreDataComponent>(uid);

        // Спробуємо дізнатися біом чанка, у якому спавниться цей астероїд
        HexMonolithicOreWeightsComponent? biomeMono = null;
        var xform = Transform(uid);
        if (xform.MapUid != null)
        {
            var chunkCoords = GetChunkCoords(uid, xform);
            var chunkUid = GetOrCreateChunk(chunkCoords, xform.MapUid.Value);
            if (chunkUid != null)
                TryComp(chunkUid.Value, out biomeMono);
        }

        var weights = component.OreWeights ?? biomeMono?.OreWeights ?? DefaultOreWeights;
        var fillChance = component.FillChance ?? biomeMono?.FillChance ?? 0.75f;
        var inclusionsChance = biomeMono?.InclusionsChance ?? 0.05f;
        var guardianChance = biomeMono?.GuardianChance ?? 0.02f;

        var primaryOre = component.PrimaryOre;
        if (component.AsteroidType == HexAsteroidType.RichSingle && primaryOre == HexOreType.None)
        {
            primaryOre = PickRandomWeightedOre(weights);
        }

        var enumerator = _map.GetAllTilesEnumerator(uid, grid);
        while (enumerator.MoveNext(out var tile))
        {
            var coords = _map.GridTileToLocal(uid, grid, tile.Value.GridIndices);
            var tileDef = _turf.GetContentTileDefinition(tile.Value).ID;
            var material = GetAsteroidMaterial(tileDef);

            string? protoToSpawn = null;
            HexOreType placedOre = HexOreType.None;

            switch (component.AsteroidType)
            {
                case HexAsteroidType.Empty:
                    protoToSpawn = GetBaseRockProto(material);
                    break;

                case HexAsteroidType.RichAll:
                    var rollAll = _random.NextFloat();
                    if (rollAll < 0.80f)
                    {
                        placedOre = PickRandomWeightedOre(weights);
                        protoToSpawn = GetOreProto(material, placedOre);
                    }
                    else if (rollAll < 0.95f)
                    {
                        protoToSpawn = GetBaseRockProto(material);
                    }
                    else
                    {
                        protoToSpawn = GetGuardianProto(material);
                    }
                    break;

                case HexAsteroidType.RichSingle:
                    var rollSingle = _random.NextFloat();
                    if (rollSingle < fillChance)
                    {
                        placedOre = primaryOre;
                        protoToSpawn = GetOreProto(material, placedOre);
                    }
                    else if (rollSingle < fillChance + inclusionsChance)
                    {
                        // Рідкісні випадкові вкраплення інших руд
                        placedOre = PickRandomWeightedOre(weights);
                        protoToSpawn = GetOreProto(material, placedOre);
                    }
                    else if (rollSingle < fillChance + inclusionsChance + guardianChance)
                    {
                        protoToSpawn = GetGuardianProto(material);
                    }
                    else
                    {
                        protoToSpawn = GetBaseRockProto(material);
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(protoToSpawn))
            {
                Spawn(protoToSpawn, coords);
            }

            if (placedOre != HexOreType.None)
            {
                oreData.Ores[tile.Value.GridIndices] = placedOre;
            }
        }
    }

    private HexOreType PickRandomWeightedOre(IReadOnlyDictionary<HexOreType, float> weights)
    {
        var totalWeight = 0f;
        foreach (var weight in weights.Values)
            totalWeight += weight;

        if (totalWeight <= 0f)
            return HexOreType.Tin;

        var roll = _random.NextFloat() * totalWeight;
        var current = 0f;
        foreach (var (ore, weight) in weights)
        {
            current += weight;
            if (roll <= current)
                return ore;
        }

        return HexOreType.Tin;
    }

    private enum MaterialType : byte
    {
        Rock,
        Basalt,
        Chromite,
        Andesite,
        Sand,
        Scrap
    }

    private MaterialType GetAsteroidMaterial(string tileDef) => tileDef switch
    {
        "FloorBasalt" => MaterialType.Basalt,
        "FloorChromite" => MaterialType.Chromite,
        "FloorCaveDrought" => MaterialType.Andesite,
        "FloorLowDesert" => MaterialType.Sand,
        "Lattice" => MaterialType.Scrap,
        _ => MaterialType.Rock
    };

    private string GetBaseRockProto(MaterialType material) => material switch
    {
        MaterialType.Basalt => "WallRockBasalt",
        MaterialType.Chromite => "WallRockChromite",
        MaterialType.Andesite => "WallRockAndesite",
        MaterialType.Sand => "WallRockSand",
        MaterialType.Scrap => "NFWallRockScrapPile",
        _ => "WallRock"
    };

    private string GetGuardianProto(MaterialType material) => material switch
    {
        MaterialType.Basalt => "NFBasaltElementalSpawner",
        MaterialType.Chromite => "NFChromiteElementalSpawner",
        MaterialType.Andesite => "NFAndesiteElementalSpawner",
        MaterialType.Sand => "NFSandElementalSpawner",
        MaterialType.Scrap => "NFAsteroidScrapBotWallSpawner",
        _ => "NFRockElementalSpawner"
    };

    private string GetOreProto(MaterialType material, HexOreType ore) => material switch
    {
        MaterialType.Basalt => ore switch
        {
            HexOreType.Coal => "WallRockBasaltCoal",
            HexOreType.Tin => "WallRockBasaltTin",
            HexOreType.Quartz => "WallRockBasaltQuartz",
            HexOreType.Salt => "WallRockBasaltSalt",
            HexOreType.Gold => "WallRockBasaltGold",
            HexOreType.Silver => "WallRockBasaltSilver",
            HexOreType.Plasma => "WallRockBasaltPlasma",
            HexOreType.Uranium => "WallRockBasaltUranium",
            HexOreType.Bananium => "WallRockBasaltBananium",
            HexOreType.ArtifactFragment => "WallRockBasaltArtifactFragment",
            HexOreType.Diamond => "WallRockBasaltDiamond",
            HexOreType.Bluespace => "NFWallRockBasaltBluespace",
            _ => "WallRockBasalt"
        },
        MaterialType.Chromite => ore switch
        {
            HexOreType.Coal => "WallRockChromiteCoal",
            HexOreType.Tin => "WallRockChromiteTin",
            HexOreType.Quartz => "WallRockChromiteQuartz",
            HexOreType.Salt => "WallRockChromiteSalt",
            HexOreType.Gold => "WallRockChromiteGold",
            HexOreType.Silver => "WallRockChromiteSilver",
            HexOreType.Plasma => "WallRockChromitePlasma",
            HexOreType.Uranium => "WallRockChromiteUranium",
            HexOreType.Bananium => "WallRockChromiteBananium",
            HexOreType.ArtifactFragment => "WallRockChromiteArtifactFragment",
            HexOreType.Diamond => "WallRockChromiteDiamond",
            HexOreType.Bluespace => "NFWallRockChromiteBluespace",
            _ => "WallRockChromite"
        },
        MaterialType.Andesite => ore switch
        {
            HexOreType.Coal => "WallRockAndesiteCoal",
            HexOreType.Tin => "WallRockAndesiteTin",
            HexOreType.Quartz => "WallRockAndesiteQuartz",
            HexOreType.Salt => "WallRockAndesiteSalt",
            HexOreType.Gold => "WallRockAndesiteGold",
            HexOreType.Silver => "WallRockAndesiteSilver",
            HexOreType.Plasma => "WallRockAndesitePlasma",
            HexOreType.Uranium => "WallRockAndesiteUranium",
            HexOreType.Bananium => "WallRockAndesiteBananium",
            HexOreType.ArtifactFragment => "WallRockAndesiteArtifactFragment",
            HexOreType.Diamond => "WallRockAndesiteDiamond",
            HexOreType.Bluespace => "NFWallRockAndesiteBluespace",
            _ => "WallRockAndesite"
        },
        MaterialType.Sand => ore switch
        {
            HexOreType.Coal => "WallRockSandCoal",
            HexOreType.Tin => "WallRockSandTin",
            HexOreType.Quartz => "WallRockSandQuartz",
            HexOreType.Salt => "WallRockSandSalt",
            HexOreType.Gold => "WallRockSandGold",
            HexOreType.Silver => "WallRockSandSilver",
            HexOreType.Plasma => "WallRockSandPlasma",
            HexOreType.Uranium => "WallRockSandUranium",
            HexOreType.Bananium => "WallRockSandBananium",
            HexOreType.ArtifactFragment => "WallRockSandArtifactFragment",
            HexOreType.Diamond => "WallRockSandDiamond",
            HexOreType.Bluespace => "NFWallRockSandBluespace",
            _ => "WallRockSand"
        },
        MaterialType.Scrap => ore switch
        {
            HexOreType.Coal => "NFWallRockScrapPileCoal",
            HexOreType.Tin => "NFWallRockScrapPileIron",
            HexOreType.Quartz => "NFWallRockScrapPileQuartz",
            HexOreType.Salt => "NFWallRockScrapPileSalt",
            HexOreType.Gold => "NFWallRockScrapPileGold",
            HexOreType.Silver => "NFWallRockScrapPileSilver",
            HexOreType.Plasma => "NFWallRockScrapPilePlasma",
            HexOreType.Uranium => "NFWallRockScrapPileUranium",
            HexOreType.Bananium => "NFWallRockScrapPileBananium",
            HexOreType.ArtifactFragment => "NFWallRockScrapPileArtifactFragment",
            HexOreType.Diamond => "NFWallRockScrapPileDiamond",
            HexOreType.Bluespace => "NFWallRockScrapPileBluespace",
            _ => "NFWallRockScrapPile"
        },
        _ => ore switch
        {
            HexOreType.Coal => "WallRockCoal",
            HexOreType.Tin => "WallRockTin",
            HexOreType.Quartz => "WallRockQuartz",
            HexOreType.Salt => "WallRockSalt",
            HexOreType.Gold => "WallRockGold",
            HexOreType.Silver => "WallRockSilver",
            HexOreType.Plasma => "WallRockPlasma",
            HexOreType.Uranium => "WallRockUranium",
            HexOreType.Bananium => "WallRockBananium",
            HexOreType.ArtifactFragment => "WallRockArtifactFragment",
            HexOreType.Diamond => "WallRockDiamond",
            HexOreType.Bluespace => "NFWallRockBluespace",
            _ => "WallRock"
        }
    };
}
