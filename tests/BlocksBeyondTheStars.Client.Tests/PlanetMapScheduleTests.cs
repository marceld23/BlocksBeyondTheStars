// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The landing map's turn in the planet-map queue (#2761) and the map-scale ponds of the bake (#2758).
/// </summary>
public sealed class PlanetMapScheduleTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void LandingMap_StartsAtOnce_WhileTwoOrbitBakesRun()
    {
        const int orbit = 1, home = 10, landing = PlanetMapSchedule.InteractivePriority;

        // Free regular slots take anything.
        Assert.True(PlanetMapSchedule.MayStart(orbit, running: 0, runningInteractive: 0));
        Assert.True(PlanetMapSchedule.MayStart(orbit, running: 1, runningInteractive: 0));

        // Entering space filled both regular slots with orbit spheres: the landing map gets its own slot,
        // another orbit sphere (even the home planet) waits.
        Assert.True(PlanetMapSchedule.MayStart(landing, running: 2, runningInteractive: 0));
        Assert.True(PlanetMapSchedule.MayStart(landing + 1, running: 2, runningInteractive: 0)); // the coarse map first
        Assert.False(PlanetMapSchedule.MayStart(home, running: 2, runningInteractive: 0));

        // One interactive slot: a second interactive request waits for a free slot of either kind.
        Assert.False(PlanetMapSchedule.MayStart(landing, running: 3, runningInteractive: 1));
        Assert.False(PlanetMapSchedule.MayStart(landing, running: 2, runningInteractive: 1));
        Assert.True(PlanetMapSchedule.MayStart(landing, running: 1, runningInteractive: 1));

        // Background bakes never use the interactive slot.
        Assert.False(PlanetMapSchedule.MayStart(orbit, running: 2, runningInteractive: 1));
    }

    [Fact]
    public void Bake_PaintsTheMapScalePonds_OnASealedWaterWorld()
    {
        // The playtest planet of 2026-10-18 (generation 26, karst): the bake reads SummarizeSurface's pond flag, never
        // the per-pond level search — and its fluid pixels are exactly the summary's ponds above the sea.
        const long seed = -7848507700092313449L;
        const int circ = 9184;
        var planet = Content.GetPlanet("karst")!;
        var map = new PlanetMapBakeJob(new PlanetMapRequest
        {
            Content = Content,
            WorldSeed = seed,
            BodyId = "sys3-p0",
            FloraKey = "Liosal · Liosal I",
            PlanetTypeKey = planet.Key,
            Circumference = circ,
            Width = 64,
            Height = 32,
            Continents = true,
            Generation = WorldDescription.CurrentTerrainGeneration,
            LavaCoreVolcanoes = true,
            GroundColors = new Dictionary<ushort, int>(),
        }).Run();

        var gen = new WorldGenerator(seed, Content);
        gen.SetWorldMode(circ, false, landingPads: null, "sys3-p0");
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        int fluid = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                bool sea = (map.Flags[i] & PlanetMapData.FlagSea) != 0;
                bool pond = (map.Flags[i] & PlanetMapData.FlagFluid) != 0;
                Assert.Equal(!sea && gen.SummarizeSurface(planet, map.WorldX(x), map.WorldZ(y)).Pond, pond);
                fluid += pond ? 1 : 0;
            }
        }

        Assert.True(fluid > 5, $"the karst planet's map shows its ponds, got {fluid}");
    }
}
