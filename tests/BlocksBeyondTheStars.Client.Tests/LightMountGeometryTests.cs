// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2683: the cards a torch or lantern is drawn from — by the chunk mesher and the placement ghost alike. A wall torch's
/// foot touches its wall and the torch leans away from it, a wall lantern hangs beside its wall on a bracket, a hanging
/// lantern's chain reaches the ceiling, and a standing light is exactly the cross-billboard the mesher has always drawn.
/// </summary>
public sealed class LightMountGeometryTests
{
    private static IEnumerable<Vector3> Corners(IEnumerable<LightMountGeometry.Card> cards)
        => cards.SelectMany(c => new[] { c.A, c.B, c.C, c.D });

    /// <summary>How far a point lies out from the wall of a wall mount (the wall face is at 0, the far side of the cell at 1).</summary>
    private static float FromWall(Vector3 p, int mount)
    {
        var (x, _, z) = ShapeCode.FaceDirection(mount);
        return ((p.X - 0.5f) * x) + ((p.Z - 0.5f) * z) + 0.5f;
    }

    /// <summary>The coordinate along a wall mount's wall (0..1 across the cell).</summary>
    private static float AlongWall(Vector3 p, int mount)
    {
        var (x, _, z) = ShapeCode.FaceDirection(mount);
        return x != 0 ? p.Z : p.X;
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)] // a torch never hangs: a forged ceiling mount stands
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    public void AStandingLight_IsTheCrossBillboard(bool lantern, int mount)
    {
        var cards = LightMountGeometry.Build(lantern, mount);
        float height = lantern ? LightMountGeometry.LanternHeight : LightMountGeometry.TorchHeight;

        Assert.Equal(3, cards.Count);
        foreach (var card in cards)
        {
            Assert.Equal(0f, card.A.Y, 4);
            Assert.Equal(0f, card.B.Y, 4);
            Assert.Equal(height, card.C.Y, 4);
            Assert.Equal(height, card.D.Y, 4);
            var mid = (card.A + card.B) * 0.5f;
            Assert.Equal(0.5f, mid.X, 4); // centred in the cell, like the cross-billboard
            Assert.Equal(0.5f, mid.Z, 4);
            Assert.Equal(LightMountGeometry.StandingHalfWidth * 2f, Vector3.Distance(card.A, card.B), 4);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AWallTorch_TouchesItsWall_AndLeansAwayFromIt(int mount)
    {
        var cards = LightMountGeometry.Build(lantern: false, mount);
        var rosette = cards.Take(3).ToList();

        // The plane along the wall: its foot sits right at the wall, its top well out in front of it.
        var front = rosette[0];
        float foot = FromWall((front.A + front.B) * 0.5f, mount);
        float top = FromWall((front.C + front.D) * 0.5f, mount);
        Assert.InRange(foot, 0f, 0.1f);
        Assert.True(top - foot > 0.2f, $"the torch must lean away from its wall (foot {foot}, top {top})");

        // A little shorter than the standing torch, and nothing leaves the cell — except the clear rim of the two side
        // planes, which dips a few hundredths into the wall behind (the stick in their middle stays in front of it).
        Assert.True(Corners(rosette).Max(p => p.Y) < LightMountGeometry.TorchHeight + 0.1f);
        foreach (var p in Corners(cards))
        {
            Assert.InRange(p.Y, 0f, 1f);
            Assert.InRange(AlongWall(p, mount), 0f, 1f);
            Assert.InRange(FromWall(p, mount), -0.08f, 1f);
        }

        // The holder: a plate flat on the wall.
        Assert.Contains(cards.Skip(3), c => new[] { c.A, c.B, c.C, c.D }.All(p => Math.Abs(FromWall(p, mount) - LightMountGeometry.PlateStandOff) < 1e-4f));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AWallLantern_HangsBesideItsWall_OnABracket(int mount)
    {
        var cards = LightMountGeometry.Build(lantern: true, mount);
        foreach (var p in Corners(cards))
        {
            Assert.InRange(p.Y, 0f, 1f);
            Assert.InRange(AlongWall(p, mount), 0f, 1f);
            Assert.InRange(FromWall(p, mount), 0f, 1f); // the lantern is wide: it keeps clear of the wall entirely
        }

        var body = cards.Take(3).ToList();
        float axis = FromWall((body[0].A + body[0].B) * 0.5f, mount);
        Assert.True(axis < 0.3f, $"the lantern hangs close to its wall, not mid-cell (axis at {axis})");
        Assert.Contains(cards.Skip(3), c => new[] { c.A, c.B, c.C, c.D }.All(p => Math.Abs(FromWall(p, mount) - LightMountGeometry.PlateStandOff) < 1e-4f));
    }

    [Fact]
    public void AHangingLantern_HangsOnAChainFromTheCeiling()
    {
        var cards = LightMountGeometry.Build(lantern: true, 1);
        foreach (var p in Corners(cards))
        {
            Assert.InRange(p.X, 0f, 1f);
            Assert.InRange(p.Y, 0f, 1.0001f); // the chain ends at the ceiling (float rounding aside)
            Assert.InRange(p.Z, 0f, 1f);
        }

        var body = cards.Take(3).ToList();
        Assert.True(body.Min(c => c.A.Y) >= 0.25f, "the lantern hangs near the top of its cell");
        Assert.True(Corners(cards).Max(p => p.Y) > 0.99f, "the chain reaches the ceiling");
    }

    [Fact]
    public void TheCardsAreBuiltOnce()
    {
        Assert.Same(LightMountGeometry.Build(true, 3), LightMountGeometry.Build(true, 3));
        Assert.Same(LightMountGeometry.Build(false, -1), LightMountGeometry.Build(false, 99)); // out of range reads as unstamped
    }
}
