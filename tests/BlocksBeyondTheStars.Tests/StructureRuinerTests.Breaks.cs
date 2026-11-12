// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Revision 2 of the ruin pass (terrain generation 39): the lattice BREAKS. A tall frame loses its top above a plane cut clean
/// through it, a long one a section of an unsupported span between two planes — never a kept cell, never a cell at or below
/// the ground row — and the settle brings down what no longer hangs on anything. Deterministic per seed; revision 1 and every
/// other profile build exactly what they built before.
/// </summary>
public sealed class StructureRuinerBreakTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly long[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    /// <summary>A mast 9 × 60 × 9: four corner posts from the ground row 0 up, a ring every five rows.</summary>
    private static VoxelGrid Mast()
    {
        const int S = 9, H = 60;
        var g = new VoxelGrid(S, H, S);
        for (int y = 0; y < H; y++)
        {
            for (int i = 0; i < S; i++)
            {
                foreach (var (x, z) in new[] { (0, 0), (0, S - 1), (S - 1, 0), (S - 1, S - 1) })
                {
                    g.Set(x, y, z, Id("iron_wall"));
                }

                if (y % 5 == 0)
                {
                    g.Set(i, y, 0, Id("iron_wall"));
                    g.Set(i, y, S - 1, Id("iron_wall"));
                    g.Set(0, y, i, Id("iron_wall"));
                    g.Set(S - 1, y, i, Id("iron_wall"));
                }
            }
        }

        return g;
    }

    /// <summary>A bridge 120 × 30 × 7 with its ground row at 10: two towers (x 20..24 and 95..99) from the bottom row to the
    /// top, a deck on row 12 from end to end on piers at both ends (x 0..11 and 108..119, from the bottom up — the side spans
    /// between them and the towers are too short to break), a cable from tower top to tower top sagging to the deck, hangers
    /// every four columns.</summary>
    private static VoxelGrid Bridge(out int ground)
    {
        const int W = 120, H = 30, L = 7, Deck = 12;
        ground = 10;
        var g = new VoxelGrid(W, H, L);
        ushort iron = Id("iron_wall");
        for (int x = 0; x < W; x++)
        {
            bool tower = (x >= 20 && x <= 24) || (x >= 95 && x <= 99), pier = x <= 11 || x >= W - 12;
            for (int z = 0; z < L; z++)
            {
                g.Set(x, Deck, z, iron);
                for (int y = 0; y < (tower ? H : pier ? Deck : 0); y++)
                {
                    g.Set(x, y, z, iron);
                }
            }

            if (x > 24 && x < 95)
            {
                double t = (x - 59.5) / 35.0;
                int cy = Deck + 1 + (int)Math.Round((H - Deck - 2) * t * t);
                for (int y = Deck + 1; y <= cy && y < H; y++)
                {
                    if (y == cy || x % 4 == 0)
                    {
                        g.Set(x, y, 0, iron);
                        g.Set(x, y, L - 1, iron);
                    }
                }
            }
        }

        return g;
    }

    private static VoxelGrid Run(VoxelGrid g, long seed, int ground, int revision, string profile = LandmarkRules.ProfileLattice,
        double strength = 0.5)
    {
        StructureRuiner.Run(g, Content, seed, profile, strength, ground, Array.Empty<Vector3i>(), "grass", revision, null);
        return g;
    }

    private static int Solid(VoxelGrid g, Func<int, int, int, bool>? where = null)
        => RuinerTestGrids.Cells(g).Count(c => g.Solid(c.X, c.Y, c.Z) && (where is null || where(c.X, c.Y, c.Z)));

    [Fact]
    public void ATallFrame_LosesItsTop_AtRevision2_AndKeepsIt_AtRevision1()
    {
        int intactTop = Solid(Mast(), (_, y, _) => y >= 45);
        foreach (long seed in Seeds)
        {
            var one = Run(Mast(), seed, 0, revision: 1);
            var two = Run(Mast(), seed, 0, revision: 2);
            Assert.True(Solid(one, (_, y, _) => y >= 45) > intactTop / 2, $"seed {seed}: revision 1 already lost the top");
            Assert.Equal(0, Solid(two, (_, y, _) => y >= 45)); // the break lies at 25..65 % of the height (+ tilt and fray)
            Assert.True(Solid(two, (_, y, _) => y >= 1 && y <= 12) > 0, $"seed {seed}: the stump fell too");
            Assert.Equal(Solid(two, (_, y, _) => y == 0), Solid(Mast(), (_, y, _) => y == 0)); // the ground row stands
        }
    }

    [Fact]
    public void ALongFrame_DropsASectionOfItsSpan_NeverItsTowersOrPiersOrTheGroundRow()
    {
        var intact = Bridge(out int ground);
        foreach (long seed in Seeds)
        {
            var g = Run(Bridge(out _), seed, ground, revision: 2);
            // A column of the deck is gone when not one of its seven cells is left (a stray speckle takes one, never all).
            var lost = Enumerable.Range(0, g.W).Where(x => Enumerable.Range(0, g.L).All(z => !g.Solid(x, 12, z))).ToList();
            Assert.True(lost.Count >= 6, $"seed {seed}: the main span lost only {lost.Count} columns of its deck");
            Assert.All(lost, x => Assert.InRange(x, 22, 97)); // the planes lie two slices off the towers (+ lean and fray)
            // The breaks never cut a tower: it stands as revision 1 leaves it (whose collapse balls revision 2 shares), but for
            // the fray of a plane next to it.
            var one = Run(Bridge(out _), seed, ground, revision: 1);
            foreach (int x0 in new[] { 20, 95 })
            {
                bool Tower(int x, int y, int z) => x >= x0 && x < x0 + 5 && y > ground;
                bool Top(int x, int y, int z) => Tower(x, y, z) && y >= g.H - 2;
                Assert.True(Solid(g, Tower) >= 0.9 * Solid(one, Tower), $"seed {seed}: the tower at {x0} fell");
                Assert.Equal(Solid(one, Top) > 0, Solid(g, Top) > 0);
            }

            for (int x = 0; x < g.W; x++)
            {
                for (int z = 0; z < g.L; z++)
                {
                    for (int y = 0; y <= ground; y++)
                    {
                        Assert.Equal(intact.Get(x, y, z), g.Get(x, y, z)); // nothing at or below it moves — no rubble over the gap
                    }
                }
            }

        }
    }

    [Fact]
    public void ABreak_NeverTakesAKeptCell_AndWhatHangsOnIt_Stands()
    {
        foreach (long seed in Seeds)
        {
            var g = Mast();
            for (int y = 0; y < g.H; y++)
            {
                g.Keep[g.Index(0, y, 0)] = true; // one corner post is kept from bottom to top
            }

            Run(g, seed, 0, revision: 2);
            Assert.All(Enumerable.Range(0, g.H), y => Assert.True(g.Solid(0, y, 0), $"seed {seed}: the kept post lost row {y}"));
            Assert.True(Solid(g, (x, y, z) => y >= 45 && !(x == 0 && z == 0)) > 0, $"seed {seed}: nothing hangs on the kept post");
        }
    }

    [Fact]
    public void Revision2_IsDeterministic_AndDiffersBySeed()
    {
        var a = RuinerTestGrids.Hash(Content, Run(Bridge(out int ground), 5, ground, revision: 2));
        Assert.Equal(a, RuinerTestGrids.Hash(Content, Run(Bridge(out _), 5, ground, revision: 2)));
        Assert.NotEqual(a, RuinerTestGrids.Hash(Content, Run(Bridge(out _), 6, ground, revision: 2)));
        Assert.NotEqual(a, RuinerTestGrids.Hash(Content, Run(Bridge(out _), 5, ground, revision: 1)));

        // A newer revision this build does not know runs the newest it knows.
        Assert.Equal(a, RuinerTestGrids.Hash(Content, Run(Bridge(out _), 5, ground, revision: 99)));
    }

    /// <summary>Only a lattice breaks: masonry, underground and hull build at revision 2 exactly what they built at revision 1,
    /// and so does a compact lattice (neither tall nor long).</summary>
    [Theory]
    [InlineData(LandmarkRules.ProfileMasonry)]
    [InlineData(LandmarkRules.ProfileUnderground)]
    [InlineData(LandmarkRules.ProfileHull)]
    public void EveryOtherProfile_BuildsAtRevision2_WhatItBuiltAtRevision1(string profile)
    {
        foreach (long seed in Seeds)
        {
            Assert.Equal(RuinerTestGrids.Hash(Content, Run(Mast(), seed, 0, 1, profile)), RuinerTestGrids.Hash(Content, Run(Mast(), seed, 0, 2, profile)));
            Assert.Equal(RuinerTestGrids.Hash(Content, Run(Bridge(out int g1), seed, g1, 1, profile)),
                RuinerTestGrids.Hash(Content, Run(Bridge(out int g2), seed, g2, 2, profile)));
            var one = RuinerTestGrids.Soup(Content);
            var two = RuinerTestGrids.Soup(Content);
            StructureRuiner.Run(one.Grid, Content, seed, profile, 0.6, one.GroundRow, one.Markers, "basalt", 1, null);
            StructureRuiner.Run(two.Grid, Content, seed, profile, 0.6, two.GroundRow, two.Markers, "basalt", 2, null);
            Assert.Equal(RuinerTestGrids.Hash(Content, one.Grid), RuinerTestGrids.Hash(Content, two.Grid));
        }

        var block = RuinerTestGrids.Soup(Content); // 24 × 16 above its ground row × 18: compact
        var again = RuinerTestGrids.Soup(Content);
        StructureRuiner.Run(block.Grid, Content, 3, LandmarkRules.ProfileLattice, 0.6, block.GroundRow, block.Markers, "basalt", 1, null);
        StructureRuiner.Run(again.Grid, Content, 3, LandmarkRules.ProfileLattice, 0.6, again.GroundRow, again.Markers, "basalt", 2, null);
        Assert.Equal(RuinerTestGrids.Hash(Content, block.Grid), RuinerTestGrids.Hash(Content, again.Grid));
    }

    /// <summary>A lattice ruin ZONE breaks too (the upper box of a tower on a masonry base): its top falls in the shared
    /// settle, the base below the zone is untouched by the break.</summary>
    [Fact]
    public void ALatticeZone_Breaks_AndItsTopFalls()
    {
        foreach (long seed in Seeds)
        {
            var g = Mast();
            var zones = new List<RuinZoneBox> { new(0, 20, 0, 8, 59, 8, LandmarkRules.ProfileLattice, 0.4) };
            for (int x = 0; x < g.W; x++)
            {
                for (int y = 0; y < 20; y++)
                {
                    for (int z = 0; z < g.L; z++)
                    {
                        g.Keep[g.Index(x, y, z)] = true; // the base: stands as built (the zone's own pass only)
                    }
                }
            }

            StructureRuiner.RunZoned(g, Content, seed, LandmarkRules.ProfileMasonry, 0.5, 0, Array.Empty<Vector3i>(), "grass", 2, null, zones);
            Assert.Equal(0, Solid(g, (_, y, _) => y >= 52));
            var intact = Mast();
            Assert.All(RuinerTestGrids.Cells(intact).Where(c => c.Y < 20 && intact.Solid(c.X, c.Y, c.Z)),
                c => Assert.True(g.Solid(c.X, c.Y, c.Z), $"seed {seed}: the base lost {c}")); // (rubble may lie on it)
        }
    }
}
