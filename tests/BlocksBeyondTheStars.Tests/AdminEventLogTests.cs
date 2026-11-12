// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3506 the event log: a PvP defeat, a death (with its cause), a join and a leave and a report each produce exactly one
/// event; the ring buffer holds 500; only admins with the panel open ever receive an event or the backlog; every event key
/// exists in English and German.
/// </summary>
public sealed class AdminEventLogTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static List<NetAdminEvent> Events(AdminTestWorld w, string key)
        => w.Server.AdminEventsForTest.Where(e => e.TextKey == key).ToList();

    [Fact]
    public void APvpDefeat_IsOneEvent_WithAttackerTargetAndPlace()
    {
        using var w = new AdminTestWorld(Content, configure: c =>
        {
            c.Rules.Pvp = PvpMode.On;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
        });
        var a = w.Join("Justus");
        var b = w.Join("Jonah");
        var tank = a.State.RespawnPoint;
        foreach (var (s, dx) in new[] { (a, 0f), (b, 2f) })
        {
            s.State.Position = new Vector3f(tank.X + 30f + dx, tank.Y + 40f, tank.Z + 30f);
            w.Server.ClearPvpProtectionForTest(s.State.PlayerId);
        }

        var deathSpot = b.State.Position;
        b.State.Health = 1f;
        w.Server.AttackPlayerForTest("Justus", "Jonah");

        var ev = Assert.Single(Events(w, "admin.ev.pvp_defeat"));
        Assert.Equal(AdminEventCategories.Pvp, ev.Category);
        Assert.Equal(new[] { "Justus", "Jonah" }, ev.Args);
        Assert.Equal("Justus", ev.ActorId);
        Assert.Equal("Jonah", ev.TargetId);
        Assert.True(ev.HasPosition);
        Assert.Equal(deathSpot.X, ev.X);
        Assert.Empty(Events(w, "admin.ev.death")); // the defeat is not a second "death" row
    }

    [Fact]
    public void ADeath_IsOneEvent_WithItsCause()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        w.Server.KillPlayerForTest(player, "@srv.death.fall");

        var ev = Assert.Single(Events(w, "admin.ev.death"));
        Assert.Equal(AdminEventCategories.Death, ev.Category);
        Assert.Equal(new[] { "Justus", "@admin.cause.fall" }, ev.Args);
        Assert.Equal("@admin.cause.other", SvGameServer.AdminDeathCause("@srv.some.thing"));
        Assert.Equal("@admin.cause.creature", SvGameServer.AdminDeathCause("@srv.death.creature"));
    }

    [Fact]
    public void AJoinAndALeave_AreOneEventEach()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");
        Assert.Single(Events(w, "admin.ev.join"), e => e.ActorId == "Justus");

        w.Server.DisconnectLocalPlayerForTest(player.State.PlayerId);

        var left = Assert.Single(Events(w, "admin.ev.leave"));
        Assert.Equal(AdminEventCategories.Join, left.Category);
        Assert.Equal("Justus", left.ActorName);
    }

    [Fact]
    public void AReport_IsOneEvent()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        w.Server.CallAdminForTest(player, "I am stuck in a cave");

        var ev = Assert.Single(w.Server.AdminEventsForTest, e => e.Category == AdminEventCategories.Report);
        Assert.Equal("admin.ev.report_help", ev.TextKey);
        Assert.Equal(new[] { "Justus", "I am stuck in a cave" }, ev.Args);
    }

    [Fact]
    public void TheLog_KeepsTheLast500()
    {
        var log = new AdminEventLog();
        for (int i = 0; i < AdminEventLog.Capacity + 40; i++)
        {
            log.Add(new NetAdminEvent { TextKey = "admin.ev.join" }, staffOnly: false);
        }

        Assert.Equal(AdminEventLog.Capacity, log.Count);
        Assert.Equal(41, log.All.First().Event.Id); // the oldest 40 are gone
        Assert.Equal(AdminEventLog.Capacity + 40, log.All.Last().Event.Id);
    }

    [Fact]
    public void OnlyAdminsWithTheirPanelOpen_ReceiveEvents()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var otherAdmin = w.Join("Mira", PlayerRole.Admin);
        var player = w.Join("Justus");
        w.Server.SetAdminModeForTest(admin, true);
        w.Server.SetAdminPanelForTest(admin, true);
        w.Server.SetAdminPanelForTest(player, true); // refused
        w.Transport.Sent.Clear();

        w.Server.KillPlayerForTest(player, "@srv.death.critical");

        Assert.Single(w.To<AdminEvent>(admin), e => e.Event.TextKey == "admin.ev.death");
        Assert.Empty(w.To<AdminEvent>(otherAdmin)); // an admin, but the panel is closed
        Assert.Empty(w.To<AdminEvent>(player));
        Assert.Empty(w.To<AdminEventBacklog>(player));
    }

    [Fact]
    public void AHostedWorldsOwner_DoesNotSeeTheFleetAdminsOwnActions()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");
        w.Server.SetAdminModeForTest(owner, true);
        w.Server.SetAdminModeForTest(op, true); // "entered admin mode" — a staff-only row

        w.Transport.Sent.Clear();
        w.Server.SetAdminPanelForTest(owner, true);
        w.Server.SetAdminPanelForTest(op, true);

        var ownerSees = w.To<AdminEventBacklog>(owner).Single().Events;
        var opSees = w.To<AdminEventBacklog>(op).Single().Events;
        Assert.DoesNotContain(ownerSees, e => e.ActorId == "Operator" && e.Category == AdminEventCategories.Admin);
        Assert.Contains(opSees, e => e.ActorId == "Operator" && e.Category == AdminEventCategories.Admin);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryEventKeyAndCause_HasAText(string language)
    {
        var table = TestLocales.Load(language);
        string[] keys =
        {
            "admin.ev.join", "admin.ev.leave", "admin.ev.pvp_defeat", "admin.ev.duel_won", "admin.ev.duel_ended", "admin.ev.death",
            "admin.ev.filter_blocked", "admin.ev.filter_pii", "admin.ev.filter_masked", "admin.ev.auto_mute", "admin.ev.silence",
            "admin.ev.unsilence", "admin.ev.kick", "admin.ev.cheat", "admin.ev.mined", "admin.ev.report_done", "admin.ev.report_reopened",
        };
        foreach (string key in keys
                     .Concat(new[] { "player", "paint", "shape", "texture", "help", "feedback" }.Select(k => "admin.ev.report_" + k))
                     .Concat(new[]
                     {
                         "bandit", "boat", "burned", "corrosive_air", "crash", "creature", "critical", "fall", "froze", "gas", "giant",
                         "poison", "ressaw", "speeder", "tank_spider", "toxic_water", "wildlife", "other",
                     }.Select(c => "admin.cause." + c)))
        {
            Assert.True(table.ContainsKey(key) && !string.IsNullOrWhiteSpace(table[key]), $"{language}.json is missing '{key}'");
        }
    }
}
