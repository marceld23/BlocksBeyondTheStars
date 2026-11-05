// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A disconnect whose teardown throws must not leave a ghost (#3197). The save of the leaving player ran before the session
/// left the session table, without a guard: when it threw, the session stayed there as joined, holding the name — and the
/// player's own rejoin went through the reconnect eviction, which ran the same teardown, threw again and ended in
/// <c>@srv.join.failed</c>, until the server restarted. A non-finite position makes the save throw (System.Text.Json refuses
/// NaN), the same trigger the bug was reproduced with.
/// </summary>
public sealed class DisconnectGhostSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_ghostsession_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport, CapturingLogger log)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo, log);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static void Join(SvGameServer server, int connectionId, string name, string token)
        => server.HandlePayloadForTest(connectionId, NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = name, Token = token }));

    /// <summary>Joins "Pilot" on connection 1 and gives the session a position no save can write.</summary>
    private static void JoinWithAnUnsavablePosition(SvGameServer server)
    {
        Join(server, 1, "Pilot", "token-a");
        server.Sessions[1].State.Position = new Vector3f(float.NaN, 70f, 0.5f);
    }

    [Fact]
    public void ADisconnectWhoseSaveThrows_StillRemovesTheSession_AndRunsTheRestOfTheTeardown()
    {
        var transport = new RecordingTransport();
        var log = new CapturingLogger();
        var server = NewServer("ghost_plain", transport, log);
        var watcher = server.AddLocalPlayer("Watcher"); // someone on the same world, to see the avatar go
        JoinWithAnUnsavablePosition(server);
        transport.Sent.Clear();

        server.DisconnectLocalPlayerForTest("Pilot");

        Assert.False(server.Sessions.ContainsKey(1));
        Assert.Equal(1, server.JoinedPlayerCountForTest); // the watcher only
        Assert.Contains(log.Errors, e => e.Contains("step 'player save' threw", StringComparison.Ordinal));
        // The cleanup behind the failed save still ran: the watcher is told the avatar is gone.
        Assert.Contains(transport.To<PlayerLeft>(watcher), left => left.PlayerId == "Pilot");
    }

    [Fact]
    public void AfterAThrowingDisconnect_TheRightTokenGetsBackIn()
    {
        var transport = new RecordingTransport();
        var server = NewServer("ghost_rejoin", transport, new CapturingLogger());
        JoinWithAnUnsavablePosition(server);
        server.DisconnectLocalPlayerForTest("Pilot");
        transport.Sent.Clear();

        Join(server, 2, "Pilot", "token-a");

        Assert.Contains(transport.Sent, x => x.Conn == 2 && x.Msg is JoinAccepted);
        Assert.DoesNotContain(transport.Sent, x => x.Conn == 2 && x.Msg is JoinRejected);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
    }

    [Fact]
    public void TheRejoinEviction_OfASessionWhoseSaveThrows_LetsTheOwnerIn()
    {
        // The lock-out itself: the ghost is still joined, the owner comes back with the right token, and the eviction's
        // teardown throws in the save — the join used to end in @srv.join.failed, every time.
        var transport = new RecordingTransport();
        var server = NewServer("ghost_evict", transport, new CapturingLogger());
        JoinWithAnUnsavablePosition(server);
        transport.Sent.Clear();

        Join(server, 2, "Pilot", "token-a");

        Assert.Contains(1, transport.Disconnected);
        Assert.False(server.Sessions.ContainsKey(1));
        Assert.Contains(transport.Sent, x => x.Conn == 2 && x.Msg is JoinAccepted);
        Assert.DoesNotContain(transport.Sent, x => x.Conn == 2 && x.Msg is JoinRejected);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
        Assert.True(float.IsFinite(server.Sessions[2].State.Position.X)); // the new session starts from the last good save
    }
}
