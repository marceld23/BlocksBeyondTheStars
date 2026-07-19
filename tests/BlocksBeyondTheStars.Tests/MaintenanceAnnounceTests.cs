// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Maintenance announcements (#249), against the real authoritative server: the restart countdown
/// re-broadcasts at its threshold marks and ends in the graceful RequestStop drain; cancel clears it;
/// mid-countdown joiners receive the active notice; the world admin's announce commands work even with
/// cheats disabled; and the gateway's POST /announce is token-gated.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // the /announce HTTP round-trips starve in the parallel suite
public sealed class MaintenanceAnnounceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MaintenanceAnnounceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_maint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer Server, LoopbackLink Link, LoopbackClientTransport Client, List<MaintenanceNotice> Notices)
        Start(string world, bool cheats = true)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = cheats },
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link), repo);
        server.Start();

        var (client, notices) = JoinAndListen(server, link, "Creator");
        return (server, link, client, notices);
    }

    /// <summary>Joins a player through the loopback transport and collects every MaintenanceNotice it receives.</summary>
    private static (LoopbackClientTransport Client, List<MaintenanceNotice> Notices) JoinAndListen(
        SvGameServer server, LoopbackLink link, string name)
    {
        var notices = new List<MaintenanceNotice>();
        var client = new LoopbackClientTransport(link);
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is MaintenanceNotice n)
            {
                notices.Add(n);
            }
        };

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        return (client, notices);
    }

    private static void TickAndPoll(SvGameServer server, LoopbackClientTransport client, double dt, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            server.Tick(dt);
            client.Poll();
        }
    }

    // ---------------- Intake validation (pure) ----------------

    [Fact]
    public void EnqueueMaintenance_ValidatesKindTextAndDuration()
    {
        var (server, _, _, _) = Start("maint_validate");

        Assert.False(server.EnqueueMaintenance(MaintenanceNotice.KindInfo, "   ", -1));   // info needs text
        Assert.False(server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, null, 0));   // no zero countdown
        Assert.False(server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, null, 181 * 60)); // above the cap
        Assert.False(server.EnqueueMaintenance(99, "x", -1)); // unknown kind

        Assert.True(server.EnqueueMaintenance(MaintenanceNotice.KindInfo, "Hello", -1));
        Assert.True(server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, null, 600));
        Assert.True(server.EnqueueMaintenance(MaintenanceNotice.KindCancelled, null, -1));
    }

    // ---------------- Restart countdown ----------------

    [Fact]
    public void ScheduledRestart_RebroadcastsAtThresholds_ThenStopsGracefully()
    {
        var (server, _, client, notices) = Start("maint_countdown");

        Assert.True(server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, "New version!", 61));
        TickAndPoll(server, client, 0.1); // apply + initial broadcast (61 s)

        TickAndPoll(server, client, 1.0, times: 65); // ride the countdown past zero + the 2 s flush

        // Initial (61) + the 60/30/10 marks + the final "restarting now" (0); the marks above the start
        // (600/300/120) are skipped by ApplyMaintenance.
        Assert.Equal(new[] { 61, 60, 30, 10, 0 }, notices.Select(n => n.SecondsRemaining).ToArray());
        Assert.All(notices, n => Assert.Equal(MaintenanceNotice.KindRestartCountdown, n.Kind));
        Assert.All(notices, n => Assert.Equal("New version!", n.Text));
        Assert.Equal("ui.maint.restart_in", notices[0].MessageKey);
        Assert.Equal("ui.maint.restarting_now", notices[^1].MessageKey);

        Assert.True(server.MaintenanceStopTriggered); // the graceful RequestStop drain fired after the flush
    }

    [Fact]
    public void Cancel_ClearsTheCountdown_AndNothingStops()
    {
        var (server, _, client, notices) = Start("maint_cancel");

        server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, null, 30);
        TickAndPoll(server, client, 1.0, times: 5);
        server.EnqueueMaintenance(MaintenanceNotice.KindCancelled, null, -1);
        TickAndPoll(server, client, 1.0);

        Assert.Equal(MaintenanceNotice.KindCancelled, notices[^1].Kind);
        Assert.Equal(-1, server.MaintenanceSecondsRemaining);

        TickAndPoll(server, client, 1.0, times: 40); // way past the original 30 s
        Assert.False(server.MaintenanceStopTriggered);
    }

    [Fact]
    public void JoinerDuringCountdown_ReceivesTheActiveNotice()
    {
        var (server, link, client, _) = Start("maint_late_join");

        server.EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, "Heads up", 300);
        TickAndPoll(server, client, 1.0, times: 5);

        // The creator leaves before the latecomer joins: the loopback link has a single connection id,
        // and a JoinRequest on an already-joined connection is dropped since #424 S8 (re-join guard) —
        // the countdown itself is server-global and survives the world being empty.
        client.Disconnect();
        server.Tick(0.1);

        var (_, lateNotices) = JoinAndListen(server, link, "Latecomer");

        var notice = Assert.Single(lateNotices);
        Assert.Equal(MaintenanceNotice.KindRestartCountdown, notice.Kind);
        Assert.Equal("Heads up", notice.Text);
        Assert.InRange(notice.SecondsRemaining, 290, 300);
    }

    // ---------------- Admin commands ----------------

    [Fact]
    public void AnnounceCommands_WorkForTheWorldAdmin_EvenWithCheatsDisabled()
    {
        var (server, _, client, notices) = Start("maint_cmd", cheats: false);

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "announce", StringArg = "Hello world" }),
            DeliveryMode.ReliableOrdered);
        TickAndPoll(server, client, 0.1, times: 2); // dispatch + the maintenance tick that broadcasts

        var info = Assert.Single(notices);
        Assert.Equal(MaintenanceNotice.KindInfo, info.Kind);
        Assert.Equal("Hello world", info.Text); // control chars are stripped before broadcasting

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "schedule_restart", IntArg = 10 }),
            DeliveryMode.ReliableOrdered);
        TickAndPoll(server, client, 0.1, times: 2);
        Assert.Equal(600, server.MaintenanceSecondsRemaining);

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "cancel_restart" }), DeliveryMode.ReliableOrdered);
        TickAndPoll(server, client, 0.1, times: 2);
        Assert.Equal(-1, server.MaintenanceSecondsRemaining);
    }

    [Fact]
    public void AnnounceCommand_IsRejected_ForNonAdmins()
    {
        var (server, link, client, _) = Start("maint_guard");
        TickAndPoll(server, client, 0.1);

        // The admin creator leaves, then a guest joins on the freed loopback connection (a second
        // JoinRequest on a live joined connection is dropped since #424 S8). The creator's WorldAdmin
        // role is persisted on THEIR player record — the guest joins as a plain player.
        client.Disconnect();
        server.Tick(0.1);

        ActionRejected? rejected = null;
        var guest = new LoopbackClientTransport(link);
        guest.PayloadReceived += pl => { if (NetCodec.Decode(pl) is ActionRejected r) { rejected = r; } };
        guest.Connect("loopback", 0);
        guest.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Guest" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        guest.Send(NetCodec.Encode(new AdminCommandIntent { Command = "announce", StringArg = "pwned" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        guest.Poll();

        Assert.NotNull(rejected);
        Assert.Equal(-1, server.MaintenanceSecondsRemaining);
    }

    // ---------------- Gateway POST /announce ----------------

    [Fact]
    public async Task AnnounceEndpoint_IsTokenGated_AndForwardsTheRequestAsync()
    {
        int port = FreePort();
        var transport = new WebSocketServerTransport("localhost")
        {
            AnnounceToken = "sesame",
        };
        var received = new List<(byte Kind, string? Text, int Seconds)>();
        transport.AnnounceReceiver = (kind, text, seconds) =>
        {
            received.Add((kind, text, seconds));
            return kind != 99; // let the test drive a "server said no" answer too
        };

        transport.Start(port);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

            // No/wrong token → 401, nothing forwarded.
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                (await PostAsync(http, null, "{\"kind\":1,\"seconds\":600}")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                (await PostAsync(http, "wrong", "{\"kind\":1,\"seconds\":600}")).StatusCode);
            Assert.Empty(received);

            // Valid token + body → 200 and the receiver saw exactly the payload.
            Assert.Equal(System.Net.HttpStatusCode.OK,
                (await PostAsync(http, "sesame", "{\"kind\":1,\"text\":\"update\",\"seconds\":600}")).StatusCode);
            Assert.Equal(((byte)1, "update", 600), Assert.Single(received));

            // Malformed JSON → 400; a receiver "no" → 400.
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await PostAsync(http, "sesame", "{not json")).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await PostAsync(http, "sesame", "{\"kind\":99}")).StatusCode);
        }
        finally
        {
            transport.Stop();
        }
    }

    [Fact]
    public async Task AnnounceEndpoint_StaysDisabled_WithoutATokenAsync()
    {
        int port = FreePort();
        var transport = new WebSocketServerTransport("localhost")
        {
            AnnounceReceiver = (_, _, _) => true, // receiver alone must not be enough
        };
        transport.Start(port);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await PostAsync(http, "anything", "{\"kind\":0,\"text\":\"x\"}")).StatusCode);
        }
        finally
        {
            transport.Stop();
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient http, string? token, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/announce");
        if (token is not null)
        {
            request.Headers.Add("X-Announce-Token", token);
        }

        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await http.SendAsync(request);
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — temp cleanup only
        }
    }
}
