// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Generation 11's glowing plants light their surroundings: the light index asks the per-cell resolver for a
/// registered plant with the cell's canonical position (the rainbow class colours each plant by its cell), on both
/// registry paths — the chunk-store scan and a live edit. Every other block keeps the id-only resolver.
/// </summary>
public sealed class PlantLightTests
{
    private const ushort Moss = 50;    // a registered plant with a fixed colour
    private const ushort Rainbow = 51; // a registered plant whose colour depends on the cell
    private const ushort Lamp = 42;
    private const int Cs = WorldConstants.ChunkSize;

    private static ClientWorld World()
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(id => id == Lamp ? 0xFFFFFF : 0);
        w.SetCellLightResolver((id, pos) => id == Moss ? 0x204030 : FloraTints.ToRgb24(FloraTints.RainbowAt(pos.X, pos.Y, pos.Z), 0.5f),
            new[] { Moss, Rainbow });
        return w;
    }

    [Fact]
    public void StoredChunk_IndexesThePlants_AtTheirCellColour()
    {
        var w = World();
        var blocks = new ushort[Cs * Cs * Cs];
        blocks[WorldConstants.LocalIndex(1, 2, 3)] = Moss;
        blocks[WorldConstants.LocalIndex(4, 5, 6)] = Rainbow;
        blocks[WorldConstants.LocalIndex(7, 7, 7)] = 9; // stone: no light
        w.StoreChunk(new ChunkCoord(1, 0, 0), blocks);

        var lights = w.LightSourcesNear(new ChunkCoord(1, 0, 0), 4);
        Assert.Equal(2, lights.Count);
        Assert.Contains(lights, l => l.Pos == new Vector3i(Cs + 1, 2, 3) && l.Rgb == 0x204030);
        Assert.Contains(lights, l => l.Pos == new Vector3i(Cs + 4, 5, 6)
            && l.Rgb == FloraTints.ToRgb24(FloraTints.RainbowAt(Cs + 4, 5, 6), 0.5f));
    }

    [Fact]
    public void LiveEdit_PlacesAndRemovesAPlantLight()
    {
        var w = World();
        w.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[Cs * Cs * Cs]);
        Assert.True(w.ApplyBlockChange(3, 4, 5, Rainbow, tint: 0, glow: 0, out _));
        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(FloraTints.ToRgb24(FloraTints.RainbowAt(3, 4, 5), 0.5f), hit.Rgb);

        Assert.True(w.ApplyBlockChange(3, 4, 5, 0, tint: 0, glow: 0, out _)); // harvested
        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
    }

    [Fact]
    public void ARegisteredClassicGlower_CastsTheResolversColour_NotItsDeclaredOne()
    {
        // #2858: a classic glower declares a light colour of its own (its block light), but it is registered so it casts
        // this world's colour of its species — the resolver asks first. A 0 from the resolver falls back to the block.
        // Full-reach values, so the player's natural-emitter switch (a static other tests flip) cannot interfere.
        const ushort Vine = 52;
        int declared = BlockLight.Pack(0x80FF80, BlockLight.DefaultRadius, surfaceOnly: false);
        int worldColour = BlockLight.Pack(0x3040FF, BlockLight.DefaultRadius, surfaceOnly: false);
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(id => id == Vine || id == Lamp ? declared : 0);
        w.SetCellLightResolver((id, _) => id == Vine ? worldColour : 0, new[] { Vine, Lamp });
        w.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[Cs * Cs * Cs]);

        Assert.True(w.ApplyBlockChange(3, 4, 5, Vine, tint: 0, glow: 0, out _));
        Assert.Equal(worldColour, Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4)).Rgb);

        Assert.True(w.ApplyBlockChange(3, 4, 5, Lamp, tint: 0, glow: 0, out _)); // registered, but the resolver says 0
        Assert.Equal(declared, Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4)).Rgb);
    }

    [Fact]
    public void WithoutTheResolver_APlantIsNoLight_AndALampStillIs()
    {
        var w = World();
        w.SetCellLightResolver(null, null);
        w.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[Cs * Cs * Cs]);
        Assert.True(w.ApplyBlockChange(3, 4, 5, Moss, tint: 0, glow: 0, out _));
        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.True(w.ApplyBlockChange(6, 4, 5, Lamp, tint: 0, glow: 0, out _));
        Assert.Equal(0xFFFFFF, Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4)).Rgb);
    }
}
