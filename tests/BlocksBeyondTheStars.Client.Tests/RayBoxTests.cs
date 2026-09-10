// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The ray-versus-box rule the door aim (#1746) ranks its hits with.</summary>
public sealed class RayBoxTests
{
    private static float Unit(float ox, float oy, float oz, float dx, float dy, float dz)
        => RayBox.Entry(ox, oy, oz, dx, dy, dz, 0f, 0f, 0f, 1f, 1f, 1f);

    [Fact]
    public void ARayAimedAtTheBox_EntersAtItsNearFace()
    {
        Assert.Equal(3f, Unit(-3f, 0.5f, 0.5f, 1f, 0f, 0f), 4);
    }

    [Fact]
    public void ARayThatPassesBeside_Misses()
    {
        Assert.Equal(float.PositiveInfinity, Unit(-3f, 2.5f, 0.5f, 1f, 0f, 0f));
    }

    [Fact]
    public void ABoxBehindTheOrigin_Misses()
    {
        Assert.Equal(float.PositiveInfinity, Unit(3f, 0.5f, 0.5f, 1f, 0f, 0f));
    }

    [Fact]
    public void AnOriginInsideTheBox_EntersAtZero()
    {
        Assert.Equal(0f, Unit(0.5f, 0.5f, 0.5f, 0f, 1f, 0f));
    }

    [Fact]
    public void ADiagonalRay_EntersWhereItCrossesTheFirstFace()
    {
        // From (-1,-1,0.5) toward +x+y: x reaches 0 at t = 1 along each axis, so the entry is at t = 1.
        Assert.Equal(1f, Unit(-1f, -1f, 0.5f, 1f, 1f, 0f), 4);
    }
}
