// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The glow level (#2857, terrain generation 29): glowing plants grew on practically every world, so each world now rolls how
/// much of its SURFACE plant life glows — none (40 %), one species (30 %), normal (20 %) or luminous (10 %). The roster, the
/// coverage rule, the surface pick weights and the prism bloom's surface clusters follow it; the caves keep their light, and
/// every older generation keeps its plants.
/// </summary>
public sealed class GlowLevelTests
{
    private const int Gen = WorldDescription.TreeVarietyGeneration;

    /// <summary>The glowing surface species, in catalog order.</summary>
    private static readonly string[] SurfaceGlowers =
    {
        "flora_glowcap", "flora_emberbloom", "flora_crystal", "flora_glowvine", "flora_sporepod", "flora_bulb",
        "flora_shardbloom", "flora_cinderbush",
    };

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private IEnumerable<PlanetType> PlantWorlds()
        => _content.Planets.Values.Where(p => !p.IsAirless && p.FloraDensity > 0).OrderBy(p => p.Key, System.StringComparer.Ordinal);

    /// <summary>World seeds (no location id, so each is its own roster seed) whose generation-29 world has this level.</summary>
    private static IEnumerable<long> SeedsOf(PlanetType planet, GlowLevel level)
    {
        for (long seed = 1; seed < 1_000_000; seed++)
        {
            if (GlowRules.LevelFor(planet, seed, Gen) == level)
            {
                yield return seed;
            }
        }
    }

    private WorldGenerator Generator(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, _content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private HashSet<ushort> IdsOf(IEnumerable<string> keys) => keys.Select(k => _content.GetBlock(k)!.NumericId.Value).ToHashSet();

    // --- The level ---

    [Fact]
    public void TheSurfaceGlowers_AreTheGlowingSpeciesOfTheSurface_NotTheCaveLightsOrThePrismBloom()
    {
        Assert.Equal(SurfaceGlowers, FloraCatalog.All.Where(FloraGenerator.IsSurfaceGlower).Select(s => s.Key));
        foreach (var key in new[] { "flora_glowmoss", "flora_glowthread", "flora_prismbloom", FloraCatalog.LanternFruitKey })
        {
            Assert.False(FloraGenerator.IsSurfaceGlower(FloraCatalog.Find(key)!), key);
        }
    }

    [Fact]
    public void TheLevels_SplitTheWorlds_40_30_20_10_AndTheGlowThemesNeverGoDark()
    {
        const int worlds = 4000;
        foreach (var key in new[] { "meadowlands", "desert", "lava", "jungle" })
        {
            var planet = _content.GetPlanet(key)!;
            var shares = Shares(planet);
            Assert.InRange(shares[GlowLevel.None], 0.37, 0.43);
            Assert.InRange(shares[GlowLevel.Low], 0.27, 0.33);
            Assert.InRange(shares[GlowLevel.Normal], 0.17, 0.23);
            Assert.InRange(shares[GlowLevel.Luminous], 0.08, 0.12);
        }

        // Crystal and ashen worlds are their crystals and embers: they never roll a dark surface.
        var glowThemes = _content.Planets.Values
            .Where(p => (FloraThemes.Resolve(p.FloraTheme).Preferred & FloraTag.Glow) != 0).ToList();
        Assert.Contains(glowThemes, p => p.Key == "crystal");
        Assert.Contains(glowThemes, p => p.Key == "ashen");
        foreach (var planet in glowThemes)
        {
            var shares = Shares(planet);
            Assert.Equal(0.0, shares[GlowLevel.None]);
            Assert.InRange(shares[GlowLevel.Low], 0.66, 0.74);
        }

        // Older generations: Normal in name only (no older rule reads it).
        var meadow = _content.GetPlanet("meadowlands")!;
        for (long seed = 1; seed <= 50; seed++)
        {
            Assert.Equal(GlowLevel.Normal, GlowRules.LevelFor(meadow, seed, Gen - 1));
        }

        Dictionary<GlowLevel, double> Shares(PlanetType planet)
        {
            var counts = new Dictionary<GlowLevel, int>
            {
                [GlowLevel.None] = 0,
                [GlowLevel.Low] = 0,
                [GlowLevel.Normal] = 0,
                [GlowLevel.Luminous] = 0,
            };
            for (int i = 0; i < worlds; i++)
            {
                counts[GlowRules.LevelFor(planet, WorldGenerator.RosterSeedFor(20261020, "glow:" + i), Gen)]++;
            }

            return counts.ToDictionary(kv => kv.Key, kv => kv.Value / (double)worlds);
        }
    }

    [Fact]
    public void WorldGeneration_ReadsTheLevel_FromTheServersRosterSeed()
    {
        // The server rolls its scan roster from RosterSeedFor(seed, location id); worldgen must see the same level.
        foreach (var key in new[] { "meadowlands", "lava", "crystal_living" })
        {
            var planet = _content.GetPlanet(key)!;
            for (int body = 0; body < 6; body++)
            {
                string location = "sys:2857:body" + body;
                var gen = new WorldGenerator(31337, _content);
                gen.SetWorldMode(WorldConstants.Circumference, false, null, location);
                gen.SetTerrainGeneration(Gen);
                Assert.Equal(GlowRules.LevelFor(planet, WorldGenerator.RosterSeedFor(31337, location), Gen), gen.GlowLevelForTest(planet));
            }
        }
    }

    // --- Rosters ---

    [Fact]
    public void Rosters_FollowTheLevel_NoneGrowsNoGlower_LowExactlyOne_LuminousEveryOneWithAHost()
    {
        int none = 0, low = 0, luminous = 0;
        foreach (var planet in PlantWorlds())
        {
            var candidates = FloraGenerator.GlowCandidatesFor(planet, Gen);
            Assert.All(candidates, k => Assert.Contains(k, SurfaceGlowers));
            for (int i = 0; i < 40; i++)
            {
                long rosterSeed = WorldGenerator.RosterSeedFor(i + 1, "glow-roster");
                var level = GlowRules.LevelFor(planet, rosterSeed, Gen);
                var roster = FloraGenerator.GenerateRoster(planet, rosterSeed, Gen);
                var glowing = roster.Where(r => r.Active && SurfaceGlowers.Contains(r.BlockKey)).Select(r => r.BlockKey).ToList();
                string where = $"{planet.Key} roster seed {rosterSeed} ({level})";
                switch (level)
                {
                    case GlowLevel.None:
                        none++;
                        Assert.True(glowing.Count == 0, $"{where}: {string.Join(", ", glowing)} glow");
                        break;
                    case GlowLevel.Low:
                        low++;
                        Assert.True(glowing.Count == System.Math.Min(1, candidates.Count), $"{where}: {string.Join(", ", glowing)} glow");
                        Assert.All(glowing, k => Assert.Contains(k, candidates));
                        break;
                    case GlowLevel.Luminous:
                        luminous++;
                        Assert.All(candidates, k => Assert.True(glowing.Contains(k), $"{where}: {k} does not glow"));
                        break;
                }

                // The prism bloom stays active on every world: the caves keep it.
                Assert.Contains(roster, r => r.Active && r.BlockKey == "flora_prismbloom");
            }
        }

        Assert.True(none > 0 && low > 0 && luminous > 0, $"levels seen: none {none}, low {low}, luminous {luminous}");
    }

    [Fact]
    public void ALowWorld_PicksItsOneGlower_AmongTheSpeciesThatCanGrowOnItsSurface()
    {
        // The meadow's grass hosts one glower (the glow vine); a lava world's basalt three (ember bloom, crystal, cinder bush).
        Assert.Equal(new[] { "flora_glowvine" }, FloraGenerator.GlowCandidatesFor(_content.GetPlanet("meadowlands")!, Gen));
        var lava = _content.GetPlanet("lava")!;
        Assert.Equal(new[] { "flora_emberbloom", "flora_crystal", "flora_cinderbush" }, FloraGenerator.GlowCandidatesFor(lava, Gen));

        var picked = new HashSet<string>();
        foreach (long seed in SeedsOf(lava, GlowLevel.Low).Take(60))
        {
            picked.UnionWith(FloraGenerator.GenerateRoster(lava, seed, Gen)
                .Where(r => r.Active && SurfaceGlowers.Contains(r.BlockKey)).Select(r => r.BlockKey));
        }

        Assert.Equal(3, picked.Count); // every candidate is somebody's pick
    }

    [Fact]
    public void Coverage_OnADarkWorld_NeverForcesAGlower_ButStillPlantsEverySurfaceAnotherSpeciesCan()
    {
        foreach (var planet in PlantWorlds().Where(p => !FloraThemes.Resolve(p.FloraTheme).Strict))
        {
            var own = new HashSet<string> { planet.SurfaceBlock };
            if (!string.IsNullOrEmpty(planet.BeachBlock))
            {
                own.Add(planet.BeachBlock);
            }

            foreach (var biome in planet.Biomes)
            {
                own.Add(biome.SurfaceBlock);
            }

            foreach (var level in new[] { GlowLevel.None, GlowLevel.Low })
            {
                foreach (long seed in SeedsOf(planet, level).Take(4))
                {
                    var roster = FloraGenerator.GenerateRoster(planet, seed, Gen);
                    foreach (var host in own)
                    {
                        bool coverable = FloraCatalog.All.Any(sp => sp.OnSurface && !sp.Rainbow && !sp.Hanging && !sp.Aquatic && !sp.Cultivated
                            && !sp.Authored && !sp.Fruit && !FloraGenerator.IsSurfaceGlower(sp) && sp.Hosts.Contains(host));
                        if (coverable)
                        {
                            Assert.True(roster.Any(r => r.Active && !r.Aquatic && FloraCatalog.Find(r.BlockKey) is { } sp && sp.OnSurface
                                    && !sp.Rainbow && !sp.Hanging && sp.Hosts.Contains(host)),
                                $"{planet.Key} seed {seed} ({level}): '{host}' stays bare");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void TheGlowVine_IsNoSignaturePlant_OfAGreenWorld_AnyMore()
    {
        var meadow = _content.GetPlanet("meadowlands")!;
        var vine = FloraCatalog.Find("flora_glowvine")!;
        var fern = FloraCatalog.Find("flora_fern")!;
        Assert.Equal(4, FloraThemes.PickWeight(FloraThemes.Resolve("temperate"), vine.Tags)); // its Lush tag made it one

        long normal = SeedsOf(meadow, GlowLevel.Normal).First();
        var gen = Generator(normal, Gen);
        Assert.Equal(GlowLevel.Normal, gen.GlowLevelForTest(meadow));
        Assert.Equal(1, gen.SurfacePickWeightForTest(meadow, "temperate", vine.Tags));
        Assert.Equal(4, gen.SurfacePickWeightForTest(meadow, "temperate", fern.Tags)); // every other plant keeps its weight
        Assert.Equal(4, Generator(normal, Gen - 1).SurfacePickWeightForTest(meadow, "temperate", vine.Tags)); // older worlds keep it
        long luminous = SeedsOf(meadow, GlowLevel.Luminous).First();
        Assert.Equal(WorldGenerator.LuminousGlowPickWeight,
            Generator(luminous, Gen).SurfacePickWeightForTest(meadow, "temperate", vine.Tags));

        // Activation: a normal green world rolls the vine like any off-theme plant (40 %), generation 28 at the signature 85 %.
        int worlds = 0, now = 0, before = 0;
        foreach (long seed in SeedsOf(meadow, GlowLevel.Normal).Take(300))
        {
            worlds++;
            now += FloraGenerator.GenerateRoster(meadow, seed, Gen).Any(r => r.Active && r.BlockKey == vine.Key) ? 1 : 0;
            before += FloraGenerator.GenerateRoster(meadow, seed, Gen - 1).Any(r => r.Active && r.BlockKey == vine.Key) ? 1 : 0;
        }

        Assert.InRange(now / (double)worlds, 0.30, 0.50);
        Assert.True(before / (double)worlds >= 0.75, $"generation 28 grew the vine on {before} of {worlds} worlds");
    }

    [Fact]
    public void Generation29_KeepsEverySpeciesIdentity_AndOnlyMovesTheGlowersAndTheCoverage()
    {
        // The per-species streams are untouched: the same names, toxic flags and ids as generation 28, and every non-glowing
        // species active there is active here (only the coverage rule may add one, where a glower no longer covers a host).
        foreach (var planet in PlantWorlds())
        {
            for (long seed = 1; seed <= 12; seed++)
            {
                var older = FloraGenerator.GenerateRoster(planet, seed, Gen - 1);
                var newer = FloraGenerator.GenerateRoster(planet, seed, Gen);
                Assert.Equal(older.Count, newer.Count);
                for (int i = 0; i < older.Count; i++)
                {
                    Assert.Equal(older[i].Id, newer[i].Id);
                    Assert.Equal(older[i].Name, newer[i].Name);
                    Assert.Equal(older[i].BlockKey, newer[i].BlockKey);
                    Assert.Equal(older[i].Toxic, newer[i].Toxic);
                    if (older[i].Active && !SurfaceGlowers.Contains(older[i].BlockKey))
                    {
                        Assert.True(newer[i].Active, $"{planet.Key} seed {seed}: {older[i].BlockKey} lost its roll");
                    }
                }
            }
        }
    }

    // --- World generation ---

    [Theory]
    [InlineData("meadowlands")]
    [InlineData("desert")]
    [InlineData("lava")]
    public void ANoneWorld_GrowsNoGlowingPlant_OnItsSurface_ALuminousOneDoes(string planetKey)
    {
        var planet = _content.GetPlanet(planetKey)!;
        var glowers = IdsOf(SurfaceGlowers.Append("flora_prismbloom"));

        var dark = SurfaceFlora(Generator(SeedsOf(planet, GlowLevel.None).First(), Gen), planet, 6);
        Assert.True(dark.Values.Sum() > 0, $"the sampled {planetKey} surface grows no plant at all");
        Assert.DoesNotContain(dark.Keys, id => glowers.Contains(id));

        var lit = SurfaceFlora(Generator(SeedsOf(planet, GlowLevel.Luminous).First(), Gen), planet, 6);
        Assert.Contains(lit.Keys, id => glowers.Contains(id));
    }

    [Fact]
    public void ALowMeadow_GrowsOnlyItsOneGlower()
    {
        var meadow = _content.GetPlanet("meadowlands")!;
        var vine = _content.GetBlock("flora_glowvine")!.NumericId.Value;
        var glowers = IdsOf(SurfaceGlowers);
        var flora = SurfaceFlora(Generator(SeedsOf(meadow, GlowLevel.Low).First(), Gen), meadow, 6);
        Assert.All(flora.Keys.Where(glowers.Contains), id => Assert.Equal(vine, id));
    }

    [Fact]
    public void PrismBlooms_GatherOnTheSurface_OnlyOnNormalAndLuminousWorlds()
    {
        var meadow = _content.GetPlanet("meadowlands")!;
        var bloom = _content.GetBlock("flora_prismbloom")!.NumericId.Value;
        int Blooms(GlowLevel level)
            => SeedsOf(meadow, level).Take(1).Sum(seed => SurfaceFlora(Generator(seed, Gen), meadow, 6).GetValueOrDefault(bloom));

        Assert.Equal(0, Blooms(GlowLevel.None));
        Assert.Equal(0, Blooms(GlowLevel.Low));
        Assert.True(Blooms(GlowLevel.Normal) + Blooms(GlowLevel.Luminous) > 0, "no prism bloom on a normal and a luminous meadow");
    }

    [Fact]
    public void ANoneWorld_KeepsThePrismBloom_InItsCaves()
    {
        var karst = _content.GetPlanet("karst")!;
        var bloom = _content.GetBlock("flora_prismbloom")!.NumericId;
        long seed = SeedsOf(karst, GlowLevel.None).First();
        var gen = Generator(seed, Gen);
        int cs = WorldConstants.ChunkSize, found = 0;
        for (int cx = 0; cx < 5 && found == 0; cx++)
            for (int cz = 0; cz < 5 && found == 0; cz++)
            {
                int ccx = 20 + cx * 3, ccz = 20 + cz * 3;
                int top = gen.SurfaceHeight(karst, ccx * cs + cs / 2, ccz * cs + cs / 2);
                for (int cy = WorldConstants.WorldToChunk(top - 60); cy <= WorldConstants.WorldToChunk(top - WorldGenerator.CaveFloraMinDepth); cy++)
                {
                    var chunk = gen.Generate(karst, new ChunkCoord(ccx, cy, ccz));
                    for (int x = 0; x < cs; x++)
                        for (int y = 0; y < cs; y++)
                            for (int z = 0; z < cs; z++)
                            {
                                if (chunk.Get(x, y, z) == bloom
                                    && cy * cs + y <= gen.SurfaceHeight(karst, ccx * cs + x, ccz * cs + z) - WorldGenerator.CaveFloraMinDepth)
                                {
                                    found++;
                                }
                            }
                }
            }

        Assert.True(found > 0, "no prism bloom in the caves of a world without surface glow");
    }

    // --- helpers ---

    /// <summary>The wild flora standing on the surface (the cell above each column's ground) of a grid of
    /// <paramref name="across"/>² chunk columns spread over the world, counted by block id.</summary>
    private Dictionary<ushort, int> SurfaceFlora(WorldGenerator gen, PlanetType planet, int across)
    {
        var wild = IdsOf(FloraCatalog.All.Where(s => s.OnSurface && !s.Aquatic && !s.Cultivated && !s.Hanging).Select(s => s.Key)
            .Where(k => _content.GetBlock(k) != null));
        var counts = new Dictionary<ushort, int>();
        int cs = WorldConstants.ChunkSize;
        for (int cx = 0; cx < across; cx++)
            for (int cz = 0; cz < across; cz++)
            {
                int ccx = 20 + cx * 4, ccz = 20 + cz * 4;
                var chunks = new Dictionary<int, ChunkData>();
                for (int x = 0; x < cs; x++)
                    for (int z = 0; z < cs; z++)
                    {
                        int y = gen.SurfaceHeight(planet, ccx * cs + x, ccz * cs + z) + 1;
                        int cy = WorldConstants.WorldToChunk(y);
                        if (!chunks.TryGetValue(cy, out var chunk))
                        {
                            chunks[cy] = chunk = gen.Generate(planet, new ChunkCoord(ccx, cy, ccz));
                        }

                        ushort id = chunk.Get(x, y - cy * cs, z).Value;
                        if (wild.Contains(id))
                        {
                            counts[id] = counts.GetValueOrDefault(id) + 1;
                        }
                    }
            }

        return counts;
    }
}
