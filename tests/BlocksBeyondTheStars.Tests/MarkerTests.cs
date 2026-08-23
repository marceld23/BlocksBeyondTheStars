// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Named map markers + ping (#1217): the per-world cap, label sanitising, ally/crew-gated visibility
/// of shared markers, the ping rate limit + TTL, and persistence across a reload.</summary>
public sealed class MarkerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MarkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_marker_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string tag = "m")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
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
