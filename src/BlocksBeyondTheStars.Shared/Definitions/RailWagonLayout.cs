// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The authored wagons of the monorail (#2113) as data both sides share: the voxel grid every client meshes
/// (<c>TrainView</c>) and the seats the server hands out (<see cref="RailRules.SeatOffsets"/>) come from the same cells, so
/// a seat can never again end up inside a cube.
/// <para>The grid is <see cref="Width"/> × <see cref="Height"/> × <see cref="Length"/> cells: x 0..2 across, y 0..4 (floor at
/// 0, roof at 4), z 0..5 along (5 = the front, toward the cab). It is drawn offset by <see cref="GridOrigin"/>, so the floor's
/// top face is the rider frame's y = 0 and the middle of the width its x = 0 (wagon-local: X across, Y up, Z toward the
/// cab).</para>
/// <para>#3471/#3473 (Lyxette, v2026.11.16): since #3464 the benches, bunks and the bar's side sill were full cubes, while
/// every seat stood at floor height in those cells — the seated eye looked through the bench and the floor, standing up
/// dropped the rider through the floor (#3472). The benches are now the built-in bench form (seat + low backrest, the
/// backrest to the wall), the sleeper's lower berths real two-cell beds under the upper bunks, and each seat sits on its
/// own furnishing cell.</para>
/// </summary>
public static class RailWagonLayout
{
    /// <summary>The grid's size in cells (across, up, along).</summary>
    public const int Width = 3, Height = 5, Length = 6;

    /// <summary>Where cell (0,0,0)'s corner lies in the rider frame.</summary>
    public static readonly Vector3f GridOrigin = new(-Width * 0.5f, -1f, -Length * 0.5f);

    /// <summary>The door opening's cells along the wagon (both sides): no furnishing, no seat there.</summary>
    public const int DoorZ0 = 2, DoorZ1 = 3;

    /// <summary>The top of a berth's mattress over the floor (the bed form's mattress, as a sleeping villager lies on it).</summary>
    public const float BerthTop = 0.62f;

    /// <summary>The pillow view's eye over the floor in a berth (the beds' <c>RestPillowEye</c>, #2823) — below the upper bunk.</summary>
    public const float BerthEyeHeight = 0.8f;

    /// <summary>How far the pillow lies from the head cell's centre toward the headboard (the beds' <c>RestPillowInset</c>).</summary>
    public const float BerthPillowInset = 0.18f;

    /// <summary>One cell of a wagon: its grid position, the block key and the packed shape descriptor (0 = a cube).</summary>
    public readonly record struct Cell(int X, int Y, int Z, string Block, int Shape);

    /// <summary>The sleeper's wagon item.</summary>
    public const string SleeperItemKey = "wagon_sleeper";

    /// <summary>The side cells (x 0 and 2) the benches of a seat wagon fill, and the bar's bench cells.</summary>
    private static readonly int[] SeatBenchZ = { 0, 1, 4, 5 };
    private static readonly int[] BarBenchZ = { 0, 1 };

    /// <summary>The seats of a wagon kind, wagon-local (X across, Y up from the floor, Z toward the cab) — each on the
    /// middle of its own furnishing cell; a sleeper's on the middle of its two-cell berth.</summary>
    public static IReadOnlyList<Vector3f> Seats(string? wagonItem) => wagonItem switch
    {
        "wagon_seats" => SeatWagonSeats,
        SleeperItemKey => SleeperBerths,
        "wagon_bar" => BarSeats,
        RailRules.CabItemKey => CabSeats,
        _ => Array.Empty<Vector3f>(),
    };

    private static readonly Vector3f[] SeatWagonSeats = SideSeats(SeatBenchZ);
    private static readonly Vector3f[] BarSeats = SideSeats(BarBenchZ);
    private static readonly Vector3f[] CabSeats = { new(0f, 0f, 1.8f) }; // the driver's place behind the console (no player seat)
    private static readonly Vector3f[] SleeperBerths =
    {
        new(CellCentreX(0), 0f, 2f), new(CellCentreX(Width - 1), 0f, 2f),
        new(CellCentreX(0), 0f, -2f), new(CellCentreX(Width - 1), 0f, -2f),
    };

    /// <summary>Front pair first, then toward the rear: left and right of every bench cell.</summary>
    private static Vector3f[] SideSeats(int[] zs)
    {
        var seats = new List<Vector3f>();
        for (int i = zs.Length - 1; i >= 0; i--)
        {
            seats.Add(new Vector3f(CellCentreX(0), 0f, CellCentreZ(zs[i])));
            seats.Add(new Vector3f(CellCentreX(Width - 1), 0f, CellCentreZ(zs[i])));
        }

        return seats.ToArray();
    }

    /// <summary>The rider-frame X of a grid column's centre.</summary>
    public static float CellCentreX(int x) => GridOrigin.X + x + 0.5f;

    /// <summary>The rider-frame Z of a grid row's centre.</summary>
    public static float CellCentreZ(int z) => GridOrigin.Z + z + 0.5f;

    /// <summary>The grid cell a rider-frame point lies in.</summary>
    public static (int X, int Y, int Z) CellOf(Vector3f local)
        => ((int)Math.Floor(local.X - GridOrigin.X), (int)Math.Floor(local.Y - GridOrigin.Y), (int)Math.Floor(local.Z - GridOrigin.Z));

    /// <summary>Whether a wagon kind's seats are berths (lie down) rather than benches (sit).</summary>
    public static bool IsBerthWagon(string? wagonItem) => wagonItem == SleeperItemKey;

    /// <summary>#3472: where a rider stands up from a seat — in the aisle beside it (the middle column, the seat's Z), never
    /// in the furnishing cell the seat is on.</summary>
    public static Vector3f StandUpPoint(Vector3f seat) => new(0f, 0f, seat.Z);

    /// <summary>#3473: which way a berth's head lies along the wagon (+1 = toward the cab, −1 = the rear): the berths' heads
    /// are at the wagon's ends.</summary>
    public static int BerthHeadSign(Vector3f berth) => berth.Z >= 0f ? 1 : -1;

    /// <summary>#3473: the pillow view's eye in a berth (rider frame) — just over the pillow at the head end.</summary>
    public static Vector3f BerthEye(Vector3f berth)
    {
        int head = BerthHeadSign(berth);
        float z = berth.Z + (head * (0.5f + BerthPillowInset));
        return new Vector3f(berth.X, BerthEyeHeight, z);
    }

    /// <summary>#3473: the way from a berth's foot to its head (radians, 0 = toward the cab) — how a sleeping passenger lies.</summary>
    public static float BerthHeadYaw(Vector3f berth) => BerthHeadSign(berth) > 0 ? 0f : (float)Math.PI;

    /// <summary>The facing of a seated rider or passenger in the wagon (radians, 0 = toward the cab, +π/2 = across to the
    /// wagon's +X): on a bench across the aisle, its back to the wall; in a berth from the head toward the foot.</summary>
    public static float SeatFacing(string? wagonItem, Vector3f seat)
    {
        if (IsBerthWagon(wagonItem))
        {
            return BerthHeadSign(seat) > 0 ? (float)Math.PI : 0f;
        }

        return seat.X < 0f ? (float)(Math.PI * 0.5) : (float)(-Math.PI * 0.5);
    }

    /// <summary>The wagon's cells: floor, roof, side walls with the door opening, the cab's closed front, then the kind's
    /// furnishing (#3464) — which replaces the wall cells it stands in.</summary>
    public static IReadOnlyList<Cell> Cells(string? wagonItem, bool cab)
    {
        var cells = new Dictionary<(int, int, int), Cell>();
        void Set(int x, int y, int z, string block, int shape = 0) => cells[(x, y, z)] = new Cell(x, y, z, block, shape);

        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Length; z++)
            {
                Set(x, 0, z, "steel_floor");
                Set(x, Height - 1, z, "metal_panel");
            }
        }

        for (int z = 0; z < Length; z++)
        {
            bool door = z == DoorZ0 || z == DoorZ1;
            foreach (int x in new[] { 0, Width - 1 })
            {
                if (door)
                {
                    Set(x, 3, z, "metal_panel"); // the lintel over the door
                    continue;
                }

                Set(x, 1, z, "metal_panel");
                Set(x, 2, z, "glass");
                Set(x, 3, z, "glass");
            }
        }

        if (cab)
        {
            for (int x = 0; x < Width; x++)
            {
                Set(x, 1, Length - 1, "metal_panel");
                Set(x, 2, Length - 1, "glass");
                Set(x, 3, Length - 1, "glass");
            }

            Set(1, 1, Length - 2, "metal_panel"); // the console
        }

        Dress(wagonItem, Set);
        var list = new List<Cell>(cells.Values);
        list.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
        return list;
    }

    /// <summary>The bench form with its backrest toward the wall of side column <paramref name="x"/>.</summary>
    private static int WallBench(int x) => ShapeCode.Pack(BlockShape.Bench, ShapeCode.YawToward(x == 0 ? -1 : 1, 0));

    private static void Dress(string? wagonItem, Action<int, int, int, string, int> set)
    {
        switch (wagonItem)
        {
            case "wagon_seats":
                foreach (int z in SeatBenchZ)
                {
                    set(0, 1, z, "wood_planks_light", WallBench(0));
                    set(Width - 1, 1, z, "wood_planks_light", WallBench(Width - 1));
                }

                break;
            case SleeperItemKey:
                DressSleeper(set);
                break;
            case "wagon_bar":
                for (int x = 0; x < Width; x++)
                {
                    for (int z = 0; z < Length; z++)
                    {
                        set(x, 0, z, "wood_planks_mahogany", 0); // a warm wooden floor
                    }
                }

                foreach (int z in BarBenchZ)
                {
                    set(0, 1, z, "wood_planks_mahogany", WallBench(0));
                    set(Width - 1, 1, z, "wood_planks_mahogany", WallBench(Width - 1));
                }

                foreach (int z in new[] { 4, 5 })
                {
                    set(0, 1, z, "shop_counter", 0);
                    set(Width - 1, 1, z, "shop_counter", 0);
                }

                break;
        }
    }

    /// <summary>The sleeper (#3473): on each side two berths — a real two-cell bed (head at the wagon's end, the foot toward
    /// the door) with a glowing bunk above it and room between the two to lie in.</summary>
    private static void DressSleeper(Action<int, int, int, string, int> set)
    {
        foreach (int x in new[] { 0, Width - 1 })
        {
            // Front berth: head at z 5, foot at z 4 (the foot lies toward −Z: yaw 2). Rear berth: head at z 0, foot at z 1.
            int frontYaw = ShapeCode.YawToward(0, -1), rearYaw = ShapeCode.YawToward(0, 1);
            set(x, 1, Length - 1, "bed", ShapeCode.Pack(BlockShape.BedHead, frontYaw));
            set(x, 1, Length - 2, "bed", ShapeCode.Pack(BlockShape.BedFoot, frontYaw));
            set(x, 1, 0, "bed", ShapeCode.Pack(BlockShape.BedHead, rearYaw));
            set(x, 1, 1, "bed", ShapeCode.Pack(BlockShape.BedFoot, rearYaw));
            foreach (int z in new[] { 0, 1, Length - 2, Length - 1 })
            {
                set(x, 2, z, "crew_bunk", 0); // the upper bunk
            }
        }
    }
}
