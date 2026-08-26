// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Scouts at the gate" (#1224): the opt-in visit that makes a base feel watched without ever threatening
/// it. The promises under test are the ones a parent would ask about — off by default, only while the owner
/// is home, never inside the zone, gone after a minute, and nothing taken or broken — plus the one thing a
/// player gets for standing up to them.
/// </summary>
public sealed class BaseVisitorsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public BaseVisitorsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_visitors_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

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

    /// <summary>A Survival world with robbers on, nothing else stamped, and no world tick — so the only bandits
    /// that can ever appear are the ones a test asks for. Machines default to on because scouts need them on
    /// (#1297: no sentry without hostiles, no scouts without a sentry to answer them); they still never spawn
    /// here since no test runs the enemy tick.</summary>
    private SvGameServer NewServer(string name, RecordingTransport transport, bool visitors, AlienActivity enemies = AlienActivity.Normal)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = enemies;
        config.Rules.Bandits = AlienActivity.Normal;
        config.Rules.BaseVisitors = visitors;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static PlayerSession Owner(SvGameServer server)
    {
        var owner = server.AddLocalPlayer("Owner");
        owner.State.AboardShip = false;
        return owner;
    }

    /// <summary>Founds a base a few blocks from the owner's feet (the sentry tests' recipe).</summary>
    private static (int Id, Vector3i Core) FoundBase(SvGameServer server, PlayerSession owner)
    {
        var feet = owner.State.Position;
        var core = new Vector3i((int)Math.Floor(feet.X) + 3, (int)Math.Floor(feet.Y) + 4, (int)Math.Floor(feet.Z));
        server.PlaceBaseForTest(owner, core);
        return (server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id, core);
    }

    private static List<CombatEntity> Scouts(SvGameServer server) => server.Bandits.Where(b => b.ScoutBaseId > 0).ToList();

    private static IEnumerable<string> MessagesTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static bool InsideZone(Vector3i core, Vector3f pos)
        => Math.Abs(Math.Floor(pos.X) - core.X) <= 8 && Math.Abs(Math.Floor(pos.Y) - core.Y) <= 8 && Math.Abs(Math.Floor(pos.Z) - core.Z) <= 8;

    [Fact]
    public void OffByDefault_AndTheDangerousPresetIsTheOnlyOneThatTurnsItOn()
    {
        Assert.False(new GameRules().BaseVisitors);
        Assert.False(ServerPresets.Get("family")!.BaseVisitors);
        Assert.False(ServerPresets.Get("coop-survival")!.BaseVisitors);
        Assert.True(ServerPresets.Get("dangerous")!.BaseVisitors);
    }

    [Fact]
    public void RuleOff_NobodyEverComes()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_off", transport, visitors: false);
        var owner = Owner(server);
        var (baseId, _) = FoundBase(server, owner);

        Assert.False(server.SpawnScoutsForTest(baseId));
        server.TrySpawnBaseScoutsForTest();

        Assert.Empty(Scouts(server));
        Assert.DoesNotContain(MessagesTo(transport, owner), m => m.StartsWith("@srv.base.scouts", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanetEnemiesOff_NobodyComes_EvenWithTheRuleOn()
    {
        // #1297: the sentry post is gated on PlanetEnemies, so with machines off a base could be visited but
        // never answer. Scouts need BOTH robbers and machines on; the client hides the option on the same pair.
        var transport = new RecordingTransport();
        var server = NewServer("visitors_no_machines", transport, visitors: true, enemies: AlienActivity.Off);
        var owner = Owner(server);
        var (baseId, _) = FoundBase(server, owner);

        Assert.False(server.SpawnScoutsForTest(baseId));
        server.TrySpawnBaseScoutsForTest();

        Assert.Empty(Scouts(server));
        Assert.DoesNotContain(MessagesTo(transport, owner), m => m.StartsWith("@srv.base.scouts", StringComparison.Ordinal));
    }

    [Fact]
    public void RuleOn_TwoScoutsArrive_NonHostile_AndTheOwnerIsTold()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_on", transport, visitors: true);
        var owner = Owner(server);
        var (baseId, core) = FoundBase(server, owner);
        transport.Sent.Clear();

        Assert.True(server.SpawnScoutsForTest(baseId));

        var scouts = Scouts(server);
        Assert.Equal(2, scouts.Count);
        Assert.All(scouts, s =>
        {
            Assert.Equal(BanditPhase.Scouting, s.BanditPhase);
            Assert.False(s.Hostile);
            Assert.Equal(baseId, s.ScoutBaseId);
            Assert.False(InsideZone(core, s.Position), "a scout starts well outside the zone");
        });
        Assert.Contains(MessagesTo(transport, owner), m => m.StartsWith("@srv.base.scouts:", StringComparison.Ordinal));
        Assert.Contains(transport.Sent, s => s.Conn == owner.ConnectionId && s.Msg is ShipAiLine v && v.LineKey == "vega.sys.base_scouts");
    }

    [Fact]
    public void NobodyHome_NoVisit()
    {
        // The same rule as the sentry: a base does not run a private war on an empty world.
        var transport = new RecordingTransport();
        var server = NewServer("visitors_away", transport, visitors: true);
        var owner = Owner(server);
        var (baseId, _) = FoundBase(server, owner);

        owner.Joined = false;
        Assert.False(server.SpawnScoutsForTest(baseId));
        server.TrySpawnBaseScoutsForTest();
        Assert.Empty(Scouts(server));

        owner.Joined = true;
        server.TrySpawnBaseScoutsForTest();
        Assert.Equal(2, Scouts(server).Count);
    }

    [Fact]
    public void Scouts_NeverEnterTheZone_AndLeaveAfterAMinute()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_walk", transport, visitors: true);
        var owner = Owner(server);
        var (baseId, core) = FoundBase(server, owner);
        Assert.True(server.SpawnScoutsForTest(baseId));

        // Walk them in at 10 Hz for 90 s — long enough to arrive, stand, and leave.
        for (int i = 0; i < 900; i++)
        {
            server.TickBanditsForTest(0.1);
            foreach (var s in server.Bandits.Where(b => b.ScoutBaseId > 0 && b.BanditPhase == BanditPhase.Scouting))
            {
                Assert.False(InsideZone(core, s.Position), $"a scout stepped into the zone at t={i / 10.0:0.0}s");
            }
        }

        Assert.DoesNotContain(server.Bandits, b => b.BanditPhase == BanditPhase.Scouting); // the visit is over
        Assert.All(Scouts(server), s => Assert.False(s.Hostile)); // …and nobody was hurt
    }

    [Fact]
    public void HittingAScout_MakesItFight_AndBeatingItCreditsTheHomestead()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_fight", transport, visitors: true);
        var owner = Owner(server);
        var (baseId, _) = FoundBase(server, owner);
        Assert.True(server.SpawnScoutsForTest(baseId));
        var scout = Scouts(server)[0];

        // Bring the scout to the owner, not the owner to the scout: the spawn bearing is random and the
        // ground out there is whatever the terrain says, while the owner's own spot is known-clear (the
        // bandit tests do the same). Reach and line of sight are then not the thing under test.
        scout.Position = new Vector3f(owner.State.Position.X + 2f, owner.State.Position.Y, owner.State.Position.Z);
        server.AttackEntity("Owner", scout.Id);

        Assert.True(scout.Hostile);
        Assert.Equal(BanditPhase.Fighting, scout.BanditPhase);
        Assert.Equal(baseId, scout.ScoutBaseId); // it remembers what it came for, so beating it still counts

        for (int i = 0; i < 12 && server.Bandits.Contains(scout); i++)
        {
            server.AttackEntity("Owner", scout.Id);
        }

        Assert.DoesNotContain(scout, server.Bandits);
        var list = server.AchievementListForTest(owner);
        Assert.Equal(1, list.Counters.TryGetValue("base:defended", out int n) ? n : 0);
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
