// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3471–#3473 (Lyxette, v2026.11.16): the wagon seats sit on their own furnishing — benches of the bench form, berths of
/// the two-cell bed — never inside a full cube and never in the door opening; standing up goes into the aisle; a berth's
/// pillow eye is in free space below the upper bunk. #3475: a chair or bench faces away from its backrest.
/// </summary>
public sealed class RailWagonLayoutTests
{
    private static readonly string[] PassengerWagons = { "wagon_seats", RailWagonLayout.SleeperItemKey, "wagon_bar" };

    private static Dictionary<(int, int, int), RailWagonLayout.Cell> CellsOf(string item)
        => RailWagonLayout.Cells(item, cab: false).ToDictionary(c => (c.X, c.Y, c.Z));

    [Theory]
    [InlineData("wagon_seats", 8)]
    [InlineData("wagon_sleeper", 4)]
    [InlineData("wagon_bar", 4)]
    public void EveryWagonKind_HasItsSeatCount(string item, int seats)
        => Assert.Equal(seats, RailRules.SeatOffsets(item).Count);

    [Theory]
    [InlineData("wagon_seats")]
    [InlineData("wagon_sleeper")]
    [InlineData("wagon_bar")]
    public void EverySeat_SitsOnItsOwnFurnishing_NeverInACubeOrTheDoor(string item)
    {
        var cells = CellsOf(item);
        bool berth = RailWagonLayout.IsBerthWagon(item);
        foreach (var seat in RailRules.SeatOffsets(item))
        {
            // A berth's ride point is the middle of its two-cell bed: test both halves around it.
            var probes = berth
                ? new[] { new Vector3f(seat.X, 0.5f, seat.Z - 0.5f), new Vector3f(seat.X, 0.5f, seat.Z + 0.5f) }
                : new[] { new Vector3f(seat.X, 0.5f, seat.Z) };
            foreach (var p in probes)
            {
                var (x, y, z) = RailWagonLayout.CellOf(p);
                Assert.NotEqual(1, x); // never the aisle
                Assert.True(z != RailWagonLayout.DoorZ0 && z != RailWagonLayout.DoorZ1, $"{item}: a seat in the door at {seat}");
                Assert.True(cells.TryGetValue((x, y, z), out var cell), $"{item}: no furnishing under the seat at {seat}");
                int shape = ShapeCode.ShapeOf(cell.Shape);
                Assert.True(berth ? FurnitureShapes.IsBedHalf(shape) : FurnitureShapes.IsSeat(shape),
                    $"{item}: the seat at {seat} is on {cell.Block} shape {shape}, not on a {(berth ? "berth" : "bench")}");
            }
        }
    }

    [Theory]
    [InlineData("wagon_seats")]
    [InlineData("wagon_sleeper")]
    [InlineData("wagon_bar")]
    public void StandingUp_PutsTheRiderInTheFreeAisle_InsideTheWagon(string item)
    {
        var cells = CellsOf(item);
        foreach (var seat in RailRules.SeatOffsets(item))
        {
            var up = RailWagonLayout.StandUpPoint(seat);
            Assert.Equal(RailRules.ClampLocal(up), up); // inside the walking box
            Assert.False(RailRules.OutsideWagon(up));
            for (float h = 0.5f; h < 2f; h += 1f)
            {
                var (x, y, z) = RailWagonLayout.CellOf(new Vector3f(up.X, h, up.Z));
                Assert.False(cells.ContainsKey((x, y, z)), $"{item}: standing up from {seat} lands in a block at {up}");
            }

            Assert.True(cells.ContainsKey(RailWagonLayout.CellOf(new Vector3f(up.X, -0.5f, up.Z))), "no floor under the aisle");
        }
    }

    [Fact]
    public void ABerthsPillowEye_IsInFreeSpace_BelowTheUpperBunk()
    {
        var cells = CellsOf(RailWagonLayout.SleeperItemKey);
        foreach (var berth in RailRules.SeatOffsets(RailWagonLayout.SleeperItemKey))
        {
            var eye = RailWagonLayout.BerthEye(berth);
            Assert.True(eye.Y > RailWagonLayout.BerthTop && eye.Y < 1f, $"eye height {eye.Y}");
            var cell = RailWagonLayout.CellOf(eye);
            Assert.True(cells.TryGetValue(cell, out var under) && under.Block == "bed", "the eye is not over the bed");
            Assert.True(cells.TryGetValue((cell.X, cell.Y + 1, cell.Z), out var above) && above.Block == "crew_bunk",
                "no upper bunk above the berth");
            Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(under.Shape)); // the pillow end
        }
    }

    [Fact]
    public void TheBedsOfTheSleeper_PairUp()
    {
        var cells = CellsOf(RailWagonLayout.SleeperItemKey);
        foreach (var head in cells.Values.Where(c => ShapeCode.ShapeOf(c.Shape) == (int)BlockShape.BedHead))
        {
            Assert.True(FurnitureShapes.TryBedPartnerOffset(head.Shape, out int dx, out int dz));
            Assert.True(cells.TryGetValue((head.X + dx, head.Y, head.Z + dz), out var foot));
            Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(foot.Shape));
        }
    }

    [Fact]
    public void ASeatedRider_FacesTheAisle_AndALyingOne_TheFootOfTheBerth()
    {
        foreach (var seat in RailRules.SeatOffsets("wagon_seats"))
        {
            float f = RailWagonLayout.SeatFacing("wagon_seats", seat);
            Assert.True(System.Math.Sin(f) * seat.X < 0, $"the seat at {seat} does not face the aisle"); // +X facing on the −X side
        }

        foreach (var berth in RailRules.SeatOffsets(RailWagonLayout.SleeperItemKey))
        {
            float look = RailWagonLayout.SeatFacing(RailWagonLayout.SleeperItemKey, berth);
            float head = RailWagonLayout.BerthHeadYaw(berth);
            Assert.True(System.Math.Cos(look) * berth.Z < 0, "the lying look points toward the wagon's end");
            Assert.True(System.Math.Cos(head) * berth.Z > 0, "the head lies at the wagon's end");
        }
    }

    [Theory]
    [InlineData(0, 180f)]
    [InlineData(1, 90f)]
    [InlineData(2, 0f)]
    [InlineData(3, 270f)]
    public void AChairOrBench_FacesAwayFromItsBackrest(int yaw, float expected)
    {
        foreach (var form in new[] { BlockShape.Chair, BlockShape.Bench })
        {
            Assert.True(FurnitureShapes.TrySeatFacingYawDegrees(ShapeCode.Pack(form, yaw), out float facing));
            Assert.Equal(expected, facing);
            var (bx, bz) = ShapeCode.YawDirection(yaw); // the backrest's side
            double rad = facing * System.Math.PI / 180.0;
            Assert.True((System.Math.Sin(rad) * bx) + (System.Math.Cos(rad) * bz) < -0.5, "facing into the backrest");
        }

        Assert.False(FurnitureShapes.TrySeatFacingYawDegrees(ShapeCode.Pack(BlockShape.Table, yaw), out _));
    }

    [Fact]
    public void EveryPassengerWagon_KeepsTheAisleAndTheDoorsFree()
    {
        foreach (string item in PassengerWagons)
        {
            var cells = CellsOf(item);
            for (int z = 0; z < RailWagonLayout.Length; z++)
            {
                Assert.False(cells.ContainsKey((1, 1, z)), $"{item}: the aisle is blocked at z {z}");
                Assert.False(cells.ContainsKey((1, 2, z)), $"{item}: the aisle is blocked at z {z}");
            }

            foreach (int x in new[] { 0, RailWagonLayout.Width - 1 })
            {
                for (int y = 1; y <= 2; y++)
                {
                    Assert.False(cells.ContainsKey((x, y, RailWagonLayout.DoorZ0)), $"{item}: the door is blocked");
                    Assert.False(cells.ContainsKey((x, y, RailWagonLayout.DoorZ1)), $"{item}: the door is blocked");
                }
            }
        }
    }
}
