// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Persisted exploration (#1113): streamed terrain fills a bounded per-body explored-cell bitmap that
/// survives a save/reload; the first landing on a body writes a "place" Discoveries entry with a knowledge
/// grant exactly once; and pre-#1113 saves backfill their landed bodies into the ledger silently on join.
/// </summary>
public sealed class ExplorationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ExplorationTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_explore_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ServerConfig Config() => new()
    {
        WorldName = "explore",
        Seed = 424242,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
    };

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new BlocksBeyondTheStars.Networking.Messages.JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    // ---- The grid itself ---------------------------------------------------------------------

    [Fact]
    public void ExploredGrid_StaysUnderTheHardByteBound_ForEveryLegalCircumference()
    {
        // 16000 is the documented planet extreme (WorldConstants size bands + bias); anything the
        // generator can roll must fit the cap, or MarkExploredCell would silently stop recording.
        foreach (int circumference in new[] { 800, 1600, 2500, 4000, 6000, 12000, 16000 })
        {
            var (cols, rows) = ExploredMap.GridFor(circumference);
            Assert.InRange(ExploredMap.ByteSize(cols, rows), 1, ExploredMap.MaxBytesPerBody);
        }
    }

    [Fact]
    public void CellIndex_CoversTheCanonicalDomain_AndRejectsOutside()
    {
        const int circ = 6000;
        int chunksAround = circ / WorldConstants.ChunkSize;                        // 375
        int latChunks = WorldConstants.LatitudePeriodFor(circ) / WorldConstants.ChunkSize;
        var (cols, rows) = ExploredMap.GridFor(circ);

        // The four canonical corners land inside the grid.
        Assert.InRange(ExploredMap.CellIndex(0, -latChunks / 2, circ), 0, cols * rows - 1);
        Assert.InRange(ExploredMap.CellIndex(chunksAround - 1, latChunks / 2 - 1, circ), 0, cols * rows - 1);

        // Outside the canonical domain reads as "no cell" instead of corrupting a neighbour's bit.
        Assert.Equal(-1, ExploredMap.CellIndex(-1, 0, circ));
        Assert.Equal(-1, ExploredMap.CellIndex(chunksAround, 0, circ));
        Assert.Equal(-1, ExploredMap.CellIndex(0, latChunks, circ));
    }

    // ---- Streaming fills the bitmap; the bitmap survives a reload ------------------------------

    [Fact]
    public void StreamedChunks_FillTheBitmap_AndItSurvivesAReload()
    {
        var paths = new SaveGamePaths(_root, "explore");
        string bodyId;
        byte[] before;
        using (var repo = new SqliteWorldRepository(paths))
        {
            using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Scout");
            for (int i = 0; i < 30; i++)
            {
                server.Tick(0.1); // let StreamChunks work through its per-tick budget
            }

            var state = server.Sessions[1].State;
            bodyId = state.CurrentLocationId;
            Assert.True(state.ExploredCells.TryGetValue(bodyId, out var map), "streaming must create the body's bitmap");
            before = (byte[])map!.Clone();
            Assert.Contains(before, b => b != 0); // at least one cell marked around the spawn
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(Config(), _content, serverTransport, repo2);
            server.Start();
            JoinAndDrain(server, client, "Scout");

            var state = server.Sessions[1].State;
            Assert.True(state.ExploredCells.TryGetValue(bodyId, out var reloaded), "the bitmap must survive the reload");
            foreach (int i in Enumerable.Range(0, before.Length))
            {
                Assert.Equal(before[i], (byte)(reloaded![i] & before[i])); // every explored bit is still set
            }

            server.Stop();
        }
    }

    // ---- First landing = a "place" discovery + knowledge, exactly once -------------------------

    [Fact]
    public void FirstLanding_WritesAPlaceEntry_AndPaysKnowledge_ExactlyOnce()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "explore"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Scout");

        var session = server.Sessions[1];
        string home = session.State.CurrentLocationId;
        Assert.Contains("place:" + home, session.State.Scanned);
        Assert.True(session.State.ScannedNames.ContainsKey("place:" + home), "the entry carries the body's display name");

        // The SPAWN world records its entry but pays NOTHING — a join-time grant would flip VEGA's
        // veteran heuristic (KnowledgePoints > 0) and skip the onboarding for every fresh save.
        Assert.Equal(0, session.State.KnowledgePoints);

        // The first real first-landing elsewhere pays — exactly once.
        var other = server.Galaxy.Systems.SelectMany(s => s.Bodies).First(b => b.Id != home);
        server.MarkArrivedOnBodyForTest(session, other.Id);
        Assert.Contains("place:" + other.Id, session.State.Scanned);
        Assert.Equal(SvGameServer.KnowledgeFirstLanding, session.State.KnowledgePoints);

        server.MarkArrivedOnBodyForTest(session, other.Id);
        Assert.Equal(SvGameServer.KnowledgeFirstLanding, session.State.KnowledgePoints);
    }

    // ---- Pre-#1113 saves: landed bodies backfill silently on join ------------------------------

    [Fact]
    public void Join_BackfillsPlacesForOldSaves_WithoutAKnowledgeWindfall()
    {
        var paths = new SaveGamePaths(_root, "explore");
        int knowledgeBefore;
        using (var repo = new SqliteWorldRepository(paths))
        {
            using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Vet");

            // Simulate a pre-#1113 save: landed bodies exist, but no "place" ledger entries.
            var state = server.Sessions[1].State;
            state.Scanned.RemoveWhere(k => k.StartsWith("place:", StringComparison.Ordinal));
            knowledgeBefore = state.KnowledgePoints;
            server.Stop(); // saves the doctored state
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
            using var client = new LoopbackClientTransport(link);
            var server = new SvGameServer(Config(), _content, serverTransport, repo2);
            server.Start();
            JoinAndDrain(server, client, "Vet");

            var state = server.Sessions[1].State;
            Assert.Contains(state.Scanned, k => k.StartsWith("place:", StringComparison.Ordinal));
            Assert.Equal(knowledgeBefore, state.KnowledgePoints); // backfill never pays knowledge
            server.Stop();
        }
    }
}
