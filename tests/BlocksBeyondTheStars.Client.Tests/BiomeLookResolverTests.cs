// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3289: the client decides which biome palette a rune stone or an ancient brick wears from the world info the far terrain
/// gets — no protocol change — so its answer must be the server's own, column for column, on a classic multi-biome world, a
/// sand-sea world (its sea field claims columns outside the altitude mix) and a single-biome lava world. A void world has
/// no biome. The chunk mesher asks from several worker threads at once.
/// </summary>
public sealed class BiomeLookResolverTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData("varied", 2)]
    [InlineData("sand_sea", 2)]
    [InlineData("lava", 1)]
    public void TheResolver_AgreesWithTheServersGenerator_ColumnForColumn(string planetKey, int minPalettes)
    {
        using var h = new ClientServerHarness(Content, c => c.StartPlanet = planetKey);
        FarTerrainWorldInfo? info = null;
        h.Client.FarTerrainWorldInfoReceived += m => info = m;
        h.Join();
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => info != null, 80), "the world info should arrive after the join");
        Assert.Equal(planetKey, info!.PlanetType);

        var resolver = BiomeLookResolver.Create(Content, h.JoinAccepted!.WorldSeed, info);
        Assert.NotNull(resolver);
        Assert.Equal(info.WorldId, resolver!.WorldId);

        var generator = h.Server.World.Generator;
        var planet = h.Server.World.Planet;
        int circ = info.Circumference > 0 ? info.Circumference : WorldConstants.Circumference;
        var seen = new HashSet<int>();
        int columns = 0;
        for (int x = 0; x < circ; x += 53)
            for (int z = -1600; z <= 1600; z += 97)
            {
                int expected = BiomePalettes.OfSurface(Content, generator.BiomeSurfaceKeyAt(planet, x, z));
                Assert.True(expected >= 0, $"{planetKey}: column ({x}, {z}) has no palette");
                Assert.Equal(expected, resolver.PaletteAt(x, z));
                Assert.Equal(expected, resolver.PaletteFn(x, z)); // the delegate the mesher takes, and the cached answer
                seen.Add(expected);
                columns++;
            }

        Assert.True(columns > 1000, $"only {columns} columns compared");
        Assert.True(seen.Count >= minPalettes, $"{planetKey}: expected at least {minPalettes} palettes, saw {seen.Count}");
    }

    [Fact]
    public void AVoidWorld_HasNoBiome()
    {
        Assert.Null(BiomeLookResolver.Create(Content, 424242,
            new FarTerrainWorldInfo { WorldId = 3, LocationId = "station", PlanetType = "orbital_station", Void = true }));
        Assert.Null(BiomeLookResolver.Create(Content, 424242,
            new FarTerrainWorldInfo { WorldId = 3, LocationId = "station", PlanetType = "orbital_station" })); // the type is void
        Assert.Null(BiomeLookResolver.Create(Content, 424242,
            new FarTerrainWorldInfo { WorldId = 4, LocationId = "x", PlanetType = "no_such_planet" }));
    }

    [Fact]
    public void ParallelQueries_AnswerLikeSequentialOnes()
    {
        var info = new FarTerrainWorldInfo
        {
            WorldId = 1,
            LocationId = "sys0-p1",
            PlanetType = "varied",
            Circumference = 6000,
            TerrainGeneration = WorldDescription.CurrentTerrainGeneration,
        };
        var columns = new List<(int X, int Z)>();
        for (int x = 0; x < 6000; x += 61)
            for (int z = -900; z <= 900; z += 67)
            {
                columns.Add((x, z));
            }

        var sequential = BiomeLookResolver.Create(Content, 777, info)!;
        var expected = columns.Select(c => sequential.PaletteAt(c.X, c.Z)).ToArray();
        Assert.True(expected.Distinct().Count() >= 2, "a varied world should show more than one palette");

        // Every column asked four times by threads running at once — through the cache's misses and hits.
        var parallel = BiomeLookResolver.Create(Content, 777, info)!;
        var answers = new int[4, columns.Count];
        Parallel.For(0, 4 * columns.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            int k = i % columns.Count;
            answers[i / columns.Count, k] = parallel.PaletteFn(columns[k].X, columns[k].Z);
        });

        for (int pass = 0; pass < 4; pass++)
            for (int k = 0; k < columns.Count; k++)
            {
                Assert.Equal(expected[k], answers[pass, k]);
            }
    }
}
