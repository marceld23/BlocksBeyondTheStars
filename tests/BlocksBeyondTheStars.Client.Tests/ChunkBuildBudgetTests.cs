// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#4045: the desktop client meshes by the machine's cores, with a time budget for the main thread's share.</summary>
public sealed class ChunkBuildBudgetTests
{
    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(4, 4)]
    [InlineData(8, 4)]
    [InlineData(10, 6)]
    [InlineData(12, 8)]
    [InlineData(16, 8)]
    [InlineData(24, 8)]
    [InlineData(64, 8)]
    public void InFlight_FollowsTheLogicalProcessors_BetweenTheOldFourAndTheCap(int logicalProcessors, int expected)
        => Assert.Equal(expected, ChunkBuildBudget.InFlight(logicalProcessors));

    [Theory]
    [InlineData(2, 4)]
    [InlineData(8, 4)]
    [InlineData(12, 8)]
    [InlineData(14, 10)]
    [InlineData(16, 12)]
    [InlineData(32, 12)]
    public void InFlightWhileLoading_UsesMoreOfALargeMachine(int logicalProcessors, int expected)
        => Assert.Equal(expected, ChunkBuildBudget.InFlightWhileLoading(logicalProcessors));

    [Fact]
    public void InFlight_StaysInsideItsBounds_AndNeverShrinksWithMoreProcessors()
    {
        int last = 0, lastLoading = 0;
        for (int cores = -4; cores <= 256; cores++)
        {
            int cap = ChunkBuildBudget.InFlight(cores), loading = ChunkBuildBudget.InFlightWhileLoading(cores);
            Assert.InRange(cap, ChunkBuildBudget.MinInFlight, ChunkBuildBudget.MaxInFlight);
            Assert.InRange(loading, ChunkBuildBudget.MinInFlight, ChunkBuildBudget.MaxInFlightLoading);
            Assert.True(loading >= cap, $"{cores} logical processors: {loading} while loading, {cap} in play");
            Assert.True(cap >= last && loading >= lastLoading, $"{cores} logical processors gave fewer than {cores - 1}");
            last = cap;
            lastLoading = loading;
        }
    }

    [Fact]
    public void MayDispatch_StopsAtTheCap_WhateverTheTime()
    {
        Assert.True(ChunkBuildBudget.MayDispatch(inFlight: 3, cap: 4, startedThisFrame: 0, elapsedMs: 0, budgetMs: 1.5));
        Assert.False(ChunkBuildBudget.MayDispatch(inFlight: 4, cap: 4, startedThisFrame: 0, elapsedMs: 0, budgetMs: 1.5));
        Assert.False(ChunkBuildBudget.MayDispatch(inFlight: 9, cap: 4, startedThisFrame: 0, elapsedMs: 0, budgetMs: 1.5));
    }

    [Fact]
    public void MayDispatch_AlwaysStartsTheFirstOfAFrame_ThenKeepsToTheBudget()
    {
        // The first of the frame runs even with the budget long spent — the pipeline moves on every frame.
        Assert.True(ChunkBuildBudget.MayDispatch(inFlight: 0, cap: 8, startedThisFrame: 0, elapsedMs: 50, budgetMs: 1.5));

        Assert.True(ChunkBuildBudget.MayDispatch(inFlight: 1, cap: 8, startedThisFrame: 1, elapsedMs: 1.49, budgetMs: 1.5));
        Assert.False(ChunkBuildBudget.MayDispatch(inFlight: 1, cap: 8, startedThisFrame: 1, elapsedMs: 1.5, budgetMs: 1.5));
    }

    [Fact]
    public void MayUpload_AlwaysTakesTheFirstOfAFrame_ThenKeepsToTheBudget()
    {
        Assert.True(ChunkBuildBudget.MayUpload(uploadedThisFrame: 0, elapsedMs: 50, budgetMs: 3));
        Assert.True(ChunkBuildBudget.MayUpload(uploadedThisFrame: 3, elapsedMs: 2.99, budgetMs: 3));
        Assert.False(ChunkBuildBudget.MayUpload(uploadedThisFrame: 3, elapsedMs: 3, budgetMs: 3));
    }

    [Theory]
    [InlineData(4.0, 3.0)]    // 250 fps: the floor
    [InlineData(16.7, 3.0)]   // 60 fps: still the floor
    [InlineData(33.3, 4.995)] // 30 fps: 15 % of the frame
    [InlineData(50.0, 7.5)]   // 20 fps
    [InlineData(100.0, 8.0)]  // a hitch: the ceiling
    [InlineData(5000.0, 8.0)] // a pause: the ceiling
    [InlineData(0.0, 3.0)]
    [InlineData(-1.0, 3.0)]
    public void UploadMs_InPlay_GrowsWithTheFrameTime_BetweenItsBounds(double lastFrameMs, double expected)
        => Assert.Equal(expected, ChunkBuildBudget.UploadMs(lastFrameMs, loading: false), 3);

    [Theory]
    [InlineData(4.0)]
    [InlineData(33.3)]
    [InlineData(5000.0)]
    public void Budgets_BehindTheLoadingVeil_AreWide_WhateverTheFrameTime(double lastFrameMs)
    {
        Assert.Equal(ChunkBuildBudget.LoadingUploadMs, ChunkBuildBudget.UploadMs(lastFrameMs, loading: true));
        Assert.Equal(ChunkBuildBudget.LoadingDispatchMs, ChunkBuildBudget.DispatchBudgetMs(loading: true));
        Assert.Equal(ChunkBuildBudget.DispatchMs, ChunkBuildBudget.DispatchBudgetMs(loading: false));
        Assert.True(ChunkBuildBudget.LoadingUploadMs > ChunkBuildBudget.MaxUploadMs);
        Assert.True(ChunkBuildBudget.LoadingDispatchMs > ChunkBuildBudget.DispatchMs);
    }
}
