// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.WorldGeneration.Sources;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3645: the maze ruin's goldens — every cell (block by key), the kept cells and every marker of a maze per size, over two
/// seeds, plus one maze with every parameter turned. A maze stands in somebody's world once the Labyrinth Builders are
/// stamped (terrain generation 43), so a moved pin is a moved world: change the source's output only behind a generation
/// gate, and re-pin only before the first release that stamps it.
/// </summary>
public sealed partial class MazeRuinSourceTests
{
    private static readonly Dictionary<string, string> MazePins = new()
    {
        ["maze_ruin:S:1"] = "77f4564012f23388",
        ["maze_ruin:S:20261114"] = "4ad12a9bdd5f59b3",
        ["maze_ruin:M:1"] = "b5090c57b5414ec9",
        ["maze_ruin:M:20261114"] = "9aa127052c767800",
        ["maze_ruin:L:1"] = "fd75c1cb03d30203",
        ["maze_ruin:L:20261114"] = "92d2a1deb4f20589",
        ["maze_ruin:S:turned"] = "b943154eb2eca9cd",
    };

    [Fact]
    public void Goldens_PerSize_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        foreach (string size in new[] { "S", "M", "L" })
        {
            foreach (long seed in Seeds.Take(2))
            {
                cases.Add(($"maze_ruin:{size}:{seed.ToString(CultureInfo.InvariantCulture)}", Fingerprint(Build(seed, ("size", size)))));
            }
        }

        var turned = Build(Seeds[2], ("size", "S"), ("height", "9"), ("braid", "60"), ("halls", "0"), ("statues", "100"),
            ("wall", "stone"), ("accent", "gold_block"), ("floor", "sand"));
        cases.Add(("maze_ruin:S:turned", Fingerprint(turned)));
        StructureGoldens.AssertPinned("maze ruins", MazePins, cases);
    }

    private static ulong Fingerprint(BuiltStructure b)
    {
        var h = new StructureGoldenHash(Content);
        h.Add(b.GroundRow).Grid(b.Width, b.Height, b.Length, b.Get, b.GetModifier, b.GetShape);
        h.Add(b.Keep.Count);
        foreach (int i in b.Keep.OrderBy(i => i))
        {
            h.Add(i);
        }

        h.Add(b.Markers.Count);
        foreach (var m in b.Markers)
        {
            h.Add(m.Type).Add(m.X).Add(m.Y).Add(m.Z).Add(m.Data);
        }

        return h.Value;
    }
}
