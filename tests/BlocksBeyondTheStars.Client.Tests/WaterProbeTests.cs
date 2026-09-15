// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1902: the client's underwater probe follows the shared wet-cell rule on a real streamed chunk — a kelp
/// stalk or a building form in a pool is water for the wash, the muffle and swimming; a plant on the bank is not.</summary>
public sealed class WaterProbeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static int Index(int x, int y, int z) => (y * WorldConstants.ChunkSize + z) * WorldConstants.ChunkSize + x;

    [Fact]
    public void PlantsAndFormsInAPool_AreWet_APlantOnTheBankIsNot()
    {
        ushort water = Content.GetBlock("water")!.NumericId.Value;
        ushort stone = Content.GetBlock("stone")!.NumericId.Value;
        ushort kelp = Content.GetBlock("flora_kelp")!.NumericId.Value;
        int n = WorldConstants.ChunkSize;
        var blocks = new ushort[n * n * n];
        for (int x = 2; x <= 8; x++)
            for (int z = 2; z <= 8; z++)
            {
                blocks[Index(x, 1, z)] = stone;
                for (int y = 2; y <= 5; y++)
                {
                    blocks[Index(x, y, z)] = water;
                }
            }

        blocks[Index(5, 3, 5)] = kelp;   // a stalk segment in the middle of the pool
        blocks[Index(4, 3, 4)] = stone;  // a stone form in the pool (shape set below)
        blocks[Index(12, 2, 12)] = kelp; // a plant on dry land …
        blocks[Index(13, 2, 12)] = water; // … beside a single water block
        blocks[Index(12, 1, 12)] = stone;

        var world = new ClientWorld();
        world.StoreChunk(new ChunkCoord(0, 0, 0), blocks,
            shapeIndex: new[] { Index(4, 3, 4) }, shapeData: new[] { (int)BlockShape.Post });

        var probe = new WaterProbe(world, Content);
        Assert.True(probe.IsFor(world, Content));
        Assert.True(probe.IsWet(3, 3, 3), "plain water");
        Assert.True(probe.IsWet(5, 3, 5), "a kelp segment inside the pool");
        Assert.True(probe.IsWet(4, 3, 4), "a post form inside the pool");
        Assert.False(probe.IsWet(12, 2, 12), "a plant on the bank beside one water block");
        Assert.False(probe.IsWet(5, 9, 5), "air above the pool");
    }
}
