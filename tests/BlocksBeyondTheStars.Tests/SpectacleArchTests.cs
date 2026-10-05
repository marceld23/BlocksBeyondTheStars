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
/// Arch lands (#2337): a cluster has arcs that cross in plan with both bars over the crossing; a bar is thinner at
/// mid-span than at its abutment and has air under its apex; the abutments stand level and own their columns; a
/// double-decker stacks its upper bar; a fallen arc keeps its pillars but has no bar and rubble between them; nothing
/// below terrain generation 21 and nothing off the tag.
/// </summary>
public sealed class SpectacleArchTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly (double X, double Z)[] Dirs =
    {
        (1.0, 0.0), (0.7071067811865476, 0.7071067811865476), (0.0, 1.0), (-0.7071067811865476, 0.7071067811865476),
        (-1.0, 0.0), (-0.7071067811865476, -0.7071067811865476), (0.0, -1.0), (0.7071067811865476, -0.7071067811865476),
    };

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static (int X, int Z) Abutment((int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed) arc, int side)
        => (arc.X + (int)Math.Round(Dirs[arc.Dir].X * arc.HalfSpan * side), arc.Z + (int)Math.Round(Dirs[arc.Dir].Z * arc.HalfSpan * side));

    /// <summary>Walks seeds for a desert world whose arcs satisfy <paramref name="pick"/>; only arcs whose abutments the
    /// arch row owns (an earlier row covering a pillar column keeps it — one landmark per column).</summary>
    private static (WorldGenerator Gen, PlanetType Planet, List<(int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed)> Arcs)
        FindArcs(Func<List<(int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed)>, bool> pick)
    {
        var planet = Content.Planets["desert"];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 7, WorldDescription.SpectacleGeneration);
            // "Owned": the ground at both abutments reaches the arc's pillar top (a double-decker's lower arc shares its
            // pillars with the upper one, whose top is higher — so "at least" rather than "exactly").
            var arcs = gen.ArchClustersForTest(planet)
                .Where(a => gen.SurfaceHeight(planet, Abutment(a, -1).X, Abutment(a, -1).Z) >= a.PillarTop
                         && gen.SurfaceHeight(planet, Abutment(a, 1).X, Abutment(a, 1).Z) >= a.PillarTop)
                .ToList();
            if (arcs.Count > 0 && pick(arcs))
            {
                return (gen, planet, arcs);
            }
        }

        Assert.Fail("no fitting arch cluster on 40 desert worlds");
        return default;
    }

    private static int RockBandsAt(WorldGenerator gen, PlanetType planet, int x, int z, out int lowestBottom, out int highestTop)
    {
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int n = gen.GetExtraBands(planet, x, z, bands);
        int count = 0;
        lowestBottom = int.MaxValue;
        highestTop = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (bands[i].Kind != WorldGenerator.BandKind.Rock)
            {
                continue;
            }

            count++;
            lowestBottom = Math.Min(lowestBottom, bands[i].Bottom);
            highestTop = Math.Max(highestTop, bands[i].Top);
        }

        return count;
    }

    [Fact]
    public void ABar_IsThinAtTheApex_ThickAtTheAbutment_WithAirUnderIt_AndLevelPillars()
    {
        var (gen, planet, arcs) = FindArcs(list => list.Any(a => !a.Collapsed));
        var arc = arcs.First(a => !a.Collapsed);
        var d = Dirs[arc.Dir];

        // At mid-span: one bar three thick (another arc may cross here — take the one at this arc's apex), air below.
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int n = gen.GetExtraBands(planet, arc.X, arc.Z, bands);
        bool apexFound = false;
        for (int i = 0; i < n; i++)
        {
            if (bands[i].Kind == WorldGenerator.BandKind.Rock && bands[i].Bottom == arc.ApexUnder)
            {
                Assert.Equal(3, bands[i].Top - bands[i].Bottom + 1);
                Assert.True(bands[i].Bottom > gen.SurfaceHeight(planet, arc.X, arc.Z) + 8, "air under the apex");
                apexFound = true;
            }
        }

        Assert.True(apexFound, "the bar covers its own span centre");

        // Near the abutment the bar is thicker.
        int nx = arc.X + (int)Math.Round(d.X * arc.HalfSpan * 0.9), nz = arc.Z + (int)Math.Round(d.Z * arc.HalfSpan * 0.9);
        n = gen.GetExtraBands(planet, nx, nz, bands);
        int thickest = 0;
        for (int i = 0; i < n; i++)
        {
            if (bands[i].Kind == WorldGenerator.BandKind.Rock)
            {
                thickest = Math.Max(thickest, bands[i].Top - bands[i].Bottom + 1);
            }
        }

        Assert.True(thickest >= 5, $"the bar is {thickest} thick near the abutment");

        // The two pillars stand level (a double-decker's upper bar may raise both alike) and the span is refused as a seat.
        var (lx, lz) = Abutment(arc, -1);
        var (rx, rz) = Abutment(arc, 1);
        Assert.Equal(gen.SurfaceHeight(planet, lx, lz), gen.SurfaceHeight(planet, rx, rz));
        Assert.True(gen.SurfaceHeight(planet, lx, lz) >= arc.PillarTop);

        Assert.True(gen.ColumnHasBandAbove(planet, arc.X, arc.Z));
    }

    [Fact]
    public void ACluster_HasArcsThatCrossInPlan_WithBothBarsOverTheCrossing()
    {
        (int X, int Z) crossing = default;
        var (gen, planet, _) = FindArcs(list =>
        {
            foreach (var a in list)
                foreach (var b in list)
                {
                    if (a.Collapsed || b.Collapsed || a.Dir == b.Dir || (a.X == b.X && a.Z == b.Z))
                    {
                        continue;
                    }

                    if (Cross(a, b, out var p))
                    {
                        crossing = p;
                        return true;
                    }
                }

            return false;
        });

        int count = RockBandsAt(gen, planet, crossing.X, crossing.Z, out int lowest, out int highest);
        Assert.True(count >= 2, $"{count} bars over the crossing");
        Assert.True(highest - lowest >= 4, "the two bars lie at different heights");
    }

    /// <summary>Segment intersection of two spans, with the crossing kept clear of both arcs' abutments.</summary>
    private static bool Cross((int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed) a,
        (int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed) b, out (int X, int Z) p)
    {
        p = default;
        var da = Dirs[a.Dir];
        var db = Dirs[b.Dir];
        double det = da.X * db.Z - da.Z * db.X;
        if (Math.Abs(det) < 1e-6)
        {
            return false;
        }

        double rx = b.X - a.X, rz = b.Z - a.Z;
        double ta = (rx * db.Z - rz * db.X) / det;
        double tb = (rx * da.Z - rz * da.X) / det;
        double margin = Math.Max(a.AbutR, b.AbutR) + 3.0;
        if (Math.Abs(ta) > a.HalfSpan - margin || Math.Abs(tb) > b.HalfSpan - margin)
        {
            return false;
        }

        p = (a.X + (int)Math.Round(da.X * ta), a.Z + (int)Math.Round(da.Z * ta));
        return true;
    }

    [Fact]
    public void ADoubleDecker_StacksItsUpperBar_OverTheLower()
    {
        var (gen, planet, arcs) = FindArcs(list => list.GroupBy(a => (a.X, a.Z, a.Dir)).Any(g => g.Count() == 2 && g.All(a => !a.Collapsed)));
        var pair = arcs.GroupBy(a => (a.X, a.Z, a.Dir)).First(g => g.Count() == 2 && g.All(a => !a.Collapsed)).OrderBy(a => a.ApexUnder).ToArray();
        int count = RockBandsAt(gen, planet, pair[0].X, pair[0].Z, out int lowest, out int highest);
        Assert.True(count >= 2);
        Assert.True(pair[1].ApexUnder > pair[0].ApexUnder + 3, "the upper deck's apex lies over the lower apex");
        Assert.True(highest >= pair[1].ApexUnder && lowest <= pair[0].ApexUnder);
        Assert.Equal(pair[1].PillarTop, gen.SurfaceHeight(planet, Abutment(pair[1], 1).X, Abutment(pair[1], 1).Z));
    }

    [Fact]
    public void AFallenArc_KeepsItsPillars_HasNoBar_AndRubbleBetweenThem()
    {
        var (gen, planet, arcs) = FindArcs(list => list.Any(a => a.Collapsed));
        var arc = arcs.First(a => a.Collapsed);
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int n = gen.GetExtraBands(planet, arc.X, arc.Z, bands);
        for (int i = 0; i < n; i++)
        {
            Assert.False(bands[i].Kind == WorldGenerator.BandKind.Rock && bands[i].Bottom == arc.ApexUnder, "a fallen arc has no bar");
        }

        var scree = Content.GetBlock("scree")!.NumericId;
        Assert.Equal(scree, gen.LandmarkPaintForTest("arch-cluster", planet, arc.X, arc.Z));
        Assert.True(gen.SurfaceHeight(planet, Abutment(arc, -1).X, Abutment(arc, -1).Z) >= arc.PillarTop);
    }

    [Fact]
    public void NothingBelowGenerationTwentyOne_AndNothingOffTheTag()
    {
        var planet = Content.Planets["desert"];
        var g20 = Gen(6158, WorldDescription.SpectacleGeneration - 1);
        Assert.Empty(g20.ArchClustersForTest(planet));
        Assert.DoesNotContain("arch-cluster", g20.LandmarkOrderForTest(planet));

        var g21 = Gen(6158, WorldDescription.SpectacleGeneration);
        Assert.Contains("arch-cluster", g21.LandmarkOrderForTest(planet));
        Assert.DoesNotContain("arch-cluster", g21.LandmarkOrderForTest(Content.Planets["jungle"]));
        Assert.Contains("arch-rubble", g21.PropActiveForTest(planet, crystalWorld: false, dryWorld: true));
        Assert.DoesNotContain("arch-rubble", g21.PropActiveForTest(Content.Planets["jungle"], crystalWorld: false, dryWorld: false));
    }
}
