// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1644 (landscape variety 1/6): the terrain-tag gates, the landmark table and the terrain-generation
/// plumbing must reproduce the pre-package behaviour exactly — every existing world stays byte-identical
/// (the goldens prove the bytes; these tests prove the gates and the order for ALL planet types).
/// </summary>
public sealed class TerrainTagsAndGenerationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The pre-#1644 predicates, frozen: key/style string checks the tags replaced.</summary>
    private static class Legacy
    {
        public static bool Buttes(PlanetType p) => (p.TerrainStyle?.ToLowerInvariant()) switch
        {
            "dunes" or "mesa" or "canyons" or "tablelands" or "badlands" => true,
            _ => Is(p, "savanna") || Is(p, "varied"),
        };

        public static bool Hoodoos(PlanetType p) => (p.TerrainStyle?.ToLowerInvariant()) switch
        {
            "badlands" or "tablelands" or "mesa" or "canyons" => true,
            _ => false,
        };

        public static bool Salt(PlanetType p)
            => string.Equals(p.BeachBlock, "salt", StringComparison.OrdinalIgnoreCase) || Is(p, "salt_flats");

        public static bool BasaltFields(PlanetType p)
            => Is(p, "lava") || Is(p, "ashen") || string.Equals(p.DeepBlock, "basalt", StringComparison.OrdinalIgnoreCase);

        public static bool LavaWorld(PlanetType p) => Is(p, "lava") || Is(p, "ashen");

        public static bool LavaOceanContinents(PlanetType p)
        {
            bool volcanic = p.SurfaceBlock == "basalt" || p.DeepBlock == "basalt";
            return (p.LavaAbundance ?? (volcanic ? 0.7 : 0.0)) > 0.0 && LavaWorld(p);
        }

        public static bool GeyserVolcanic(PlanetType p, bool volcanoWorld)
            => (p.LavaAbundance ?? 0.0) > 0.0 || LavaWorld(p) || volcanoWorld;

        public static bool CrystalProps(PlanetType p)
            => p.Key.Contains("crystal") || p.Ores.Exists(o => o.Block == "crystal") || p.CaveThreshold > 0.62;

        private static bool Is(PlanetType p, string key) => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SolidGround(PlanetType p) => !p.Void && !p.Cratered && !p.FloatingIslands;

    [Fact]
    public void TerrainTags_ReproduceTheFormerKeyAndStyleGates_ForEveryPlanetType()
    {
        var gen = new WorldGenerator(424242, Content);
        gen.SetLavaCoreVolcanoes(true); // the creation-time default — the gates below are independent of it
        // The eight data-only types of #1649 exist BECAUSE the tags decide (an ash-sea world routes lava rivers
        // without carrying the lava/ashen key) — the frozen predicates only describe the classic types.
        var gen1Types = new HashSet<string> { "red_desert", "boreal", "archipelago", "glacier", "meadowlands", "ashen_ocean", "dust_bowl", "frozen_ocean" };
        foreach (var planet in Content.Planets.Values.Where(p => !gen1Types.Contains(p.Key)))
        {
            var gates = gen.WonderGatesForTest(planet);
            string k = planet.Key;
            Assert.Equal(SolidGround(planet) && Legacy.Buttes(planet), gates["tableMountains"]);
            Assert.Equal(SolidGround(planet) && Legacy.Buttes(planet), gates["arches"]);
            Assert.Equal(!planet.Void && !planet.Cratered && Legacy.Hoodoos(planet), gates["hoodoos"]);
            Assert.Equal(Legacy.Salt(planet), gates["saltPolygons"]);
            Assert.Equal(SolidGround(planet) && Legacy.BasaltFields(planet), gates["basaltFields"]);
            Assert.Equal(Legacy.LavaWorld(planet), gates["lavaRivers"]);
            Assert.Equal(Legacy.LavaOceanContinents(planet), gates["lavaOceanContinents"]);
            Assert.Equal(Legacy.GeyserVolcanic(planet, gates["volcanoes"]), gates["geyserVolcanic"]);
            Assert.Equal(Legacy.CrystalProps(planet), gates["crystalProps"]);
            Assert.True(k.Length > 0);
        }
    }

    [Fact]
    public void TerrainTags_ParseCaseInsensitively_AndReportUnknownNames()
    {
        var tags = TerrainTags.Parse(new[] { "Volcanic", "buttes", "HOODOOS" }, out var unknown);
        Assert.Null(unknown);
        Assert.Equal(TerrainTag.Volcanic | TerrainTag.Buttes | TerrainTag.Hoodoos, tags);

        tags = TerrainTags.Parse(new[] { "buttes", "lava_lamp" }, out unknown);
        Assert.Equal("lava_lamp", unknown);
        Assert.Equal(TerrainTag.Buttes, tags);

        Assert.Equal(TerrainTag.None, TerrainTags.Parse(null, out unknown));
        Assert.Null(unknown);
    }

    [Fact]
    public void ContentLoad_ResolvesTags_AndRejectsUnknownOnes()
    {
        Assert.True(Content.Planets["lava"].HasTag(TerrainTag.Volcanic));
        Assert.True(Content.Planets["rocky"].HasTag(TerrainTag.Buttes | TerrainTag.Hoodoos));
        Assert.False(Content.Planets["jungle"].HasTag(TerrainTag.Buttes));
        Assert.Equal(TerrainTag.Wetland, Content.Planets["ocean"].Tags); // #1647: marsh sheets on the ocean world's flats
        Assert.Equal(TerrainTag.None, Content.Planets["lava"].Tags & ~TerrainTag.Volcanic);

        var bad = new PlanetType { Key = "bad", TerrainTags = new List<string> { "volcanic", "moon_cheese" } };
        bad.Tags = TerrainTags.Parse(bad.TerrainTags, out var unknown);
        Assert.Equal("moon_cheese", unknown);
    }

    [Fact]
    public void LandmarkTable_KeepsTheFormerIfChainPrecedence()
    {
        var gen = new WorldGenerator(424242, Content);
        gen.SetLavaCoreVolcanoes(true);
        var full = new[]
        {
            "volcano", "caldera", "massif", "table-mountain", "overhang", "travertine", "penitentes",
            "cenote", "crevasse", "rift", "mega-rift",
        };
        foreach (var planet in Content.Planets.Values)
        {
            var order = gen.LandmarkOrderForTest(planet);
            // Every active row appears in the global table order — a subsequence of the full chain.
            int cursor = 0;
            foreach (var name in order)
            {
                cursor = Array.IndexOf(full, name, cursor);
                Assert.True(cursor >= 0, $"{planet.Key}: '{name}' out of precedence order");
                cursor++;
            }
        }

        // The varied start world carries the big families (volcanoes, massifs, buttes, rifts) in that order.
        var varied = gen.LandmarkOrderForTest(Content.Planets["varied"]);
        Assert.Equal(new[] { "volcano", "caldera", "massif", "table-mountain" }, varied.Take(4).ToArray());
        Assert.Contains("rift", varied);
        // Airless bodies carry none.
        Assert.Empty(gen.LandmarkOrderForTest(Content.Planets["asteroid"]));
    }

    [Fact]
    public void PropTable_KeepsTheFormerPrecedence()
    {
        // #1648 appends the generation-1 rows after the classic five — the classic precedence is the prefix.
        Assert.Equal(new[] { "monolith", "stone-circle", "boulder", "crystal-shard", "dead-tree" },
            WorldGenerator.PropOrderForTest().Take(5).ToArray());
    }

    [Fact]
    public void TerrainGeneration_IsAppliedPerGenerator_AndInvalidatesTheColumnMemo()
    {
        var gen = new WorldGenerator(7, Content);
        Assert.Equal(0, gen.TerrainGeneration);
        var planet = Content.Planets["varied"];
        int h0 = gen.SurfaceHeight(planet, 100, 37);
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, gen.TerrainGeneration);
        Assert.Equal(0, gen.CachedColumnProfiles);
        // Since #1645 generation 1 reshapes the relief (scale jitter alone moves nearly every column), and
        // switching back restores the classic height exactly — the generation is a pure input, never state.
        bool anyDiffers = false;
        for (int x = 0; x < 640 && !anyDiffers; x += 32)
        {
            anyDiffers = gen.SurfaceHeight(planet, x, 37) != new WorldGenerator(7, Content).SurfaceHeight(planet, x, 37);
        }

        Assert.True(anyDiffers, "generation 1 should change the relief somewhere");
        gen.SetTerrainGeneration(0);
        Assert.Equal(h0, gen.SurfaceHeight(planet, 100, 37));
    }

    [Fact]
    public void JoinAccepted_CarriesTheTerrainGeneration_ThroughMessagePack()
    {
        var m = new JoinAccepted { PlayerId = "p", WorldSeed = 5, TerrainContinents = true, TerrainGeneration = 1 };
        var bytes = NetCodec.Encode(m);
        var back = Assert.IsType<JoinAccepted>(NetCodec.Decode(bytes));
        Assert.Equal(1, back.TerrainGeneration);
        Assert.True(back.TerrainContinents);

        // A pre-#1644 sender leaves the field 0 — the classic generators.
        var legacy = Assert.IsType<JoinAccepted>(NetCodec.Decode(NetCodec.Encode(new JoinAccepted { PlayerId = "p" })));
        Assert.Equal(0, legacy.TerrainGeneration);
    }
}
