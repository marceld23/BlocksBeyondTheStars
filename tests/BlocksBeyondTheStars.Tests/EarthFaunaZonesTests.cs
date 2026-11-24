// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4017: where the animals of Earth live — a record's biome affinity is the biome of its FIRST listed climate zone (the rabbit's
/// temperate meadows, not the tundra the Earth type lists first), with no other record of any type moving; the cat keeps to its
/// zones. The spawn on the real Earth is <see cref="EarthFaunaSpawnTests"/>.
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
