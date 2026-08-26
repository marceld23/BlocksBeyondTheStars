// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Named map markers + ping (#1217): the per-world cap, label sanitising, ally/crew-gated visibility
/// of shared markers, the ping rate limit + TTL, and persistence across a reload. Since #1293 also: shared
/// markers outlive their owner's session, the view is pushed when the alliance/crew circle changes, and a
/// player's pings leave a world with them.</summary>
public sealed class MarkerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MarkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_marker_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every decoded message the server sends, per connection — the way to assert that a
    /// player was PUSHED a fresh marker list rather than merely that one could be computed for them.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private static ServerConfig Config(string tag)
    {
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules.FreeSpaceFlight = true; // the world-switch test travels
        return config;
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string tag = "m", IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(Config(tag), _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        return server;
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAlliance(a, b);
        server.RespondAlliance(b, a, accept: true);
    }

    /// <summary>The marker lists pushed to one connection since the recorder was last cleared, oldest first.</summary>
    private static List<MarkerList> ListsPushedTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<MarkerList>().ToList();

    /// <summary>Another landable body in the same star system (no jump generator needed to reach it).</summary>
    private CelestialBody OtherPlanet(SvGameServer server)
    {
        string sys = server.Galaxy.FindBody(server.ActiveLocationId)!.SystemId;
        return server.Galaxy.AllBodies().First(b =>
            b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
            && b.SystemId == sys
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.Id != server.ActiveLocationId);
    }

    [Fact]
    public void EightPerWorld_TheNinthIsRefused()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(i + 1, server.SetMarkerForTest("Alice", 10 + i, 64, 10, "spot " + i));
            }

            Assert.Equal(8, server.SetMarkerForTest("Alice", 99, 64, 99, "one too many"));
        }
    }

    [Fact]
    public void UpdateById_EditsInPlace_InsteadOfCountingAgainstTheCap()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.SetMarkerForTest("Alice", 10, 64, 10, "ore here", icon: 2, color: 1);
            var mine = server.VisibleMarkersForTest("Alice").Single();

            int count = server.SetMarkerForTest("Alice", 20, 64, 20, "ore was here", icon: 3, color: 2, shared: true, id: mine.Id);

            Assert.Equal(1, count);
            var updated = server.VisibleMarkersForTest("Alice").Single();
            Assert.Equal(mine.Id, updated.Id);
            Assert.Equal("ore was here", updated.Label);
            Assert.True(updated.Shared);
        }
    }

    [Fact]
    public void Label_IsStrippedAndClamped_LikeABeaconLabel()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.SetMarkerForTest("Alice", 10, 64, 10, "lineone\nand far too long a label tail"); // digits would trip the leetspeak screen — its own test
            string label = server.VisibleMarkersForTest("Alice").Single().Label;
            Assert.DoesNotContain('', label);
            Assert.DoesNotContain('\n', label);
            Assert.True(label.Length <= 24);
        }
    }

    [Fact]
    public void SharedMarkers_ReachAllies_PrivateOnesNever()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Ally(server, "Alice", "Bob");
            server.SetMarkerForTest("Alice", 10, 64, 10, "for us", shared: true);
            server.SetMarkerForTest("Alice", 20, 64, 20, "just mine", shared: false);

            Assert.Equal(2, server.VisibleMarkersForTest("Alice").Count);
            var bobSees = server.VisibleMarkersForTest("Bob");
            Assert.Single(bobSees);
            Assert.Equal("for us", bobSees[0].Label);
            Assert.Equal("Alice", bobSees[0].OwnerId);
        }
    }

    [Fact]
    public void NonAllies_ReceiveNothing()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.SetMarkerForTest("Alice", 10, 64, 10, "for allies only", shared: true);
            Assert.Empty(server.VisibleMarkersForTest("Bob")); // no alliance, no crew — shared or not
        }
    }

    [Fact]
    public void CrewMates_SeeSharedMarkers_WithoutAManualAlliance()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.CrewActionForTest("Alice", "create", "Map Friends");
            string crewId = server.CrewSnapshots.Single().Id;
            server.CrewActionForTest("Alice", "invite", target: "Bob");
            server.CrewActionForTest("Bob", "accept", crewId);

            server.SetMarkerForTest("Alice", 10, 64, 10, "crew spot", shared: true);

            var bobSees = server.VisibleMarkersForTest("Bob");
            Assert.Single(bobSees);
            Assert.Equal("crew spot", bobSees[0].Label);
        }
    }

    [Fact]
    public void Remove_DropsTheMarker()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.SetMarkerForTest("Alice", 10, 64, 10, "temp");
            string id = server.VisibleMarkersForTest("Alice").Single().Id;

            server.RemoveMarkerForTest("Alice", id);

            Assert.Empty(server.VisibleMarkersForTest("Alice"));
        }
    }

    [Fact]
    public void Ping_IsRateLimited_AndExpires()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Ally(server, "Alice", "Bob");

            server.PingForTest("Alice", 10, 64, 10);
            server.PingForTest("Alice", 20, 64, 20); // inside the 5 s window — swallowed
            Assert.Single(server.VisibleMarkersForTest("Bob").Where(m => m.Ping));

            server.AdvanceUptimeForTest(6);
            server.PingForTest("Alice", 20, 64, 20); // window over — accepted
            Assert.Equal(2, server.VisibleMarkersForTest("Bob").Count(m => m.Ping));

            server.AdvanceUptimeForTest(31); // both pings are past their 30 s TTL now
            server.TickMarkerPingsForTest();
            Assert.Empty(server.VisibleMarkersForTest("Bob").Where(m => m.Ping));
            Assert.Empty(server.VisibleMarkersForTest("Alice").Where(m => m.Ping));
        }
    }

    [Fact]
    public void Pings_StayInsideTheAllianceCircle()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.PingForTest("Alice", 10, 64, 10);
            Assert.Single(server.VisibleMarkersForTest("Alice").Where(m => m.Ping)); // the sender sees their own
            Assert.Empty(server.VisibleMarkersForTest("Bob"));                       // a stranger sees nothing
        }
    }

    [Fact]
    public void Markers_SurviveAReload()
    {
        var paths = new SaveGamePaths(_root, "persist");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.SetMarkerForTest("Alice", 10, 64, 10, "still here", icon: 5, color: 3, shared: true);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var server2 = new SvGameServer(new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st2, repo2);
            server2.Start();
            var p = server2.AddLocalPlayer("Alice");

            var m = p.State.Markers.Single();
            Assert.Equal("still here", m.Label);
            Assert.Equal(5, m.Icon);
            Assert.Equal(3, m.Color);
            Assert.True(m.Shared);
            Assert.Single(server2.VisibleMarkersForTest("Alice"));
        }
    }

    // ---------------- #1293: offline owners, circle changes, leaving a world ----------------

    [Fact]
    public void SharedMarker_StaysVisible_WhileTheOwnerIsOffline_AndAcrossAReload()
    {
        // The family meeting point must not disappear when the kid logs off — and it must still be there
        // when the server comes back up with only the other player on it.
        var paths = new SaveGamePaths(_root, "offline");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var server = new SvGameServer(Config("offline"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            server.SetMarkerForTest("Alice", 10, 64, 10, "meet here", shared: true);
            server.SetMarkerForTest("Alice", 20, 64, 20, "my stash", shared: false);
            server.PingForTest("Alice", 30, 64, 30);
            Assert.Equal(2, server.VisibleMarkersForTest("Bob").Count); // the shared marker + the live ping

            server.DisconnectLocalPlayerForTest("Alice");

            var bobSees = server.VisibleMarkersForTest("Bob");
            var marker = Assert.Single(bobSees);
            Assert.Equal("meet here", marker.Label);   // shared: stays
            Assert.Equal("Alice", marker.OwnerId);
            Assert.False(marker.Ping);                 // the ping left with its owner
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var server2 = new SvGameServer(Config("offline"), _content, new LoopbackServerTransport(new LoopbackLink()), repo2);
            server2.Start();
            server2.AddLocalPlayer("Bob"); // Alice never joins this run

            var bobSees = server2.VisibleMarkersForTest("Bob");
            var marker = Assert.Single(bobSees);
            Assert.Equal("meet here", marker.Label);
            Assert.Equal("Alice", marker.OwnerId);
            Assert.True(marker.Shared);
        }
    }

    [Fact]
    public void OfflineOwnersSharedMarker_IsStillGatedByTheAlliance()
    {
        var paths = new SaveGamePaths(_root, "offline_gate");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var server = new SvGameServer(Config("offline_gate"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.SetMarkerForTest("Alice", 10, 64, 10, "for allies only", shared: true);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var server2 = new SvGameServer(Config("offline_gate"), _content, new LoopbackServerTransport(new LoopbackLink()), repo2);
            server2.Start();
            server2.AddLocalPlayer("Bob");
            Assert.Empty(server2.VisibleMarkersForTest("Bob")); // no alliance, no crew — offline or not
        }
    }

    [Fact]
    public void AllianceFormedAfterSharing_PushesTheList_AndDissolvingPushesItAgain()
    {
        // No marker event happens here at all: the only thing that changes is who Bob is allied with — and
        // the map on his screen must follow without him having to open the marker menu.
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "ally_push", transport);
        using (repo)
        {
            var bob = server.AddLocalPlayer("Bob2");
            server.SetMarkerForTest("Alice", 10, 64, 10, "for us", shared: true);
            Assert.Empty(server.VisibleMarkersForTest("Bob2"));

            transport.Sent.Clear();
            Ally(server, "Alice", "Bob2");

            var pushed = ListsPushedTo(transport, bob);
            Assert.NotEmpty(pushed);
            var visible = Assert.Single(pushed.Last().Markers);
            Assert.Equal("for us", visible.Label);
            Assert.Equal("Alice", visible.OwnerId);

            transport.Sent.Clear();
            server.DissolveAlliance("Alice", "Bob2");

            pushed = ListsPushedTo(transport, bob);
            Assert.NotEmpty(pushed);
            Assert.Empty(pushed.Last().Markers);
        }
    }

    [Fact]
    public void CrewJoinAndLeave_PushTheMarkerList()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "crew_push", transport);
        using (repo)
        {
            var bob = server.AddLocalPlayer("Bob2");
            server.SetMarkerForTest("Alice", 10, 64, 10, "crew spot", shared: true);
            server.CrewActionForTest("Alice", "create", "Map Friends");
            string crewId = server.CrewSnapshots.Single().Id;
            server.CrewActionForTest("Alice", "invite", target: "Bob2");

            transport.Sent.Clear();
            server.CrewActionForTest("Bob2", "accept", crewId);

            var pushed = ListsPushedTo(transport, bob);
            Assert.NotEmpty(pushed);
            Assert.Equal("crew spot", Assert.Single(pushed.Last().Markers).Label);

            transport.Sent.Clear();
            server.CrewActionForTest("Bob2", "leave");

            pushed = ListsPushedTo(transport, bob);
            Assert.NotEmpty(pushed);
            Assert.Empty(pushed.Last().Markers);
        }
    }

    [Fact]
    public void OwnerLeavingTheWorld_ClearsTheirPings_ForTheAllyLeftBehind_ButKeepsTheSharedMarker()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, "leave_world", transport);
        using (repo)
        {
            var bob = server.AddLocalPlayer("Bob2");
            Ally(server, "Alice", "Bob2");
            server.SetMarkerForTest("Alice", 10, 64, 10, "meet here", shared: true);
            server.PingForTest("Alice", 30, 64, 30);
            Assert.Single(server.VisibleMarkersForTest("Bob2").Where(m => m.Ping));

            transport.Sent.Clear();
            var away = OtherPlanet(server);
            server.Travel("Alice", away.Id);

            var bobSees = server.VisibleMarkersForTest("Bob2");
            Assert.Empty(bobSees.Where(m => m.Ping));                       // the shout stopped with the shouter
            Assert.Equal("meet here", Assert.Single(bobSees).Label);        // the marker is a record and stays

            var pushed = ListsPushedTo(transport, bob);                     // …and Bob was told, not left to poll
            Assert.NotEmpty(pushed);
            Assert.DoesNotContain(pushed.Last().Markers, m => m.Ping);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
