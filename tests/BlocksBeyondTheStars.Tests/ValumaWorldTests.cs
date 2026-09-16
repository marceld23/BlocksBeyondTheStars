// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Valuma (2026-09, Justus' player report): a rare, peaceful-looking world of wide flat grass plains — no mountains, no
/// volcanoes, hardly a tree, no ruins, animals that never bite — and one shapeshifter, the Sreekmakra, hidden among them.
/// The terrain and roster rules are read on generation-8 worlds only.
/// </summary>
public sealed class ValumaWorldTests
{
    private const string Key = "valuma";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlanetType Valuma => Content.GetPlanet(Key)!;

    [Fact]
    public void Valuma_IsDataComplete_AndGatedToGenerationEight()
    {
        var p = Valuma;
        Assert.True(p.Exotic);
        Assert.False(p.OncePerGalaxy); // rare, not unique
        Assert.Equal(WorldDescription.ExtremePlanetsGeneration, p.MinTerrainGeneration);
        Assert.True(p.CalmTerrain);
        Assert.True(p.PeacefulFauna);
        Assert.True(p.RestrictStructures);
        Assert.Equal(new[] { "net_fragments" }, p.AllowedStructures);
        Assert.Contains("sreekmakra", p.AuthoredCreatures);
        Assert.Equal("breathable", p.Atmosphere);
        Assert.InRange(p.SpawnWeight, 1, 2);
        Assert.True((p.TreeDensity ?? 0.0) < 0.01, "no dense forests");

        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            Assert.Contains("\"planet.valuma.name\": \"Valuma\"", File.ReadAllText(file));
        }

        foreach (var code in new[] { "en", "de" })
        {
            Assert.Contains("\"planet.valuma.desc\"", File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")));
        }
    }

    [Fact]
    public void Valuma_GrowsNoMountainsVolcanoesOrEscarpments_OnAnySeed()
    {
        for (long seed = 1; seed <= 24; seed++)
        {
            var gen = new WorldGenerator(seed, Content);
            gen.SetLavaCoreVolcanoes(true);
            gen.SetTerrainGeneration(WorldDescription.ExtremePlanetsGeneration);
            var gates = gen.WonderGatesForTest(Valuma);
            Assert.False(gates["volcanoes"], $"seed {seed}: volcanoes");
            Assert.False(gates["massifs"], $"seed {seed}: massifs");
            Assert.False(gates["rifts"], $"seed {seed}: rifts");

            // Wide plains: the surface spread over a stretch of the world stays small.
            int lo = int.MaxValue, hi = int.MinValue;
            for (int x = 0; x < 2000; x += 50)
                for (int z = -1000; z < 1000; z += 50)
                {
                    int y = gen.SurfaceHeight(Valuma, x, z);
                    lo = Math.Min(lo, y);
                    hi = Math.Max(hi, y);
                }

            Assert.True(hi - lo <= 48, $"seed {seed}: the plains rise {hi - lo} blocks");
        }

        // The same type on a generation-7 generator keeps the classic landmark gates (volcanoes on a lava-core world).
        var older = new WorldGenerator(1, Content);
        older.SetLavaCoreVolcanoes(true);
        older.SetTerrainGeneration(WorldDescription.ExtremePlanetsGeneration - 1);
        Assert.True(older.WonderGatesForTest(Valuma)["volcanoes"]);
    }

    [Fact]
    public void Valuma_RosterIsPeaceful_WithTheSreekmakraAtItsTail()
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            var roster = CreatureGenerator.GenerateRoster(Valuma, seed, WorldDescription.ExtremePlanetsGeneration, Content.AuthoredCreaturesFor(Valuma));
            var rolled = roster.Where(s => !s.Id.StartsWith("au_", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(rolled);
            Assert.All(rolled, s =>
            {
                Assert.True(s.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish, $"{s.Id} is {s.Temperament}");
                Assert.Equal(0f, s.AttackDamage);
            });
            Assert.Equal("au_sreekmakra", roster[^1].Id);
            Assert.Contains(rolled, s => s.Habitat == CreatureHabitat.Land); // something to copy
        }
    }
}
