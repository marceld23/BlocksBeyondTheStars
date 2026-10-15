// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared air engine (#2650) headless, against an array-backed world: vertical spans, the outside at the sky line,
/// openings named where a room meets the outside (#2654), doors as bulkheads that chain sealed rooms (#2651/#2653),
/// unloaded cells as unknown (never a leak), time slicing, the emergency brake, and the measurement of a one-million-cell
/// hall (memory and per-step cost).
/// </summary>
public sealed class AirEngineTests
{
    private readonly ITestOutputHelper _out;

    public AirEngineTests(ITestOutputHelper output) => _out = output;

    /// <summary>A box of cells [0, size) per axis; everything outside the box is wall, everything at or above
    /// <see cref="SkyY"/> that is not a wall is the outside. Cells are open by default.</summary>
    private sealed class GridSpace : IAirSpace
    {
        private const byte Open = 0, Wall = 1, Door = 2, Unknown = 3, Sand = 4;
        private readonly byte[] _cells;
        private readonly Dictionary<(int, int, int), int> _doorAt = new();
        private readonly List<List<Vector3i>> _doors = new();

        public GridSpace(int sx, int sy, int sz)
        {
            SizeX = sx;
            SizeY = sy;
            SizeZ = sz;
            SkyY = sy;
            _cells = new byte[sx * sy * sz];
        }

        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        public int SkyY { get; set; }

        /// <summary>Above this height an unknown cell met on the way up is the unbuilt sky.</summary>
        public int BuiltTop { get; set; } = int.MaxValue;

        public long Reads { get; private set; }

        private int Index(int x, int y, int z) => (y * SizeZ + z) * SizeX + x;

        private bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ;

        public void Set(int x, int y, int z, byte v) => _cells[Index(x, y, z)] = v;

        public void SetWall(int x, int y, int z) => Set(x, y, z, Wall);

        public void SetOpen(int x, int y, int z) => Set(x, y, z, Open);

        public void SetSand(int x, int y, int z) => Set(x, y, z, Sand);

        public void SetUnknown(int x, int y, int z) => Set(x, y, z, Unknown);

        /// <summary>A hollow box: walls on the shell of [x0..x1]×[y0..y1]×[z0..z1], air inside.</summary>
        public void Shell(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        bool shell = x == x0 || x == x1 || y == y0 || y == y1 || z == z0 || z == z1;
                        Set(x, y, z, shell ? Wall : Open);
                    }
        }

        /// <summary>A 1-wide, 2-tall door at (x, y..y+1, z).</summary>
        public int AddDoor(int x, int y, int z)
        {
            int index = _doors.Count;
            var cells = new List<Vector3i> { new(x, y, z), new(x, y + 1, z) };
            _doors.Add(cells);
            foreach (var c in cells)
            {
                Set(c.X, c.Y, c.Z, Door);
                _doorAt[(c.X, c.Y, c.Z)] = index;
            }

            return index;
        }

        public AirCell Classify(int x, int y, int z)
        {
            Reads++;
            if (!Inside(x, y, z))
            {
                return y >= SkyY ? AirCell.Sky : AirCell.Wall;
            }

            switch (_cells[Index(x, y, z)])
            {
                case Wall:
                    return AirCell.Wall;
                case Door:
                    return AirCell.Door;
                case Unknown:
                    return AirCell.Unknown;
                default:
                    return y >= SkyY ? AirCell.Sky : AirCell.Open;
            }
        }

        public long ColumnKey(int x, int z) => ((long)x << 32) | (uint)z;

        public Vector3i Canonical(int x, int y, int z) => new(x, y, z);

        public bool UnknownAboveIsSky(int x, int y, int z) => y > BuiltTop;

        public bool HoldsBlock(int x, int y, int z) => Inside(x, y, z) && _cells[Index(x, y, z)] == Sand;

        public int DoorAt(int x, int y, int z) => _doorAt.TryGetValue((x, y, z), out int d) ? d : -1;

        public void AddDoorSeeds(int door, List<Vector3i> into)
        {
            foreach (var c in _doors[door])
            {
                into.Add(new Vector3i(c.X + 1, c.Y, c.Z));
                into.Add(new Vector3i(c.X - 1, c.Y, c.Z));
                into.Add(new Vector3i(c.X, c.Y, c.Z + 1));
                into.Add(new Vector3i(c.X, c.Y, c.Z - 1));
            }
        }

        public int WrapDx(int dx) => dx;

        public int WrapDz(int dz) => dz;
    }

    private static readonly AirFillLimits Generous = new(50_000_000, 5_000_000);

    private static AirFill FillFrom(GridSpace space, Vector3i seed, bool chain = false, AirFillLimits? limits = null)
    {
        var fill = new AirFill(space, limits ?? Generous, chain, new Vector3i(0, 0, 0));
        fill.AddSeed(seed);
        fill.Step(-1);
        return fill;
    }

    [Fact]
    public void ASealedBox_IsOneSealedPocket_HoldingExactlyItsAir()
    {
        var space = new GridSpace(20, 30, 20);
        space.Shell(2, 12, 5, 10, 2, 8); // interior 9 × 4 × 5
        var fill = FillFrom(space, new Vector3i(5, 6, 5));

        var pocket = Assert.Single(fill.Pockets);
        Assert.True(pocket.Sealed);
        Assert.Equal(9 * 4 * 5, pocket.Cells);
        Assert.Equal(9 * 5, pocket.Spans); // one span per column: floor to ceiling
        Assert.Null(pocket.Opening);
        Assert.Same(pocket, fill.PocketAt(11, 9, 7));
        Assert.Null(fill.PocketAt(12, 9, 7)); // the wall
    }

    [Fact]
    public void AHoleInTheRoof_Leaks_AndIsNamedAtTheHole_NotAtTheSkyLine()
    {
        var space = new GridSpace(20, 60, 20);
        space.Shell(2, 12, 5, 15, 2, 8); // roof plane y 15
        space.SetOpen(9, 15, 6);         // one roof block missing

        var fill = FillFrom(space, new Vector3i(3, 6, 3)); // far from the hole: the fill reaches it from a neighbour column
        var pocket = fill.Pockets[0];
        Assert.True(pocket.Leaky);
        Assert.Equal(new Vector3i(9, 15, 6), pocket.Opening);

        var under = FillFrom(space, new Vector3i(9, 6, 6)); // standing right under the hole
        Assert.Equal(new Vector3i(9, 15, 6), under.Pockets[0].Opening);
    }

    [Fact]
    public void AHoleInTheSideWall_IsNamedAtTheHole_AtItsOwnHeight()
    {
        var space = new GridSpace(30, 60, 20);
        space.Shell(2, 12, 5, 15, 2, 8);
        space.SetOpen(12, 7, 5); // a window in the +x wall, two above the floor (floor plane y 5, first air y 6)

        var fill = FillFrom(space, new Vector3i(4, 6, 5));
        Assert.True(fill.Pockets[0].Leaky);
        Assert.Equal(new Vector3i(12, 7, 5), fill.Pockets[0].Opening);
    }

    [Fact]
    public void ADoorwayWithoutADoor_AtGroundLevel_IsNamedAtTheFloorYouStandOn()
    {
        var space = new GridSpace(30, 60, 20);
        for (int x = 0; x < 30; x++)
            for (int z = 0; z < 20; z++)
            {
                space.SetWall(x, 5, z); // level ground: the outside floor is the room's floor level
            }

        space.Shell(2, 12, 5, 10, 2, 8);
        for (int y = 6; y <= 8; y++)
        {
            space.SetOpen(12, y, 5); // a 3-tall doorway, no door in it
        }

        var fill = FillFrom(space, new Vector3i(4, 6, 5));
        Assert.True(fill.Pockets[0].Leaky);
        Assert.Equal(new Vector3i(12, 6, 5), fill.Pockets[0].Opening);
    }

    [Fact]
    public void ASandRoof_IsNamedAtTheSand()
    {
        var space = new GridSpace(20, 60, 20);
        space.Shell(2, 12, 5, 15, 2, 8);
        for (int x = 9; x <= 11; x++)
        {
            space.SetSand(x, 15, 7); // a sand patch in the roof
        }

        var fill = FillFrom(space, new Vector3i(3, 6, 3));
        Assert.True(fill.Pockets[0].Leaky);
        var opening = fill.Pockets[0].Opening!.Value;
        Assert.Equal(15, opening.Y);
        Assert.True(space.HoldsBlock(opening.X, opening.Y, opening.Z), $"named {opening}, not the sand");
    }

    /// <summary>Room A (x 3..7) and room B (x 9..13) share the wall x 8 with a door in it.</summary>
    private static (GridSpace Space, int Door) TwoRooms()
    {
        var space = new GridSpace(20, 40, 12);
        space.Shell(2, 8, 5, 10, 2, 8);
        space.Shell(8, 14, 5, 10, 2, 8);
        int door = space.AddDoor(8, 6, 5);
        return (space, door);
    }

    [Fact]
    public void ADoor_SeparatesTwoRooms_AndASealedRoomChainsToTheNext()
    {
        var (space, door) = TwoRooms();
        var alone = FillFrom(space, new Vector3i(4, 6, 4));
        var a = Assert.Single(alone.Pockets);
        Assert.True(a.Sealed);
        Assert.Equal(new[] { door }, a.Doors);
        Assert.Null(alone.PocketAt(11, 6, 4)); // without chaining, room B is not looked at

        var chained = FillFrom(space, new Vector3i(4, 6, 4), chain: true);
        Assert.Equal(2, chained.Pockets.Count);
        Assert.True(chained.PocketAt(11, 6, 4)!.Sealed);
        Assert.NotEqual(chained.PocketAt(4, 6, 4)!.Id, chained.PocketAt(11, 6, 4)!.Id);
    }

    [Fact]
    public void ALeakyRoom_NeverContinuesTheChain()
    {
        var (space, _) = TwoRooms();
        space.SetOpen(4, 10, 4); // a hole in room A's roof
        space.SkyY = 12;
        var fill = FillFrom(space, new Vector3i(4, 6, 4), chain: true);
        Assert.True(fill.Pockets[0].Leaky);
        Assert.Null(fill.PocketAt(11, 6, 4)); // room B is not reached through the leaky room …

        var direct = FillFrom(space, new Vector3i(11, 6, 4), chain: true);
        Assert.True(direct.PocketAt(11, 6, 4)!.Sealed); // … but seeded on its own it still holds air: a bulkhead
    }

    [Fact]
    public void UnloadedCells_BoundAPocketLikeWalls_AndNeverMakeItLeak()
    {
        var space = new GridSpace(40, 40, 12);
        space.Shell(2, 30, 5, 10, 2, 8);
        for (int x = 20; x <= 31; x++)
            for (int y = 0; y < 40; y++)
                for (int z = 0; z < 12; z++)
                {
                    space.SetUnknown(x, y, z); // the east wing is not loaded
                }

        var fill = FillFrom(space, new Vector3i(4, 6, 4));
        var pocket = Assert.Single(fill.Pockets);
        Assert.True(pocket.Sealed);
        Assert.True(pocket.TouchesUnknown);
        Assert.True(fill.SawUnknown);
    }

    [Fact]
    public void AnUnloadedChunkAboveARoofHole_IsTheSky_UnlessSomethingWasBuiltUpThere()
    {
        var space = new GridSpace(20, 60, 20);
        space.Shell(2, 12, 5, 15, 2, 8);
        space.SetOpen(9, 15, 6);
        for (int x = 0; x < 20; x++)
            for (int y = 32; y < 60; y++)
                for (int z = 0; z < 20; z++)
                {
                    space.SetUnknown(x, y, z); // the server streamed only up to y 31 over the base
                }

        space.BuiltTop = 15; // the roof is the highest build
        Assert.True(FillFrom(space, new Vector3i(4, 6, 4)).Pockets[0].Leaky);

        space.BuiltTop = 50; // a tall hall's ceiling in the unloaded chunk: unknown, not sky
        Assert.True(FillFrom(space, new Vector3i(4, 6, 4)).Pockets[0].Sealed);
    }

    [Fact]
    public void AFillSlicedOverManySteps_FindsWhatTheWholeFillFinds()
    {
        var space = new GridSpace(60, 30, 60);
        space.Shell(1, 58, 1, 25, 1, 58);
        var whole = FillFrom(space, new Vector3i(10, 10, 10));

        var sliced = new AirFill(space, Generous, chainDoors: false, new Vector3i(0, 0, 0));
        sliced.AddSeed(new Vector3i(10, 10, 10));
        int steps = 0;
        while (!sliced.Step(0, minSpans: 16))
        {
            steps++;
            Assert.True(steps < 100_000);
        }

        Assert.True(steps > 10, $"a zero budget must slice the fill (steps {steps})");
        Assert.Equal(whole.CellCount, sliced.CellCount);
        Assert.True(sliced.Pockets[0].Sealed);
    }

    [Fact]
    public void TheEmergencyBrake_StopsARunThatGrowsTooLarge()
    {
        var space = new GridSpace(60, 30, 60);
        space.Shell(1, 58, 1, 25, 1, 58); // 56 × 23 × 56 ≈ 72 000 cells
        var braked = FillFrom(space, new Vector3i(10, 10, 10), limits: new AirFillLimits(10_000, 1_000_000));
        Assert.True(braked.Braked);
        Assert.True(braked.Done);
        Assert.False(braked.Pockets[0].Complete); // nothing claims to be sealed — the owner keeps its last result
    }

    [Fact]
    public void RoomsTheNewFillCannotReach_AreCarriedOver_WhenUnloadedCellsHidThem()
    {
        var (space, _) = TwoRooms();
        var before = FillFrom(space, new Vector3i(4, 6, 4), chain: true);
        Assert.Equal(2, before.Pockets.Count);
        int roomB = before.PocketAt(11, 6, 4)!.Id;

        // Room A's chunk unloads: the seed falls on an unknown cell, nothing is reached.
        for (int x = 0; x <= 7; x++)
            for (int y = 0; y < 40; y++)
                for (int z = 0; z < 12; z++)
                {
                    space.SetUnknown(x, y, z);
                }

        var after = FillFrom(space, new Vector3i(4, 6, 4), chain: true);
        Assert.True(after.SawUnknown);
        Assert.Null(after.PocketAt(11, 6, 4));
        after.AdoptSealedFrom(before);
        var carried = after.PocketAt(11, 6, 4)!;
        Assert.True(carried.Sealed);
        Assert.True(carried.Carried);
        Assert.Equal(roomB, carried.Id); // the same room, the same id
    }

    [Fact]
    public void ALeakIntoAHugeCave_EndsAtTheSky()
    {
        // A small room with a hole in its floor into a 110 × 20 × 110 cave (242 000 cells — the old fill gave up at 64 000
        // and called it "too large"), whose only way out is a shaft up to the sky at the far corner.
        var space = new GridSpace(120, 80, 120);
        space.Shell(0, 119, 0, 21, 0, 119);    // the cave, y 1..20
        space.Shell(2, 8, 21, 27, 2, 8);       // the room on top of it, floor = the cave's roof at y 21
        space.SetOpen(5, 21, 5);               // the hole in the room's floor
        for (int y = 21; y < 80; y++)
        {
            space.SetOpen(115, y, 115);        // the shaft out of the far corner
        }

        space.SkyY = 60;
        var fill = FillFrom(space, new Vector3i(4, 23, 4));
        var pocket = fill.Pockets[0];
        Assert.True(pocket.Leaky, "the room leaks — into the cave and up the shaft");
        Assert.False(fill.Braked);
        Assert.Equal(115, pocket.Opening!.Value.X);
        Assert.Equal(115, pocket.Opening!.Value.Z);
        _out.WriteLine($"cave leak: {fill.SpanCount} spans, {fill.CellCount} cells held, {space.Reads} reads");
    }

    [Fact]
    public void AMillionCellHall_BreathesWithinThePerTickBudget_WithBoundedMemory()
    {
        // 100 × 100 × 100 interior. Each step gets the server's 2 ms tick budget; the fill must finish, hold 10 000 spans,
        // and stay within a few MB however many steps it takes.
        var space = new GridSpace(102, 120, 102);
        space.Shell(0, 101, 0, 101, 0, 101);
        long budget = (long)(2.0 * Stopwatch.Frequency / 1000.0);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var fill = new AirFill(space, new AirFillLimits(2_000_000, 200_000), chainDoors: false, new Vector3i(0, 0, 0));
        fill.AddSeed(new Vector3i(50, 50, 50));
        int steps = 0;
        double worstMs = 0, totalMs = 0;
        bool done = false;
        while (!done)
        {
            long t0 = Stopwatch.GetTimestamp();
            done = fill.Step(budget, minSpans: 64);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            worstMs = Math.Max(worstMs, ms);
            totalMs += ms;
            steps++;
            Assert.True(steps < 1_000_000);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        _out.WriteLine($"1M hall: {steps} steps, total {totalMs:0.0} ms, worst step {worstMs:0.00} ms, " +
                       $"{fill.SpanCount} spans, {fill.CellCount} cells, {space.Reads} reads, {allocated / 1024} KB allocated");

        var pocket = Assert.Single(fill.Pockets);
        Assert.True(pocket.Sealed, "a closed hall of a million cells breathes — no size limit");
        Assert.Equal(1_000_000, fill.CellCount);
        Assert.Equal(10_000, fill.SpanCount);
        Assert.True(allocated < 8L * 1024 * 1024, $"spans, not cells: {allocated / 1024} KB for a million-cell hall");
        Assert.True(worstMs < 50, $"one step holds roughly to its budget (worst {worstMs:0.00} ms)");
    }
}
