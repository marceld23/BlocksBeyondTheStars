// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2407: the client's light index carries the natural emitters (lava, crystals, glowing flora) as short-reach sources —
/// a surface-only one only from a cell with an air neighbour — and drops them when the player's switch is off, while
/// the fixtures keep their full reach either way. Same rule on both registry paths (chunk-store scan and live edit).
/// </summary>
[Collection("NaturalEmittersLight")] // the switch is process-wide state
public sealed class NaturalEmitterLightTests
{
    private const ushort Stone = 7;
    private const ushort Crystal = 28;
    private const ushort Lamp = 16;
    private const int CrystalRgb = 0xA8CBFF;
    private const int LampRgb = 0xFFFFFF;
    private const int Cs = WorldConstants.ChunkSize;

    private static int Resolve(ushort id) => id switch
    {
        Crystal => BlockLight.Pack(CrystalRgb, 4, surfaceOnly: true),
        Lamp => BlockLight.Pack(LampRgb, BlockLight.DefaultRadius, surfaceOnly: false),
        _ => 0,
    };

    private static ClientWorld World(ushort[] blocks)
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(Resolve);
        w.StoreChunk(new ChunkCoord(0, 0, 0), blocks);
        return w;
    }

    private static int Index(int x, int y, int z) => WorldConstants.LocalIndex(x, y, z); // the raw layout ChunkData.FromRaw expects

    [Fact]
    public void CrystalBesideAir_IsIndexedWithItsReach()
    {
        ClientWorld.NaturalEmittersLight = true;
        var blocks = new ushort[Cs * Cs * Cs];
        blocks[Index(3, 4, 5)] = Crystal;
        var w = World(blocks);

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(new Vector3i(3, 4, 5), hit.Pos);
        Assert.Equal(CrystalRgb, BlockLight.ColorFrom(hit.Rgb));
        Assert.Equal(4, BlockLight.RadiusFrom(hit.Rgb));
        Assert.True(BlockLight.SurfaceOnlyFrom(hit.Rgb));
    }

    [Fact]
    public void CrystalSealedInStone_IsNoSource_UntilAFaceOpens()
    {
        ClientWorld.NaturalEmittersLight = true;
        var blocks = new ushort[Cs * Cs * Cs];
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = Stone;
        }

        blocks[Index(3, 4, 5)] = Crystal;
        var w = World(blocks);
        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));

        Assert.True(w.ApplyBlockChange(3, 5, 5, 0, tint: 0, glow: 0, out _)); // mine the block above it
        Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
    }

    [Fact]
    public void SwitchOff_KeepsOnlyTheFixtures()
    {
        var blocks = new ushort[Cs * Cs * Cs];
        blocks[Index(3, 4, 5)] = Crystal;
        blocks[Index(8, 4, 5)] = Lamp;
        try
        {
            ClientWorld.NaturalEmittersLight = false;
            var w = World(blocks);
            var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
            Assert.Equal(new Vector3i(8, 4, 5), hit.Pos);
            Assert.Equal(BlockLight.DefaultRadius, BlockLight.RadiusFrom(hit.Rgb));
        }
        finally
        {
            ClientWorld.NaturalEmittersLight = true;
        }
    }
}
