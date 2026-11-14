// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3612: <see cref="HullShell"/> — the visible shell of a big hull, kept up to date as its cells change. Built, it sees
/// exactly what <see cref="StationHull.VisibleCells"/> sees (the NPC stations' flown hull); changed cell by cell — mined,
/// glazed, built over — it never misses a cell a fresh shell would show, and it names every cell a change laid open.
/// </summary>
public sealed class HullShellTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A mutable sparse copy of a station: block and packed shape per non-air cell.</summary>
    private sealed class Grid
    {
        public readonly Dictionary<Vector3i, ushort> Blocks = new();
        public readonly Dictionary<Vector3i, int> Shapes = new();

        public static Grid Of(StationStructure s)
        {
            var g = new Grid();
            for (int x = 0; x < s.Width; x++)
                for (int y = 0; y < s.Height; y++)
                    for (int z = 0; z < s.Length; z++)
                    {
                        if (s.Get(x, y, z) != 0)
                        {
                            g.Blocks[new Vector3i(x, y, z)] = s.Get(x, y, z);
                            if (s.GetShape(x, y, z) != 0)
                            {
                                g.Shapes[new Vector3i(x, y, z)] = s.GetShape(x, y, z);
                            }
                        }
                    }

            return g;
        }

        public ushort Get(Vector3i c) => Blocks.TryGetValue(c, out ushort b) ? b : (ushort)0;

        public int Shape(Vector3i c) => Shapes.TryGetValue(c, out int s) ? s : 0;

        public void Set(Vector3i c, ushort block)
        {
            Shapes.Remove(c);
            if (block == 0)
            {
                Blocks.Remove(c);
            }
            else
            {
                Blocks[c] = block;
            }
        }

        public (Vector3i Min, Vector3i Max) Box()
            => (new Vector3i(Blocks.Keys.Min(c => c.X), Blocks.Keys.Min(c => c.Y), Blocks.Keys.Min(c => c.Z)),
                new Vector3i(Blocks.Keys.Max(c => c.X), Blocks.Keys.Max(c => c.Y), Blocks.Keys.Max(c => c.Z)));

        public HullShell Shell()
        {
            var (min, max) = Box();
            return HullShell.Build(min, max, Blocks, Get, Shape, StationHull.SeeThroughIds(Content));
        }
    }

    [Theory]
    [InlineData("small", 3L)]
    [InlineData("medium", 11L)]
    [InlineData("large", 29L)]
    [InlineData("large", 404L)]
    public void Built_ItSeesExactlyWhatTheStationHullSees(string tier, long seed)
    {
        var station = StationGenerator.Generate(tier, seed, Content);
        var grid = Grid.Of(station);
        var shell = grid.Shell();
        var expected = new HashSet<Vector3i>(StationHull.VisibleCells(station, Content));

        var shown = grid.Blocks.Keys.Where(shell.IsVisible).ToHashSet();

        Assert.True(expected.SetEquals(shown),
            $"{tier}/{seed}: {expected.Except(shown).Count()} hull cells missing, {shown.Except(expected).Count()} hidden cells shown");
        Assert.True(shown.Count < grid.Blocks.Count, "a station has cells nobody sees from outside");
    }

    [Theory]
    [InlineData(5L)]
    [InlineData(77L)]
    public void Changed_NeverMissesACellAFreshShellShows_AndNamesWhatItLaysOpen(long seed)
    {
        var grid = Grid.Of(StationGenerator.Generate("large", seed, Content));
        var shell = grid.Shell();
        var rng = new Random((int)seed);
        ushort glass = Content.GetBlock("glass")!.NumericId.Value;
        ushort wall = Content.GetBlock("iron_wall")!.NumericId.Value;
        var shown = grid.Blocks.Keys.Where(shell.IsVisible).ToHashSet();
        int laidOpenTotal = 0;
        for (int step = 0; step < 60; step++)
        {
            var cells = grid.Blocks.Keys.ToList();
            var visible = cells.Where(shell.IsVisible).ToList();
            var cell = (step % 4) switch
            {
                0 or 1 => visible[rng.Next(visible.Count)], // mine into the hull from outside
                2 => cells[rng.Next(cells.Count)], // anywhere (also deep inside)
                _ => visible[rng.Next(visible.Count)], // glaze or wall over a hull cell
            };
            ushort block = (step % 4) switch
            {
                3 => step % 8 == 3 ? glass : wall,
                _ => (ushort)0,
            };

            grid.Set(cell, block);
            var laid = new List<Vector3i>();
            shell.Changed(cell, laid);
            laidOpenTotal += laid.Count;

            Assert.DoesNotContain(cell, laid);
            Assert.All(laid, c => Assert.True(grid.Get(c) != 0 && !shown.Contains(c), $"step {step}: {c} was no new hull cell"));
            shown.UnionWith(laid);
            if (block != 0 && shell.IsVisible(cell))
            {
                shown.Add(cell);
            }

            var fresh = grid.Shell();
            var missing = grid.Blocks.Keys.Where(c => fresh.IsVisible(c) && !shell.IsVisible(c)).ToList();
            Assert.True(missing.Count == 0, $"step {step} ({cell} → {block}): {missing.Count} cells a fresh shell shows are missing, e.g. {missing.FirstOrDefault()}");
        }

        Assert.True(laidOpenTotal > 0, "mining into the hull laid something open");
    }

    [Fact]
    public void AHoleIntoAClosedHall_LaysOpenItsWalls_AndABlockBuiltPastTheBox_IsAlwaysShown()
    {
        // A closed 9 × 9 × 9 box around a closed 5 × 5 × 5 one: only the outer faces show until the roof gets a hole.
        var grid = new Grid();
        for (int x = 0; x < 9; x++)
            for (int y = 0; y < 9; y++)
                for (int z = 0; z < 9; z++)
                {
                    bool outer = x == 0 || x == 8 || y == 0 || y == 8 || z == 0 || z == 8;
                    bool inRange = x >= 2 && x <= 6 && y >= 2 && y <= 6 && z >= 2 && z <= 6;
                    bool innerShell = inRange && (x == 2 || x == 6 || y == 2 || y == 6 || z == 2 || z == 6);
                    if (outer || innerShell)
                    {
                        grid.Set(new Vector3i(x, y, z), 1);
                    }
                }

        var shell = grid.Shell();
        Assert.False(shell.IsVisible(new Vector3i(2, 4, 4)), "the inner box is hidden");

        var hole = new Vector3i(4, 8, 4);
        grid.Set(hole, 0);
        var laid = new List<Vector3i>();
        shell.Changed(hole, laid);

        Assert.Contains(new Vector3i(4, 6, 4), laid); // the inner box's roof below the hole
        Assert.Contains(new Vector3i(2, 4, 4), laid); // its side wall across the gap
        Assert.False(laid.Contains(new Vector3i(4, 4, 4)), "nothing inside the inner box");
        Assert.True(shell.IsVisible(new Vector3i(40, 40, 40)), "a cell past the box is always shown");
    }
}
