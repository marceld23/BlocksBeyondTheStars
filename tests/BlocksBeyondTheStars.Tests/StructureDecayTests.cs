// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3516: from terrain generation 38 the fallen settlements, the monuments, the buried G.D.S. ruins and the SPS labs decay
/// through the shared ruin pass. Per pass: generation 37 is still the old decay (the goldens of <c>StructureGoldens*</c> pin the
/// default, which is the old decay — these tests pin generation 37 to it), generation 38 is different, deterministic, keeps
/// its markers and leaves nothing hanging in the air. This file: the helpers and the fallen settlements; the other passes in
/// the partial files.
/// </summary>
public sealed partial class StructureDecayTests
{
    private const int Old = WorldDescription.LandmarkGeneration; // 37
    private const int New = WorldDescription.StructurePipelineGeneration; // 38

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly long[] Seeds = { 1, 20261112, 0x5DEECE66DL };

    private static readonly string[] Tiers = { "hamlet", "village", "town", "city" };

    private static ulong Hash(SettlementStructure s) => StructureGoldens.Settlement(Content, s);

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static bool IsFlora(ushort id) => id != 0 && Content.BlockById(new BlockId(id)) is { Category: "flora" };

    private static IEnumerable<(int X, int Y, int Z)> Cells(int w, int h, int l)
    {
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    yield return (x, y, z);
                }
    }

    /// <summary>The blocks that hang on nothing: not reached from a block in rows 0 … <paramref name="anchorRows"/> through
    /// touching blocks (faces and edges — the ruin pass's own rule).</summary>
    private static List<(int X, int Y, int Z)> Floating(int w, int h, int l, Func<int, int, int, ushort> get, int anchorRows = 0)
    {
        var held = new HashSet<(int, int, int)>();
        var open = new Stack<(int X, int Y, int Z)>();
        foreach (var c in Cells(w, h, l))
        {
            if (c.Y <= anchorRows && get(c.X, c.Y, c.Z) != 0 && held.Add(c))
            {
                open.Push(c);
            }
        }

        while (open.Count > 0)
        {
            var (x, y, z) = open.Pop();
            foreach (var n in Touching(x, y, z))
            {
                if (n.X >= 0 && n.Y >= 0 && n.Z >= 0 && n.X < w && n.Y < h && n.Z < l && get(n.X, n.Y, n.Z) != 0 && held.Add(n))
                {
                    open.Push(n);
                }
            }
        }

        return Cells(w, h, l).Where(c => get(c.X, c.Y, c.Z) != 0 && !held.Contains(c)).ToList();
    }

    private static IEnumerable<(int X, int Y, int Z)> Touching(int x, int y, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int axes = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                    if (axes is 1 or 2)
                    {
                        yield return (x + dx, y + dy, z + dz);
                    }
                }
    }

    private static List<(string Type, int X, int Y, int Z)> MarkerList(SettlementStructure s)
        => s.Markers.Select(m => (m.Type, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)).ToList();

    // ---------------------------------------------------------------- fallen settlements

    [Fact]
    public void FallenSettlement_BeforeGeneration38_IsTheOldDecay()
    {
        foreach (string tier in Tiers)
        {
            foreach (long seed in Seeds)
            {
                ulong golden = Hash(SettlementGenerator.Generate(tier, true, seed, "grass", Content));
                Assert.Equal(golden, Hash(SettlementGenerator.Generate(tier, true, seed, "grass", Content, terrainGeneration: Old)));
                Assert.Equal(golden, Hash(SettlementGenerator.Generate(tier, true, seed, "grass", Content, terrainGeneration: 0)));
            }
        }
    }

    [Fact]
    public void FallenSettlement_OfGeneration38_DecaysThroughTheSharedPass_KeepingItsLoot()
    {
        int flora = 0;
        foreach (string tier in Tiers)
        {
            foreach (long seed in Seeds)
            {
                var old = SettlementGenerator.Generate(tier, true, seed, "grass", Content, terrainGeneration: Old);
                var ruin = SettlementGenerator.Generate(tier, true, seed, "grass", Content, terrainGeneration: New);
                string at = $"{tier} {seed}";
                Assert.Equal(Hash(ruin), Hash(SettlementGenerator.Generate(tier, true, seed, "grass", Content, terrainGeneration: New)));
                Assert.NotEqual(Hash(old), Hash(ruin));
                Assert.True(ruin.Ruined, at);

                // The layout up to the decay is the old one: the same loot markers, each with its cell and the one above free.
                Assert.Equal(MarkerList(old), MarkerList(ruin));
                Assert.NotEmpty(ruin.Markers);
                Assert.All(ruin.Markers, m =>
                {
                    Assert.Equal("loot", m.Type);
                    Assert.Equal(0, ruin.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z));
                    Assert.Equal(0, ruin.Get(m.LocalPos.X, m.LocalPos.Y + 1, m.LocalPos.Z));
                });

                Assert.Empty(Floating(ruin.Width, ruin.Height, ruin.Length, ruin.Get));
                flora += Cells(ruin.Width, ruin.Height, ruin.Length).Count(c => IsFlora(ruin.Get(c.X, c.Y, c.Z)));

                // Height-graded like the old decay: the lower half keeps more standing than the upper half.
                int half = ruin.Height / 2;
                var standing = Cells(ruin.Width, ruin.Height, ruin.Length).Where(c => ruin.Get(c.X, c.Y, c.Z) != 0).ToList();
                Assert.True(standing.Count(c => c.Y < half) > standing.Count(c => c.Y >= half), $"{at}: the upper half outlasted the lower");
            }
        }

        Assert.True(flora > 0, "the biome's flora reclaims the ground of a fallen settlement");
    }

    [Fact]
    public void InhabitedSettlement_IsTheSameOnEveryGeneration()
    {
        foreach (string tier in Tiers)
        {
            ulong golden = Hash(SettlementGenerator.Generate(tier, false, Seeds[1], "sand", Content));
            Assert.Equal(golden, Hash(SettlementGenerator.Generate(tier, false, Seeds[1], "sand", Content, terrainGeneration: New)));
        }
    }
}
