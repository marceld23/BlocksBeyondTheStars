// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
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
/// #2972 (Justus: "switch weapons and friendly fire on and off in the world settings"): on-foot PvP under the world's
/// rules — <c>GameRules.Pvp</c> and <c>GameRules.FriendlyFire</c> had no reader at all before. Kid-friendly: off by default,
/// a defeated player keeps every item, a duel never kills.
/// </summary>
public sealed class PvpTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public PvpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pvp_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Started(string world, RecordingTransport transport, Action<GameRules> configure)
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
        configure(config.Rules);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Two players on foot in clear air two blocks apart, past their spawn protection.</summary>
    private static (PlayerSession A, PlayerSession B) TwoOnFoot(SvGameServer server)
    {
        var a = server.AddLocalPlayer("Justus");
        var b = server.AddLocalPlayer("Jonah");
        var tank = a.State.RespawnPoint;
        foreach (var (s, dx) in new[] { (a, 0f), (b, 2f) })
        {
            s.State.AboardShip = false;
            s.State.Position = new Vector3f(tank.X + 30f + dx, tank.Y + 40f, tank.Z + 30f);
            server.ClearPvpProtectionForTest(s.State.PlayerId);
        }

        return (a, b);
    }

    [Theory]
    [InlineData(PvpMode.Off, false, false, "", "", false, PvpRules.OffToken)]
    [InlineData(PvpMode.Off, true, false, "", "", true, PvpRules.OffToken)] // Off is off — not even a duel
    [InlineData(PvpMode.On, false, false, "", "", false, null)]
    [InlineData(PvpMode.On, false, true, "", "", false, PvpRules.AllyToken)] // allies are spared …
    [InlineData(PvpMode.On, true, true, "", "", false, null)]                // … unless friendly fire is on
    [InlineData(PvpMode.DuelsOnly, false, false, "", "", false, PvpRules.DuelsOnlyToken)]
    [InlineData(PvpMode.DuelsOnly, false, true, "", "", true, null)]          // a duel is agreed by both — allies too
    [InlineData(PvpMode.GroupBased, false, false, "", "c2", false, PvpRules.NoCrewToken)]
    [InlineData(PvpMode.GroupBased, false, false, "c1", "c2", false, null)]
    [InlineData(PvpMode.GroupBased, false, true, "c1", "c1", false, PvpRules.AllyToken)]
    [InlineData(PvpMode.GroupBased, true, true, "c1", "c1", false, null)]
    public void TheRuleTable(PvpMode mode, bool friendlyFire, bool allied, string crewA, string crewB, bool duel, string? expected)
        => Assert.Equal(expected, PvpRules.Refusal(mode, friendlyFire, allied, crewA, crewB, duel));

    [Fact]
    public void PvpOff_IsTheDefault_AndNobodyGetsHurt()
    {
        Assert.Equal(PvpMode.Off, new GameRules().Pvp);
        Assert.False(new GameRules().FriendlyFire);
        var transport = new RecordingTransport();
        var server = Started("pvp_off", transport, _ => { });
        var (a, b) = TwoOnFoot(server);

        server.AttackPlayerForTest("Justus", "Jonah");

        Assert.Equal(100f, b.State.Health);
        Assert.Contains(transport.Sent, x => x.Conn == a.ConnectionId && x.Msg is ActionRejected r && r.Reason == PvpRules.OffToken);
    }

    [Fact]
    public void PvpOn_AHitHurts_AndTheTargetListNamesTheOther()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_on", transport, r => r.Pvp = PvpMode.On);
        var (a, b) = TwoOnFoot(server);

        server.AttackPlayerForTest("Justus", "Jonah");
        Assert.True(b.State.Health < 100f, $"health {b.State.Health}");

        server.TickPvpForTest();
        Assert.Contains(transport.Sent, x => x.Conn == a.ConnectionId && x.Msg is PvpTargets t && t.PlayerIds.Contains("Jonah"));
    }

    [Fact]
    public void Allies_AreSpared_UnlessFriendlyFireIsOn()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_allies", transport, r => r.Pvp = PvpMode.On);
        var (a, b) = TwoOnFoot(server);
        server.RequestAllianceForTest("Justus", "Jonah");
        server.RespondAllianceForTest("Jonah", "Justus", accept: true);
        Assert.True(server.AreAllied("Justus", "Jonah"));

        Assert.Equal(PvpRules.AllyToken, server.PvpRefusalForTest("Justus", "Jonah"));
        server.RulesForTest.FriendlyFire = true;
        Assert.Null(server.PvpRefusalForTest("Justus", "Jonah"));
    }

    [Fact]
    public void ThePlayersOutOfAnyFight_AreProtected()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_guards", transport, r => r.Pvp = PvpMode.On);
        var (a, b) = TwoOnFoot(server);
        Assert.Null(server.PvpRefusalForTest("Justus", "Jonah"));

        b.State.AboardShip = true;
        Assert.Equal(PvpRules.ProtectedToken, server.PvpRefusalForTest("Justus", "Jonah"));
        b.State.AboardShip = false;

        b.State.GodMode = true;
        Assert.Equal(PvpRules.ProtectedToken, server.PvpRefusalForTest("Justus", "Jonah"));
        b.State.GodMode = false;

        a.State.AboardShip = true; // nor from a ship
        Assert.Equal(PvpRules.ProtectedToken, server.PvpRefusalForTest("Justus", "Jonah"));
    }

    [Fact]
    public void AFreshJoin_IsProtectedForAMoment()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_join", transport, r => r.Pvp = PvpMode.On);
        var a = server.AddLocalPlayer("Justus");
        var b = server.AddLocalPlayer("Jonah");
        foreach (var s in new[] { a, b })
        {
            s.State.AboardShip = false;
        }

        server.TickPvpForTest(); // the pass that first sees them arms the protection
        Assert.Equal(PvpRules.ProtectedToken, server.PvpRefusalForTest("Justus", "Jonah"));
        server.AdvanceUptimeForTest(PvpRules.SpawnProtectionSeconds + 0.5);
        Assert.Null(server.PvpRefusalForTest("Justus", "Jonah"));
    }

    [Fact]
    public void ADefeatByAPlayer_NeverCostsAnItem_AndTheWorldHearsWhoWon()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_defeat", transport, r =>
        {
            r.Pvp = PvpMode.On;
            r.DeathPenalty = DeathPenalty.Hard;
            r.KeepInventoryOnDeath = false;
        });
        var (a, b) = TwoOnFoot(server);
        b.State.Inventory.Add("iron_ore", 20, 99);
        b.State.Health = 1f;

        server.AttackPlayerForTest("Justus", "Jonah");

        Assert.True(b.State.Health > 0f); // respawned
        Assert.Equal(20, b.State.Inventory.CountOf("iron_ore"));
        Assert.Contains(transport.Sent, x => x.Conn == a.ConnectionId && x.Msg is ServerMessage m && m.Text.Contains("Jonah", StringComparison.Ordinal));
        Assert.Contains(transport.Sent, x => x.Conn == b.ConnectionId && x.Msg is RespawnNotice n && n.Reason == PvpRules.DefeatedYouToken + ":Justus");
        Assert.Equal(PvpRules.ProtectedToken, server.PvpRefusalForTest("Justus", "Jonah")); // a moment to get up
    }

    [Fact]
    public void ADuel_IsTheOnlyFight_InDuelsOnly_AndNeverKills()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_duel", transport, r => r.Pvp = PvpMode.DuelsOnly);
        var (a, b) = TwoOnFoot(server);
        Assert.Equal(PvpRules.DuelsOnlyToken, server.PvpRefusalForTest("Justus", "Jonah"));

        server.DuelForTest("Justus", "challenge", "Jonah");
        Assert.Contains(transport.Sent, x => x.Conn == b.ConnectionId && x.Msg is DuelNotice n && n.Kind == "request" && n.Other == "Justus");
        server.DuelForTest("Jonah", "accept", "Justus");
        Assert.Null(server.PvpRefusalForTest("Justus", "Jonah"));

        var spot = b.State.Position;
        b.State.Health = 1f;
        transport.Sent.Clear();
        server.AttackPlayerForTest("Justus", "Jonah");

        Assert.Equal(PvpRules.DuelLoserHealth, b.State.Health); // back up, right where the duel was fought
        Assert.Equal(spot, b.State.Position);
        Assert.DoesNotContain(transport.Sent, x => x.Msg is RespawnNotice);
        Assert.Contains(transport.Sent, x => x.Conn == b.ConnectionId && x.Msg is DuelNotice n && n.Kind == "ended" && n.Winner == "Justus");
        Assert.Equal(PvpRules.DuelsOnlyToken, server.PvpRefusalForTest("Justus", "Jonah")); // the duel is over
    }

    [Fact]
    public void ADuelChallenge_RunsOut()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_duel_timeout", transport, r => r.Pvp = PvpMode.DuelsOnly);
        var (a, _) = TwoOnFoot(server);
        server.DuelForTest("Justus", "challenge", "Jonah");

        server.AdvanceUptimeForTest(PvpRules.DuelRequestSeconds + 1);
        server.TickPvpForTest();
        server.DuelForTest("Jonah", "accept", "Justus");

        Assert.Contains(transport.Sent, x => x.Conn == a.ConnectionId && x.Msg is DuelNotice n && n.Kind == "declined");
        Assert.Equal(PvpRules.DuelsOnlyToken, server.PvpRefusalForTest("Justus", "Jonah"));
    }

    [Fact]
    public void TheWorldAdmin_SwitchesPvpAndFriendlyFireLive()
    {
        var transport = new RecordingTransport();
        var server = Started("pvp_live", transport, _ => { });
        var admin = server.AddLocalPlayer("Marcel");
        admin.State.Role = BlocksBeyondTheStars.Shared.State.PlayerRole.WorldAdmin;

        server.HandlePayloadForTest(admin.ConnectionId, BlocksBeyondTheStars.Networking.NetCodec.Encode(
            new SetWorldRulesIntent { Pvp = "GroupBased", FriendlyFire = "On" }));

        Assert.Equal(PvpMode.GroupBased, server.RulesForTest.Pvp);
        Assert.True(server.RulesForTest.FriendlyFire);
        Assert.Contains(transport.Sent, x => x.Conn == admin.ConnectionId && x.Msg is ServerRules r && r.Pvp == "GroupBased" && r.FriendlyFire == "On");
    }
}
