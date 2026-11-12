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
/// The options of the shared ruin pass (#3515): each one changes what it names and nothing else — compared cell by cell
/// with the default pass over the same grid and seed.
/// </summary>
public sealed class StructureRuinerOptionTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly long[] Seeds = { 1, 2, 3, 4, 5, 6 };

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static RuinerTestGrids.Case Tower() => RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Tower());

    private static RuinerTestGrids.Case Soup() => RuinerTestGrids.Soup(Content);

    private static VoxelGrid Ruin(RuinerTestGrids.Case c, long seed, RuinOptions? options, double strength = 0)
    {
        StructureRuiner.Run(c.Grid, Content, seed, LandmarkRules.ProfileMasonry, strength, c.GroundRow, c.Markers, "grass",
            StructureRuiner.Revision, options);
        return c.Grid;
    }

    /// <summary>The cells whose block differs: (cell, block of the default ruin, block of the other).</summary>
    private static List<((int X, int Y, int Z) Cell, ushort A, ushort B)> Diff(VoxelGrid a, VoxelGrid b)
        => RuinerTestGrids.Cells(a).Where(c => a.Get(c.X, c.Y, c.Z) != b.Get(c.X, c.Y, c.Z))
            .Select(c => (c, a.Get(c.X, c.Y, c.Z), b.Get(c.X, c.Y, c.Z))).ToList();

    private static int Count(VoxelGrid g, ushort block) => RuinerTestGrids.Cells(g).Count(c => g.Get(c.X, c.Y, c.Z) == block);

    [Fact]
    public void Overgrowth_Off_TakesTheFloraAndNothingElse()
    {
        int flora = 0;
        foreach (long seed in Seeds)
        {
            var diff = Diff(Ruin(Tower(), seed, null), Ruin(Tower(), seed, new RuinOptions { Overgrowth = false }));
            Assert.All(diff, d => Assert.Equal((Id("flora_fern"), (ushort)0), (d.A, d.B)));
            flora += diff.Count;
        }

        Assert.True(flora > 0, "no flora grew in the default ruins");
    }

    [Fact]
    public void Overgrowth_AFixedFlora_GrowsWhereTheBiomesWould()
    {
        int grown = 0;
        foreach (long seed in Seeds)
        {
            var diff = Diff(Ruin(Tower(), seed, null), Ruin(Tower(), seed, new RuinOptions { OvergrowthBlock = "flora_moss" }));
            Assert.All(diff, d => Assert.Equal((Id("flora_fern"), Id("flora_moss")), (d.A, d.B)));
            grown += diff.Count;
        }

        Assert.True(grown > 0);

        // A key the content does not know is ignored: the biome's flora grows.
        Assert.Empty(Diff(Ruin(Tower(), 1, null), Ruin(Tower(), 1, new RuinOptions { OvergrowthBlock = "no_such_flora" })));
    }

    [Fact]
    public void RubblePalette_TheRubbleComesFromThePaletteOnly()
    {
        var palette = new[] { new RuinWeightedBlock("scrap_pile", 3), new RuinWeightedBlock("rusted_panel", 1), new RuinWeightedBlock("nope", 5) };
        var used = new HashSet<ushort>();
        foreach (long seed in Seeds)
        {
            var c = Tower();
            var diff = Diff(Ruin(Tower(), seed, null), Ruin(c, seed, new RuinOptions { RubblePalette = palette }));
            Assert.All(diff, d =>
            {
                Assert.NotEqual(0, d.A); // the same cells hold rubble, only of another material
                Assert.Contains(d.B, new[] { Id("scrap_pile"), Id("rusted_panel") });
                Assert.InRange(d.Cell.Y, c.GroundRow, c.GroundRow + 3);
            });
            used.UnionWith(diff.Select(d => d.B));
        }

        Assert.Equal(2, used.Count); // both weights were drawn, the unknown key never
    }

    [Fact]
    public void ErodeShaped_LetsShapedPiecesGo_AndChangesNothingWithoutThem()
    {
        int lost = 0;
        foreach (long seed in Seeds)
        {
            var plain = Ruin(Soup(), seed, null);
            var eroded = Ruin(Soup(), seed, new RuinOptions { ErodeShaped = true });
            var shapedPlain = RuinerTestGrids.Cells(plain).Where(c => plain.ShapeAt(c.X, c.Y, c.Z) != 0).ToHashSet();
            var shapedEroded = RuinerTestGrids.Cells(eroded).Where(c => eroded.ShapeAt(c.X, c.Y, c.Z) != 0).ToHashSet();
            Assert.Subset(shapedPlain, shapedEroded);
            lost += shapedPlain.Count - shapedEroded.Count;

            // The tower has no shaped piece: the option changes nothing there.
            var tower = Ruin(Tower(), seed, new RuinOptions { ErodeShaped = true });
            Assert.Equal(RuinerTestGrids.Hash(Content, Ruin(Tower(), seed, null)), RuinerTestGrids.Hash(Content, tower));
        }

        Assert.True(lost > 0, "erosion never took a shaped piece");
    }

    [Fact]
    public void StrengthAt_OneEverywhere_IsTheDefault_AndASparedBoxLosesLess()
    {
        var spare = new RuinOptions { StrengthAt = RuinOptions.SpareBox(0, 0, 7, 15, 0.1) }; // the tower's west half
        int lostSpared = 0, lostOther = 0, lostDefault = 0;
        foreach (long seed in Seeds)
        {
            var intact = Tower().Grid;
            var plain = Ruin(Tower(), seed, null);
            Assert.Equal(RuinerTestGrids.Hash(Content, plain),
                RuinerTestGrids.Hash(Content, Ruin(Tower(), seed, new RuinOptions { StrengthAt = (_, _) => 1.0 })));

            var spared = Ruin(Tower(), seed, spare);
            int Lost(VoxelGrid g, bool west) => RuinerTestGrids.Cells(intact)
                .Count(c => (c.X < 8) == west && c.Y > 3 && intact.Get(c.X, c.Y, c.Z) != 0 && g.Get(c.X, c.Y, c.Z) == 0);
            lostSpared += Lost(spared, west: true);
            lostOther += Lost(spared, west: false);
            lostDefault += Lost(plain, west: true);
        }

        Assert.True(lostSpared * 2 < lostOther, $"the spared half lost {lostSpared}, the other {lostOther}");
        Assert.True(lostSpared * 2 < lostDefault, $"the spared half lost {lostSpared}, without the map {lostDefault}");
    }

    [Fact]
    public void ProtectRows_StayUntouched_AndHoldWhatHangsOnThem()
    {
        const int Rows = 12;
        foreach (long seed in Seeds)
        {
            var intact = Tower().Grid;
            var g = Ruin(Tower(), seed, new RuinOptions { ProtectRowsUpTo = Rows }, strength: 1.0);
            foreach (var (x, y, z) in RuinerTestGrids.Cells(g).Where(c => c.Y <= Rows))
            {
                Assert.Equal(intact.Get(x, y, z), g.Get(x, y, z));
                Assert.Equal(intact.ShapeAt(x, y, z), g.ShapeAt(x, y, z));
            }

            // Nothing hangs in the air: every piece reaches down into the protected rows.
            Assert.All(RuinerTestGrids.Pieces(g), p => Assert.Contains(p, c => c.Y <= Rows));
            Assert.True(RuinerTestGrids.Cells(g).Count(c => c.Y > Rows && g.Solid(c.X, c.Y, c.Z))
                        < RuinerTestGrids.Cells(intact).Count(c => c.Y > Rows && intact.Solid(c.X, c.Y, c.Z)), "nothing decayed above");
        }
    }

    [Fact]
    public void LightOffBlock_IsWhatEveryDeadLightBecomes()
    {
        int dead = 0;
        foreach (long seed in Seeds)
        {
            var g = Ruin(Tower(), seed, new RuinOptions { LightOffBlock = "light_red_off" });
            var diff = Diff(Ruin(Tower(), seed, null), g);
            Assert.All(diff, d => Assert.Equal((Id("torch_off"), Id("light_red_off")), (d.A, d.B)));
            Assert.Equal(0, Count(g, Id("torch_off")));
            dead += diff.Count;
        }

        Assert.True(dead > 0);
    }

    [Fact]
    public void Substitutions_WeatherTheirBlock_WithTheirChance()
    {
        int moss = 0, stone = 0;
        foreach (long seed in Seeds)
        {
            var g = Ruin(Tower(), seed, new RuinOptions { Substitutions = new[] { new RuinSubstitution("stone", "moss_stone", 0.3) } });
            var diff = Diff(Ruin(Tower(), seed, null), g);
            Assert.All(diff, d => Assert.Equal((Id("stone"), Id("moss_stone")), (d.A, d.B)));
            moss += Count(g, Id("moss_stone"));
            stone += Count(g, Id("stone"));
        }

        Assert.InRange(moss / (double)(moss + stone), 0.24, 0.36);

        // Two rules for one block: the first that hits wins, the second takes the rest. An empty target takes the block.
        var both = Ruin(Tower(), 1, new RuinOptions
        {
            Substitutions = new[]
            {
                new RuinSubstitution("stone", "moss_stone", 0.5), new RuinSubstitution("stone", "rusted_panel", 1.0),
                new RuinSubstitution("torch", string.Empty, 1.0),
            },
        });
        Assert.DoesNotContain(RuinerTestGrids.Cells(both), c => c.Y > 3 && both.Get(c.X, c.Y, c.Z) == Id("stone"));
        Assert.True(Count(both, Id("moss_stone")) > 0 && Count(both, Id("rusted_panel")) > 0);
        Assert.Equal(0, Count(both, Id("torch")));
    }

    [Fact]
    public void Scorch_MarksAroundTheBreaches_WithItsChances()
    {
        const int Soot = 0x202020;
        var intact = Tower().Grid;
        int Tinted(VoxelGrid g) => g.Mods.Count(m => m.Value.Tint == Soot);
        foreach (long seed in Seeds)
        {
            // No chance, no mark — and nothing else moves.
            var none = Ruin(Tower(), seed, new RuinOptions { Scorch = new RuinScorch { Tint = Soot } });
            Assert.Equal(RuinerTestGrids.Hash(Content, Ruin(Tower(), seed, null)), RuinerTestGrids.Hash(Content, none));

            // Everywhere: every plain block above the rubble rows is marked (a dead torch too), no glass, light or ladder is.
            var all = Ruin(Tower(), seed, new RuinOptions { Scorch = new RuinScorch { Tint = Soot, NearBreachChance = 1, ElsewhereChance = 1 } });
            foreach (var (x, y, z) in RuinerTestGrids.Cells(all).Where(c => c.Y > 3 && all.Solid(c.X, c.Y, c.Z)))
            {
                bool plain = Array.IndexOf(new[] { Id("glass"), Id("torch"), Id("ladder") }, all.Get(x, y, z)) < 0;
                Assert.Equal(plain, all.Mods.TryGetValue(all.Index(x, y, z), out var m) && m.Tint == Soot);
            }

            // Near the breaches only: fewer marks, each next to a cell the decay took out.
            var near = Ruin(Tower(), seed, new RuinOptions { Scorch = new RuinScorch { Tint = Soot, NearBreachChance = 1 } });
            Assert.InRange(Tinted(near), 1, Tinted(all) - 1);
            foreach (var (x, y, z) in RuinerTestGrids.Cells(near).Where(c => c.Y >= 5 && near.Mods.TryGetValue(near.Index(c.X, c.Y, c.Z), out var m) && m.Tint == Soot))
            {
                Assert.Contains(Around(x, y, z), n => intact.Solid(n.X, n.Y, n.Z) && !near.Solid(n.X, n.Y, n.Z));
            }

            // A scorch block replaces the block instead.
            var scrap = Ruin(Tower(), seed, new RuinOptions { Scorch = new RuinScorch { Block = "scrap_metal", NearBreachChance = 0.5 } });
            Assert.True(Count(scrap, Id("scrap_metal")) > 0);
        }
    }

    [Fact]
    public void MarkerRules_AreShared_KeepOnlyFilters_AndSeatLeavesAStrayCellAlone()
    {
        Assert.Same(RuinMarkerRules.Kept, LandmarkGenerator.RuinMarkers);
        var markers = new List<SettlementMarker>
        {
            new(LandmarkRules.ChestMarker, new Vector3i(1, 1, 1)), new("npc", new Vector3i(2, 1, 1)), new("gds_cache", new Vector3i(3, 1, 1)),
        };
        var own = new List<SettlementMarker>(markers);
        RuinMarkerRules.KeepOnly(markers);
        Assert.Equal(new[] { LandmarkRules.ChestMarker }, markers.Select(m => m.Type));
        RuinMarkerRules.KeepOnly(own, new[] { "gds_cache" });
        Assert.Equal(new[] { "gds_cache" }, own.Select(m => m.Type));

        var g = new VoxelGrid(4, 6, 4);
        g.Set(1, 0, 1, Id("stone"));
        Assert.Equal(new Vector3i(1, 1, 1), RuinMarkerRules.Seat(g, new Vector3i(1, 4, 1), groundRow: 0)); // drops onto the stone
        g.Set(2, 0, 2, Id("stone"));
        g.Set(2, 1, 2, Id("stone"));
        g.Set(2, 2, 2, Id("stone"));
        Assert.Equal(new Vector3i(2, 1, 2), RuinMarkerRules.Seat(g, new Vector3i(2, 1, 2), groundRow: 0)); // stays, its cell and head room freed
        Assert.Equal(((ushort)0, (ushort)0), (g.Get(2, 1, 2), g.Get(2, 2, 2)));
        Assert.Equal(new Vector3i(9, 9, 9), RuinMarkerRules.Seat(g, new Vector3i(9, 9, 9), groundRow: 0));

        g.KeepBox(-3, 0, 0, 1, 1, 0);
        g.KeepWhere((_, y, _, b) => b != 0 && y == 0);
        Assert.Equal(new[] { (0, 0, 0), (0, 1, 0), (1, 0, 0), (1, 0, 1), (1, 1, 0), (2, 0, 2) },
            RuinerTestGrids.Cells(g).Where(c => g.Keep[g.Index(c.X, c.Y, c.Z)]).OrderBy(c => c));
    }

    private static IEnumerable<(int X, int Y, int Z)> Around(int x, int y, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    yield return (x + dx, y + dy, z + dz);
                }
            }
        }
    }
}
