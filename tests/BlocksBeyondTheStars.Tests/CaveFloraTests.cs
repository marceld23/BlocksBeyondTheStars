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
/// Generation 11, the cave flora wave: plants in caves (the cave habitat's own species and the surface fungi), the
/// rainbow glow class (caves + rare surface clusters, every plant its own colour), glowers that light their
/// surroundings, and cold-adapted species that survive the cold. Every rule is gated on the generation, so an older
/// world keeps its plants.
/// </summary>
public sealed class CaveFloraTests
{
    private const int Gen = WorldDescription.CaveFloraGeneration;
    private static readonly string[] CaveOnly = { "flora_cavecap", "flora_glowmoss", "flora_glowthread" };
    private static readonly string[] ColdAdapted = { "flora_frostflower", "flora_snowbush", "flora_icereed", "flora_lichen" };

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // --- Catalog ---

    [Fact]
    public void WaveSpecies_AreGenerationEleven_AppendedAfterTheHangingKelp_AndBeforeTheCrops()
    {
        // A wild species' roster id is its catalog index: the wave must append, never insert, or every world's
        // plants would be renamed.
        var keys = FloraCatalog.All.Select(s => s.Key).ToList();
        int kelp = keys.IndexOf("flora_hangkelp");
        Assert.Equal(new[] { "flora_cavecap", "flora_glowmoss", "flora_glowthread", "flora_prismbloom" },
            keys.Skip(kelp + 1).Take(4));
        // The fruit trees (#2038, generation 14) appended their four shapes after this wave, still before the crops.
        Assert.Equal(new[] { "flora_fruit_round", "flora_fruit_long", "flora_fruit_grape", "flora_fruit_banana" },
            keys.Skip(kelp + 5).Take(4));
        Assert.All(FloraCatalog.All.Skip(kelp + 9), sp => Assert.True(sp.Cultivated, $"{sp.Key} sits among the crops"));
        foreach (var key in keys.Skip(kelp + 1).Take(4))
        {
            var sp = FloraCatalog.Find(key)!;
            Assert.Equal(Gen, sp.MinGeneration);
            Assert.NotNull(_content.GetBlock(key));
        }

        Assert.All(CaveOnly, k => Assert.Equal(FloraHabitat.Cave, FloraCatalog.Find(k)!.Habitat));
        Assert.True(FloraCatalog.Find("flora_glowthread")!.Hanging);
        Assert.Equal(FloraHabitat.Both, FloraCatalog.Find("flora_prismbloom")!.Habitat);
        Assert.True(FloraCatalog.IsRainbow("flora_prismbloom"));
        // Marcel: the surface fungi grow in caves too.
        foreach (var fungus in new[] { "flora_mushroom", "flora_glowcap", "flora_puffball", "flora_sporepod" })
        {
            Assert.Equal(FloraHabitat.Both, FloraCatalog.Find(fungus)!.Habitat);
            Assert.Contains("stone", FloraCatalog.Find(fungus)!.CaveHosts);
        }
    }

    [Fact]
    public void TheNewGlowers_LightTheirSurroundings_AndTheCapDoesNot()
    {
        Assert.True(FloraCatalog.LightOf("flora_glowmoss") > 0f);
        Assert.True(FloraCatalog.LightOf("flora_glowthread") > 0f);
        Assert.True(FloraCatalog.LightOf("flora_prismbloom") > 0f);
        Assert.Equal(0f, FloraCatalog.LightOf("flora_cavecap"));
        Assert.Equal(0f, FloraCatalog.LightOf("flora_glowcap")); // the classic glowers keep their self-glow only
        foreach (var key in new[] { "flora_glowmoss", "flora_glowthread", "flora_prismbloom" })
        {
            Assert.True((_content.GetBlock(key)!.Emission ?? 0f) > 0f, $"{key} must glow");
        }
    }

    [Fact]
    public void CaveHosts_AreFloraHosts_SoAHarvestedCavePlantRegrows()
    {
        foreach (var sp in FloraCatalog.All)
        {
            if (sp.Fruit)
            {
                continue; // #2038: a fruit hangs from a leaf, and a leaf is no ground — never a FloraHost, never in a cave
            }

            foreach (var host in sp.CaveHosts)
            {
                if (_content.GetBlock(host) is { } block)
                {
                    Assert.True(block.FloraHost, $"'{host}' (a cave host of {sp.Key}) must carry the FloraHost flag");
                }
            }
        }
    }

    // --- Rosters ---

    [Fact]
    public void OlderGenerations_NeverActivateTheWave_AndBarrenWorldsStayEmpty()
    {
        var meadow = _content.GetPlanet("meadowlands")!;
        var glacier = _content.GetPlanet("glacier")!;
        for (long seed = 1; seed <= 40; seed++)
        {
            Assert.DoesNotContain(FloraGenerator.GenerateRoster(meadow, seed, Gen - 1),
                s => s.Active && FloraCatalog.Find(s.BlockKey)!.MinGeneration == Gen);
            Assert.Empty(FloraGenerator.GenerateRoster(glacier, seed, Gen - 1));
        }
    }

    [Fact]
    public void EveryPlantWorld_KeepsItsCavesPlanted_AndGrowsTheRainbowClass()
    {
        foreach (var planet in _content.Planets.Values.Where(p => !p.IsAirless && p.FloraDensity > 0))
        {
            for (long seed = 1; seed <= 12; seed++)
            {
                var roster = FloraGenerator.GenerateRoster(planet, seed, Gen);
                Assert.True(roster.Any(s => s.Active && CaveOnly.Contains(s.BlockKey)),
                    $"{planet.Key} seed {seed}: no cave species active");
                Assert.Contains(roster, s => s.Active && s.BlockKey == "flora_prismbloom");
            }
        }
    }

    [Fact]
    public void BarrenWorlds_GrowCaveFlora_AboutHalfTheTime_AndOnlyCaveSpecies()
    {
        // Marcel: "everywhere in caves, with a certain probability" — a barren or airless world rolls it per world.
        var barren = _content.Planets.Values
            .Where(p => (p.IsAirless || p.FloraDensity <= 0) && !p.Void && p.CaveThreshold > 0).ToList();
        Assert.NotEmpty(barren);
        int worlds = 0, planted = 0;
        foreach (var planet in barren)
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                var roster = FloraGenerator.GenerateRoster(planet, seed, Gen);
                worlds++;
                if (roster.Count == 0)
                {
                    continue;
                }

                planted++;
                Assert.All(roster, s => Assert.True(FloraCatalog.Find(s.BlockKey)!.InCaves, $"{s.BlockKey} on barren {planet.Key}"));
                Assert.Contains(roster, s => s.Active && CaveOnly.Contains(s.BlockKey));
            }
        }

        double share = planted / (double)worlds;
        Assert.InRange(share, 0.4, 0.6);
    }

    // --- Generation ---

    [Fact]
    public void CavePlants_GrowUnderground_OnCaveRock_AndNeverOnAnOlderWorld()
    {
        var karst = _content.GetPlanet("karst")!;
        var ids = CaveOnly.ToDictionary(k => _content.GetBlock(k)!.NumericId, k => k);
        var thread = _content.GetBlock("flora_glowthread")!.NumericId;
        var rock = FloraCatalog.Find("flora_cavecap")!.CaveHosts
            .Select(k => _content.GetBlock(k)?.NumericId).Where(i => i.HasValue).Select(i => i!.Value).ToHashSet();

        int found = 0, threads = 0;
        foreach (int generation in new[] { Gen - 1, Gen })
        {
            var gen = new WorldGenerator(20260925, _content);
            gen.SetTerrainGeneration(generation);
            int count = ScanUnderground(gen, karst, (chunk, x, y, z, wy, surface) =>
            {
                var id = chunk.Get(x, y, z);
                if (!ids.ContainsKey(id))
                {
                    return false;
                }

                Assert.True(wy <= surface - WorldGenerator.CaveFloraMinDepth, $"{ids[id]} at y {wy}, ground {surface}");
                if (id == thread)
                {
                    threads++;
                    Assert.Contains(chunk.Get(x, y + 1, z), rock); // rooted in the ceiling
                    Assert.True(chunk.Get(x, y - 1, z).IsAir, "a glow thread hangs into open air");
                }
                else
                {
                    Assert.Contains(chunk.Get(x, y - 1, z), rock); // stands on the cave floor
                }

                return true;
            });
            if (generation < Gen)
            {
                Assert.Equal(0, count);
            }
            else
            {
                found = count;
            }
        }

        Assert.True(found > 0, "no cave plant in the scanned karst caves");
        Assert.True(threads > 0, "no glow thread hanging from any scanned cave ceiling");
    }

    [Fact]
    public void ColdWorlds_GrowTheirColdFlora_FromGenerationEleven_FrostflowerIncluded()
    {
        var ids = ColdAdapted.ToDictionary(k => _content.GetBlock(k)!.NumericId, k => k);
        var seen = new HashSet<string>();
        foreach (var key in new[] { "tundra", "ice", "boreal" })
        {
            var planet = _content.GetPlanet(key)!;
            for (long seed = 1; seed <= 4; seed++)
            {
                var old = new WorldGenerator(seed * 7919, _content);
                old.SetWorldMode(WorldConstants.Circumference, false, null, $"cold:{key}:{seed}");
                old.SetTerrainGeneration(Gen - 1);
                var gen = new WorldGenerator(seed * 7919, _content);
                gen.SetWorldMode(WorldConstants.Circumference, false, null, $"cold:{key}:{seed}");
                gen.SetTerrainGeneration(Gen);
                if (key != "boreal")
                {
                    Assert.Equal(0, CountSurface(old, planet, id => ids.ContainsKey(id), null));
                }

                CountSurface(gen, planet, id => ids.ContainsKey(id), id => seen.Add(ids[id]));
            }
        }

        Assert.Contains("flora_frostflower", seen);
    }

    [Fact]
    public void RainbowBlooms_GrowInRareSurfaceClusters_OnGenerationEleven()
    {
        var jungle = _content.GetPlanet("jungle")!;
        var bloom = _content.GetBlock("flora_prismbloom")!.NumericId;
        int columns = 12 * 12 * WorldConstants.ChunkSize * WorldConstants.ChunkSize; // CountSurface's grid, per world
        int blooms = 0;
        for (long seed = 1; seed <= 3; seed++)
        {
            var gen = new WorldGenerator(seed * 104729, _content);
            gen.SetWorldMode(WorldConstants.Circumference, false, null, $"rainbow:{seed}");
            gen.SetTerrainGeneration(Gen);
            blooms += CountSurface(gen, jungle, id => id == bloom, null);

            var old = new WorldGenerator(seed * 104729, _content);
            old.SetWorldMode(WorldConstants.Circumference, false, null, $"rainbow:{seed}");
            old.SetTerrainGeneration(Gen - 1);
            Assert.Equal(0, CountSurface(old, jungle, id => id == bloom, null));
        }

        Assert.True(blooms > 0, "no rainbow bloom on three jungle worlds");
        Assert.True(blooms < 3 * columns / 100, $"{blooms} rainbow blooms — a carpet, not a find");
    }

    [Fact]
    public void RainbowColour_IsDeterministic_Vivid_AndDiffersFromPlantToPlant()
    {
        var hues = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            var c = FloraTints.RainbowAt(i * 3 - 90, 40 + i % 7, -i * 5);
            Assert.Equal(c, FloraTints.RainbowAt(i * 3 - 90, 40 + i % 7, -i * 5));
            float max = System.Math.Max(c.R, System.Math.Max(c.G, c.B));
            float min = System.Math.Min(c.R, System.Math.Min(c.G, c.B));
            Assert.True(max >= 0.99f && max - min >= 0.75f, $"cell {i}: ({c.R}, {c.G}, {c.B}) is not vivid");
            float[] ch = { c.R, c.G, c.B };
            hues.Add((System.Array.IndexOf(ch, max) * 3) + System.Array.IndexOf(ch, min)); // which channel leads, which trails
        }

        Assert.True(hues.Count >= 5, $"only {hues.Count} of the six hue sectors over 200 plants");
        Assert.Equal(0, FloraTints.ToRgb24((0f, 0f, 0f), 0.5f));
        Assert.Equal(0x808080, FloraTints.ToRgb24((1f, 1f, 1f), 0.5f));
    }

    // --- helpers ---

    /// <summary>Scans the chunks below the surface of a grid of columns; returns how many cells the probe accepted.</summary>
    private static int ScanUnderground(WorldGenerator gen, PlanetType planet,
        System.Func<ChunkData, int, int, int, int, int, bool> probe)
    {
        int cs = WorldConstants.ChunkSize, hits = 0;
        for (int cx = 0; cx < 6; cx++)
            for (int cz = 0; cz < 6; cz++)
            {
                int ccx = 20 + cx * 3, ccz = 20 + cz * 3;
                int top = gen.SurfaceHeight(planet, ccx * cs + cs / 2, ccz * cs + cs / 2);
                for (int cy = WorldConstants.WorldToChunk(top - 60); cy <= WorldConstants.WorldToChunk(top); cy++)
                {
                    var chunk = gen.Generate(planet, new ChunkCoord(ccx, cy, ccz));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                        {
                            int surface = gen.SurfaceHeight(planet, ccx * cs + x, ccz * cs + z);
                            for (int y = 1; y < cs - 1; y++)
                            {
                                if (probe(chunk, x, y, z, cy * cs + y, surface))
                                {
                                    hits++;
                                }
                            }
                        }
                }
            }

        return hits;
    }

    /// <summary>Counts the surface cells (the ground top and the few cells above it) a predicate matches.</summary>
    private static int CountSurface(WorldGenerator gen, PlanetType planet, System.Func<BlockId, bool> match,
        System.Action<BlockId>? onHit)
    {
        int cs = WorldConstants.ChunkSize, hits = 0;
        for (int cx = 0; cx < 24; cx += 2)
            for (int cz = 0; cz < 24; cz += 2)
            {
                int ccx = 20 + cx * 2, ccz = 20 + cz * 2;
                int top = gen.SurfaceHeight(planet, ccx * cs + cs / 2, ccz * cs + cs / 2);
                var chunks = new[] { WorldConstants.WorldToChunk(top - 4), WorldConstants.WorldToChunk(top + 6) }.Distinct();
                foreach (int cy in chunks)
                {
                    var chunk = gen.Generate(planet, new ChunkCoord(ccx, cy, ccz));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                        {
                            int surface = gen.SurfaceHeight(planet, ccx * cs + x, ccz * cs + z);
                            int ly = surface + 1 - cy * cs;
                            if (ly < 0 || ly >= cs)
                            {
                                continue;
                            }

                            var id = chunk.Get(x, ly, z);
                            if (match(id))
                            {
                                hits++;
                                onHit?.Invoke(id);
                            }
                        }
                }
            }

        return hits;
    }
}
