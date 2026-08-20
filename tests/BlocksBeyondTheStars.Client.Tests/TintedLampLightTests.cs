// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Tinted lamp light (#1126): the light-source registry's colour priority is glow &gt; dye-on-a-light-source
/// &gt; the block's natural colour — a red-dyed lamp floods red light, while a dye on a plain block never
/// turns it into a lamp. Covers both registry paths (the live edit and the chunk-store scan).
/// </summary>
public sealed class TintedLampLightTests
{
    private const ushort Lamp = 42;   // resolver: a light source with a warm natural colour
    private const ushort Stone = 7;   // resolver: not a light source
    private const int Natural = 0xFFC747;

    private static ClientWorld World()
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(id => id == Lamp ? Natural : 0);
        w.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[WorldConstants.ChunkSize * WorldConstants.ChunkSize * WorldConstants.ChunkSize]);
        return w;
    }

    [Fact]
    public void DyedLamp_CastsItsDyeColour()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Lamp, tint: 0xFF00FF, glow: 0, out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(0xFF00FF, hit.Rgb);
    }

    [Fact]
    public void PlainLamp_KeepsItsNaturalColour()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Lamp, tint: 0, glow: 0, out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(Natural, hit.Rgb);
    }

    [Fact]
    public void ExplicitGlow_BeatsTheDye()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Lamp, tint: 0xFF00FF, glow: 0x00FF00, out _));

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(0x00FF00, hit.Rgb);
    }

    [Fact]
    public void DyeOnANonSourceBlock_NeverCreatesALamp()
    {
        var w = World();
        Assert.True(w.ApplyBlockChange(3, 4, 5, Stone, tint: 0xFF00FF, glow: 0, out _));

        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
    }

    [Fact]
    public void ChunkStoreScan_AppliesTheSamePriority()
    {
        // The bulk path: a stored chunk whose sparse modifier arrays dye one lamp cell.
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetBlockLightResolver(id => id == Lamp ? Natural : 0);

        var blocks = new ushort[WorldConstants.ChunkSize * WorldConstants.ChunkSize * WorldConstants.ChunkSize];
        int idx = WorldConstants.LocalIndex(3, 4, 5);
        blocks[idx] = Lamp;
        w.StoreChunk(new ChunkCoord(0, 0, 0), blocks,
            modIndex: new[] { idx }, modTint: new[] { 0x123456 }, modGlow: new[] { 0 },
            shapeIndex: System.Array.Empty<int>(), shapeData: System.Array.Empty<int>());

        var hit = Assert.Single(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 4));
        Assert.Equal(0x123456, hit.Rgb);
    }
}
