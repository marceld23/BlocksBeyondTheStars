// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// No settlement ever sits on a landing pad, over 30 jungle worlds. The slow sweep of the settlement overhaul, in a
/// class of its own so the shard packer can place it away from <see cref="SettlementRuinSkewTests"/>, and run on two
/// workers — it is 30 independent world boots (#2456).
/// </summary>
public sealed class SettlementPadClearanceTests : IDisposable
{
    private readonly SettlementTestWorlds _worlds = new();

    [Fact]
    [Trait("Category", "Slow")]
    public void Settlements_NeverSitOnALandingPad()
    {
        Parallel.Invoke(() => CheckSeeds(1, 15), () => CheckSeeds(16, 30));
    }

    private void CheckSeeds(long first, long last)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir()); // one per worker: nothing shared between the halves
        for (long seed = first; seed <= last; seed++)
        {
            var server = _worlds.Start(content, "jungle", seed, out var repo);
            using (repo)
            {
                if (!server.HasSettlement)
                {
                    continue;
                }

                int circ = server.World.Circumference;
                foreach (var s in server.SettlementsForTest)
                {
                    foreach (var pad in server.LandingPadCenters)
                    {
                        int dx = WorldConstants.WrapDeltaX(pad.X - s.MinX, circ);
                        bool insideX = dx >= 0 && dx <= s.MaxX - s.MinX;
                        bool insideZ = pad.Z >= s.MinZ && pad.Z <= s.MaxZ;
                        Assert.False(insideX && insideZ,
                            $"seed {seed}: a landing pad ({pad.X},{pad.Z}) fell inside a settlement footprint.");
                    }
                }
            }
        }
    }

    public void Dispose() => _worlds.Dispose();
}
