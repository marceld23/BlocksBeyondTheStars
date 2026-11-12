// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3516: the buried G.D.S. ruins and the SPS labs of generation 38 decay through the shared ruin pass.</summary>
public sealed partial class StructureDecayTests
{
    private static AncientRuinLayout Gds(long seed, int generation, int crossI = 1, int crossJ = 1)
    {
        var kit = Content.KitByKey("ruin_gds_1")!;
        return AncientRuinGenerator.Compose(seed, Content, CityLayoutSpec.FromKit(kit), kit,
            Content.SettlementTemplates.Where(t => t.IsModule).ToList(), new List<string>(), crossI, crossJ,
            terrainGeneration: generation);
    }

    private static ulong Hash(AncientRuinLayout r) => StructureGoldens.AncientRuin(Content, r);

    // ---------------------------------------------------------------- the buried G.D.S. ruin

    [Fact]
    public void GdsRuin_BeforeGeneration38_IsTheOldDecay()
    {
        for (long seed = 1; seed <= 3; seed++)
        {
            ulong golden = Hash(Gds(seed * 7919, 0, 1 + (int)(seed % 2), 1));
            Assert.Equal(golden, Hash(Gds(seed * 7919, Old, 1 + (int)(seed % 2), 1)));
        }
    }

    [Fact]
    public void GdsRuin_OfGeneration38_DecaysThroughTheSharedPass_KeepingItsCratesAndItsEntrance()
    {
        ushort crate = Id("crate"), ressaw = Id("ressaw"), ladder = Id("ladder");
        for (long seed = 1; seed <= 4; seed++)
        {
            int ci = 1 + (int)(seed % 2), cj = 1 + (int)(seed / 2 % 2);
            var old = Gds(seed * 7919, Old, ci, cj);
            var ruin = Gds(seed * 7919, New, ci, cj);
            Assert.Equal(Hash(ruin), Hash(Gds(seed * 7919, New, ci, cj)));
            Assert.NotEqual(Hash(old), Hash(ruin));

            // The crates (gds_cache): at least four, each a crate block on dry floor.
            Assert.True(ruin.Caches.Count >= AncientRuinGenerator.MinCaches, $"seed {seed}: {ruin.Caches.Count} crates");
            foreach (var c in ruin.Caches)
            {
                Assert.Equal(crate, ruin.Get(c.X, c.Y, c.Z));
                ushort below = ruin.Get(c.X, c.Y - 1, c.Z);
                Assert.True(below != ressaw && Content.BlockById(new BlockId(below)) is { Solid: true }, $"seed {seed}: crate {c} not on dry floor");
            }

            // The entrance landing is exactly as composed (the crates topped up around it aside), the tube open.
            var crates = ruin.Caches.Concat(old.Caches).ToHashSet();
            var (sx, sz) = ruin.Shaft;
            foreach (var (x, y, z) in Cells(ruin.Width, ruin.Height, ruin.Length))
            {
                bool landing = Math.Max(Math.Abs(x - sx), Math.Abs(z - sz)) <= AncientRuinGenerator.EntranceClearance;
                if (landing && !crates.Contains(new Vector3i(x, y, z)))
                {
                    Assert.True(old.Get(x, y, z) == ruin.Get(x, y, z), $"seed {seed}: the landing changed at ({x},{y},{z})");
                }
            }

            Assert.All(Enumerable.Range(1, ruin.Height - 1), y => Assert.Equal(ladder, ruin.Get(sx, y, sz)));
            Assert.NotEmpty(ruin.BasinColumns);
            Assert.DoesNotContain(Cells(ruin.Width, ruin.Height, ruin.Length), c => IsFlora(ruin.Get(c.X, c.Y, c.Z)));
            var floating = Floating(ruin.Width, ruin.Height, ruin.Length, ruin.Get);
            Assert.True(floating.Count == 0, $"seed {seed}: {floating.Count} blocks hang in the air, first {floating.FirstOrDefault()}");
        }
    }

    // ---------------------------------------------------------------- the SPS lab

    [Fact]
    public void SpsLab_BeforeGeneration38_IsTheOldHoledBuild()
    {
        foreach (long seed in Seeds)
        {
            ulong golden = Hash(SpsLabGenerator.Generate(seed, "snow", Content));
            Assert.Equal(golden, Hash(SpsLabGenerator.Generate(seed, "snow", Content, Old)));
        }
    }

    [Fact]
    public void SpsLab_OfGeneration38_IsBuiltIntact_ThenWeathersWeakly()
    {
        ushort rusted = Id("rusted_panel"), terminal = Id("factory_terminal"), panel = Id(SpsLabGenerator.PanelBlock);
        string? firstModule = null;
        for (long seed = 0; seed < 64; seed++)
        {
            long s = seed * 7919 + 13;
            var lab = SpsLabGenerator.Generate(s, "snow", Content, New);
            if (seed < 4)
            {
                Assert.Equal(Hash(lab), Hash(SpsLabGenerator.Generate(s, "snow", Content, New)));
                Assert.NotEqual(Hash(SpsLabGenerator.Generate(s, "snow", Content, Old)), Hash(lab));
            }

            // The pads and the doorways stay under the open sky (the placement trusts the list).
            foreach (var (x, z) in SpsLabGenerator.OpenColumns)
            {
                Assert.NotEqual(0, lab.Get(x, 0, z));
                Assert.All(Enumerable.Range(1, lab.Height - 1), y => Assert.True(lab.Get(x, y, z) == 0, $"lab {seed}: open column ({x},{z}) roofed at y {y}"));
            }

            // The lab module stands as built — its terminal, its walls and its WHOLE roof (the cold, airless room the server finds):
            // keep cells, so neither the crumble nor an erosion bite reaches it. The module is seed-independent, so every lab
            // holds the same cells there.
            Assert.Equal(terminal, lab.Get(8, 1, 3));
            string module = LabModuleCells(lab);
            firstModule ??= module;
            Assert.True(firstModule == module, $"lab {seed}: the lab module differs from the first lab's");
            for (int x = SpsLabGenerator.LabModule.MinX; x <= SpsLabGenerator.LabModule.MaxX; x++)
            {
                for (int z = SpsLabGenerator.LabModule.MinZ; z <= SpsLabGenerator.LabModule.MaxZ; z++)
                {
                    Assert.True(lab.Get(x, 5, z) == panel, $"lab {seed}: roof cell ({x},5,{z}) is {lab.Get(x, 5, z)}");
                }
            }

            Assert.Contains(lab.Markers, m => m.Type == "data_terminal" && m.LocalPos == new Vector3i(8, 2, 3));
            Assert.True(lab.Markers.Count(m => m.Type == "sps_cache") >= 2, $"lab {seed}: caches missing");
            Assert.All(lab.Markers, m => Assert.Equal(0, lab.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)));
            Assert.Contains(Cells(lab.Width, lab.Height, lab.Length), c => lab.Get(c.X, c.Y, c.Z) == rusted);
            var floating = Floating(lab.Width, lab.Height, lab.Length, lab.Get);
            Assert.True(floating.Count == 0, $"lab {seed}: {floating.Count} blocks hang in the air, first {floating.FirstOrDefault()}");
        }
    }

    /// <summary>Every cell of the lab module's box (all rows) as one string.</summary>
    private static string LabModuleCells(SettlementStructure lab)
    {
        var sb = new System.Text.StringBuilder();
        var (minX, minZ, maxX, maxZ) = SpsLabGenerator.LabModule;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = 0; y < lab.Height; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    sb.Append(lab.Get(x, y, z)).Append(',');
                }
            }
        }

        return sb.ToString();
    }
}
