// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The order of the gates in front of every admin command (#2886): the role first (a world admin, a fleet admin, or the
/// owner of a base here asking <c>/basewalls</c>), then the fleet-admin gate of <c>/spectate</c> and <c>/goto</c>, then
/// the world's cheat rule for the cheat commands. An unknown verb answers <c>cheats_disabled</c> on a world without
/// cheats and <c>unknown_cmd</c> on one with them — the old switch reached its default only past the cheat gate, and the
/// command table keeps that answer. Verbs are matched in any letter case.
/// </summary>
public sealed class AdminCommandGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_admingate_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport, out SqliteWorldRepository repo, bool cheats)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            Rules = cheats
                ? new GameRules { AdminCheats = true, AllowCheatsInSurvival = true }
                : new GameRules { AdminCheats = false },
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot in mid-air on rocky, as a world admin or a plain player.</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name, bool admin)
    {
        var s = server.AddLocalPlayer(name);
        s.State.Role = admin ? PlayerRole.WorldAdmin : PlayerRole.Player;
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(300f, 120, 0);
        return s;
    }

    /// <summary>Runs one admin command and returns what the server sent because of it.</summary>
    private static List<(int Conn, object Msg)> Run(SvGameServer server, RecordingTransport t, PlayerSession who,
        string command, string? arg = null)
    {
        int before = t.Sent.Count;
        server.HandleForTest(who, new AdminCommandIntent { Command = command, StringArg = arg });
        return t.Sent.Skip(before).ToList();
    }

    /// <summary>The refusals one player got for a command they typed (an <see cref="ActionRejected"/> for "admin").</summary>
    private static List<string> Refusals(List<(int Conn, object Msg)> sent, PlayerSession who)
        => sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>()
            .Where(r => r.Action == "admin").Select(r => r.Reason).ToList();

    /// <summary>The chat lines one player got (a <see cref="ServerMessage"/> sent to their connection).</summary>
    private static List<string> Lines(List<(int Conn, object Msg)> sent, PlayerSession who)
        => sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToList();

    [Theory]
    [InlineData("players")]
    [InlineData("fly")]
    [InlineData("spectate")]
    [InlineData("no_such_command")]
    public void NonAdmin_AnyVerb_IsRefusedWithNotAdmin(string verb)
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_nonadmin_" + verb, t, out var repo, cheats: true);
        using (repo)
        {
            var who = OnFoot(server, "Guest", admin: false);

            var sent = Run(server, t, who, verb);

            Assert.Equal("@srv.admin.not_admin", Assert.Single(Refusals(sent, who)));
            Assert.Empty(Lines(sent, who));
        }
    }

    [Fact]
    public void WorldAdmin_UnknownVerb_CheatsOff_AnswersCheatsDisabled()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_unknown_off", t, out var repo, cheats: false);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, who, "no_such_command");

            Assert.Equal("@srv.admin.cheats_disabled", Assert.Single(Refusals(sent, who)));
            Assert.Empty(Lines(sent, who));
        }
    }

    [Fact]
    public void WorldAdmin_UnknownVerb_CheatsOn_AnswersUnknownCmd()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_unknown_on", t, out var repo, cheats: true);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, who, "no_such_command");

            Assert.Equal("@srv.admin.unknown_cmd", Assert.Single(Refusals(sent, who)));
            Assert.Empty(Lines(sent, who));
        }
    }

    [Fact]
    public void WorldAdmin_NullVerb_CheatsOn_AnswersUnknownCmd()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_null_on", t, out var repo, cheats: true);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, who, null!);

            Assert.Equal("@srv.admin.unknown_cmd", Assert.Single(Refusals(sent, who)));
        }
    }

    [Fact]
    public void WorldAdmin_NullVerb_CheatsOff_AnswersCheatsDisabled()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_null_off", t, out var repo, cheats: false);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, who, null!);

            Assert.Equal("@srv.admin.cheats_disabled", Assert.Single(Refusals(sent, who)));
        }
    }

    [Fact]
    public void WorldAdmin_CheatVerb_CheatsOff_AnswersCheatsDisabled()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_cheat_off", t, out var repo, cheats: false);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);
            bool flyBefore = who.State.Fly;

            var sent = Run(server, t, who, "fly");

            Assert.Equal("@srv.admin.cheats_disabled", Assert.Single(Refusals(sent, who)));
            Assert.Empty(Lines(sent, who));
            Assert.Equal(flyBefore, who.State.Fly);
            Assert.False(who.State.Fly);
        }
    }

    [Fact]
    public void WorldAdmin_RoleVerb_CheatsOff_IsAnswered()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_role_off", t, out var repo, cheats: false);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, who, "players");

            Assert.NotEmpty(Lines(sent, who));
            Assert.Empty(Refusals(sent, who));
        }
    }

    [Theory]
    [InlineData("spectate", true)]
    [InlineData("spectate", false)]
    [InlineData("goto", true)]
    [InlineData("goto", false)]
    public void WorldAdmin_SpectateAndGoto_AnswerObserverReserved(string verb, bool cheats)
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_observer_" + verb + "_" + cheats, t, out var repo, cheats);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);
            Assert.False(who.IsFleetAdmin);

            var sent = Run(server, t, who, verb, "rocky");

            Assert.Equal("@srv.admin.observer_reserved", Assert.Single(Refusals(sent, who)));
            Assert.Empty(Lines(sent, who));
        }
    }

    [Fact]
    public void FleetAdmin_WithPlayerRole_PassesRoleGate()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_fleet", t, out var repo, cheats: false);
        using (repo)
        {
            var who = OnFoot(server, "Operator", admin: false);
            who.IsFleetAdmin = true;

            var sent = Run(server, t, who, "players");

            Assert.NotEmpty(Lines(sent, who));
            Assert.Empty(Refusals(sent, who));
        }
    }

    /// <summary>#1728: the role gate's one exemption — a plain player who owns a base on this body may ask
    /// <c>/basewalls</c> (a read-only report about their own walls), also on a world without cheats. The same player is
    /// refused before founding the base and for every other verb, and a plain player who owns no base here stays refused.</summary>
    [Fact]
    public void BaseOwner_WithPlayerRole_PassesRoleGate_ForBaseWallsOnly()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_basewalls_owner", t, out var repo, cheats: false);
        using (repo)
        {
            var owner = OnFoot(server, "Builder", admin: false);
            var stranger = OnFoot(server, "Stranger", admin: false);
            Assert.Equal("@srv.admin.not_admin", Assert.Single(Refusals(Run(server, t, owner, "basewalls"), owner)));

            server.PlaceBaseForTest(owner, new Vector3i(300, 118, 0));

            var sent = Run(server, t, owner, "basewalls");
            Assert.Empty(Refusals(sent, owner));
            Assert.Contains(Lines(sent, owner), l => l.Contains("Builder's Base", StringComparison.Ordinal));

            Assert.Empty(Refusals(Run(server, t, owner, "BaseWalls"), owner)); // verbs match in any letter case
            Assert.Equal("@srv.admin.not_admin", Assert.Single(Refusals(Run(server, t, owner, "players"), owner)));
            Assert.Equal("@srv.admin.not_admin", Assert.Single(Refusals(Run(server, t, stranger, "basewalls"), stranger)));
        }
    }

    [Fact]
    public void VerbsAreCaseInsensitive()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_case", t, out var repo, cheats: true);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);
            Assert.False(who.State.Fly);

            var fly = Run(server, t, who, "FLY");
            Assert.True(who.State.Fly);
            Assert.Equal("@srv.admin.fly_on", Assert.Single(Lines(fly, who)));
            Assert.Empty(Refusals(fly, who));

            var players = Run(server, t, who, "Players");
            Assert.NotEmpty(Lines(players, who));
            Assert.Empty(Refusals(players, who));
        }
    }

    /// <summary>The command table: the forty verbs and the gate each one sits behind. A new verb is a line here too.</summary>
    [Fact]
    public void Table_ListsExactlyTheFortyVerbsWithTheirGates()
    {
        var expected = new Dictionary<string, SvGameServer.AdminCommandGate>(StringComparer.Ordinal)
        {
            ["ai_mission"] = SvGameServer.AdminCommandGate.Role,
            ["announce"] = SvGameServer.AdminCommandGate.Role,
            ["kick"] = SvGameServer.AdminCommandGate.Role,
            ["silence"] = SvGameServer.AdminCommandGate.Role,
            ["unsilence"] = SvGameServer.AdminCommandGate.Role,
            ["schedule_restart"] = SvGameServer.AdminCommandGate.Role,
            ["cancel_restart"] = SvGameServer.AdminCommandGate.Role,
            ["players"] = SvGameServer.AdminCommandGate.Role,
            ["builds"] = SvGameServer.AdminCommandGate.Role,
            ["basewalls"] = SvGameServer.AdminCommandGate.Role,
            ["creatures"] = SvGameServer.AdminCommandGate.Role,
            ["shapewipe"] = SvGameServer.AdminCommandGate.Role,
            ["paintwipe"] = SvGameServer.AdminCommandGate.Role,
            ["texturewipe"] = SvGameServer.AdminCommandGate.Role,
            ["where"] = SvGameServer.AdminCommandGate.Role,
            ["set_mode"] = SvGameServer.AdminCommandGate.Role,
            ["set_world_mode"] = SvGameServer.AdminCommandGate.Role,
            ["tutorial"] = SvGameServer.AdminCommandGate.Role,
            ["story_status"] = SvGameServer.AdminCommandGate.Role,
            ["cutscene"] = SvGameServer.AdminCommandGate.Role,
            ["spectate"] = SvGameServer.AdminCommandGate.FleetAdmin,
            ["goto"] = SvGameServer.AdminCommandGate.FleetAdmin,
            ["give_item"] = SvGameServer.AdminCommandGate.Cheats,
            ["teleport_to_location"] = SvGameServer.AdminCommandGate.Cheats,
            ["teleport_to_named"] = SvGameServer.AdminCommandGate.Cheats,
            ["teleport_to_player"] = SvGameServer.AdminCommandGate.Cheats,
            ["set_time"] = SvGameServer.AdminCommandGate.Cheats,
            ["set_weather"] = SvGameServer.AdminCommandGate.Cheats,
            ["fly"] = SvGameServer.AdminCommandGate.Cheats,
            ["godmode"] = SvGameServer.AdminCommandGate.Cheats,
            ["summon_giant"] = SvGameServer.AdminCommandGate.Cheats,
            ["summon_arachnid"] = SvGameServer.AdminCommandGate.Cheats,
            ["summon_biped"] = SvGameServer.AdminCommandGate.Cheats,
            ["summon_wanderer"] = SvGameServer.AdminCommandGate.Cheats,
            ["instant_build"] = SvGameServer.AdminCommandGate.Cheats,
            ["advance_story"] = SvGameServer.AdminCommandGate.Cheats,
            ["reveal_finale"] = SvGameServer.AdminCommandGate.Cheats,
            ["grant_module"] = SvGameServer.AdminCommandGate.Cheats,
            ["reveal_lore"] = SvGameServer.AdminCommandGate.Cheats,
            ["goto_core"] = SvGameServer.AdminCommandGate.Cheats,
        };
        var actual = SvGameServer.AdminCommandTableForTest().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        Assert.Equal(40, expected.Count);
        Assert.Equal(40, actual.Count);
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (verb, gate) in expected)
        {
            Assert.True(gate == actual[verb], $"/{verb}: expected the {gate} gate, the table has {actual[verb]}");
        }
    }

    [Fact]
    public void Fly_Toggles_OnThenOff()
    {
        var t = new RecordingTransport();
        var server = NewServer("gate_fly", t, out var repo, cheats: true);
        using (repo)
        {
            var who = OnFoot(server, "Admin", admin: true);
            Assert.False(who.State.Fly);

            var on = Run(server, t, who, "fly");
            Assert.Equal("@srv.admin.fly_on", Assert.Single(Lines(on, who)));
            Assert.True(who.State.Fly);

            var off = Run(server, t, who, "fly");
            Assert.Equal("@srv.admin.fly_off", Assert.Single(Lines(off, who)));
            Assert.False(who.State.Fly);
        }
    }
}
