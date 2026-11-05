// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3208: the near full-column stream radius and its upward span are host knobs (default 4 chunks and 3 up; the
/// browser keeps 3 and 2). Pins the defaults and clamps, that every near chunk any radius/span can stream stays inside the
/// sweep's keep radius and the sent-set prune for every view distance (no evict-and-re-stream thrash), and — with a real
/// world — that the wider radius streams the full column 4 chunks out, with the measured per-player cost printed.</summary>
public sealed class NearColumnRadiusTests : IDisposable
{
    private const int BytesPerChunk = WorldConstants.BlocksPerChunk * sizeof(ushort);

    private readonly string _root;
    private readonly GameContent _content;
    private readonly ITestOutputHelper _output;

    public NearColumnRadiusTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "bbts_nearcol_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void Knobs_DefaultToFourChunksAndThreeUp_AndClampIntoTheirRanges()
    {
        var config = new ServerConfig();
        Assert.Equal(4, config.NearColumnRadiusChunks);
        Assert.Equal(3, config.NearColumnChunksUp);
        Assert.Equal(512, config.MaxAnchoredChunks);

        config.NearColumnRadiusChunks = 1;
        config.NearColumnChunksUp = 1;
        config.MaxAnchoredChunks = -3;
        Assert.Equal((2, 2, 0), (config.NearColumnRadiusChunks, config.NearColumnChunksUp, config.MaxAnchoredChunks));

        config.NearColumnRadiusChunks = 40;
        config.NearColumnChunksUp = 9;
        config.MaxAnchoredChunks = int.MaxValue;
        Assert.Equal((ServerConfig.NearColumnRadiusCeiling, ServerConfig.NearColumnChunksUpCeiling, ServerConfig.MaxAnchoredChunksCeiling),
            (config.NearColumnRadiusChunks, config.NearColumnChunksUp, config.MaxAnchoredChunks));

        // The knobs survive the server.json round trip like every other key.
        var reloaded = ServerConfig.FromJson(new ServerConfig { NearColumnRadiusChunks = 6, NearColumnChunksUp = 2, MaxAnchoredChunks = 64 }.ToJson());
        Assert.Equal((6, 2, 64), (reloaded.NearColumnRadiusChunks, reloaded.NearColumnChunksUp, reloaded.MaxAnchoredChunks));
    }

    /// <summary>The near column streams 3 chunks down and the knob's span up, out to the radius but never past the view's
    /// disc. Every such chunk must lie inside the far-chunk sweep's keep radius (view + 4) and the per-session sent-set prune
    /// (min(view + 4, 20)) — measured chunk to chunk like the sweep does — or it would be evicted and re-streamed every 10 s.
    /// This is why the upward span stops at 3: a fourth chunk up is outside the keep radius at view distance 1.</summary>
    [Fact]
    public void NearColumns_StayInsideTheSweepKeepRadius_ForEveryRadiusSpanAndSliderValue()
    {
        for (int radius = ServerConfig.NearColumnRadiusMin; radius <= ServerConfig.NearColumnRadiusCeiling; radius++)
            for (int up = ServerConfig.NearColumnChunksUpMin; up <= ServerConfig.NearColumnChunksUpCeiling; up++)
                for (int view = 1; view <= 16; view++)
                {
                    int stream = view + 1;
                    int keep = Math.Min(view + 4, 20);
                    int deepest = Math.Max(3, up); // 3 chunks down, `up` chunks up
                    for (int dx = -stream; dx <= stream; dx++)
                        for (int dz = -stream; dz <= stream; dz++)
                        {
                            if (!SvGameServer.IsColumnInStreamDisc(dx, dz, stream) || !SvGameServer.IsNearFullColumn(dx, dz, radius))
                            {
                                continue;
                            }

                            Assert.True(dx * dx + dz * dz + deepest * deepest <= keep * keep,
                                $"radius {radius}, up {up}, view {view}: near column ({dx},{dz}) reaches past the keep radius {keep}");
                        }
                }

        // And a fourth chunk up WOULD thrash: column (3, 1) streams at view 1 and lies within radius 4.
        Assert.True(SvGameServer.IsColumnInStreamDisc(3, 1, 2) && SvGameServer.IsNearFullColumn(3, 1, 4));
        Assert.True(3 * 3 + 1 * 1 + 4 * 4 > (1 + 4) * (1 + 4));
    }

    [Fact]
    public void IsNearFullColumn_IsAChebyshevSquare()
    {
        Assert.True(SvGameServer.IsNearFullColumn(4, -4, 4));
        Assert.True(SvGameServer.IsNearFullColumn(0, 4, 4));
        Assert.False(SvGameServer.IsNearFullColumn(5, 0, 4));
        Assert.False(SvGameServer.IsNearFullColumn(4, 0, 3));
    }

    /// <summary>A real world, filled until the view settles, at view distance 4 and 8 for the pre-#3208 shape (radius 3,
    /// 2 up), the radius alone (4, 2 up) and the new default (4, 3 up). A column 4 chunks out carries its full vertical
    /// span only with radius 4, and the streamed set grows by the expected ring. The numbers are printed (the #3208 report).</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void WiderRadius_StreamsTheFullColumnFourChunksOut_AndThePerPlayerCostIsMeasured()
    {
        foreach (int view in new[] { 4, 8 })
        {
            var old = StreamUntilSettled($"old_{view}", view, radius: 3, up: 2);
            var wider = StreamUntilSettled($"r4_{view}", view, radius: 4, up: 2);
            var standard = StreamUntilSettled($"r4u3_{view}", view, radius: 4, up: 3);
            foreach (var (label, run) in new[] { ("radius 3, 2 up (before)", old), ("radius 4, 2 up", wider), ("radius 4, 3 up (default)", standard) })
            {
                _output.WriteLine($"view {view}, {label}: {run.Sent} chunks streamed per player = {run.Sent * BytesPerChunk / 1024.0 / 1024.0:F2} MB raw voxels; " +
                                  $"{run.Loaded} chunks resident on the server; column 4 out: {run.LayersFourOut} layers");
            }

            Assert.Equal(6, wider.LayersFourOut);    // -3..+2: the full near column
            Assert.Equal(7, standard.LayersFourOut); // -3..+3
            Assert.InRange(old.LayersFourOut, 1, 6); // the far surface band (6 = a flooded column's capped band)
            Assert.True(wider.Sent > old.Sent, $"view {view}: radius 4 must stream more ({wider.Sent}) than radius 3 ({old.Sent})");
            Assert.True(standard.Sent > wider.Sent, $"view {view}: the third chunk up must stream more ({standard.Sent} vs {wider.Sent})");
        }
    }

    private (int Sent, int Loaded, int LayersFourOut) StreamUntilSettled(string name, int view, int radius, int up)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = view,
            ChunkStreamPerTick = ServerConfig.ChunkStreamPerTickCeiling,
            NearColumnRadiusChunks = radius,
            NearColumnChunksUp = up,
            MaxAnchoredChunks = 0, // the streamed set alone
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Surveyor");
        var center = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        // Fill until nothing new arrives; a tiny dt so the 10 s sweep never trips while streaming.
        int stable = 0, last = -1;
        for (int i = 0; i < 1000 && stable < 3; i++)
        {
            server.TickForTest(0.01);
            stable = p.Streaming.SentChunks.Count == last ? stable + 1 : 0;
            last = p.Streaming.SentChunks.Count;
        }

        int layers = 0;
        for (int cy = center.Y - 8; cy <= center.Y + 8; cy++)
        {
            if (p.Streaming.SentChunks.Contains(WorldConstants.CanonicalChunk(new ChunkCoord(center.X + 4, cy, center.Z), server.World.Circumference)))
            {
                layers++;
            }
        }

        return (p.Streaming.SentChunks.Count, server.World.LoadedChunkCount, layers);
    }
}
