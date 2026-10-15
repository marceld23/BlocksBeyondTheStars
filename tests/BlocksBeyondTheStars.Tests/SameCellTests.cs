// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2613 — <see cref="WorldConstants.SameCell"/>: a canonical server cell and a cell read off the unbounded scene are the
/// same cell when they meet the short way round both seams. Lyxette's ice moon (circumference 3232) had his crates at
/// X = C − 3 while the crosshair, on the other side of the home pad at X = 0, read X = −3 — and E never matched.
/// </summary>
public sealed class SameCellTests
{
    private const int MoonCirc = 3232; // Gheyee II-b, the moon of the report

    [Fact]
    public void ACrateAcrossTheSeam_IsTheCellInTheCrosshair()
    {
        Assert.True(WorldConstants.SameCell(MoonCirc - 3, 70, 5, -3, 70, 5, MoonCirc)); // canonical vs west of X = 0
        Assert.True(WorldConstants.SameCell(2, 70, 5, MoonCirc + 2, 70, 5, MoonCirc));  // a lapped player, east of the seam
        Assert.True(WorldConstants.SameCell(17, 70, -4, 17, 70, -4, MoonCirc));         // no seam at all
    }

    [Fact]
    public void TheNeighbouringCells_AreNot()
    {
        Assert.False(WorldConstants.SameCell(MoonCirc - 3, 70, 5, -2, 70, 5, MoonCirc)); // one east
        Assert.False(WorldConstants.SameCell(MoonCirc - 3, 70, 5, -4, 70, 5, MoonCirc)); // one west
        Assert.False(WorldConstants.SameCell(MoonCirc - 3, 70, 5, -3, 71, 5, MoonCirc)); // one up — Y never wraps
        Assert.False(WorldConstants.SameCell(MoonCirc - 3, 70, 5, -3, 70, 6, MoonCirc)); // one north
    }

    [Fact]
    public void TheLatitudeSeam_IsCrossedTheShortWayToo()
    {
        int half = WorldConstants.LatitudePeriodFor(MoonCirc) / 2;
        Assert.True(WorldConstants.SameCell(10, 70, half - 1, 10, 70, -half - 1, MoonCirc));
        Assert.False(WorldConstants.SameCell(10, 70, half - 1, 10, 70, -half, MoonCirc));
    }

    [Fact]
    public void SameCell_AgreesWithCanonicalBlock()
    {
        // Two cells are the same cell exactly when they canonicalize to the same block — for any lap either side.
        for (int lap = -2; lap <= 2; lap++)
        {
            for (int x = -5; x <= 5; x++)
            {
                int scene = x + lap * MoonCirc;
                var a = WorldConstants.CanonicalBlock(new BlocksBeyondTheStars.Shared.Geometry.Vector3i(scene, 64, 3), MoonCirc);
                var b = WorldConstants.CanonicalBlock(new BlocksBeyondTheStars.Shared.Geometry.Vector3i(MoonCirc - 3, 64, 3), MoonCirc);
                Assert.Equal(a.Equals(b), WorldConstants.SameCell(scene, 64, 3, MoonCirc - 3, 64, 3, MoonCirc));
            }
        }
    }
}
