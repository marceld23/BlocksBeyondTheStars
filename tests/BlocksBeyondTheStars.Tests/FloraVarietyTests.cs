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
/// Flora variety V2: per-biome flora themes, the vegetation-richness mask, patchy themed species selection
/// and the tree archetypes. Verifies the catalog/theme data is consistent and that world generation actually
/// grows a varied, themed, clustered plant life (deterministically — server and client preview agree).
/// </summary>
public sealed class FloraVarietyTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // --- Catalog + theme data ---

    [Fact]
    public void EveryCatalogSpecies_HasHosts_AndABlock()
    {
        foreach (var sp in FloraCatalog.All)
        {
            Assert.NotEmpty(sp.Hosts);
            Assert.True(_content.Blocks.ContainsKey(sp.Key), $"flora '{sp.Key}' must have a block definition");
        }
    }

    [Fact]
    public void NewFloraAndLeafBlocks_Exist()
    {
        foreach (var key in new[]
        {
            "flora_grasstuft", "flora_rockflower", "flora_snowbush", "flora_icereed", "flora_saltgrass",
            "flora_cinderbush", "pine_needles", "palm_frond",
        })
        {
            Assert.True(_content.Blocks.ContainsKey(key), $"block '{key}' must exist");
        }
    }

    [Fact]
    public void Catalog_HasBothTallAndShortSpecies_AndIsTallMatchesHeight()
    {
        Assert.Contains(FloraCatalog.All, s => s.Height == FloraHeight.Tall);
        Assert.Contains(FloraCatalog.All, s => s.Height == FloraHeight.Short);
        foreach (var sp in FloraCatalog.All)
        {
            Assert.Equal(sp.Height == FloraHeight.Tall, FloraCatalog.IsTall(sp.Key));
        }
    }

    [Fact]
    public void Themes_ResolveWithFallback_AndPreferPreferredTags()
    {
        Assert.Equal("temperate", FloraThemes.Resolve("").Name);
        Assert.Equal("temperate", FloraThemes.Resolve("does-not-exist").Name);

        var tropical = FloraThemes.Resolve("tropical");
        Assert.True((tropical.Preferred & FloraTag.Tropical) != 0);
        // Preferred species are commoner + weightier than off-theme ones.
        Assert.True(FloraThemes.ActivationChance(tropical, FloraTag.Tropical)
                    > FloraThemes.ActivationChance(tropical, FloraTag.Cold));
        Assert.True(FloraThemes.PickWeight(tropical, FloraTag.Tropical)
                    > FloraThemes.PickWeight(tropical, FloraTag.Cold));
    }

    [Fact]
    public void EveryFloraPlanetThemeResolves_ToAKnownTheme()
    {
        foreach (var planet in _content.Planets.Values.Where(p => !string.IsNullOrEmpty(p.FloraTheme)))
        {
            // Resolve returns the matched theme's canonical name, or "temperate" for an unknown name. So a
            // configured name that round-trips to itself is a real theme (typos would fall back to temperate).
            Assert.Equal(planet.FloraTheme.ToLowerInvariant(), FloraThemes.Resolve(planet.FloraTheme).Name);
        }
    }

    // --- World generation ---

    /// <summary>Generates every chunk across a chunk-area band that brackets the surface (so flora/trees in the
    /// air above ground are captured) and tallies block ids (each chunk once). The vertical band is derived
    /// from the planet's height profile so it is independent of the chunk size + terrain style.</summary>
    private static Dictionary<ushort, int> CountArea(WorldGenerator gen, PlanetType planet, int chunksXZ)
    {
        int cs = WorldConstants.ChunkSize;
        int yMinW = planet.BaseHeight - planet.Amplitude - cs;        // well below the lowest surface
        int yMaxW = planet.BaseHeight + planet.Amplitude * 2 + 24;    // above the tallest terrain + crowns
        int cyLo = System.Math.Max(0, yMinW / cs);
        int cyHi = yMaxW / cs + 1;

        var counts = new Dictionary<ushort, int>();
        for (int cx = 0; cx < chunksXZ; cx++)
            for (int cz = 0; cz < chunksXZ; cz++)
                for (int cy = cyLo; cy <= cyHi; cy++)
                {
                    var coord = WorldConstants.WorldToChunk(new Vector3i(cx * cs, cy * cs, cz * cs));
                    var chunk = gen.Generate(planet, coord);
                    for (int lx = 0; lx < cs; lx++)
                        for (int ly = 0; ly < cs; ly++)
                            for (int lz = 0; lz < cs; lz++)
                            {
                                ushort id = chunk.Get(lx, ly, lz).Value;
                                if (id != 0)
                                {
                                    counts[id] = counts.TryGetValue(id, out var c) ? c + 1 : 1;
                                }
                            }
                }

        return counts;
    }

    private HashSet<ushort> FloraIds()
    {
        var set = new HashSet<ushort>();
        foreach (var sp in FloraCatalog.All)
        {
            if (_content.GetBlock(sp.Key) is { } b)
            {
                set.Add(b.NumericId.Value);
            }
        }

        return set;
    }

    [Fact]
    public void Worldgen_GrowsSeveralDistinctFloraSpecies_OnALushWorld()
    {
        var planet = _content.GetPlanet("jungle")!; // tropical, dense flora
        var gen = new WorldGenerator(2026, _content);
        var floraIds = FloraIds();

        var counts = CountArea(gen, planet, chunksXZ: 4);
        int distinct = counts.Keys.Count(id => floraIds.Contains(id));
        Assert.True(distinct >= 3, $"a lush world should grow a variety of plants (got {distinct} distinct species).");
    }

    [Fact]
    public void Worldgen_StampsTrees_OnAForestedWorld()
    {
        var planet = _content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(2026, _content);
        var counts = CountArea(gen, planet, chunksXZ: 4);

        var log = _content.GetBlock("wood_log")!.NumericId.Value;
        var leaves = _content.GetBlock("tree_leaves")!.NumericId.Value;
        Assert.True(counts.GetValueOrDefault(log) > 0, "a forested world must place tree trunks.");
        Assert.True(counts.GetValueOrDefault(leaves) > 0, "broadleaf/jungle crowns use tree_leaves.");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Worldgen_ConiferWorld_GrowsPines_NotBroadleafCrowns()
    {
        var planet = _content.GetPlanet("highland")!; // alpine theme → conifers only
        var gen = new WorldGenerator(7, _content);
        var counts = CountArea(gen, planet, chunksXZ: 6);

        var log = _content.GetBlock("wood_log")!.NumericId.Value;
        var pine = _content.GetBlock("pine_needles")!.NumericId.Value;
        var leaves = _content.GetBlock("tree_leaves")!.NumericId.Value;

        Assert.True(counts.GetValueOrDefault(log) > 0, "the conifer world should have trees in this area.");
        Assert.True(counts.GetValueOrDefault(pine) > 0, "an alpine world's trees are conifers (pine_needles).");
        Assert.Equal(0, counts.GetValueOrDefault(leaves)); // alpine grows no broadleaf crowns
    }
}
