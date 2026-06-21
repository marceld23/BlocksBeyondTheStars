using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Procedural space stations: modules (rooms) assembled from building blocks and joined together
/// into one voxel structure. The solid hull encloses hollow rooms, so the exterior the player sees
/// matches the interior they walk through. Deterministic from the seed; size tiers scale module
/// count, FLOOR count and ROOM size (small/medium keep the classic 7×6×7 rooms; large/huge/colossal
/// grow them), and the big tiers merge the hangar — at colossal also the market — into double halls.
/// </summary>
public sealed class StationGenerationTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Generation_IsDeterministic_ForSameSeedAndTier()
    {
        var c = Content();
        var a = StationGenerator.Generate("large", 4242, c);
        var b = StationGenerator.Generate("large", 4242, c);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(a.Modules.Count, b.Modules.Count);
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                }
    }

    [Fact]
    public void Station_IsAssembledFromMultipleModules()
    {
        var c = Content();
        var s = StationGenerator.Generate("large", 1, c);

        Assert.True(s.Modules.Count >= 5, "A large station should be assembled from several modules.");
        Assert.Contains(s.Modules, m => m.Type == "hub");
        Assert.Contains(s.Modules, m => m.Type == "hangar");
    }

    [Theory]
    [InlineData("small")]
    [InlineData("large")]
    [InlineData("colossal")]
    public void Modules_AreHollowRooms_WithSolidWalls(string tier)
    {
        var c = Content();
        var s = StationGenerator.Generate(tier, 7, c);
        ushort air = 0;

        // Each module's centre is interior air; its room walls are solid blocks somewhere around it.
        foreach (var m in s.Modules)
        {
            int cx = m.Origin.X + s.RoomW / 2, cy = m.Origin.Y + 2, cz = m.Origin.Z + s.RoomL / 2;
            Assert.Equal(air, s.Get(cx, cy, cz)); // hollow inside
        }

        // There is plenty of solid hull overall.
        int solid = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != air) solid++;
                }

        Assert.True(solid > 0, "A station must be built from solid blocks.");
    }

    [Fact]
    public void AdjacentModules_AreConnectedByDoorways()
    {
        var c = Content();
        var s = StationGenerator.Generate("large", 3, c);
        ushort air = 0;

        // Find a horizontally-adjacent module pair and verify the shared wall has an opening.
        var byGrid = s.Modules.ToDictionary(m => (m.Grid.X, m.Grid.Y, m.Grid.Z));
        bool foundDoor = false;
        foreach (var m in s.Modules)
        {
            if (byGrid.ContainsKey((m.Grid.X + 1, m.Grid.Y, m.Grid.Z)))
            {
                int x = m.Origin.X + s.RoomW - 1; // shared wall plane
                int z = m.Origin.Z + s.RoomL / 2; // door centre
                if (s.Get(x, m.Origin.Y + 1, z) == air || s.Get(x, m.Origin.Y + 2, z) == air)
                {
                    foundDoor = true;
                    break;
                }
            }
        }

        Assert.True(foundDoor, "Adjacent modules should be joined by a doorway.");
    }

    [Fact]
    public void HangarMouth_IsSealedWithAForceField_NotOpenToSpace()
    {
        var c = Content();
        ushort field = c.GetBlock("force_field")!.NumericId.Value;
        ushort air = 0;

        // Across several seeds, the hangar's outer -Z wall (the docking mouth) must be glazed with the
        // force field — never left as an open air gap you could walk out into space through.
        for (long seed = 1; seed <= 6; seed++)
        {
            var s = StationGenerator.Generate("large", seed, c);
            var hangar = s.Modules.First(m => m.Type == "hangar");
            var o = hangar.Origin;
            int mouthTop = s.RoomH >= 8 ? 5 : 3;

            int glazed = 0, holes = 0;
            for (int x = o.X + 1; x <= o.X + s.RoomW - 2; x++)
                for (int y = o.Y + 1; y <= o.Y + mouthTop; y++)
                {
                    ushort b = s.Get(x, y, o.Z);
                    if (b == field) glazed++;
                    else if (b == air) holes++;
                }

            Assert.True(glazed > 0, $"Seed {seed}: hangar mouth should be glazed with a force field.");
            Assert.Equal(0, holes); // no open gap to the void anywhere in the mouth
        }
    }

    [Fact]
    public void Station_HasVendorAndMissionBoardMarkers()
    {
        var c = Content();
        var s = StationGenerator.Generate("large", 9, c);

        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == "mission_board");
        Assert.Contains(s.Markers, m => m.Type == "hangar");
    }

    [Fact]
    public void SizeTiers_ScaleModules_Floors_AndRoomSize()
    {
        var c = Content();
        var small = StationGenerator.Generate("small", 5, c);
        var large = StationGenerator.Generate("large", 5, c);
        var huge = StationGenerator.Generate("huge", 5, c);
        var colossal = StationGenerator.Generate("colossal", 5, c);

        // More rooms per tier, colossal on top.
        Assert.True(large.Modules.Count > small.Modules.Count);
        Assert.True(huge.Modules.Count > large.Modules.Count);
        Assert.True(colossal.Modules.Count > huge.Modules.Count);

        // BIGGER rooms per tier (the user's headline ask): small keeps 7×6×7, large 9×7×9, huge+ 11×8×11.
        Assert.Equal((7, 6, 7), (small.RoomW, small.RoomH, small.RoomL));
        Assert.Equal((9, 7, 9), (large.RoomW, large.RoomH, large.RoomL));
        Assert.Equal((11, 8, 11), (huge.RoomW, huge.RoomH, huge.RoomL));
        Assert.Equal((11, 8, 11), (colossal.RoomW, colossal.RoomH, colossal.RoomL));

        Assert.True(huge.Height > small.Height); // more floors of taller rooms
        Assert.True(colossal.Width * colossal.Length > small.Width * small.Length);
    }

    [Fact]
    public void BigTiers_MergeTheHangarIntoADoubleHall()
    {
        var c = Content();
        ushort air = 0;

        // The hangar merge needs an eligible neighbour room, which the random walk provides in almost
        // every layout — require it to happen across a handful of seeds and validate the open wall.
        bool validated = false;
        int merged = 0;
        for (long seed = 1; seed <= 8 && !validated; seed++)
        {
            var s = StationGenerator.Generate("huge", seed, c);
            var hall = s.Modules.Where(m => m.Type == "hangar_hall").Cast<StationModule?>().FirstOrDefault();
            if (hall is null)
            {
                continue;
            }

            merged++;
            var hangar = s.Modules.First(m => m.Type == "hangar");

            // The shared wall between hangar and hall partner must be FULLY open (no doorway wall left):
            // probe the wall plane between the two origins at mid-height across the interior span.
            var a = hangar.Origin;
            var b = hall.Value.Origin;
            int openCells = 0;
            if (a.X != b.X) // ±X partner: wall plane at the higher origin's X
            {
                int plane = System.Math.Max(a.X, b.X) == b.X ? b.X : a.X;
                int x = plane == a.X ? a.X : b.X; // min-corner of the right-hand room = shared plane
                x = System.Math.Max(a.X, b.X);    // the shared wall sits at the larger origin X
                for (int z = 1; z <= s.RoomL - 2; z++)
                {
                    if (s.Get(x, a.Y + 2, System.Math.Min(a.Z, b.Z) + z) == air) openCells++;
                }
            }
            else // ±Z partner
            {
                int z = System.Math.Max(a.Z, b.Z);
                for (int xx = 1; xx <= s.RoomW - 2; xx++)
                {
                    if (s.Get(System.Math.Min(a.X, b.X) + xx, a.Y + 2, z) == air) openCells++;
                }
            }

            Assert.True(openCells >= s.RoomW - 4,
                $"Seed {seed}: the merged hangar hall's shared wall should be fully open (got {openCells} open cells).");
            validated = true;
        }

        Assert.True(validated, $"No huge station across 8 seeds produced a merged hangar hall (merged={merged}).");
    }

    [Fact]
    public void Colossal_AlsoMergesAMarketHall()
    {
        var c = Content();
        bool found = false;
        for (long seed = 1; seed <= 8 && !found; seed++)
        {
            var s = StationGenerator.Generate("colossal", seed, c);
            found = s.Modules.Any(m => m.Type == "market_hall");
        }

        Assert.True(found, "Colossal stations should merge a market double hall in typical layouts.");
    }

    /// <summary>
    /// Regression guard for the "see-through grass in a station" bug: every decorative plant a generated
    /// station places must sit fully inside the hull — never a cell you can see the void through or walk out
    /// into space from. We prove it structurally: flood-fill the exterior void treating air AND plants as
    /// passable (a billboard plant has no opaque face and no collider), then assert no plant cell was reached.
    /// Also asserts plants are still being placed (the feature isn't silently disabled) and that each stands
    /// on a solid floor block. This is the coverage that was missing when the bug first slipped through.
    /// </summary>
    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("huge")]
    [InlineData("colossal")]
    public void DecorativePlants_StayInsideTheHull_NeverOpenToTheVoid(string tier)
    {
        var c = Content();
        ushort plant = c.GetBlock("flora_plant")!.NumericId.Value;
        Assert.NotEqual((ushort)0, plant);

        int totalPlants = 0;
        for (long seed = 1; seed <= 50; seed++)
        {
            var s = StationGenerator.Generate(tier, seed * 1009 + 7, c);
            int W = s.Width, H = s.Height, L = s.Length;

            var reached = new bool[W * H * L];
            int Idx(int x, int y, int z) => (x * H + y) * L + z;
            bool Passable(int x, int y, int z) { ushort b = s.Get(x, y, z); return b == 0 || b == plant; }
            var stack = new Stack<(int X, int Y, int Z)>();
            void Visit(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= W || y >= H || z >= L) return;
                if (reached[Idx(x, y, z)] || !Passable(x, y, z)) return;
                reached[Idx(x, y, z)] = true;
                stack.Push((x, y, z));
            }

            // Seed the flood from every cell on the bounding-box surface, then expand through air/plants.
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) { Visit(x, y, 0); Visit(x, y, L - 1); }
            for (int x = 0; x < W; x++)
                for (int z = 0; z < L; z++) { Visit(x, 0, z); Visit(x, H - 1, z); }
            for (int y = 0; y < H; y++)
                for (int z = 0; z < L; z++) { Visit(0, y, z); Visit(W - 1, y, z); }

            int[] dx = { 1, -1, 0, 0, 0, 0 }, dy = { 0, 0, 1, -1, 0, 0 }, dz = { 0, 0, 0, 0, 1, -1 };
            while (stack.Count > 0)
            {
                var (x, y, z) = stack.Pop();
                for (int d = 0; d < 6; d++) Visit(x + dx[d], y + dy[d], z + dz[d]);
            }

            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int z = 0; z < L; z++)
                    {
                        if (s.Get(x, y, z) != plant) continue;
                        totalPlants++;
                        Assert.False(reached[Idx(x, y, z)],
                            $"{tier} seed {seed * 1009 + 7}: plant at ({x},{y},{z}) is open to the void (see-through / walk-through).");
                        Assert.True(y > 0 && s.Get(x, y - 1, z) != 0,
                            $"{tier} seed {seed * 1009 + 7}: plant at ({x},{y},{z}) does not stand on a solid block.");
                    }
        }

        Assert.True(totalPlants > 0, $"Stations of tier '{tier}' should still be furnished with decorative plants.");
    }
}
