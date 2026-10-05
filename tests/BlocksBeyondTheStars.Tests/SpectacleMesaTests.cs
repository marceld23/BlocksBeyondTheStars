// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Bridge mesas and the impossible tables (#2338): a deck spans from rim to rim with air under its middle, a broken
/// deck has a gap with rubble, a visor overhangs the wall, a ring table's outer cap floats, a two-storey table has a
/// second cap, a holed table has several gates — and nothing of it below terrain generation 21.
/// </summary>
public sealed class SpectacleMesaTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static bool Owned(WorldGenerator gen, PlanetType planet, string row, int x, int z)
    {
        double off = gen.LandmarkOffsetForTest(row, planet, x, z);
        return off > 0.0 && gen.SurfaceHeight(planet, x, z) == gen.RawSurfaceHeightForTest(planet, x, z) + (int)Math.Round(off);
    }

    private static (WorldGenerator Gen, PlanetType Planet, ((int X, int Z, double R)[] Tables, int RimY, (int From, int To, bool Broken)[] Bridges) Cluster, int Bridge)
        FindBridge(bool broken)
    {
        var planet = Content.Planets["desert"];
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 13, WorldDescription.SpectacleGeneration);
            foreach (var cluster in gen.MesaClustersForTest(planet))
            {
                for (int b = 0; b < cluster.Bridges.Length; b++)
                {
                    var bridge = cluster.Bridges[b];
                    var a = cluster.Tables[bridge.From];
                    var c = cluster.Tables[bridge.To];
                    if (bridge.Broken == broken && Owned(gen, planet, "mesa-cluster", a.X, a.Z) && Owned(gen, planet, "mesa-cluster", c.X, c.Z))
                    {
                        return (gen, planet, cluster, b);
                    }
                }
            }
        }

        Assert.Fail("no bridge mesa with a " + (broken ? "broken" : "whole") + " deck on 60 desert worlds");
        return default;
    }

    private static (WorldGenerator Gen, PlanetType Planet, (int X, int Z, double R, double H, string Variant) Table) FindTable(string variant)
    {
        var planet = Content.Planets["desert"];
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 17, WorldDescription.SpectacleGeneration);
            foreach (var t in gen.TablesForTest(planet))
            {
                if (t.Variant == variant && Owned(gen, planet, "table-mountain", t.X, t.Z))
                {
                    return (gen, planet, t);
                }
            }
        }

        Assert.Fail($"no {variant} table on 60 desert worlds");
        return default;
    }

    private static bool RockBandAt(WorldGenerator gen, PlanetType planet, int x, int z, out WorldGenerator.ColumnBand band)
    {
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int n = gen.GetExtraBands(planet, x, z, bands);
        band = default;
        for (int i = 0; i < n; i++)
        {
            if (bands[i].Kind == WorldGenerator.BandKind.Rock && (band.Top == 0 || bands[i].Top > band.Top))
            {
                band = bands[i];
            }
        }

        return band.Top != 0;
    }

    [Fact]
    public void ADeck_SpansFromRimToRim_LevelWithBothTables_WithAirUnderItsMiddle()
    {
        var (gen, planet, cluster, b) = FindBridge(broken: false);
        var a = cluster.Tables[cluster.Bridges[b].From];
        var c = cluster.Tables[cluster.Bridges[b].To];
        Assert.Equal(cluster.RimY, gen.SurfaceHeight(planet, a.X, a.Z));
        Assert.Equal(cluster.RimY, gen.SurfaceHeight(planet, c.X, c.Z));

        int mx = (a.X + c.X) / 2, mz = (a.Z + c.Z) / 2;
        Assert.True(RockBandAt(gen, planet, mx, mz, out var deck), "the deck covers the midpoint");
        Assert.Equal(cluster.RimY, deck.Top);
        Assert.True(deck.Bottom > gen.SurfaceHeight(planet, mx, mz) + 10, "air under the deck's middle");
    }

    [Fact]
    public void ABrokenDeck_HasAGapInItsMiddle_AndRubbleBelow()
    {
        var (gen, planet, cluster, b) = FindBridge(broken: true);
        var a = cluster.Tables[cluster.Bridges[b].From];
        var c = cluster.Tables[cluster.Bridges[b].To];
        int mx = (a.X + c.X) / 2, mz = (a.Z + c.Z) / 2;
        Assert.False(RockBandAt(gen, planet, mx, mz, out var deck) && deck.Top == cluster.RimY, "no deck over the gap");
        Assert.Equal(Content.GetBlock("scree")!.NumericId, gen.LandmarkPaintForTest("mesa-cluster", planet, mx, mz));
    }

    [Fact]
    public void AVisorTable_OverhangsItsWall()
    {
        var (gen, planet, t) = FindTable("Visor");
        int x = t.X + (int)Math.Round(t.R) + 4, z = t.Z;
        Assert.True(RockBandAt(gen, planet, x, z, out var visor), "the visor reaches past the wall");
        Assert.True(visor.Top - visor.Bottom + 1 >= 4, "a visor is at least four thick");
        Assert.True(visor.Bottom > gen.SurfaceHeight(planet, x, z) + 10, "air under the visor");
        Assert.InRange(visor.Top - gen.SurfaceHeight(planet, t.X, t.Z), -3, 3); // level with the cap
    }

    [Fact]
    public void ARingTable_FloatsItsOuterCap_OverAirAroundTheCore()
    {
        var (gen, planet, t) = FindTable("Ring");
        int x = t.X + (int)Math.Round(t.R * 0.85), z = t.Z;
        Assert.True(RockBandAt(gen, planet, x, z, out var ring), "the ring covers the outer cap");
        Assert.True(ring.Bottom > gen.SurfaceHeight(planet, x, z) + 10, "air under the ring");
        Assert.True(gen.SurfaceHeight(planet, t.X, t.Z) > gen.SurfaceHeight(planet, x, z) + 20, "the core is the only ground");
    }

    [Fact]
    public void ATwoStoreyTable_CarriesASecondCap()
    {
        var (gen, planet, t) = FindTable("TwoStorey");
        int cap = gen.SurfaceHeight(planet, t.X + (int)Math.Round(t.R * 0.5), t.Z);
        int storey = gen.SurfaceHeight(planet, t.X, t.Z);
        Assert.True(storey >= cap + 13, $"the second storey stands {storey - cap} over the cap");
    }

    [Fact]
    public void AHoledTable_HasGatesOnSeveralBearings()
    {
        // A gate needs a table of radius ≥ 60 (a small butte keeps its wall whole), so pick a big holed table.
        var planet = Content.Planets["desert"];
        WorldGenerator? gen = null;
        (int X, int Z, double R, double H, string Variant) t = default;
        for (long s = 1; s <= 80 && gen is null; s++)
        {
            var g = Gen(s * 6151 + 17, WorldDescription.SpectacleGeneration);
            foreach (var table in g.TablesForTest(planet))
            {
                if (table.Variant == "Holed" && table.R >= 60.0 && Owned(g, planet, "table-mountain", table.X, table.Z))
                {
                    gen = g;
                    t = table;
                    break;
                }
            }
        }

        Assert.NotNull(gen);

        // Walk a circle at 80 % of the radius and count the separate runs of columns a gate worm cuts through.
        Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
        int steps = 720;
        var hit = new bool[steps];
        for (int i = 0; i < steps; i++)
        {
            double a = i * (Math.PI * 2.0 / steps);
            int x = t.X + (int)Math.Round(Math.Cos(a) * t.R * 0.8), z = t.Z + (int)Math.Round(Math.Sin(a) * t.R * 0.8);
            hit[i] = gen!.TunnelSpans(planet, x, z, spans) > 0;
        }

        int runs = 0;
        for (int i = 0; i < steps; i++)
        {
            if (hit[i] && !hit[(i + steps - 1) % steps])
            {
                runs++;
            }
        }

        Assert.True(runs >= 2, $"a holed table has {runs} gate(s) through its wall");
    }

    [Fact]
    public void NothingBelowGenerationTwentyOne()
    {
        var planet = Content.Planets["desert"];
        var g20 = Gen(6160, WorldDescription.SpectacleGeneration - 1);
        Assert.Empty(g20.MesaClustersForTest(planet));
        Assert.DoesNotContain("mesa-cluster", g20.LandmarkOrderForTest(planet));
        Assert.All(g20.TablesForTest(planet), t => Assert.Equal("Plain", t.Variant));

        var g21 = Gen(6160, WorldDescription.SpectacleGeneration);
        Assert.Contains("mesa-cluster", g21.LandmarkOrderForTest(planet));
        Assert.DoesNotContain("mesa-cluster", g21.LandmarkOrderForTest(Content.Planets["jungle"]));
    }
}
