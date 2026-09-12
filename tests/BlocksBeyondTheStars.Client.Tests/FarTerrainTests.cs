// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1820–#1823: the far terrain, its haze, the build overlay, chunk visibility and the build order.</summary>
public sealed class FarTerrainTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void FarViewRange_DefaultsPerPlatform_Normalizes_Cycles_AndClampsToSmallWorlds()
    {
        Assert.Equal(1024, FarViewRange.Normalize(FarViewRange.Unset, browserOrMobile: false));
        Assert.Equal(512, FarViewRange.Normalize(FarViewRange.Unset, browserOrMobile: true));
        Assert.Equal(0, FarViewRange.Normalize(0, false));
        Assert.Equal(512, FarViewRange.Normalize(600, false));
        Assert.Equal(1024, FarViewRange.Normalize(5000, false));
        Assert.Equal(512, FarViewRange.Next(0));
        Assert.Equal(1024, FarViewRange.Next(512));
        Assert.Equal(0, FarViewRange.Next(1024));
        Assert.Equal(1024, FarViewRange.ClampToWorld(1024, 6000, WorldConstants.LatitudePeriodFor(6000)));
        Assert.True(FarViewRange.ClampToWorld(1024, 800, WorldConstants.LatitudePeriodFor(800)) < 400, "an asteroid must not wrap into itself");
    }

    [Fact]
    public void FarHaze_OffKeepsTheOldEdge_ThinAirSeesFarther_AndNeverCloserThanTheChunks()
    {
        Assert.Equal(64f * 1.0f, FarHaze.BaseFar(64f, 0, 0f), 3);
        Assert.Equal(64f * 0.85f, FarHaze.BaseFar(64f, 0, 1f), 3);

        float thin = FarHaze.BaseFar(128f, 1024, 0.1f);
        float soupy = FarHaze.BaseFar(128f, 1024, 0.9f);
        Assert.True(thin > soupy, $"thin={thin} soupy={soupy}");
        Assert.True(thin > 800f, "a thin atmosphere shows most of the far range");
        Assert.True(soupy >= 128f);
        Assert.True(FarHaze.BaseFar(256f, 512, 1f) >= 256f, "the haze never ends inside the streamed chunks");
    }

    [Fact]
    public void TheFarTerrainSource_ReproducesTheStreamedTerrain_ColumnForColumn()
    {
        var content = LoadContent();
        using var h = new ClientServerHarness(content, c => c.ViewDistanceChunks = 2);
        FarTerrainWorldInfo? info = null;
        h.Client.FarTerrainWorldInfoReceived += m => info = m;
        h.Join();
        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => info != null && h.Chunks.Count > 40, 80), "world info and chunks should arrive");

        var source = FarTerrainSource.Create(content, h.JoinAccepted!.WorldSeed, info!);
        Assert.NotNull(source);
        int checkedColumns = 0, matching = 0;
        foreach (var chunk in h.Chunks.Values.Take(60))
        {
            var origin = WorldConstants.ChunkOrigin(new ChunkCoord(chunk.Cx, chunk.Cy, chunk.Cz));
            for (int lx = 1; lx < 16; lx += 5)
                for (int lz = 1; lz < 16; lz += 5)
                {
                    int x = origin.X + lx, z = origin.Z + lz;
                    int ground = source!.GroundHeight(x, z);
                    if (WorldConstants.WorldToChunk(ground) != chunk.Cy)
                    {
                        continue; // the column's surface lies in another chunk
                    }

                    checkedColumns++;
                    var top = h.World.GetBlock(x, ground, z);
                    var below = h.World.GetBlock(x, ground - 1, z);
                    if (!top.IsAir && !below.IsAir)
                    {
                        matching++;
                    }
                }
        }

        Assert.True(checkedColumns >= 10, $"too few surface columns sampled ({checkedColumns})");
        Assert.True(matching >= checkedColumns * 0.9, $"the far terrain disagrees with the streamed chunks: {matching}/{checkedColumns}");
    }

    [Fact]
    public void TheOverlay_KeepsTheNewestTile_WrapsLookups_AndPlansRequestsNearestFirstOnce()
    {
        var overlay = new FarTerrainOverlay();
        overlay.Reset(6000);
        var tile = new FarTerrainTile
        {
            TileX = 0,
            TileZ = 0,
            Version = 2,
            Cells = new byte[] { 0, 17 },
            TopY = new short[] { 120, 90 },
            Blocks = new ushort[] { 5, 6 },
            Tints = new[] { 0, 0x00FF00 },
        };
        Assert.True(overlay.Apply(tile));
        Assert.True(overlay.TryGetTop(1, 1, out var top));
        Assert.Equal(121, top.Top);
        Assert.True(overlay.TryGetTop(6000 + 5, 5, out var wrapped), "a lookup one lap east wraps to the same tile");
        Assert.Equal(91, wrapped.Top);
        Assert.False(overlay.Apply(new FarTerrainTile { TileX = 0, TileZ = 0, Version = 1 }), "an older version is ignored");
        Assert.True(overlay.TryGetTopInArea(0, 0, 8, out var best));
        Assert.Equal(121, best.Top);

        var first = overlay.NextRequest(100f, 100f, 256, 4, now: 10.0);
        Assert.Equal(8, first.Length);
        Assert.Equal((1, 1), (first[0], first[1])); // the tile under (100, 100)
        var second = overlay.NextRequest(100f, 100f, 256, 1000, now: 11.0);
        Assert.DoesNotContain((first[2], first[3]), Pairs(second));

        // An unanswered request is asked again after the retry window; an answered tile (0,0) is not.
        var retry = overlay.NextRequest(100f, 100f, 256, 1000, now: 11.0 + FarTerrainOverlay.RetryAfterSeconds + 1);
        Assert.Contains((first[2], first[3]), Pairs(retry));
        Assert.DoesNotContain((0, 0), Pairs(retry));
    }

    private static IEnumerable<(int, int)> Pairs(int[] flat)
    {
        for (int i = 0; i + 1 < flat.Length; i += 2)
        {
            yield return (flat[i], flat[i + 1]);
        }
    }

    [Fact]
    public void TheLayout_CoversThePlayer_AndKeepsTheFarLevelOutOfTheNearDisc()
    {
        var keys = FarTerrainLayout.Desired(500f, -300f, 1024);
        Assert.Contains(keys, k => k.Level == 0 && k.OriginX <= 500 && k.OriginX + 128 > 500 && k.OriginZ <= -300 && k.OriginZ + 128 > -300);
        Assert.Contains(keys, k => k.Level == 1);
        float inner = FarTerrainLayout.InnerRadius(1, 1024);
        foreach (var k in keys.Where(k => k.Level == 1))
        {
            int size = FarTerrainLayout.PatchSize(1);
            float farDx = Math.Max(Math.Abs(k.OriginX - 500f), Math.Abs(k.OriginX + size - 500f));
            float farDz = Math.Max(Math.Abs(k.OriginZ + 300f), Math.Abs(k.OriginZ + size + 300f));
            Assert.True(farDx * farDx + farDz * farDz >= inner * inner, $"{k} lies wholly inside the near disc");
        }

        Assert.Empty(FarTerrainLayout.Desired(0f, 0f, 0));
        Assert.DoesNotContain(FarTerrainLayout.Desired(0f, 0f, 256), k => k.Level == 1);
    }

    [Fact]
    public void APatch_IsAClosedGridWithSkirts_AndABuildRaisesItsVertex()
    {
        var content = LoadContent();
        var info = new FarTerrainWorldInfo { WorldId = 1, LocationId = "sys0-p1", PlanetType = "rocky", Circumference = 6000 };
        var source = FarTerrainSource.Create(content, 424242, info)!;
        var overlay = new FarTerrainOverlay();
        overlay.Reset(6000);
        var key = new FarPatchKey(0, 0, 0);

        var plain = new FarPatchBuilder(key, 512, source).BuildGeometry(overlay, (s, e, t) => 0xFF808080);
        Assert.Equal(17 * 17 + 4 * 17, plain.VertexCount);
        Assert.Equal(16 * 16 * 6 + 4 * 16 * 6, plain.Indices.Length);
        Assert.All(plain.Indices, i => Assert.InRange(i, 0, plain.VertexCount - 1));

        // A 400-block tower in the cell under vertex (1, 1) = world (8, 8).
        overlay.Apply(new FarTerrainTile
        {
            TileX = 0,
            TileZ = 0,
            Version = 1,
            Cells = new byte[] { (byte)(2 * FarTerrainTile.CellsPerSide + 2) },
            TopY = new short[] { 400 },
            Blocks = new ushort[] { 3 },
            Tints = new[] { 0 },
        });
        var built = new FarPatchBuilder(key, 512, source).BuildGeometry(overlay, (s, e, t) => e ? 0xFF0000FFu : 0xFF808080u);
        int v = 1 * 17 + 1;
        Assert.True(built.Positions[v * 3 + 1] > 390f, $"the tower should raise its vertex (y={built.Positions[v * 3 + 1]})");
        Assert.Equal(0xFF0000FFu, built.Colors[v]);
    }

    [Fact]
    public void Connectivity_OpenSolidAndATunnel()
    {
        Assert.Equal(ChunkVisibility.AllConnected, ChunkVisibility.ComputeConnectivity((x, y, z) => true));
        Assert.Equal(0, ChunkVisibility.ComputeConnectivity((x, y, z) => false));

        // A straight tunnel along X at y = z = 8: only −X and +X connect.
        ushort tunnel = ChunkVisibility.ComputeConnectivity((x, y, z) => y == 8 && z == 8);
        Assert.True(ChunkVisibility.Connects(tunnel, ChunkVisibility.NegX, ChunkVisibility.PosX));
        Assert.False(ChunkVisibility.Connects(tunnel, ChunkVisibility.NegX, ChunkVisibility.PosY));
        Assert.False(ChunkVisibility.Connects(tunnel, ChunkVisibility.NegZ, ChunkVisibility.PosZ));
    }

    [Fact]
    public void TheWalk_StopsAtSolidRock_ButSeesThroughOpenAir()
    {
        var camera = new ChunkCoord(0, 0, 0);
        var sealedCave = new List<ChunkCoord>();
        ChunkVisibility.Walk(camera, c => c == camera ? ChunkVisibility.AllConnected : 0, 4, -4, 4, sealedCave);
        Assert.Contains(new ChunkCoord(1, 0, 0), sealedCave);      // the rock around the cave is seen (its faces)
        Assert.DoesNotContain(new ChunkCoord(3, 0, 0), sealedCave); // nothing behind it
        Assert.DoesNotContain(new ChunkCoord(0, 3, 0), sealedCave); // no surface above a sealed cave

        var openAir = new List<ChunkCoord>();
        ChunkVisibility.Walk(camera, c => ChunkVisibility.AllConnected, 2, -1, 1, openAir);
        Assert.Equal(5 * 3 * 5, openAir.Count);

        var unloaded = new List<ChunkCoord>();
        ChunkVisibility.Walk(camera, c => Math.Abs(c.X) <= 1 ? ChunkVisibility.AllConnected : ChunkVisibility.Blocked, 4, 0, 0, unloaded);
        Assert.DoesNotContain(unloaded, c => Math.Abs(c.X) > 1);

        // The vertical-LOD case: a ridge 6 chunks east whose loaded band sits 3 chunks above the camera, the air between
        // never streamed. Passable air above every column's band lets the walk reach the ridge over the top; the ridge's
        // rock (loaded, solid) is drawn, the passable air is not.
        var ridge = new List<ChunkCoord>();
        ChunkVisibility.Walk(camera, c =>
        {
            if (c.X == 0 && c.Y == 0) return ChunkVisibility.AllConnected; // the camera's column at ground level (air)
            if (c.X == 6 && c.Y == 3) return 0;                            // the ridge's surface band chunk (solid)
            if (c.X == 6) return c.Y > 3 ? ChunkVisibility.Passable : ChunkVisibility.Blocked;
            return c.Y > 0 ? ChunkVisibility.Passable : ChunkVisibility.Blocked;
        }, 8, -2, 5, ridge);
        Assert.Contains(new ChunkCoord(6, 3, 0), ridge);
        Assert.DoesNotContain(ridge, c => c.Y > 0 && c.X != 6); // pass-through air is never "visible"
    }

    [Fact]
    public void BuildPriority_NearFirst_ThenAheadBeforeBehind()
    {
        float footing = ChunkBuildPriority.Key(8, -16, 8, 1f, 0f, 0f, 0f);
        float farAhead = ChunkBuildPriority.Key(64, 0, 0, 1f, 0f, 0f, 0f);
        float farBehind = ChunkBuildPriority.Key(-64, 0, 0, 1f, 0f, 0f, 0f);
        Assert.True(footing < farAhead && farAhead < farBehind);

        float movingAhead = ChunkBuildPriority.Key(0, 0, 96, 0f, 0f, 0f, 48f);
        float movingBehind = ChunkBuildPriority.Key(0, 0, -64, 0f, 0f, 0f, 48f);
        Assert.True(movingAhead < movingBehind, "the look-ahead pulls the chunks the player flies into forward");
    }
}
