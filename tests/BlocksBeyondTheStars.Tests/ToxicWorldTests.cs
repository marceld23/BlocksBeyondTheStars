// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 13, the toxic worlds (#2024): a rare exotic planet class whose worlds ROLL their traits — corrosive air,
/// toxic water, cave fauna, rare-ore outcrops (<see cref="WorldTraits"/>) — with no surface life and every ore vein
/// shallow. The pure half: content, the galaxy roll, the trait rolls, rosters, worldgen, the water palette, the suit.
/// </summary>
public sealed class ToxicWorldTests
{
    private const int Gen = WorldDescription.ToxicWorldsGeneration;
    private const string Key = "toxic_world";

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private PlanetType Toxic => _content.GetPlanet(Key)!;

    // --- The class ---

    [Fact]
    public void TheClass_IsARareExoticGenerationThirteenType_WithNoSurfaceLife_AndOnlyShallowOres()
    {
        Assert.Equal(13, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen); // generation 14 (#2038) came after this wave
        var p = Toxic;
        Assert.Equal(Gen, p.MinTerrainGeneration);
        Assert.True(p.Exotic);
        Assert.True(p.Selectable);
        Assert.Equal(2, p.SpawnWeight);
        Assert.Equal("toxic", p.Atmosphere);
        Assert.Equal(0.0, p.FloraDensity);
        Assert.Equal(0.0, p.TreeDensity);
        Assert.Equal("none", p.CreatureAbundance);
        Assert.True(p.CaveThreshold > 0, "caves host the rare cave life");

        // Marcel: the traits and their chances.
        Assert.Equal(0.4, p.CorrosiveAirChance);
        Assert.Equal(0.8, p.WaterDamageChance);
        Assert.True(p.AirDamagePerSecond > 0 && p.WaterDamagePerSecond > 0);
        Assert.InRange(p.CaveFaunaChance, 0.15, 0.2);
        Assert.InRange(p.CaveFloraChance!.Value, 0.15, 0.2);
        Assert.InRange(p.OreOutcropChance, 0.15, 0.2);
        Assert.True(p.RuinedSettlementsOnly);
        Assert.True(p.SettlementsBias < 0.2 && p.RuinsBias < 1 && p.FactoriesBias < 1);

        // Ores ALWAYS shallow, and the rare tier-2 veins come first (the first vein that hits wins).
        Assert.All(p.Ores, o => Assert.True(o.MinDepth <= 8, $"{o.Block} starts {o.MinDepth} deep"));
        int firstCommon = p.Ores.FindIndex(o => !o.RareTier);
        Assert.True(firstCommon >= 5, "the rare veins lead the list");
        Assert.All(p.Ores.Take(firstCommon), o => Assert.True(o.RareTier, $"{o.Block} is not tier 2"));
        Assert.All(p.Ores, o => Assert.NotNull(_content.GetBlock(o.Block)));
        Assert.All(p.Biomes, b => Assert.NotNull(_content.GetBlock(b.SurfaceBlock)));

        // Every classic type keeps the no-op defaults (Titas' water stays always toxic).
        foreach (var other in _content.Planets.Values.Where(t => t.Key != Key))
        {
            Assert.Equal(0.0, other.CorrosiveAirChance);
            Assert.Equal(1.0, other.WaterDamageChance);
            Assert.Equal(0.0, other.CaveFaunaChance);
            Assert.Null(other.CaveFloraChance);
            Assert.Equal(0.0, other.OreOutcropChance);
            Assert.Equal(1.0, other.SettlementsBias);
            Assert.False(other.RuinedSettlementsOnly);
        }
    }

    [Fact]
    public void TheClass_AppearsOnlyInGenerationThirteenGalaxies_OnPlanetsAndMoons_AndExoticOffRemovesIt()
    {
        int gen12 = 0, planets = 0, moons = 0, exoticOff = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            gen12 += CountToxic(seed, new WorldDescription { TerrainGeneration = Gen - 1 }, out _);
            planets += CountToxic(seed, new WorldDescription { TerrainGeneration = Gen }, out int m) - m;
            moons += m;
            exoticOff += CountToxic(seed, new WorldDescription { TerrainGeneration = Gen, ExoticWorlds = Frequency.Off }, out _);
        }

        Assert.Equal(0, gen12);
        Assert.Equal(0, exoticOff);
        Assert.True(planets > 0, "a planet of the class in 40 galaxies");
        Assert.True(moons > 0, "a moon of the class in 40 galaxies");
        // Rare: weight 2 of the gated pool ≈ half a world per galaxy (measured 0.47).
        Assert.InRange((planets + moons) / 40.0, 0.15, 1.2);
    }

    // --- The trait rolls ---

    [Fact]
    public void Traits_RollPerWorld_AtTheirChances_Deterministically_AndOnlyFromGenerationThirteen()
    {
        var p = Toxic;
        int n = 3000, air = 0, water = 0, fauna = 0, outcrops = 0, both = 0;
        for (int i = 0; i < n; i++)
        {
            long roster = WorldGenerator.RosterSeedFor(20260926, $"sys{i % 40}-p{i / 40}");
            var t = WorldTraits.For(p, roster, Gen);
            Assert.Equal(t.CorrosiveAir, WorldTraits.For(p, roster, Gen).CorrosiveAir); // deterministic
            air += t.CorrosiveAir ? 1 : 0;
            water += t.ToxicWater ? 1 : 0;
            fauna += t.CaveFauna ? 1 : 0;
            outcrops += t.OreOutcrops ? 1 : 0;
            both += t.CorrosiveAir && t.ToxicWater ? 1 : 0;

            var old = WorldTraits.For(p, roster, Gen - 1);
            Assert.False(old.CorrosiveAir || old.ToxicWater || old.CaveFauna || old.OreOutcrops);
        }

        Assert.InRange(air / (double)n, 0.36, 0.44);
        Assert.InRange(water / (double)n, 0.76, 0.84);
        Assert.InRange(fauna / (double)n, p.CaveFaunaChance - 0.04, p.CaveFaunaChance + 0.04);
        Assert.InRange(outcrops / (double)n, p.OreOutcropChance - 0.04, p.OreOutcropChance + 0.04);
        Assert.InRange(both / (double)n, 0.28, 0.36); // independent rolls: 0.4 × 0.8
    }

    [Fact]
    public void Traits_TitasWaterStaysToxicAtGenerationEight_AndAClassicTypeRollsNothing()
    {
        var titas = _content.GetPlanet("titas")!;
        for (long s = 1; s <= 50; s++)
        {
            Assert.True(WorldTraits.For(titas, s, WorldDescription.ExtremePlanetsGeneration).ToxicWater);
            Assert.False(WorldTraits.For(titas, s, Gen).CorrosiveAir);
            var rocky = WorldTraits.For(_content.GetPlanet("rocky")!, s, Gen);
            Assert.False(rocky.CorrosiveAir || rocky.ToxicWater || rocky.CaveFauna || rocky.OreOutcrops);
        }
    }

    // --- Cave life (#2029) ---

    [Fact]
    public void CaveFauna_IsOneOrTwoCaveSpecies_OnlyWhenRolled()
    {
        var p = Toxic;
        int withFauna = 0;
        for (long s = 1; s <= 400; s++)
        {
            var roster = CreatureGenerator.GenerateRoster(p, s, Gen);
            bool rolled = WorldTraits.For(p, s, Gen).CaveFauna;
            if (!rolled)
            {
                Assert.Empty(roster);
                continue;
            }

            withFauna++;
            Assert.InRange(roster.Count, 1, 2);
            Assert.All(roster, sp => Assert.Equal(CreatureHabitat.Cave, sp.Habitat));
            Assert.Equal(roster.Select(sp => sp.Name), CreatureGenerator.GenerateRoster(p, s, Gen).Select(sp => sp.Name));
            Assert.Empty(CreatureGenerator.GenerateRoster(p, s, Gen - 1)); // never below the generation
        }

        Assert.True(withFauna > 30, $"only {withFauna} of 400 worlds kept cave fauna");
    }

    [Fact]
    public void CaveFlora_UsesTheTypesChance_AndOtherBarrenTypesKeepTheirHalf()
    {
        int toxic = 0, scrap = 0, n = 1500;
        var scrapyard = _content.GetPlanet("scrapyard")!;
        for (long s = 1; s <= n; s++)
        {
            toxic += FloraGenerator.GenerateRoster(Toxic, s, Gen).Count > 0 ? 1 : 0;
            scrap += FloraGenerator.GenerateRoster(scrapyard, s, Gen).Count > 0 ? 1 : 0;
        }

        Assert.InRange(toxic / (double)n, 0.14, 0.22);
        Assert.InRange(scrap / (double)n, 0.45, 0.55);
    }

    // --- Worldgen: shallow rare ore, rare outcrops, no surface flora ---

    [Fact]
    public void RareOre_LiesWithinEightBlocksOfTheSurface_AndNoPlantGrowsOnIt()
    {
        var gen = NewGenerator(20260926, "toxic:ores");
        var p = Toxic;
        var rare = RareOreIds(p);
        int cs = WorldConstants.ChunkSize, shallowRare = 0, surfaceFlora = 0;
        for (int cx = 0; cx < 4; cx++)
            for (int cz = 0; cz < 4; cz++)
            {
                int ccx = 30 + cx * 3, ccz = 30 + cz * 3;
                int top = gen.SurfaceHeight(p, ccx * cs + cs / 2, ccz * cs + cs / 2);
                for (int cy = WorldConstants.WorldToChunk(top - 12); cy <= WorldConstants.WorldToChunk(top + 2); cy++)
                {
                    var chunk = gen.Generate(p, new ChunkCoord(ccx, cy, ccz));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                        {
                            int surface = gen.SurfaceHeight(p, ccx * cs + x, ccz * cs + z);
                            for (int y = 0; y < cs; y++)
                            {
                                int depth = surface - (cy * cs + y);
                                var id = chunk.Get(x, y, z);
                                if (depth is >= 1 and <= 8 && rare.Contains(id))
                                {
                                    shallowRare++;
                                }

                                if (depth == -1 && _content.BlockById(id)?.Key.StartsWith("flora_", System.StringComparison.Ordinal) == true)
                                {
                                    surfaceFlora++;
                                }
                            }
                        }
                }
            }

        Assert.True(shallowRare > 50, $"only {shallowRare} tier-2 ore cells within eight blocks of the surface");
        Assert.Equal(0, surfaceFlora);
    }

    [Fact]
    public void OreOutcrops_LieOnTheSurface_OnlyOnAWorldThatRolledThem()
    {
        var rare = RareOreIds(Toxic);

        Toxic.OreOutcropChance = 1.0;
        var rolled = NewGenerator(424242, "toxic:outcrops");
        Assert.Contains("ore-outcrop", rolled.PropActiveForTest(Toxic, false, false));
        int with = CountAboveSurface(rolled, rare);

        // A world without the trait: the row is off. (Ore also shows in the hoodoo and butte rock that stands above the
        // height field, so the baseline is not zero — the outcrops are what the trait adds on top of it.) Its own body id:
        // the per-world profile is memoised across generator instances, and in the game a chance never changes.
        Toxic.OreOutcropChance = 0.0;
        var plain = NewGenerator(424242, "toxic:plain");
        Assert.DoesNotContain("ore-outcrop", plain.PropActiveForTest(Toxic, false, false));
        int without = CountAboveSurface(plain, rare);
        Assert.True(with > without + 10, $"the outcrops add only {with - without} rare-ore cells on the surface");

        // No other type ever opens the row.
        foreach (var other in _content.Planets.Values.Where(t => t.Key != Key))
        {
            Assert.DoesNotContain("ore-outcrop", NewGenerator(424242, "toxic:outcrops").PropActiveForTest(other, false, false));
        }
    }

    // --- The water palette (#2027) and the suit (#2026) ---

    [Fact]
    public void ToxicWater_PicksThePoisonPalette_OnlyForAutoWater()
    {
        var p = Toxic;
        for (long s = 1; s <= 60; s++)
        {
            var (clean, cleanMode) = FluidTints.ForWorld(s, $"sys{s}-p1", p, Gen);
            var (poison, poisonMode) = FluidTints.ForWorld(s, $"sys{s}-p1", p, Gen, toxicWater: true);
            Assert.Equal(FluidTints.Mode.Tint, cleanMode);
            Assert.Equal(FluidTints.Mode.Tint, poisonMode);
            int r = (poison >> 16) & 0xFF, g = (poison >> 8) & 0xFF, b = poison & 0xFF;
            Assert.True(b < r && b < g, $"poison water #{poison:x6} must not read blue");
            Assert.Equal(clean, FluidTints.ForWorld(s, $"sys{s}-p1", p, Gen, toxicWater: false).Rgb); // the classic pick is untouched
        }

        var titas = _content.GetPlanet("titas")!;
        Assert.Equal(FluidTints.ForWorld(5, "sys3-p1", titas, Gen).Rgb, FluidTints.ForWorld(5, "sys3-p1", titas, Gen, toxicWater: true).Rgb);
    }

    [Fact]
    public void SuitLiners_ResistCorrosion_OnlyTheBestCounts()
    {
        var items = _content.Items.Values;
        Assert.Equal(0f, SuitEquipment.CorrosionResistance(items, _ => false));
        Assert.Equal(0.25f, SuitEquipment.CorrosionResistance(items, k => k == "suit_liner_1"));
        Assert.Equal(0.65f, SuitEquipment.CorrosionResistance(items, k => k is "suit_liner_1" or "suit_liner_3"));
        Assert.True(SuitEquipment.IsSuitGear(_content.GetItem("suit_liner_2")!));
        Assert.All(items.Where(i => i.CorrosionResistance > 0), i => Assert.StartsWith("suit_liner_", i.Key));
    }

    // --- helpers ---

    private int CountToxic(long seed, WorldDescription desc, out int moons)
    {
        var galaxy = new UniverseGenerator(seed * 7919 + 13, desc, _content).Generate();
        var bodies = galaxy.AllBodies().Where(b => b.PlanetType == Key).ToList();
        moons = bodies.Count(b => b.Kind == CelestialKind.Moon);
        Assert.All(bodies, b => Assert.True(b.Kind is CelestialKind.Planet or CelestialKind.Moon));
        return bodies.Count;
    }

    private WorldGenerator NewGenerator(long seed, string location)
    {
        var gen = new WorldGenerator(seed, _content);
        gen.SetWorldMode(WorldConstants.Circumference, false, null, location);
        gen.SetTerrainGeneration(Gen);
        return gen;
    }

    private HashSet<BlockId> RareOreIds(PlanetType p)
        => p.Ores.Where(o => o.RareTier).Select(o => _content.GetBlock(o.Block)!.NumericId).ToHashSet();

    /// <summary>Rare-ore cells one or two above the ground top, over a 12 × 12 grid of chunk columns.</summary>
    private int CountAboveSurface(WorldGenerator gen, HashSet<BlockId> rare)
    {
        var p = Toxic;
        int cs = WorldConstants.ChunkSize, hits = 0;
        for (int cx = 0; cx < 12; cx++)
            for (int cz = 0; cz < 12; cz++)
            {
                int ccx = 20 + cx * 2, ccz = 20 + cz * 2;
                int top = gen.SurfaceHeight(p, ccx * cs + cs / 2, ccz * cs + cs / 2);
                foreach (int cy in new[] { WorldConstants.WorldToChunk(top - 4), WorldConstants.WorldToChunk(top + 6) }.Distinct())
                {
                    var chunk = gen.Generate(p, new ChunkCoord(ccx, cy, ccz));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                        {
                            int surface = gen.SurfaceHeight(p, ccx * cs + x, ccz * cs + z);
                            for (int ly = surface + 1 - cy * cs; ly <= surface + 2 - cy * cs; ly++)
                            {
                                if (ly >= 0 && ly < cs && rare.Contains(chunk.Get(x, ly, z)))
                                {
                                    hits++;
                                }
                            }
                        }
                }
            }

        return hits;
    }
}
