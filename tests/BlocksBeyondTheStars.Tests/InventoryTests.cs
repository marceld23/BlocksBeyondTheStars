using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public class InventoryTests
{
    [Fact]
    public void Add_StacksAndSpillsToNewSlots()
    {
        var inv = new Inventory(2);
        int leftover = inv.Add("stone", 120, maxStack: 99);

        Assert.Equal(0, leftover);
        Assert.Equal(120, inv.CountOf("stone"));
    }

    [Fact]
    public void Add_ReturnsLeftover_WhenFull()
    {
        var inv = new Inventory(1);
        int leftover = inv.Add("stone", 150, maxStack: 99);

        Assert.Equal(51, leftover);
        Assert.Equal(99, inv.CountOf("stone"));
    }

    [Fact]
    public void Remove_FailsWhenInsufficient_AndChangesNothing()
    {
        var inv = new Inventory(4);
        inv.Add("iron_ore", 5, 99);

        Assert.False(inv.Remove("iron_ore", 10));
        Assert.Equal(5, inv.CountOf("iron_ore"));

        Assert.True(inv.Remove("iron_ore", 5));
        Assert.Equal(0, inv.CountOf("iron_ore"));
    }
}

public class WorldMathTests
{
    [Fact]
    public void WorldToChunk_HandlesNegatives()
    {
        Assert.Equal(-1, WorldConstants.WorldToChunk(-1));
        Assert.Equal(0, WorldConstants.WorldToChunk(0));
        Assert.Equal(0, WorldConstants.WorldToChunk(15));
        Assert.Equal(1, WorldConstants.WorldToChunk(16));
    }

    [Fact]
    public void WorldToLocal_IsAlwaysInRange()
    {
        for (int w = -40; w <= 40; w++)
        {
            int local = WorldConstants.WorldToLocal(w);
            Assert.InRange(local, 0, WorldConstants.ChunkSize - 1);
        }
    }

    [Fact]
    public void ChunkData_SetGet_RoundTrips()
    {
        var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
        chunk.Set(1, 2, 3, new BlockId(7));
        Assert.Equal((ushort)7, chunk.Get(1, 2, 3).Value);
        Assert.True(chunk.Get(0, 0, 0).IsAir);
    }

    [Fact]
    public void LocalIndex_IsUniquePerCell()
    {
        var seen = new HashSet<int>();
        for (int y = 0; y < WorldConstants.ChunkSize; y++)
        for (int z = 0; z < WorldConstants.ChunkSize; z++)
        for (int x = 0; x < WorldConstants.ChunkSize; x++)
        {
            Assert.True(seen.Add(WorldConstants.LocalIndex(x, y, z)));
        }

        Assert.Equal(WorldConstants.BlocksPerChunk, seen.Count);
    }
}
