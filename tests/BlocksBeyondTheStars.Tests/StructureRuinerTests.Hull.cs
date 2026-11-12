// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The <c>hull</c> ruin profile (#3515), the decay of a wreck: no ground, so nothing is anchored by its height — what
/// stays is what hangs on a kept cell, a protected row or the largest piece that is left; no crumbled patches, no rubble,
/// no flora, but breaches anywhere.
/// </summary>
public sealed class StructureRuinerHullTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    /// <summary>A small ship hull, 15 × 7 × 21: keel and deck plates, a roof with lights, side windows, two bulkheads with
    /// doorways, a shaped seat, one kept reactor cell on the deck and a loot marker.</summary>
    private static (VoxelGrid Grid, List<SettlementMarker> Markers) Hull()
    {
        const int W = 15, H = 7, L = 21;
        var g = new VoxelGrid(W, H, L);
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++)
            {
                for (int z = 0; z < L; z++)
                {
                    bool shell = x == 0 || x == W - 1 || z == 0 || z == L - 1 || y == 0 || y == H - 1;
                    bool bulkhead = (z == 7 || z == 14) && !(x >= 6 && x <= 8 && y >= 1 && y <= 3);
                    if (!shell && !bulkhead)
                    {
                        continue;
                    }

                    string key = y == H - 1 && x % 4 == 2 && z % 5 == 2 ? "light_white"
                        : (x == 0 || x == W - 1) && y == 3 && z % 3 == 1 ? "glass"
                        : "iron_wall";
                    g.Set(x, y, z, Id(key));
                }
            }
        }

        g.Set(4, 1, 3, Id("iron_wall"), shape: 2); // a seat
        g.Set(7, 1, 10, Id("iron_wall"));
        g.Keep[g.Index(7, 1, 10)] = true; // the reactor
        return (g, new List<SettlementMarker> { new("loot", new Vector3i(3, 1, 3)) });
    }

    private static VoxelGrid Ruin(long seed, double strength = 0, int groundRow = 0, RuinOptions? options = null)
    {
        var (g, markers) = Hull();
        StructureRuiner.Run(g, Content, seed, LandmarkRules.ProfileHull, strength, groundRow, markers, "grass", StructureRuiner.Revision, options);
        return g;
    }

    [Fact]
    public void Hull_LeavesNoPieceAdrift_ExceptOnAKeptCell()
    {
        foreach (double strength in new[] { 0.0, 0.5, 1.0 })
        {
            for (long seed = 1; seed <= 12; seed++)
            {
                var g = Ruin(seed, strength);
                Assert.True(g.Solid(7, 1, 10), "the kept reactor went");
                var loose = RuinerTestGrids.Pieces(g).Where(p => !p.Any(c => g.Keep[g.Index(c.X, c.Y, c.Z)])).ToList();
                Assert.True(loose.Count <= 1, $"seed {seed} at {strength}: {loose.Count} pieces hang on nothing");
            }
        }
    }

    [Fact]
    public void Hull_HasNoGround_NoRubbleNoFlora_ButBreaches()
    {
        var (intact, _) = Hull();
        int breached = 0;
        var prints = new HashSet<ulong>();
        for (long seed = 1; seed <= 12; seed++)
        {
            var g = Ruin(seed, 0.5);
            Assert.Equal(RuinerTestGrids.Hash(Content, g), RuinerTestGrids.Hash(Content, Ruin(seed, 0.5)));
            Assert.Equal(RuinerTestGrids.Hash(Content, g), RuinerTestGrids.Hash(Content, Ruin(seed, 0.5, groundRow: 3))); // no ground
            prints.Add(RuinerTestGrids.Hash(Content, g));

            // Nothing new lies anywhere: every block left stood there before (a dead light is its switched-off twin).
            Assert.All(RuinerTestGrids.Cells(g).Where(c => g.Solid(c.X, c.Y, c.Z)), c => Assert.True(intact.Solid(c.X, c.Y, c.Z)));
            int lost = RuinerTestGrids.Cells(intact).Count(c => intact.Solid(c.X, c.Y, c.Z) && !g.Solid(c.X, c.Y, c.Z));
            breached += lost > 0 ? 1 : 0;
        }

        Assert.True(breached >= 10, $"only {breached} of 12 hulls were breached");
        Assert.Equal(12, prints.Count);

        // Breaches reach the keel too: a hull has no protected bottom unless the caller protects rows.
        Assert.Contains(Enumerable.Range(1, 30), seed =>
        {
            var g = Ruin(seed, 1.0);
            return RuinerTestGrids.Cells(intact).Any(c => c.Y == 0 && intact.Solid(c.X, c.Y, c.Z) && !g.Solid(c.X, c.Y, c.Z));
        });
    }

    [Fact]
    public void Hull_ProtectedRows_KeepTheDeck_AndScorchMarksTheBreaches()
    {
        var (intact, _) = Hull();
        int marked = 0;
        for (long seed = 1; seed <= 8; seed++)
        {
            var g = Ruin(seed, 1.0, options: new RuinOptions
            {
                ProtectRowsUpTo = 1,
                Scorch = new RuinScorch { Tint = 0x101010, NearBreachChance = 0.7 },
            });
            foreach (var (x, y, z) in RuinerTestGrids.Cells(g).Where(c => c.Y <= 1))
            {
                Assert.Equal(intact.Get(x, y, z), g.Get(x, y, z));
                Assert.False(g.Mods.ContainsKey(g.Index(x, y, z)), "a protected row was scorched");
            }

            marked += g.Mods.Count(m => m.Value.Tint == 0x101010);
        }

        Assert.True(marked > 0, "no hull was scorched");
    }

    [Fact]
    public void Hull_IsAWreckProfile_NotALandmarkOne()
    {
        Assert.Contains(LandmarkRules.ProfileHull, LandmarkRules.AllProfiles);
        Assert.DoesNotContain(LandmarkRules.ProfileHull, LandmarkRules.Profiles);
        Assert.All(LandmarkRules.Profiles, p => Assert.Contains(p, LandmarkRules.AllProfiles));

        var t = new StructureTemplate { Key = "x", Width = 4, Height = 4, Length = 4, Landmark = true, RuinProfile = "hull" };
        Assert.Equal(LandmarkRules.ProfileMasonry, LandmarkRules.ProfileOf(t));
        Assert.Contains(LandmarkRules.Problems(t), p => p.Contains("wrecks", System.StringComparison.Ordinal));
    }
}
