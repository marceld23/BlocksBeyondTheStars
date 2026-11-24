// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4017: where the animals of Earth live — a record's biome affinity is the biome of its FIRST listed climate zone (the rabbit's
/// temperate meadows, not the tundra the Earth type lists first), with no other record of any type moving; the cat keeps to its
/// zones; the dolphin and the whale keep to the sea (<c>seaOnly</c>: water the generator's sea covers, never a pond or a river).
/// The spawn on the real Earth is <c>EarthFaunaSpawnTests</c>.
/// </summary>
public sealed class EarthFaunaZonesTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly ITestOutputHelper _output;

    public EarthFaunaZonesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheFirstListedZone_DecidesTheAffinity_AndOnlyTheRabbitOnEarthMoves()
    {
        var content = Shipped.Value;
        var moved = new List<string>();
        foreach (var record in content.AuthoredCreatures.Values)
        {
            foreach (var type in content.Planets.Values)
            {
                int before = LegacyAffinity(record.BiomeSurfaces, record.FloraThemes, type);
                int after = CreatureBiomes.AffinityIn(record.BiomeSurfaces, record.FloraThemes, type);
                if (before != after)
                {
                    moved.Add($"{record.Key}@{type.Key}: {before} -> {after}");
                }
            }
        }

        foreach (var line in moved)
        {
            _output.WriteLine(line);
        }

        Assert.Equal(new[] { "rabbit@sol_earth: 0 -> 3" }, moved);
    }

    [Fact]
    public void TheZoneOrder_WinsOverTheTypesBiomeOrder_AndFallsBackToTheNextZoneAndTheSurfaces()
    {
        var planet = new PlanetType
        {
            Key = "probe",
            FloraTheme = "temperate",
            Biomes =
            {
                new Biome { SurfaceBlock = "snow", FloraTheme = "tundra" },
                new Biome { SurfaceBlock = "grass" }, // the type's own zone: temperate
                new Biome { SurfaceBlock = "sand", FloraTheme = "savanna" },
            },
        };
        Assert.Equal(1, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "temperate", "tundra" }, planet));
        Assert.Equal(0, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "tundra", "temperate" }, planet));
        Assert.Equal(2, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "desert", "Savanna" }, planet)); // next zone
        Assert.Equal(0, CreatureBiomes.AffinityIn(new[] { "snow" }, new[] { "desert" }, planet));               // then the surface
        Assert.Equal(-1, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "desert" }, planet));
    }

    [Fact]
    public void OnEarth_EveryLandAnimal_IsNativeToItsFirstZone()
    {
        var content = Shipped.Value;
        var earth = content.GetPlanet("sol_earth")!;
        foreach (var record in content.AuthoredCreaturesFor(earth).Where(r => r.FloraThemes.Count > 0))
        {
            int affinity = CreatureBiomes.AffinityIn(record.BiomeSurfaces, record.FloraThemes, earth);
            Assert.True(affinity >= 0, record.Key);
            Assert.Equal(FloraThemes.Resolve(record.FloraThemes[0]).Name, CreatureBiomes.ThemeAt(earth, affinity));
        }
    }

    [Fact]
    public void TheCat_KeepsToTheTemperateZoneAndTheSavanna()
    {
        var cat = Shipped.Value.AuthoredCreatures["cat"];
        Assert.True(cat.BiomeExclusive);
        Assert.Equal(new[] { "temperate", "savanna" }, cat.FloraThemes);
    }

    [Fact]
    public void TheSeaAnimals_KeepToTheSea_TheRecordsAndTheSpeciesSayIt()
    {
        var content = Shipped.Value;
        var earth = content.GetPlanet("sol_earth")!;
        foreach (var record in content.AuthoredCreatures.Values)
        {
            bool sea = record.Key is "dolphin" or "whale";
            Assert.Equal(sea, record.SeaOnly);
            Assert.Equal(sea, CreatureGenerator.MakeAuthored(record, earth, 7).SeaOnly);
        }

        var landSea = new AuthoredCreature { Key = "probe", DropItem = string.Empty, SeaOnly = true, Habitat = CreatureHabitat.Land };
        var probeContent = new GameContent(
            blocks: Array.Empty<BlockDefinition>(), items: Array.Empty<ItemDefinition>(), recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(), shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(), authoredCreatures: new[] { landSea });
        var ex = Assert.Throws<ContentValidationException>(probeContent.Validate);
        Assert.Contains(ex.Problems, p => p.Contains("'probe'") && p.Contains("keeps to the sea"));
    }

    [Fact]
    public void TheSea_IsTheWaterBelowTheSeaLevel_NeverAPondOrARiver()
    {
        var content = Shipped.Value;
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        int circ = WorldConstants.Circumference, period = WorldConstants.LatitudePeriodFor(circ);
        int seaColumns = 0, ponds = 0, rivers = 0;
        for (int x = 0; x < circ && (seaColumns < 20 || ponds < 10 || rivers < 3); x += 29)
        {
            for (int z = -period / 2; z < period / 2; z += 37)
            {
                if (!gen.TryGetWaterSurface(planet, x, z, out int top, out _))
                {
                    continue;
                }

                if (gen.SurfacePondDepth(planet, x, z) > 0)
                {
                    ponds++;
                    Assert.False(gen.IsSeaColumn(planet, x, z), $"the pond at {x},{z} reads as sea");
                }
                else if (gen.SurfaceRiverDepth(planet, x, z) > 0)
                {
                    rivers++;
                    Assert.False(gen.IsSeaColumn(planet, x, z), $"the river at {x},{z} reads as sea");
                }
                else if (gen.IsSeaColumn(planet, x, z))
                {
                    seaColumns++;
                    Assert.Equal(sea, top); // the sea's water stands at the sea level
                }
            }
        }

        _output.WriteLine($"jungle/7: {seaColumns} sea columns, {ponds} pond columns, {rivers} river columns");
        Assert.True(seaColumns >= 20 && ponds >= 10 && rivers >= 3, $"{seaColumns} sea, {ponds} ponds, {rivers} rivers");
    }

    /// <summary>The affinity rule before #4017, verbatim: the first of the type's biomes the record names by surface or zone.</summary>
    private static int LegacyAffinity(IReadOnlyList<string> surfaces, IReadOnlyList<string> themes, PlanetType planet)
    {
        for (int i = 0; i < planet.Biomes.Count; i++)
        {
            if (CreatureBiomes.NamesBiome(surfaces, themes, planet.Biomes[i], planet))
            {
                return i;
            }
        }

        return -1;
    }
}
