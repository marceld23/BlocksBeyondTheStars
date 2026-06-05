using System.Linq;
using Spacecraft.Shared.Content;
using Spacecraft.WorldGeneration;
using Xunit;

namespace Spacecraft.Tests;

/// <summary>
/// Procedural space stations: modules (rooms) assembled from building blocks and joined together
/// into one voxel structure. The solid hull encloses hollow rooms, so the exterior the player sees
/// matches the interior they walk through. Deterministic from the seed; size tiers scale the build.
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

    [Fact]
    public void Modules_AreHollowRooms_WithSolidWalls()
    {
        var c = Content();
        var s = StationGenerator.Generate("medium", 7, c);
        ushort air = 0;

        // Each module's centre is interior air; its room walls are solid blocks somewhere around it.
        foreach (var m in s.Modules)
        {
            int cx = m.Origin.X + 3, cy = m.Origin.Y + 2, cz = m.Origin.Z + 3;
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
                int x = m.Origin.X + 6; // shared wall plane (RoomW-1)
                int z = m.Origin.Z + 3; // door centre
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

            int glazed = 0, holes = 0;
            for (int x = o.X + 1; x <= o.X + 5; x++)
            for (int y = o.Y + 1; y <= o.Y + 3; y++)
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
    public void SizeTiers_ScaleTheBuild()
    {
        var c = Content();
        var small = StationGenerator.Generate("small", 5, c);
        var huge = StationGenerator.Generate("huge", 5, c);

        Assert.True(huge.Modules.Count > small.Modules.Count);
        Assert.True(huge.Height >= small.Height);
    }
}
