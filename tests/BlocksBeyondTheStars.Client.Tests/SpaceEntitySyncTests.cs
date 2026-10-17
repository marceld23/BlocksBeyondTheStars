// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2708: the flight view's stale-view scan — a view whose entity left the snapshot must go, even while the
/// snapshot holds view-less entities (voxel asteroids, debris, hull-backed stations and wrecks).</summary>
public sealed class SpaceEntitySyncTests
{
    [Fact]
    public void ACollectedDrop_IsStale_WhileViewlessRocksFillTheSnapshot()
    {
        var views = new Dictionary<string, object> { ["drop1"] = new object(), ["drone1"] = new object() };
        var seen = new HashSet<string> { "ast1", "ast2", "ast3", "drone1" };
        for (int i = 0; i < 8; i++)
        {
            seen.Add("debris" + i);
        }

        var stale = new List<string> { "left over from the last frame" };
        int count = SpaceEntitySync.CollectStale(views, seen, stale);

        Assert.Equal(1, count);
        Assert.Equal(new[] { "drop1" }, stale);
        Assert.False(views.Count > seen.Count, "the old count rule would have skipped this scan");
    }

    [Fact]
    public void NothingIsStale_WhenEveryViewIsInTheSnapshot()
    {
        var views = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var stale = new List<string>();

        Assert.Equal(0, SpaceEntitySync.CollectStale(views, new HashSet<string> { "a", "b", "rock" }, stale));
        Assert.Empty(stale);
    }

    [Fact]
    public void AnEmptySnapshot_MakesEveryViewStale()
    {
        // Leaving space (no snapshot at all) clears every model.
        var views = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var stale = new List<string>();

        Assert.Equal(2, SpaceEntitySync.CollectStale(views, new HashSet<string>(), stale));
        Assert.Equal(new[] { "a", "b" }, stale.OrderBy(s => s));
    }
}
