// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Crashed ship wrecks: a known ship design's hull built from blocks, then decayed (breaches +
/// scorch) so it's explorable for loot. The intact hull is kept as a repair mask so the wreck can
/// be restored block-by-block. Deterministic from the seed.
/// </summary>
public sealed class WreckGenerationTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Generation_IsDeterministic_ForSameSeed()
    {
        var c = Content();
        var design = c.GetShip("hauler")!;
        var a = WreckGenerator.Generate(design, 4242, c);
        var b = WreckGenerator.Generate(design, 4242, c);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Origin, b.Origin);
        Assert.Equal(a.BreachCount(), b.BreachCount());
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                }
    }

    [Fact]
    public void Wreck_IsDecayed_FewerBlocksThanIntactHull()
    {
        var c = Content();
        var w = WreckGenerator.Generate(c.GetShip("starter")!, 1, c);

        int intact = w.IntactHullCount();
        Assert.True(intact > 0);
        Assert.True(w.BreachCount() > 0, "A wreck must have breaches (missing hull) to be a wreck.");
        Assert.True(w.BreachCount() < intact, "A wreck should not be entirely gone.");
    }

    [Fact]
    public void RepairMask_MarksMissingHullAsBreaches()
    {
        var c = Content();
        var w = WreckGenerator.Generate(c.GetShip("starter")!, 3, c);

        // Every breach is a cell the intact hull fills but the current blocks don't.
        for (int x = 0; x < w.Width; x++)
            for (int y = 0; y < w.Height; y++)
                for (int z = 0; z < w.Length; z++)
                {
                    if (w.IsBreach(x, y, z))
                    {
                        Assert.NotEqual((ushort)0, w.IntactAt(x, y, z));
                        Assert.Equal((ushort)0, w.Get(x, y, z));
                    }
                }
    }

    [Fact]
    public void Wreck_HasLootAndModuleMarkers_NoLiveServices()
    {
        var c = Content();
        var w = WreckGenerator.Generate(c.GetShip("hauler")!, 9, c);

        Assert.Contains(w.Markers, m => m.Type == "loot");
        Assert.Contains(w.Markers, m => m.Type == "module"); // a recoverable module to salvage
        // No vendors or mission boards — wrecks are derelict.
        Assert.DoesNotContain(w.Markers, m => m.Type == "vendor" || m.Type == "mission_board");
    }

    [Fact]
    public void Origin_IsHumanOrAlien()
    {
        var c = Content();
        var w = WreckGenerator.Generate(c.GetShip("scout")!, 7, c);
        Assert.Contains(w.Origin, new[] { "human", "alien" });
    }

    [Fact]
    public void Wreck_IsEnterable_HasAGashInAWall()
    {
        var c = Content();
        var w = WreckGenerator.Generate(c.GetShip("starter")!, 5, c);
        ushort air = 0;

        // The -Z wall (z = 0) must have an opening near the centre (the crash gash).
        bool open = false;
        for (int x = 0; x < w.Width && !open; x++)
            for (int y = 1; y < w.Height; y++)
            {
                if (w.IntactAt(x, y, 0) != 0 && w.Get(x, y, 0) == air) { open = true; break; }
            }

        Assert.True(open, "A wreck should be open enough to enter.");
    }
}
