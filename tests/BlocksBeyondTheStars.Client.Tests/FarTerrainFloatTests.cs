// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3958–#3960: what floats over the far terrain. A height field drawn up to a sky island's top made every island a
/// mountain from afar; now the height field follows the ground, every free band is a block slab with sky under it, the
/// waterfalls under island rims are water curtains, and a build on an island raises the island instead of a pillar.
/// </summary>
public sealed class FarTerrainFloatTests
{
    private const long Seed = 424242;
    private const int Circumference = 6000;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static FarTerrainSource SkySource() => FarTerrainSource.Create(Content, Seed, new FarTerrainWorldInfo
    {
        WorldId = 1,
        LocationId = "sys0-p1",
        PlanetType = "skylands",
        Circumference = Circumference,
        TerrainGeneration = WorldDescription.CurrentTerrainGeneration,
    })!;

    private static WorldGenerator SkyGenerator()
    {
        var generator = new WorldGenerator(Seed, Content);
        generator.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        generator.SetWorldMode(Circumference, false, new List<LandingPadFlatten>(), "sys0-p1");
        return generator;
    }

    /// <summary>#2333/#3958: the highest band is still what the eye sees on top — but a sky island with open air under it
    /// is a slab of its own over the ground, not the column's height-field top.</summary>
    [Fact]
    public void ASkyIsland_IsAFloatingSlabOverTheGround_NotTheColumnsTop()
    {
        var source = SkySource();
        var generator = SkyGenerator();
        var planet = Content.GetPlanet("skylands")!;
        var floats = new FarFloat[FarTerrainSource.MaxFloats];
        int islands = 0;
        for (int x = 0; x < 2000 && islands < 25; x += 7)
            for (int z = -900; z < 900 && islands < 25; z += 11)
            {
                if (!generator.TryGetHighestBand(planet, x, z, out var band))
                {
                    continue;
                }

                var sample = source.Sample(x, z, detail: true, floats, out int count);
                int seen = sample.Top;
                for (int k = 0; k < count; k++)
                {
                    if (floats[k].Kind == FarFloatKind.Solid)
                    {
                        seen = Math.Max(seen, floats[k].Top);
                        Assert.True(floats[k].Bottom > sample.Top + FarTerrainSource.AttachGap, $"open air under the slab at ({x}, {z})");
                        Assert.True(floats[k].Top > floats[k].Bottom);
                    }
                }

                Assert.Equal(Math.Max(band.Top + 1, sample.Top), seen);
                if (count > 0 && floats[0].Kind == FarFloatKind.Solid)
                {
                    Assert.Equal(sample.Top, source.Sample(x, z, detail: true).Top); // the plain overload: the same height field
                    islands++;
                }
            }

        Assert.True(islands >= 10, $"a sky world should have floating island columns in the scanned area ({islands})");
    }

    /// <summary>#3958: a patch over an island keeps its height-field vertex on the ground and carries the island as slab
    /// geometry from the island's underside to its top.</summary>
    [Fact]
    public void APatchOverAnIsland_KeepsTheGroundVertex_AndAddsTheSlab()
    {
        var source = SkySource();
        var (x, z, island, ground) = FindFloat(source, FarFloatKind.Solid);
        var key = new FarPatchKey(0, FloorDiv(x, 128), FloorDiv(z, 128));
        int i = (x - key.OriginX) / 8, j = (z - key.OriginZ) / 8;
        var overlay = new FarTerrainOverlay();
        overlay.Reset(Circumference);

        var geo = new FarPatchBuilder(key, 512, source).BuildGeometry(overlay, (s, e, t) => 0xFF808080u);
        float offset = FarTerrainLayout.HeightOffset(0);
        Assert.Equal(17 * 17 + 4 * 17, geo.GridVertexCount);
        Assert.True(geo.VertexCount > geo.GridVertexCount, "the island adds slab vertices");
        Assert.All(geo.Indices, n => Assert.InRange(n, 0, geo.VertexCount - 1));
        Assert.Equal(ground + offset, geo.Positions[((j * 17) + i) * 3 + 1], 3);

        var (bottom, top) = SlabSpanAt(geo, i * 8f, j * 8f);
        Assert.Equal(island.Bottom + offset, bottom, 3);
        Assert.Equal(island.Top + offset, top, 3);
        Assert.True(geo.MaxY >= island.Top + offset && geo.MinY <= ground + offset, "the bounds hold the slab");
    }

    /// <summary>#3959: a waterfall under an island rim is a column of water-coloured faces from its foot to the island.</summary>
    [Fact]
    public void AWaterfall_IsAWaterCurtainFromItsFootToTheIsland()
    {
        var source = SkySource();
        var (x, z, fall, _) = FindFloat(source, FarFloatKind.Waterfall);
        Assert.Equal(FarSurface.Water, fall.Surface.Surface);
        var key = new FarPatchKey(0, FloorDiv(x, 128), FloorDiv(z, 128));
        float cx = x - key.OriginX, cz = z - key.OriginZ;
        var overlay = new FarTerrainOverlay();
        overlay.Reset(Circumference);
        const uint Water = 0xFF00FF00u;
        var geo = new FarPatchBuilder(key, 512, source)
            .BuildGeometry(overlay, (s, e, t) => s.Surface == FarSurface.Water && s.Top == fall.Top ? Water : 0xFF808080u);

        float offset = FarTerrainLayout.HeightOffset(0);
        float lo = float.MaxValue, hi = float.MinValue;
        for (int v = geo.GridVertexCount; v < geo.VertexCount; v++)
        {
            if (geo.Colors[v] == Water && Math.Abs(geo.Positions[v * 3] - cx) <= 4f && Math.Abs(geo.Positions[v * 3 + 2] - cz) <= 4f)
            {
                lo = Math.Min(lo, geo.Positions[v * 3 + 1]);
                hi = Math.Max(hi, geo.Positions[v * 3 + 1]);
                Assert.Equal(0f, geo.Normals[v * 3 + 1]); // a curtain: side faces only
            }
        }

        Assert.Equal(fall.Bottom + offset, lo, 3);
        Assert.Equal(fall.Top + offset, hi, 3);
    }

    /// <summary>#3960: a build standing on an island raises the island's slab, never a pillar from the ground.</summary>
    [Fact]
    public void ABuildOnAnIsland_RaisesTheSlab_AndLeavesTheGroundVertex()
    {
        var source = SkySource();
        var (x, z, island, ground) = FindFloat(source, FarFloatKind.Solid);
        var key = new FarPatchKey(0, FloorDiv(x, 128), FloorDiv(z, 128));
        int i = (x - key.OriginX) / 8, j = (z - key.OriginZ) / 8;
        int tx = FloorDiv(x, 64), tz = FloorDiv(z, 64);
        int roof = island.Top + 9; // the top block of a house on the island (its top face one higher)
        var overlay = new FarTerrainOverlay();
        overlay.Reset(Circumference);
        overlay.Apply(new FarTerrainTile
        {
            TileX = tx,
            TileZ = tz,
            Version = 1,
            Cells = new[] { (byte)((((z - tz * 64) / 4) * FarTerrainTile.CellsPerSide) + ((x - tx * 64) / 4)) },
            TopY = new[] { (short)roof },
            Blocks = new ushort[] { 3 },
            Tints = new[] { 0 },
        });

        const uint Built = 0xFF0000FFu;
        var geo = new FarPatchBuilder(key, 512, source).BuildGeometry(overlay, (s, e, t) => e ? Built : 0xFF808080u);
        float offset = FarTerrainLayout.HeightOffset(0);
        int gridVertex = (j * 17) + i;
        Assert.Equal(ground + offset, geo.Positions[gridVertex * 3 + 1], 3);
        Assert.NotEqual(Built, geo.Colors[gridVertex]);

        var (bottom, top) = SlabSpanAt(geo, i * 8f, j * 8f);
        Assert.Equal(island.Bottom + offset, bottom, 3);
        Assert.Equal(roof + 1 + offset, top, 3);
        bool builtTop = false;
        for (int v = geo.GridVertexCount; v < geo.VertexCount; v++)
        {
            builtTop |= geo.Colors[v] == Built && Math.Abs(geo.Positions[v * 3 + 1] - top) < 0.01f;
        }

        Assert.True(builtTop, "the raised slab top wears the build's colour");
    }

    /// <summary>A world without bands has no floats and its patches no extra vertices (the gate).</summary>
    [Fact]
    public void AWorldWithoutBands_FloatsNothing()
    {
        var source = FarTerrainSource.Create(Content, Seed, new FarTerrainWorldInfo
        {
            WorldId = 1,
            LocationId = "sys0-p1",
            PlanetType = "rocky",
            Circumference = Circumference,
        })!;
        var overlay = new FarTerrainOverlay();
        overlay.Reset(Circumference);
        var geo = new FarPatchBuilder(new FarPatchKey(0, 3, -2), 512, source).BuildGeometry(overlay, (s, e, t) => 0xFF808080u);
        Assert.Equal(geo.GridVertexCount, geo.VertexCount);
        Assert.Equal(16 * 16 * 6 + 4 * 16 * 6, geo.Indices.Length);
    }

    /// <summary>The far level's slabs are discarded inside the near level's whole reach, not only inside the height field's
    /// overlap — a coarse island beside the fine one in the overlap ring would show twice.</summary>
    [Fact]
    public void TheFarLevelsSlabs_StayOutOfTheNearLevelsWholeReach()
    {
        var source = SkySource();
        var overlay = new FarTerrainOverlay();
        overlay.Reset(Circumference);
        var near = new FarPatchBuilder(new FarPatchKey(0, 0, 0), 1024, source).BuildGeometry(overlay, (s, e, t) => 0u);
        var far = new FarPatchBuilder(new FarPatchKey(1, 2, 0), 1024, source).BuildGeometry(overlay, (s, e, t) => 0u);
        Assert.Equal(0f, near.FloatInnerRadius);
        Assert.Equal(FarTerrainLayout.NearReach(1024), far.FloatInnerRadius);
        Assert.Equal(FarTerrainLayout.NearReach(1024) - FarTerrainLayout.LevelOverlap, far.InnerRadius);
    }

    /// <summary>The first point of the near level's 8-block grid that carries a float of the kind: its position, the float
    /// (the lowest of the kind) and the column's height-field top.</summary>
    private static (int X, int Z, FarFloat Float, int Ground) FindFloat(FarTerrainSource source, FarFloatKind kind)
    {
        var floats = new FarFloat[FarTerrainSource.MaxFloats];
        for (int x = 0; x < 4000; x += 8)
            for (int z = -1200; z < 1200; z += 8)
            {
                var sample = source.Sample(x, z, detail: true, floats, out int count);
                int solids = 0;
                for (int k = 0; k < count; k++)
                {
                    solids += floats[k].Kind == FarFloatKind.Solid ? 1 : 0;
                }

                for (int k = 0; k < count; k++)
                {
                    if (floats[k].Kind == kind && (kind != FarFloatKind.Solid || solids == 1)) // one slab: a clear span to check
                    {
                        return (x, z, floats[k], sample.Top);
                    }
                }
            }

        throw new InvalidOperationException($"no {kind} float on the sky world's scanned grid");
    }

    /// <summary>The lowest bottom face and the highest top face of the slabs centred on (cx, cz). The floating shapes are
    /// quads of four vertices each after the height field; a top or bottom quad's centre is its point.</summary>
    private static (float Bottom, float Top) SlabSpanAt(FarPatchGeometry geo, float cx, float cz)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int v = geo.GridVertexCount; v + 3 < geo.VertexCount; v += 4)
        {
            float x = 0f, z = 0f;
            for (int c = 0; c < 4; c++)
            {
                x += geo.Positions[(v + c) * 3] * 0.25f;
                z += geo.Positions[(v + c) * 3 + 2] * 0.25f;
            }

            if (Math.Abs(x - cx) < 0.01f && Math.Abs(z - cz) < 0.01f && geo.Normals[v * 3 + 1] != 0f)
            {
                lo = Math.Min(lo, geo.Positions[v * 3 + 1]);
                hi = Math.Max(hi, geo.Positions[v * 3 + 1]);
            }
        }

        Assert.True(lo <= hi, $"no slab corners at ({cx}, {cz})");
        return (lo, hi);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
