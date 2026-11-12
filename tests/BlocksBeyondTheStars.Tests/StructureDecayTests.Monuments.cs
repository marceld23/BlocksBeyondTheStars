// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3516: the monuments of generation 38 weather through the shared ruin pass.</summary>
public sealed partial class StructureDecayTests
{
    private static SettlementStructure Monument(string archetype, long seed, int generation, bool withCache = true,
        string surface = "grass", bool airless = false)
        => MonumentGenerator.Generate(archetype, seed, surface, Content, withCache, generation, airless);

    [Fact]
    public void Monument_BeforeGeneration38_IsTheOldErosion()
    {
        foreach (string archetype in MonumentGenerator.ArchetypesGen21)
        {
            foreach (long seed in Seeds)
            {
                bool cache = seed % 2 == 1;
                ulong golden = Hash(MonumentGenerator.Generate(archetype, seed, "grass", Content, cache));
                Assert.Equal(golden, Hash(Monument(archetype, seed, Old, cache)));
                Assert.Equal(golden, Hash(Monument(archetype, seed, Old, cache, airless: true))); // the flag only reaches the new pass
            }
        }
    }

    [Fact]
    public void Monument_OfGeneration38_WeathersThroughTheSharedPass_KeepingItsRunesAndCache()
    {
        ushort rune = Id("rune_stone");
        foreach (string archetype in MonumentGenerator.ArchetypesGen21)
        {
            foreach (long seed in Seeds)
            {
                string at = $"{archetype} {seed}";
                var relic = Monument(archetype, seed, New);
                Assert.Equal(Hash(relic), Hash(Monument(archetype, seed, New)));
                Assert.NotEqual(Hash(Monument(archetype, seed, Old)), Hash(relic));

                var cache = Assert.Single(relic.Markers);
                Assert.Equal("relic_cache", cache.Type);
                Assert.Empty(Monument(archetype, seed, New, withCache: false).Markers);
                if (!MonumentGenerator.IsFossil(archetype))
                {
                    Assert.True(Cells(relic.Width, relic.Height, relic.Length).Any(c => relic.Get(c.X, c.Y, c.Z) == rune),
                        $"{at}: no rune left to scan");
                }

                var floating = Floating(relic.Width, relic.Height, relic.Length, relic.Get, anchorRows: 1);
                Assert.True(floating.Count == 0, $"{at}: {floating.Count} blocks hang in the air, first {floating.FirstOrDefault()}");
            }
        }
    }

    [Fact]
    public void Monument_OnAnAirlessBody_GrowsNoFlora()
    {
        int flora = 0;
        foreach (string archetype in MonumentGenerator.ArchetypesGen21)
        {
            foreach (long seed in Seeds)
            {
                var dead = Monument(archetype, seed, New, airless: true);
                Assert.DoesNotContain(Cells(dead.Width, dead.Height, dead.Length), c => IsFlora(dead.Get(c.X, c.Y, c.Z)));
                var living = Monument(archetype, seed, New);
                flora += Cells(living.Width, living.Height, living.Length).Count(c => IsFlora(living.Get(c.X, c.Y, c.Z)));
            }
        }

        Assert.True(flora > 0, "with air, the biome's flora grows around the low relics");
    }

    [Fact]
    public void Gate_OfGeneration38_KeepsItsDoorwayWalkable()
    {
        for (long seed = 1; seed <= 24; seed++)
        {
            var gate = Monument("gate", seed * 7919, New, withCache: false);
            int cz = gate.Length / 2;
            for (int x = 5; x <= 7; x++)
                for (int y = 1; y <= 8; y++) // the piers stand at least 8 high
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Assert.True(gate.Get(x, y, cz + dz) == 0, $"seed {seed}: the doorway is blocked at ({x},{y},{cz + dz})");
                    }

            Assert.True(Enumerable.Range(9, 3).Any(y => gate.Get(6, y, cz) != 0), $"seed {seed}: the lintel over the doorway fell");
        }
    }

    [Fact]
    public void MonumentSource_OnAnAirlessPlanetType_IsTheAirlessMonument()
    {
        string airless = Content.Planets.Values.First(p => p.IsAirless && !p.SpaceSky).Key;
        const long seed = 424242;
        bool cache = new Random(unchecked((int)(seed ^ (seed >> 32)))).NextDouble() < 0.35;
        var built = StructureSources.Get("monument")!.Build(new StructureBuildRequest(Content, seed)
        {
            Params = new Dictionary<string, string>(StringComparer.Ordinal) { ["archetype"] = "altar" },
            Surface = "grass",
            PlanetType = airless,
        });
        var direct = Monument("altar", seed, WorldDescription.CurrentTerrainGeneration, cache, airless: true);
        Assert.Equal(direct.Width, built.Width);
        Assert.All(Cells(direct.Width, direct.Height, direct.Length), c => Assert.Equal(direct.Get(c.X, c.Y, c.Z), built.Get(c.X, c.Y, c.Z)));
    }
}
