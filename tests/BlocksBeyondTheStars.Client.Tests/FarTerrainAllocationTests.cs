// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4052: sampling the far terrain must not allocate once a world is warm. The client samples a column for every drawn
/// chunk on its main thread (the far view's column mask) and thousands of columns per far patch; a profiler capture of
/// 2026-11-26 showed 28 000 allocations a second from one closure the world generator built on every calibration
/// lookup, hit or miss.
/// </summary>
public sealed class FarTerrainAllocationTests
{
    private const int Columns = 64;
    private const int Passes = 40;

    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData("rocky", false)]
    [InlineData("rocky", true)]
    [InlineData("jungle", false)]
    public void Sample_OfAWarmWorld_AllocatesNothing(string planetType, bool detail)
    {
        var info = new FarTerrainWorldInfo { WorldId = 1, LocationId = "sys0-p1", PlanetType = planetType, Circumference = 6000 };
        var source = FarTerrainSource.Create(LoadContent(), 424242, info)!;
        Span<FarFloat> floats = stackalloc FarFloat[FarTerrainSource.MaxFloats];

        // Warm: the world's calibration and profile, the column memos, and the JIT's own first-call work.
        for (int pass = 0; pass < 4; pass++)
        {
            SampleRow(source, detail, floats);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < Passes; pass++)
        {
            SampleRow(source, detail, floats);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2048,
            $"{allocated} bytes for {Passes * Columns} samples of a warm world ({allocated / (double)(Passes * Columns):0.0} per sample)");
    }

    private static void SampleRow(FarTerrainSource source, bool detail, Span<FarFloat> floats)
    {
        for (int i = 0; i < Columns; i++)
        {
            source.Sample(100 + (i * 16), 200, detail, floats, out _);
        }
    }
}
