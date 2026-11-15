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

    [Theory]
    [InlineData("wagon_seats")]
    [InlineData("wagon_bar")]
    public void NoFullCube_StandsOverABench_TheRoomAboveIsFreeUpToThePane(string item)
    {
        // #3657 (a playtest): the side windows were full glass cubes, so the seated eye sat inside the window block.
        var cells = CellsOf(item);
        foreach (var seat in RailRules.SeatOffsets(item))
        {
            var (x, y, z) = RailWagonLayout.CellOf(new Vector3f(seat.X, 0.5f, seat.Z));
            for (int up = y + 1; up < RailWagonLayout.Height - 1; up++)
            {
                if (cells.TryGetValue((x, up, z), out var above))
                {
                    Assert.False(ShapeCode.IsCube(above.Shape), $"{item}: a full {above.Block} cube over the seat at {seat} (y {up})");
                }
            }
        }
    }

    [Theory]
    [InlineData("wagon_seats", false)]
    [InlineData("wagon_sleeper", false)]
    [InlineData("wagon_bar", false)]
    [InlineData(RailRules.CabItemKey, true)]
    public void EverySideWindow_IsAThinPane_OnTheWallsOuterFace(string item, bool cab)
    {
        var cells = RailWagonLayout.Cells(item, cab).ToDictionary(c => (c.X, c.Y, c.Z));
        int windows = 0;
        foreach (var c in cells.Values.Where(c => c.Block == RailWagonLayout.WindowBlock))
        {
            bool cabCorner = cab && c.Z == RailWagonLayout.Length - 1 && (c.X == 0 || c.X == RailWagonLayout.Width - 1);
            if (cabCorner)
            {
                continue; // the cab's front corners close two faces at once: a full glass cube, nothing sits under it
            }

            Assert.Equal((int)BlockShape.Panel, ShapeCode.ShapeOf(c.Shape));
            var (fx, fy, fz) = ShapeCode.FaceDirection(ShapeCode.UpFaceOf(c.Shape)); // the pane lies on the face OPPOSITE its up-face
            Assert.Equal(0, fy);
            if (cab && c.Z == RailWagonLayout.Length - 1)
            {
                Assert.Equal((0, -1), (fx, fz)); // the windscreen lies on the front (+Z) face
                Assert.Equal(1, c.X);
            }
            else
            {
                Assert.True(c.Y is 2 or 3, $"{item}: a window at y {c.Y}");
                Assert.Equal(c.X == 0 ? (1, 0) : (-1, 0), (fx, fz)); // the side pane lies on the column's outer face
                Assert.True(c.X == 0 || c.X == RailWagonLayout.Width - 1, $"{item}: a window in the aisle at x {c.X}");
            }

            windows++;
        }

        Assert.True(windows >= 4, $"{item}: only {windows} window panes");
    }

    [Fact]
    public void TheUpperWall_IsPanesAndLintels_OnlyTheSleepersBunksReplaceAPane_AndTheCabsDoorsStayOpen()
    {
        // The furnishing is laid over the panes: the sleeper's upper bunks replace one, nothing else does.
        foreach (string item in PassengerWagons)
        {
            foreach (var c in RailWagonLayout.Cells(item, cab: false).Where(c => c.Y is 2 or 3 && c.X != 1))
            {
                bool furnishing = c.Block is "crew_bunk";
                bool lintel = c.Block == "metal_panel" && c.Y == 3 && (c.Z == RailWagonLayout.DoorZ0 || c.Z == RailWagonLayout.DoorZ1);
                Assert.True(furnishing || lintel || c.Block == RailWagonLayout.WindowBlock, $"{item}: {c.Block} in the wall at {c}");
            }
        }

        var cab = RailWagonLayout.Cells(RailRules.CabItemKey, cab: true).ToDictionary(c => (c.X, c.Y, c.Z));
        foreach (int x in new[] { 0, RailWagonLayout.Width - 1 })
        {
            Assert.False(cab.ContainsKey((x, 1, RailWagonLayout.DoorZ0)), "the cab's door is blocked");
            Assert.False(cab.ContainsKey((x, 2, RailWagonLayout.DoorZ1)), "the cab's door is blocked");
        }
    }
}
