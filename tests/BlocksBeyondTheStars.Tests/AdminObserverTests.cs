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
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Fleet-admin observer mode (issue #487) and the admin inspection commands (issue #488).
///
/// The point of these tests is the thing that is easy to get wrong and impossible to see in a screenshot:
/// that an observer produces NO outbound traffic about themselves and leaves no footprint in the world, and
/// that the power is bounded to the fleet admin rather than to whoever owns a world.
/// </summary>
public sealed class AdminObserverTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public AdminObserverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_observer_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every server send so a test can assert who was told what.</summary>
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

    private SvGameServer NewServer(string name, RecordingTransport transport, params string[] fleetAdmins)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            FleetAdminPlayers = fleetAdmins.ToList(),
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static void Spectate(SvGameServer server, PlayerSession session, string arg)
        => server.HandleForTest(session, new AdminCommandIntent { Command = "spectate", StringArg = arg });

    // ---------------- The role split (issue #487) ----------------

    [Fact]
    public void WorldAdmin_WithoutFleetAdmin_CannotObserve()
    {
        var transport = new RecordingTransport();
        var server = NewServer("role_split", transport); // nobody is a fleet admin

        var owner = server.AddLocalPlayer("Owner");
        Assert.Equal(PlayerRole.WorldAdmin, owner.State.Role); // the first joiner owns the world...
        Assert.False(owner.IsFleetAdmin);                      // ...but is not staff

        Spectate(server, owner, "on");

        Assert.False(owner.Spectating);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Action == "admin");
    }

    [Fact]
    public void FleetAdmin_IsGrantedFromConfig_AndNeverPersisted()
    {
        var transport = new RecordingTransport();
        var server = NewServer("fleet_grant", transport, "Operator");

        var op = server.AddLocalPlayer("Operator");
        Assert.True(op.IsFleetAdmin);

        // The elevation must not leak into the save: a downloaded/re-uploaded world would otherwise carry
        // operator rights into a world the operator does not control.
        Assert.NotEqual("FleetAdmin", op.State.Role.ToString());
        Assert.DoesNotContain("FleetAdmin", Enum.GetNames<PlayerRole>());
    }

    // ---------------- Invisibility (issue #487) ----------------

    [Fact]
    public void Observer_IsNotBroadcast_ToOtherPlayers()
    {
        var transport = new RecordingTransport();
        var server = NewServer("invisible", transport, "Operator");

        var player = server.AddLocalPlayer("Justus");
        var op = server.AddLocalPlayer("Operator");
        player.State.AboardShip = false;
        op.State.Position = player.State.Position; // right next to them, well inside the presence radius

        // Baseline: while visible, the operator IS broadcast to the player.
        transport.Sent.Clear();
        server.Tick(0.2);
        Assert.Contains(transport.Sent, x => x.Conn == player.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Operator");

        Spectate(server, op, "on");
        Assert.True(op.Spectating);

        // Entering must tell the world to drop the avatar it can already see, or every client keeps a frozen
        // copy of the admin standing there forever.
        Assert.Contains(transport.Sent, x => x.Msg is PlayerLeft l && l.PlayerId == "Operator");

        transport.Sent.Clear();
        server.Tick(0.2);

        Assert.DoesNotContain(transport.Sent, x => x.Msg is PlayerPresence p && p.PlayerId == "Operator");

        // The observer still SEES everyone else — one-way invisibility is the whole point.
        Assert.Contains(transport.Sent, x => x.Conn == op.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Justus");
    }

    [Fact]
    public void Observer_IsInvulnerable_AndIgnoredByCreatures()
    {
        var transport = new RecordingTransport();
        var server = NewServer("untouchable", transport, "Operator");
        var op = server.AddLocalPlayer("Operator");

        Spectate(server, op, "on");

        // GodMode is what the creature/enemy targeting code already honours, so observer mode reuses it
        // rather than inventing a second "please ignore me" flag that half the systems would miss.
        Assert.True(op.State.GodMode);
        Assert.True(op.State.Stealthed);

        op.State.Health = 10f;
        server.Tick(1.0);
        Assert.Equal(100f, op.State.Health); // pinned, not merely "not damaged this tick"
    }

    [Fact]
    public void Observer_LeavesNoFootprint_NoShipNoPad()
    {
        var transport = new RecordingTransport();
        var server = NewServer("footprint", transport, "Operator");
        var op = server.AddLocalPlayer("Operator");
        op.AssignedPadIndex = 3;

        transport.Sent.Clear();
        Spectate(server, op, "on");

        Assert.Equal(-1, op.AssignedPadIndex); // the pad is communal and finite — give it back
        Assert.DoesNotContain(transport.Sent, x => x.Msg is LandedShipState s && !s.Removed && s.PlayerId == "Operator");
    }

    [Fact]
    public void Observer_DoesNotCountAsAPlayer_ForTheServerCap()
    {
        var transport = new RecordingTransport();
        var server = NewServer("slots", transport, "Operator");

        server.AddLocalPlayer("Justus");
        var op = server.AddLocalPlayer("Operator");
        Spectate(server, op, "on");
        server.Tick(1.1); // the status snapshot republishes about once a second

        Assert.Contains("\"joinedPlayers\":1", server.StatusJson);
    }

    [Fact]
    public void Observer_MayRemoveBlocks_ButNotCraft()
    {
        var transport = new RecordingTransport();
        var server = NewServer("readonly", transport, "Operator");
        var op = server.AddLocalPlayer("Operator");
        Spectate(server, op, "on");

        // Moderation stays possible: removing an offensive build is the only in-world lever there is.
        Assert.True(SvGameServer.SpectatorMayHandleForTest(new MineBlockIntent()));

        // Everything that would change the world in a way nobody can attribute is dropped.
        Assert.False(SvGameServer.SpectatorMayHandleForTest(new CraftIntent()));
        Assert.False(SvGameServer.SpectatorMayHandleForTest(new PlaceBlockIntent()));
        Assert.False(SvGameServer.SpectatorMayHandleForTest(new AttackEntityIntent()));
    }

    [Fact]
    public void LeavingObserverMode_RestoresNormalPlay()
    {
        var transport = new RecordingTransport();
        var server = NewServer("leave", transport, "Operator");
        var op = server.AddLocalPlayer("Operator");

        Spectate(server, op, "on");
        Spectate(server, op, "off");

        Assert.False(op.Spectating);
        Assert.False(op.State.GodMode);
        Assert.False(op.State.Stealthed);

        transport.Sent.Clear();
        server.AddLocalPlayer("Justus").State.Position = op.State.Position;
        server.Tick(0.2);
        Assert.Contains(transport.Sent, x => x.Msg is PlayerPresence p && p.PlayerId == "Operator");
    }

    // ---------------- Inspection commands (issue #488) ----------------

    [Fact]
    public void Players_ListsOfflinePlayersFromTheSave()
    {
        var transport = new RecordingTransport();
        var server = NewServer("players_cmd", transport, "Operator");

        var gone = server.AddLocalPlayer("Justus");
        gone.State.Position = new Vector3f(120f, 70f, -40f);
        server.SaveAllForTest();
        server.DisconnectLocalPlayerForTest("Justus");

        var op = server.AddLocalPlayer("Operator");
        transport.Sent.Clear();
        server.HandleForTest(op, new AdminCommandIntent { Command = "players" });

        var lines = transport.Sent.Where(x => x.Msg is ServerMessage).Select(x => ((ServerMessage)x.Msg).Text).ToList();
        Assert.Contains(lines, l => l.Contains("Justus", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("120/70/-40", StringComparison.Ordinal));
    }

    [Fact]
    public void Where_ReportsAnOfflinePlayersLastPosition()
    {
        var transport = new RecordingTransport();
        var server = NewServer("where_cmd", transport, "Operator");

        var gone = server.AddLocalPlayer("Justus");
        gone.State.Position = new Vector3f(10f, 65f, 20f);
        server.SaveAllForTest();
        server.DisconnectLocalPlayerForTest("Justus");

        var op = server.AddLocalPlayer("Operator");
        transport.Sent.Clear();
        server.HandleForTest(op, new AdminCommandIntent { Command = "where", StringArg = "Justus" });

        var lines = transport.Sent.Where(x => x.Msg is ServerMessage).Select(x => ((ServerMessage)x.Msg).Text).ToList();
        Assert.Contains(lines, l => l.Contains("10/65/20", StringComparison.Ordinal));
    }

    [Fact]
    public void InspectionCommands_WorkWithCheatsDisabled()
    {
        // The whole point of the role gate: AdminCheats defaults to false and hosted worlds never turn it on,
        // so hanging oversight on that option would make it dead exactly where it is needed.
        var transport = new RecordingTransport();
        var server = NewServer("no_cheats", transport, "Operator");
        var op = server.AddLocalPlayer("Operator");

        transport.Sent.Clear();
        server.HandleForTest(op, new AdminCommandIntent { Command = "players" });

        Assert.DoesNotContain(transport.Sent, x => x.Msg is ActionRejected);
        Assert.Contains(transport.Sent, x => x.Msg is ServerMessage);
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test host still holding the file is not a test failure.
        }
    }
}
