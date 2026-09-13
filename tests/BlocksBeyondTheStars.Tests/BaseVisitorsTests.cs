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

    // ---------------- #1855: scouts inside a walled fortress ----------------

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>A fortress far wider than the radius-8 zone: a stone pad floating above every natural feature
    /// out to the base's whole reach box (so the outside-in fill of #1315 starts on it), a 2-high ring of walls
    /// at Chebyshev <paramref name="ring"/>, the core at the centre and the owner inside. Returns the base id,
    /// the core cell and the feet level on the pad.</summary>
    private (int Id, Vector3i Core, int Feet) Fortress(SvGameServer server, PlayerSession owner, int cx, int cz, int ring)
    {
        const int reach = 48;
        var stone = _content.GetBlock("stone")!.NumericId;
        int maxTop = int.MinValue;
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                maxTop = Math.Max(maxTop, SurfaceTopY(server, cx + dx, cz + dz));
            }

        int padY = maxTop + 8;
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
                if (Math.Abs(dx) == ring || Math.Abs(dz) == ring)
                {
                    if (Math.Abs(dx) <= ring && Math.Abs(dz) <= ring)
                    {
                        server.World.SetBlock(new Vector3i(cx + dx, padY + 1, cz + dz), stone);
                        server.World.SetBlock(new Vector3i(cx + dx, padY + 2, cz + dz), stone);
                    }
                }
            }

        owner.State.Position = new Vector3f(cx + 2.5f, padY + 1, cz + 0.5f);
        var core = new Vector3i(cx, padY + 1, cz);
        server.PlaceBaseForTest(owner, core);
        int id = server.BaseSnapshots.Single(b => b.OwnerId == owner.State.PlayerId).Id;
        return (id, core, padY + 1);
    }

    private static int ChebyshevXZ(Vector3i core, Vector3f pos)
        => Math.Max(Math.Abs((int)Math.Floor(pos.X) - core.X), Math.Abs((int)Math.Floor(pos.Z) - core.Z));

    /// <summary>The report: "bandit scouts appear inside my fortress". The bearing was blind and the distance a
    /// fixed 40 blocks, so a ring of walls wider than that had the pair materialise inside. The spawn now tries
    /// bearings and radii and takes only open ground — outside every walled yard, like the wildlife spawner.</summary>
    [Fact]
    public void Scouts_NeverSpawnInsideAClosedWallRing()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_fortress", transport, visitors: true);
        var owner = Owner(server);
        int cx = (int)Math.Floor(owner.State.Position.X), cz = (int)Math.Floor(owner.State.Position.Z);
        var (baseId, core, feet) = Fortress(server, owner, cx, cz, ring: 44); // wider than the 40-block spawn ring
        Assert.True(server.InWalledBaseAreaForTest(cx + 40, feet, cz), "the fixture: the old spawn ring lies inside the walls");

        for (int visit = 0; visit < 6; visit++)
        {
            Assert.True(server.SpawnScoutsForTest(baseId), $"visit {visit} found no open ground around the fortress");
        }

        var scouts = Scouts(server);
        Assert.Equal(12, scouts.Count);
        Assert.All(scouts, s =>
        {
            // Strictly outside: Chebyshev 44 is the wall itself, and a scout on the rampart is not outside either.
            Assert.True(ChebyshevXZ(core, s.Position) > 44, $"a scout spawned inside (or on) the walls at {s.Position}, core {core}, pad feet {feet}");
            Assert.False(server.InWalledBaseAreaForTest((int)Math.Floor(s.Position.X), (int)Math.Floor(s.Position.Y), (int)Math.Floor(s.Position.Z)),
                $"a scout spawned on fenced-in ground at {s.Position}");
        });
    }

    /// <summary>Even a scout that spawned outside used to end up inside: its fence was the radius-8 zone cube,
    /// and a bandit step knew no walls — a two-block wall read as a step, so it climbed over and walked to an
    /// edge point that lay inside the player's walls. One scout at the foot of the wall, one already ON the
    /// wall top with the yard right below it: neither gets inside — the climb is refused as a wall, the step
    /// down as a step onto fenced-in ground.</summary>
    [Fact]
    public void Scouts_NeitherClimbTheWall_NorStepDownIntoTheYard()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_walls", transport, visitors: true);
        var owner = Owner(server);
        int cx = (int)Math.Floor(owner.State.Position.X), cz = (int)Math.Floor(owner.State.Position.Z);
        var (baseId, core, feet) = Fortress(server, owner, cx, cz, ring: 20);
        Assert.True(server.SpawnScoutsForTest(baseId));

        var scouts = Scouts(server);
        Assert.Equal(2, scouts.Count);
        scouts[0].Position = new Vector3f(cx + 24.5f, feet, cz + 0.5f);     // outside, four blocks from the +X wall
        scouts[1].Position = new Vector3f(cx + 20.5f, feet + 2, cz + 0.5f); // on the wall top, the yard one step down

        for (int i = 0; i < 600; i++)
        {
            server.TickBanditsForTest(0.1);
            foreach (var s in server.Bandits.Where(b => b.ScoutBaseId > 0 && b.BanditPhase == BanditPhase.Scouting))
            {
                Assert.True(ChebyshevXZ(core, s.Position) >= 20, $"a scout got inside the walls at t={i / 10.0:0.0}s: {s.Position}");
            }
        }

        Assert.Equal(feet, scouts[0].Position.Y, 1); // it never climbed
    }

    /// <summary>A shut wooden gate is a door ENTITY, not a block — the body sweep sees air there. The enclosure
    /// fence is what stops a scout at the gate: the doorway cell and everything behind it read as fenced in
    /// (#1315: a shut door counts as wall), so the step onto the threshold is refused.</summary>
    [Fact]
    public void AScout_DoesNotWalkThroughAShutGate()
    {
        var transport = new RecordingTransport();
        var server = NewServer("visitors_gate", transport, visitors: true);
        var owner = Owner(server);
        int cx = (int)Math.Floor(owner.State.Position.X), cz = (int)Math.Floor(owner.State.Position.Z);
        var (baseId, core, feet) = Fortress(server, owner, cx, cz, ring: 20);

        // A 1-wide gateway in the +X wall, filled with a wooden door (placed shut).
        server.RemoveBlockForTest(cx + 20, feet, cz);
        server.RemoveBlockForTest(cx + 20, feet + 1, cz);
        owner.State.Inventory.Add("door_wood", 2, 16);
        owner.State.Position = new Vector3f(cx + 21.5f, feet, cz + 0.5f);
        server.PlaceBlock(owner.State.PlayerId, cx + 20, feet, cz, "door_wood");
        var door = server.DoorSnapshots.Single(d => d.Kind == "wood");
        Assert.False(door.Open);
        Assert.True(server.World.GetBlock(new Vector3i(cx + 20, feet, cz)).IsAir, "the fixture: a door leaves its cell air");
        Assert.True(server.InWalledBaseAreaForTest(cx + 19, feet, cz), "the fixture: a shut gate keeps the yard closed");
        owner.State.Position = new Vector3f(cx + 2.5f, feet, cz + 0.5f); // back inside, so the scouts have someone to look at

        Assert.True(server.SpawnScoutsForTest(baseId));
        var scout = Scouts(server)[0];
        scout.Position = new Vector3f(cx + 23.5f, feet, cz + 0.5f); // in front of the gate, its edge point straight behind it

        for (int i = 0; i < 300; i++)
        {
            server.TickBanditsForTest(0.1);
            Assert.True(ChebyshevXZ(core, scout.Position) >= 20, $"the scout walked through the shut gate at t={i / 10.0:0.0}s: {scout.Position}");
        }
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
