// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Back to my ship" (#2286): the pause menu's way out for a stuck player on foot. The server puts the player back
/// aboard only when every gate holds — world rule on, on foot, own ship landed here, not in a fight, not falling,
/// cooldown over — and the cooldown really lasts the three minutes.
/// </summary>
public sealed class ReturnToShipTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ReturnToShipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_rts_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Records every server send so a test can assert the snap and the toasts.</summary>
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

    /// <summary>A quiet rocky world WITH the starter ship parked (the heal tank is the arrival): no wildlife, machines or
    /// robbers wander into the gates by chance — the tests stage every fight themselves.</summary>
    private SvGameServer Started(string world, RecordingTransport? transport = null, Action<GameRules>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        configure?.Invoke(config.Rules);
        IServerTransport st = transport is not null ? transport : new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Joins a player and puts them on foot in clear air 30 blocks from the heal tank — inside the spawn-adopt
    /// radius, outside the hull. God mode keeps the vitals out of these tests (they are about the gates).</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        Assert.NotEqual(Vector3f.Zero, p.State.RespawnPoint); // the ship is parked: the heal tank is the respawn point
        p.State.GodMode = true;
        p.State.AboardShip = false;
        var tank = p.State.RespawnPoint;
        p.State.Position = new Vector3f(tank.X + 30f, tank.Y + 40f, tank.Z + 30f);
        return p;
    }

    [Fact]
    public void OnFootWithTheShipLandedHere_GoesBackAboard_WithTheSnapAndTheCooldown()
    {
        var transport = new RecordingTransport();
        var server = Started("rts_ok", transport);
        var p = OnFoot(server, "Lost");
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));

        server.ReturnToShip("Lost");

        Assert.True(p.State.AboardShip);
        Assert.Equal(p.State.RespawnPoint, p.State.Position);
        Assert.Contains(transport.Sent, x => x.Conn == p.ConnectionId && x.Msg is RespawnNotice n
            && n.Reason == "@srv.return_ship.done" && !n.Died && n.X.Equals(p.State.RespawnPoint.X));
        Assert.DoesNotContain(transport.Sent, x => x.Msg is ActionRejected r && r.Action == "return_ship");
        // The state update carries the cooldown, so the pause menu can grey its button.
        Assert.Contains(transport.Sent, x => x.Msg is PlayerStateUpdate u && u.PlayerId == "Lost"
            && u.ReturnToShipCooldownSeconds > ReturnToShipRules.CooldownSeconds - 5);

        // #865: a move report still in flight from the stuck spot must not drag the player back outside — the snap arms
        // the spawn-adopt gate like every other server teleport. A report beyond the adopt radius is dropped …
        var tank = p.State.RespawnPoint;
        server.MoveForTest("Lost", tank.X + 200f, tank.Y + 40f, tank.Z + 200f);
        Assert.Equal(tank, p.State.Position);
        // … and the first report at the tank adopts it, after which the client's movement is trusted again.
        server.MoveForTest("Lost", tank.X, tank.Y, tank.Z);
        server.MoveForTest("Lost", tank.X + 1f, tank.Y, tank.Z);
        Assert.Equal(tank.X + 1f, p.State.Position.X);
    }

    [Fact]
    public void FromTheHeldPauseMenu_TheIntentIsServed_AndTheHoldStays()
    {
        // The button lives in the pause menu — and in singleplayer that menu is what holds the world, where #995 drops
        // every gameplay intent. The intent is whitelisted like the resume path, so it gets through the dispatcher;
        // the hold itself is the menu's to release when it closes.
        var transport = new RecordingTransport();
        var server = Started("rts_paused", transport);
        var p = OnFoot(server, "Lost");

        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);

        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new ReturnToShipIntent()));

        Assert.True(p.State.AboardShip);
        Assert.Equal(p.State.RespawnPoint, p.State.Position);
        Assert.Contains(transport.Sent, x => x.Conn == p.ConnectionId && x.Msg is RespawnNotice n && n.Reason == "@srv.return_ship.done");
        Assert.True(server.IsPaused); // the menu releases the hold itself, when it closes
    }

    [Fact]
    public void Cooldown_HoldsForThreeMinutes_ThenReleases()
    {
        var transport = new RecordingTransport();
        var server = Started("rts_cd", transport);
        var p = OnFoot(server, "Lost");
        var outside = p.State.Position;
        server.ReturnToShip("Lost");
        Assert.True(p.State.AboardShip);

        // Out again right away: still recharging, and the refusal says how long.
        p.State.AboardShip = false;
        p.State.Position = outside;
        Assert.Equal("@srv.return_ship.cooldown:3:00", server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(170.0);
        Assert.StartsWith("@srv.return_ship.cooldown:0:1", server.ReturnToShipRefusalForTest("Lost"));
        transport.Sent.Clear();
        server.ReturnToShip("Lost");
        Assert.False(p.State.AboardShip); // refused — still standing outside
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Action == "return_ship"
            && r.Reason.StartsWith("@srv.return_ship.cooldown:", StringComparison.Ordinal));

        server.AdvanceUptimeForTest(10.5);
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));
        server.ReturnToShip("Lost");
        Assert.True(p.State.AboardShip);
    }

    [Fact]
    public void InAFight_IsRefused_UntilTheGraceHasPassed()
    {
        var server = Started("rts_fight");
        var p = OnFoot(server, "Lost");
        var pos = p.State.Position;
        server.SpawnBanditAtForTest(new Vector3f(pos.X + 2f, pos.Y, pos.Z), "Lost"); // in clear air beside the player
        var bandit = server.Bandits[^1];

        server.AttackEntity("Lost", bandit.Id); // the blow lands — the starter drill punches like a fist (#2306)
        Assert.True(bandit.Hull < bandit.HullMax, "the punch must land for this test to mean anything");
        Assert.Equal("@srv.return_ship.in_combat", server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(ReturnToShipRules.CombatGraceSeconds - 1.0);
        Assert.Equal("@srv.return_ship.in_combat", server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(1.5);
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));
    }

    [Fact]
    public void APendingHoldUp_CountsAsAFight()
    {
        var server = Started("rts_holdup");
        var p = OnFoot(server, "Lost");
        p.BanditDemandId = 7; // a robber is waiting for the answer
        Assert.Equal("@srv.return_ship.in_combat", server.ReturnToShipRefusalForTest("Lost"));

        p.BanditDemandId = 0;
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));
    }

    [Fact]
    public void Falling_IsRefused_LandingOrAStaleReadingIsNot()
    {
        var server = Started("rts_fall");
        var p = OnFoot(server, "Lost");
        var pos = p.State.Position;

        server.MoveForTest("Lost", pos.X, pos.Y, pos.Z); // the first report adopts the spot and measures nothing
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(0.1);
        server.MoveForTest("Lost", pos.X, pos.Y - 3f, pos.Z); // 30 blocks per second down
        Assert.Equal("@srv.return_ship.falling", server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(0.1);
        server.MoveForTest("Lost", pos.X, pos.Y - 3f, pos.Z); // level again: landed
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));

        server.AdvanceUptimeForTest(0.1);
        server.MoveForTest("Lost", pos.X, pos.Y - 6f, pos.Z); // falling again …
        Assert.Equal("@srv.return_ship.falling", server.ReturnToShipRefusalForTest("Lost"));
        server.AdvanceUptimeForTest(ReturnToShipRules.FallSampleMaxAgeSeconds + 0.5); // … but the reading is stale
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));
    }

    [Fact]
    public void ShipNotLandedOnThisBody_IsRefused()
    {
        var server = Started("rts_body");
        OnFoot(server, "Lost");
        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));

        server.UnparkShipForTest("Lost"); // what a launch does: the ship is no longer parked in this world
        Assert.Equal("@srv.return_ship.no_ship_here", server.ReturnToShipRefusalForTest("Lost"));
    }

    [Fact]
    public void NotOnFoot_IsRefused()
    {
        var server = Started("rts_foot");
        var p = OnFoot(server, "Lost");

        p.State.AboardShip = true;
        Assert.Equal("@srv.return_ship.aboard", server.ReturnToShipRefusalForTest("Lost"));
        p.State.AboardShip = false;

        p.State.InSpeeder = "speeder-1";
        Assert.Equal("@srv.return_ship.on_foot_only", server.ReturnToShipRefusalForTest("Lost"));
        p.State.InSpeeder = string.Empty;

        p.State.InTrain = "train:0";
        Assert.Equal("@srv.return_ship.on_foot_only", server.ReturnToShipRefusalForTest("Lost"));
        p.State.InTrain = string.Empty;

        p.State.InEva = true;
        Assert.Equal("@srv.return_ship.on_foot_only", server.ReturnToShipRefusalForTest("Lost"));
        p.State.InEva = false;

        Assert.Null(server.ReturnToShipRefusalForTest("Lost"));
    }

    [Fact]
    public void WorldRuleOff_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started("rts_off", transport, r => r.ReturnToShip = false);
        var p = OnFoot(server, "Lost");

        Assert.Equal("@srv.return_ship.disabled", server.ReturnToShipRefusalForTest("Lost"));
        server.ReturnToShip("Lost");
        Assert.False(p.State.AboardShip);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Action == "return_ship" && r.Reason == "@srv.return_ship.disabled");
    }

    [Fact]
    public void WorldRuleOn_ByDefault_AndAnOlderServersRulesReadEmpty()
    {
        Assert.True(new GameRules().ReturnToShip);
        // The wire field is "On"/"Off"; a server from before the rule never sets it, and the client hides the button on empty.
        Assert.Equal(string.Empty, new ServerRules().ReturnToShip);
    }

    [Theory]
    [InlineData(180.0, "3:00")]
    [InlineData(65.0, "1:05")]
    [InlineData(10.0, "0:10")]
    [InlineData(0.2, "0:01")]
    [InlineData(0.0, "0:00")]
    [InlineData(-3.0, "0:00")]
    public void FormatRemaining_IsMinutesAndSeconds_RoundedUp(double seconds, string expected)
        => Assert.Equal(expected, ReturnToShipRules.FormatRemaining(seconds));

    [Fact]
    public void EveryReturnToShipLocaleKey_ExistsInBothLanguages()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (var key in new[]
                 {
                     "ui.pause.return_ship", "ui.pause.return_ship_cooldown", "ui.worldopt.return_ship",
                     "srv.return_ship.done", "srv.return_ship.disabled", "srv.return_ship.aboard",
                     "srv.return_ship.on_foot_only", "srv.return_ship.no_ship_here", "srv.return_ship.in_combat",
                     "srv.return_ship.falling", "srv.return_ship.cooldown",
                 })
        {
            Assert.True(en.ContainsKey(key), $"missing EN locale key: {key}");
            Assert.True(de.ContainsKey(key), $"missing DE locale key: {key}");
        }

        Assert.Contains("{name}", en["srv.return_ship.cooldown"]); // the server's m:ss rides in the token's one argument
        Assert.Contains("{name}", de["srv.return_ship.cooldown"]);
        Assert.Contains("{0}", en["ui.pause.return_ship_cooldown"]);
        Assert.Contains("{0}", de["ui.pause.return_ship_cooldown"]);
    }
}
