// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3420 (Justus: "ghost asteroids" — the ship drifted into and through rocks): the flown ship stops at a rock's
/// surface with the part of its move that pointed into the rock taken away, slides along it, leaves it freely, cannot
/// tunnel through a small rock in one long frame, and a ship that is stuck inside a rock is eased out gently.</summary>
public sealed class RockKeepOutTests
{
    private const float R = RockKeepOut.ShipRadius;

    [Fact]
    public void TheShipRadius_IsTheSharedNumberTheServerRamsBy()
    {
        Assert.Equal(BlocksBeyondTheStars.Shared.Definitions.SpaceCombatRules.ShipRockRadius, RockKeepOut.ShipRadius);
        Assert.Equal(2.5f, RockKeepOut.ShipRadius); // the station hull's radius (#1917): the ship meets both alike
    }

    [Fact]
    public void FlyingStraightIntoARock_StopsAtItsSurface()
    {
        var wall = Wall(); // fills x 0..1
        var to = RockKeepOut.Resolve(wall, WallCentre, new Vector3(-6f, 0.5f, 0.5f), new Vector3(-1f, 0.5f, 0.5f), 0f);

        Assert.Equal(-R, to.X, 3); // held at the ship's radius from the face — never inside
        Assert.Equal(0.5f, to.Y, 4);
        Assert.Equal(0.5f, to.Z, 4);
    }

    [Fact]
    public void AnAngledMoveIntoTheRock_LosesOnlyItsInwardPart_AndSlidesAlong()
    {
        var wall = Wall();
        var from = new Vector3(-R, 0.5f, 0.5f); // resting at the surface
        var to = RockKeepOut.Resolve(wall, WallCentre, from, from + new Vector3(0.8f, 0f, 0.6f), 0f);

        Assert.Equal(-R, to.X, 3);     // the inward part is gone…
        Assert.Equal(1.1f, to.Z, 3);   // …the part along the surface is kept: the ship slides
        Assert.Equal(0.5f, to.Y, 4);
    }

    [Fact]
    public void FlyingAwayFromTheRock_IsNeverHeldBack()
    {
        var wall = Wall();
        var from = new Vector3(-R, 0.5f, 0.5f);
        var away = new Vector3(-R - 0.7f, 0.9f, -0.2f);

        Assert.Equal(away, RockKeepOut.Resolve(wall, WallCentre, from, away, 0.1f));
    }

    [Fact]
    public void FarFromAnyRock_TheMoveIsUntouched()
    {
        var rock = Ball(3);
        var from = new Vector3(-30f, 4f, 2f);
        var to = new Vector3(-28f, 4.5f, 2.2f);

        Assert.Equal(to, RockKeepOut.Resolve(rock, BallCentre, from, to, 0.1f));
    }

    [Fact]
    public void HoldingThrustIntoTheRock_RestsStill_NoJitter()
    {
        var wall = Wall();
        var p = new Vector3(-6f, 0.5f, 0.5f);
        var push = new Vector3(0.25f, 0f, 0f); // ~14 u/s at 60 fps, straight in
        for (int frame = 0; frame < 40; frame++)
        {
            p = RockKeepOut.Resolve(wall, WallCentre, p, p + push, RockKeepOut.EaseOutSpeed / 60f);
        }

        var rest = p;
        for (int frame = 0; frame < 20; frame++)
        {
            p = RockKeepOut.Resolve(wall, WallCentre, p, p + push, RockKeepOut.EaseOutSpeed / 60f);
            Assert.Equal(rest, p); // the very same spot, frame after frame
        }

        Assert.Equal(-R, rest.X, 3);
    }

    [Fact]
    public void ALongFrame_CannotCarryTheShipThroughASmallRock()
    {
        var pebble = Ball(1); // three blocks across
        var from = new Vector3(-7f, 0.5f, 0.5f);
        var to = RockKeepOut.Resolve(pebble, BallCentre, from, new Vector3(8f, 0.5f, 0.5f), 0f); // 15 units in one hitch

        Assert.True(to.X < 0f, $"the ship ended at {to} — on the far side of the pebble");
        Assert.True(Distance(pebble, to) >= R - 0.01f, $"the ship ended inside the pebble's shell at {to}");
    }

    [Fact]
    public void AnOffCentreHitOnARoundRock_GlancesAround_NeverIn()
    {
        var rock = Ball(3);
        var p = new Vector3(-9f, 0.5f, 2.2f);
        var step = new Vector3(0.25f, 0f, 0f); // straight on: only the part along the surface is left, so it slides slowly
        for (int frame = 0; frame < 200; frame++)
        {
            p = RockKeepOut.Resolve(rock, BallCentre, p, p + step, 0f);
            Assert.True(Distance(rock, p) >= R - 0.01f, $"frame {frame}: the ship is inside the rock's shell at {p}");
        }

        Assert.True(p.X > 4f, $"the ship should have slid round the rock and on, but sits at {p}");
    }

    [Fact]
    public void RandomMovesNearARock_NeverEndInside()
    {
        var rock = Ball(2);
        var rng = new System.Random(3420);
        int touched = 0;
        for (int i = 0; i < 400; i++)
        {
            var from = BallCentre + RandomDirection(rng) * (float)(R + 2.5 + rng.NextDouble() * 4.0);
            if (Distance(rock, from) < R)
            {
                continue; // only starts outside the shell
            }

            var to = from + RandomDirection(rng) * (float)(rng.NextDouble() * 4.0);
            var end = RockKeepOut.Resolve(rock, BallCentre, from, to, 0f);
            Assert.True(Distance(rock, end) >= R - 0.02f, $"{from} → {to} ended inside the shell at {end}");
            touched += end == to ? 0 : 1;
        }

        Assert.True(touched > 20, $"only {touched} of the random moves touched the rock — the test lost its point");
    }

    [Fact]
    public void AShipStuckInsideARock_IsEasedOutGently()
    {
        var rock = Ball(2);
        var p = BallCentre + new Vector3(0.6f, 0f, 0f); // a rock appeared around the ship
        float ease = RockKeepOut.EaseOutSpeed / 60f;

        var next = RockKeepOut.Resolve(rock, BallCentre, p, p, ease);

        Assert.True(next.X > p.X, "the ship should move out, away from the rock's middle");
        Assert.True(Vector3.Distance(p, next) <= ease + 1e-4f, "no pop: one frame eases it out by the gentle step at most");

        for (int frame = 0; frame < 600 && Distance(rock, next) < R - 0.01f; frame++)
        {
            next = RockKeepOut.Resolve(rock, BallCentre, next, next, ease);
        }

        Assert.True(Distance(rock, next) >= R - 0.01f, $"after ten seconds the ship should be out, but sits at {next}");
    }

    [Fact]
    public void NoRock_NoChange()
    {
        var from = new Vector3(1f, 2f, 3f);
        var to = new Vector3(2f, 2f, 3f);

        Assert.Equal(to, RockKeepOut.Resolve(new Dictionary<Vector3i, BlockId>(), Vector3.Zero, from, to, 0.1f));
    }

    [Fact]
    public void TheNearestBlock_IsMeasuredToItsBox_AndInsideIsZero()
    {
        var cells = new Dictionary<Vector3i, BlockId> { [new Vector3i(0, 0, 0)] = new BlockId(1) };

        Assert.True(VoxelSurface.TryNearest(cells, new Vector3f(-2f, 0.5f, 0.5f), 3f, out var face, out float d));
        Assert.Equal(2f, d, 4);
        Assert.Equal(0f, face.X, 4); // on the face

        Assert.True(VoxelSurface.TryNearest(cells, new Vector3f(2f, 2f, 0.5f), 3f, out var edge, out d));
        Assert.Equal(System.MathF.Sqrt(2f), d, 4); // round the edge
        Assert.Equal(1f, edge.X, 4);
        Assert.Equal(1f, edge.Y, 4);

        Assert.True(VoxelSurface.TryNearest(cells, new Vector3f(0.3f, 0.6f, 0.9f), 3f, out _, out d));
        Assert.Equal(0f, d);

        Assert.False(VoxelSurface.TryNearest(cells, new Vector3f(-4f, 0.5f, 0.5f), 3f, out _, out _)); // beyond reach
    }

    // ---------------- rocks ----------------

    private static readonly Vector3 WallCentre = new(0.5f, 0.5f, 0.5f);
    private static readonly Vector3 BallCentre = new(0.5f, 0.5f, 0.5f);

    /// <summary>A flat face: one layer of blocks at x = 0, wide in y and z.</summary>
    private static Dictionary<Vector3i, BlockId> Wall()
    {
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int y = -8; y <= 8; y++)
        {
            for (int z = -8; z <= 8; z++)
            {
                cells[new Vector3i(0, y, z)] = new BlockId(1);
            }
        }

        return cells;
    }

    /// <summary>A round rock like the server's asteroids: the cells -r..r within the radius (its middle is at 0.5).</summary>
    private static Dictionary<Vector3i, BlockId> Ball(int r)
    {
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int x = -r; x <= r; x++)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int z = -r; z <= r; z++)
                {
                    if (x * x + y * y + z * z <= r * r)
                    {
                        cells[new Vector3i(x, y, z)] = new BlockId(1);
                    }
                }
            }
        }

        return cells;
    }

    private static float Distance(Dictionary<Vector3i, BlockId> cells, Vector3 p)
        => VoxelSurface.TryNearest(cells, new Vector3f(p.X, p.Y, p.Z), R + 1f, out _, out float d) ? d : float.MaxValue;

    private static Vector3 RandomDirection(System.Random rng)
    {
        var v = new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1));
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitX;
    }
}
