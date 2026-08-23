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

/// <summary>
/// Crews (#1216): membership implies pairwise alliance with every other member, but the crew edges live apart
/// from the manual alliance graph — leaving the crew keeps a manual alliance, dissolving a manual alliance
/// keeps crew access. Owner-only management, online-only invites, one crew per player, the 8-member cap, the
/// oldest member inheriting an abandoned crew, name screening, and persistence across a reload.
/// </summary>
public sealed class CrewTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crew_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string tag = "c", params string[] players)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        foreach (var p in players.Length > 0 ? players : new[] { "Alice", "Bob", "Cara" })
        {
            server.AddLocalPlayer(p);
        }

        return server;
    }

    /// <summary>Founds a crew as Alice and brings the given players aboard via invite + accept.</summary>
    private static string Crew(SvGameServer server, string name, params string[] members)
    {
        server.CrewActionForTest("Alice", "create", name);
        string crewId = server.CrewSnapshots.Single().Id;
        foreach (var m in members)
        {
            server.CrewActionForTest("Alice", "invite", target: m);
            server.CrewActionForTest(m, "accept", crewId);
        }

        return crewId;
    }

    [Fact]
    public void CreateInviteAccept_AlliesEveryPair_WithoutTouchingTheManualGraph()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob", "Cara");

            Assert.True(server.AreAllied("Alice", "Bob"));
            Assert.True(server.AreAllied("Bob", "Cara"));   // members ally TRANSITIVELY through the crew
            Assert.True(server.AreAllied("Cara", "Alice"));
            Assert.False(server.PairAllied("Alice", "Bob")); // …but no manual pairwise edges were created
            Assert.Empty(server.AllianceSnapshots);
        }
    }

    [Fact]
    public void LeavingTheCrew_CutsOnlyTheCrewEdges()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob", "Cara");

            // Alice and Bob ALSO shake hands manually — that edge must survive Bob leaving the crew.
            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);

            server.CrewActionForTest("Bob", "leave");

            Assert.True(server.AreAllied("Alice", "Bob"));  // the manual alliance carries on
            Assert.False(server.AreAllied("Bob", "Cara"));  // the crew-only edge is gone
            Assert.True(server.AreAllied("Alice", "Cara")); // the remaining crew is untouched
        }
    }

    [Fact]
    public void DissolvingAManualAlliance_LeavesCrewAccessStanding()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob");
            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);

            server.DissolveAlliance("Bob", "Alice");

            Assert.False(server.PairAllied("Alice", "Bob"));
            Assert.True(server.AreAllied("Alice", "Bob")); // still crew mates
        }
    }

    [Fact]
    public void TheNinthMember_IsRefused()
    {
        var players = new[] { "Alice", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9" };
        var server = NewServer(out var repo, "cap", players);
        using (repo)
        {
            Crew(server, "Volle Crew", "P2", "P3", "P4", "P5", "P6", "P7", "P8");
            Assert.Equal(8, server.CrewSnapshots.Single().Members.Length);

            server.CrewActionForTest("Alice", "invite", target: "P9"); // rejected: full
            string crewId = server.CrewSnapshots.Single().Id;
            server.CrewActionForTest("P9", "accept", crewId);          // no invite to accept

            Assert.Equal(8, server.CrewSnapshots.Single().Members.Length);
            Assert.False(server.AreAllied("Alice", "P9"));
        }
    }

    [Fact]
    public void OwnerLeaving_HandsTheCrewToTheOldestMember()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob", "Cara"); // joined in this order

            server.CrewActionForTest("Alice", "leave");

            var crew = server.CrewSnapshots.Single();
            Assert.Equal("Bob", crew.OwnerId);           // longest-serving member inherits
            Assert.Equal(2, crew.Members.Length);
            Assert.True(server.AreAllied("Bob", "Cara"));
            Assert.False(server.AreAllied("Alice", "Bob"));
        }
    }

    [Fact]
    public void LastMemberLeaving_DissolvesTheCrew()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Solo");
            server.CrewActionForTest("Alice", "leave");
            Assert.Empty(server.CrewSnapshots);
        }
    }

    [Fact]
    public void Kick_IsOwnerOnly_AndRemovesTheMember()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob", "Cara");

            server.CrewActionForTest("Bob", "kick", target: "Cara"); // not the owner — refused
            Assert.Equal(3, server.CrewSnapshots.Single().Members.Length);

            server.CrewActionForTest("Alice", "kick", target: "Cara");
            Assert.Equal(2, server.CrewSnapshots.Single().Members.Length);
            Assert.False(server.AreAllied("Alice", "Cara"));
        }
    }

    [Fact]
    public void InvitesGoOnlyToOnlineCrewlessPlayers_AndOnePlayerHasOneCrew()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Erste", "Bob");

            server.CrewActionForTest("Alice", "invite", target: "Nemo"); // not online → no invite
            Assert.Equal("", server.CrewActionForTest("Nemo", "accept", server.CrewSnapshots.Single().Id));

            // Cara founds her own crew — now Alice's invite to Cara must be refused (one crew per player).
            server.CrewActionForTest("Cara", "create", "Zweite");
            server.CrewActionForTest("Alice", "invite", target: "Cara");
            string first = server.CrewSnapshots.Single(c => c.Name == "Erste").Id;
            server.CrewActionForTest("Cara", "accept", first); // no invite exists → nothing happens

            Assert.Equal(2, server.CrewSnapshots.Count);
            Assert.False(server.AreAllied("Alice", "Cara"));
        }
    }

    [Fact]
    public void CrewName_IsScreened_OnCreateAndRename()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            server.CrewActionForTest("Alice", "create", "h.i.t.l.e.r"); // blocked by the name screen (#1221)
            Assert.Empty(server.CrewSnapshots);

            Crew(server, "Nette Crew");
            server.CrewActionForTest("Alice", "rename", "h.i.t.l.e.r");
            Assert.Equal("Nette Crew", server.CrewSnapshots.Single().Name); // rename refused, old name stands

            server.CrewActionForTest("Alice", "rename", "Immer noch nett aber viel zu lang als Name");
            Assert.True(server.CrewSnapshots.Single().Name.Length <= 24);   // clamped like a beacon label
        }
    }

    [Fact]
    public void Disband_RemovesEveryEdgeAndTheCrew()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Crew(server, "Sternenbande", "Bob", "Cara");
            server.CrewActionForTest("Alice", "disband");

            Assert.Empty(server.CrewSnapshots);
            Assert.False(server.AreAllied("Alice", "Bob"));
            Assert.False(server.AreAllied("Bob", "Cara"));
        }
    }

    [Fact]
    public void Crew_SurvivesAReload()
    {
        var paths = new SaveGamePaths(_root, "persist");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Alice");
            server.AddLocalPlayer("Bob");
            Crew(server, "Bleibt bestehen", "Bob");
            Assert.True(server.AreAllied("Alice", "Bob"));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var server2 = new SvGameServer(new ServerConfig { WorldName = "persist", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st2, repo2);
            server2.Start();

            Assert.True(server2.SameCrew("Alice", "Bob")); // restored at Start, no join needed
            var crew = server2.CrewSnapshots.Single();
            Assert.Equal("Bleibt bestehen", crew.Name);
            Assert.Equal("Alice", crew.OwnerId);
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
