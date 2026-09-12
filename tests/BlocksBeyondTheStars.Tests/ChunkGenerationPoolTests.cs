// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1817: first-visit chunks are generated on worker threads, each owning a sibling generator. The streamed world
/// must be bit-for-bit the world inline generation makes, and a streaming pass must send exactly what it sent before.
/// #1818: the streaming order prefers the direction of view and travel, and the near ring always comes first.
/// </summary>
public sealed class ChunkGenerationPoolTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_genpool_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SiblingGenerators_OnParallelThreads_ProduceTheSingleThreadChunks()
    {
        var planet = Content.GetPlanet("varied")!;
        var template = new WorldGenerator(20260912, Content);
        template.SetWorldOptionFactors(1.4, 0.7);
        template.SetContinentsEnabled(true);
        template.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);

        var coords = new List<ChunkCoord>();
        for (int cx = -3; cx <= 4; cx++)
            for (int cz = -2; cz <= 2; cz++)
                for (int cy = 2; cy <= 5; cy++)
                {
                    coords.Add(new ChunkCoord(cx, cy, cz));
                }

        var serial = template.CreateSibling();
        serial.SetWorldMode(5472, cratered: false, landingPads: null, locationId: "pool-test:body");
        var expected = coords.Select(c => WorldGenerationGoldenTests.HashChunk(serial.Generate(planet, c))).ToArray();

        var actual = new ulong[coords.Count];
        Parallel.For(0, 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, worker =>
        {
            var gen = template.CreateSibling();
            gen.SetWorldMode(5472, cratered: false, landingPads: null, locationId: "pool-test:body");
            for (int i = 0; i < coords.Count; i++)
            {
                if (i % 4 == worker)
                {
                    actual[i] = WorldGenerationGoldenTests.HashChunk(gen.Generate(planet, coords[i]));
                }
            }
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PooledStreaming_SendsTheSameChunks_WithTheSameBlocks_AsInlineStreaming()
    {
        var inline = Stream("inline", workers: 0, out var inlineServer, out var inlineRepo);
        var pooled = Stream("pooled", workers: 2, out var pooledServer, out var pooledRepo);
        try
        {
            // Same passes, same budgets → the same chunk sets and the same order of arrival.
            Assert.Equal(inline.Order, pooled.Order);
            foreach (var coord in inline.Order)
            {
                Assert.Equal(WorldGenerationGoldenTests.HashChunk(inlineServer.World.GetOrLoadChunk(coord)),
                    WorldGenerationGoldenTests.HashChunk(pooledServer.World.GetOrLoadChunk(coord)));
            }

            Assert.True(pooledServer.ChunksFromWorkersForTest > 0, "no chunk came from a worker thread");
            Assert.Equal(0, inlineServer.ChunksFromWorkersForTest);
        }
        finally
        {
            inlineServer.Stop();
            pooledServer.Stop();
            inlineRepo.Dispose();
            pooledRepo.Dispose();
        }
    }

    private (List<ChunkCoord> Order, int Dummy) Stream(string name, int workers, out SvGameServer server, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = "pool",
            Seed = 77,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 3,
            ChunkStreamPerTick = 16,
            ChunkGenWorkers = workers,
        };
        server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Walker");
        var order = new List<ChunkCoord>();
        var seen = new HashSet<ChunkCoord>();
        for (int tick = 0; tick < 12; tick++)
        {
            server.TickForTest(0.1);
            // SentChunks is a set; rebuild the arrival order by diffing after each pass.
            foreach (var c in p.SentChunks.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
            {
                if (seen.Add(c))
                {
                    order.Add(c);
                }
            }
        }

        return (order, 0);
    }

    [Fact]
    public void StreamPriority_PrefersTheDirectionOfView_AtEqualDistance()
    {
        // Looking along +X (yaw 90°): a chunk 3 ahead streams before the chunk 3 behind and the one 3 beside.
        int ahead = SvGameServer.StreamPriorityKey(3, 0, 0, 1f, 0f, 0f, 0f);
        int beside = SvGameServer.StreamPriorityKey(0, 0, 3, 1f, 0f, 0f, 0f);
        int behind = SvGameServer.StreamPriorityKey(-3, 0, 0, 1f, 0f, 0f, 0f);
        Assert.True(ahead < beside && beside < behind, $"ahead={ahead} beside={beside} behind={behind}");
    }

    [Fact]
    public void StreamPriority_LooksAheadAlongTheVelocity()
    {
        // Flying +Z at 2 chunks of look-ahead: the chunk 4 ahead now outranks the chunk 3 behind.
        int ahead4 = SvGameServer.StreamPriorityKey(0, 0, 4, 0f, 0f, 0f, 2f);
        int behind3 = SvGameServer.StreamPriorityKey(0, 0, -3, 0f, 0f, 0f, 2f);
        Assert.True(ahead4 < behind3, $"ahead4={ahead4} behind3={behind3}");
    }

    [Fact]
    public void StreamPriority_NearRingAlwaysFirst_AndStandingStillIsNearestFirst()
    {
        // The footing column (Chebyshev ≤ 1) beats any far chunk, even one dead ahead on the look-ahead anchor.
        int footing = SvGameServer.StreamPriorityKey(1, -3, 1, 0f, 1f, 0f, 0f);
        int farAheadOnAnchor = SvGameServer.StreamPriorityKey(0, 0, 4, 0f, 1f, 0f, 4f);
        Assert.True(footing < farAheadOnAnchor, $"footing={footing} far={farAheadOnAnchor}");

        // No look direction, no motion: the order is plain distance.
        var offsets = new (int, int, int)[] { (2, 0, 0), (3, 1, 0), (0, 0, -5), (4, -2, 4), (6, 0, 1) };
        var byKey = offsets.OrderBy(o => SvGameServer.StreamPriorityKey(o.Item1, o.Item2, o.Item3, 0f, 0f, 0f, 0f)).ToArray();
        var byDistance = offsets.OrderBy(o => o.Item1 * o.Item1 + o.Item2 * o.Item2 + o.Item3 * o.Item3).ToArray();
        Assert.Equal(byDistance, byKey);
    }

    [Fact]
    public void ChunkGenWorkers_DefaultsToTwo_MapsFromCliAndEnv_AndClamps()
    {
        Assert.Equal(2, new ServerConfig().ChunkGenWorkers);
        var cli = new ServerConfig();
        var applied = cli.ApplyCommandLine(new[] { "--chunk-gen-workers", "4" });
        Assert.Equal(4, cli.ChunkGenWorkers);
        Assert.Contains("chunk-gen-workers", applied);
        Assert.Equal(ServerConfig.ChunkGenWorkersCeiling, new ServerConfig { ChunkGenWorkers = 99 }.ChunkGenWorkers);
        Assert.Equal(0, new ServerConfig { ChunkGenWorkers = -3 }.ChunkGenWorkers);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a SQLite handle can linger on Windows — the temp folder is harmless
        }
    }
}
