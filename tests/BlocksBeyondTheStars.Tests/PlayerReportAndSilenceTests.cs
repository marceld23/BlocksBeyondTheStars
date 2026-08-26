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
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The two halves of "someone is being unpleasant and I can do something about it": a player report that
/// works without any account (#1222) and an admin who can pause a chat instead of only kicking (#1223).
///
/// They share one thing — the per-session ring buffer of recent chat lines — which is why they were built
/// together: a report is worth little without the evidence, and the same excerpt is what makes an admin's
/// decision reviewable afterwards.
/// </summary>
public sealed class PlayerReportAndSilenceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public PlayerReportAndSilenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_report_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    /// <summary>A reporter with no radio at all (the arcade guest's situation — reporting must not depend on
    /// equipment) and a talker who holds one, so their lines are relayed and recorded as evidence.</summary>
    private static (PlayerSession Alice, PlayerSession Bob) Pair(SvGameServer server)
    {
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.InstallId = "install-alice";
        bob.InstallId = "install-bob";
        bob.State.Inventory.Add("comm_radio", 1, 1);
        return (alice, bob);
    }

    private static void Say(SvGameServer server, PlayerSession who, string text)
    {
        who.LastChatTick = Environment.TickCount - 10_000; // the 700 ms slot is TickCount-based
        server.Chat(who.State.Name, text);
    }

    private static IEnumerable<string> MessagesTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static IEnumerable<string> RejectionsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason);

    private static IEnumerable<ChatMessage> ChatTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ChatMessage>();

    // ---------------- Reporting a player (#1222) ----------------

    [Fact]
    public void AReport_CarriesTheReportedPlayersOwnLines_AndBothInstallIds()
    {
        var transport = new RecordingTransport();
        var server = NewServer("report_ok", transport);
        var (alice, bob) = Pair(server);

        Say(server, bob, "give me your stuff or else");
        Say(server, bob, "i said give it");
        transport.Sent.Clear();

        Say(server, alice, "/report Bob he keeps threatening me");

        Assert.Contains("@srv.report.sent:Bob", MessagesTo(transport, alice));

        string json = Assert.IsType<string>(server.LastPlayerReportJsonForTest);
        Assert.Contains("player-report", json);
        Assert.Contains("he keeps threatening me", json);
        Assert.Contains("give me your stuff or else", json);
        Assert.Contains("i said give it", json);
        Assert.Contains("install-alice", json); // the reporter's identity — all an arcade guest has
        Assert.Contains("install-bob", json);
        Assert.Contains("report_ok", json);     // which world it happened on
    }

    [Fact]
    public void AReport_NeedsNoRadio_AndIsNeverRelayedAsChat()
    {
        // Alice holds nothing. Reporting is intercepted before the radio gate for the same reason /bump is:
        // the moment you need it is not the moment to go and craft something.
        var transport = new RecordingTransport();
        var server = NewServer("report_norelay", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "/report Bob rude");

        Assert.Contains("@srv.report.sent:Bob", MessagesTo(transport, alice));
        Assert.Empty(ChatTo(transport, bob));
        Assert.DoesNotContain(transport.Sent, s => s.Msg is ChatMessage c && c.Text.Contains("/report"));
    }

    [Fact]
    public void ABareReport_ShowsHowToUseIt()
    {
        var transport = new RecordingTransport();
        var server = NewServer("report_usage", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "/report");

        Assert.Contains("@srv.report.usage", MessagesTo(transport, alice));
        Assert.Null(server.LastPlayerReportJsonForTest);
    }

    [Fact]
    public void ReportingSomeoneWhoIsNotHere_OrYourself_FilesNothing()
    {
        var transport = new RecordingTransport();
        var server = NewServer("report_target", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "/report Nobody at all");
        Assert.Contains("@srv.report.no_target", MessagesTo(transport, alice));

        Say(server, alice, "/report Alice");
        Assert.Contains("@srv.report.self", MessagesTo(transport, alice));

        Assert.Null(server.LastPlayerReportJsonForTest);
    }

    [Fact]
    public void APlayerNameWithASpace_IsFoundWholeRatherThanCutInHalf()
    {
        // #980 all over again: names contain spaces, so the whole argument is tried as a name first.
        var transport = new RecordingTransport();
        var server = NewServer("report_spaces", transport);
        var alice = server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("mincraft Fan");
        transport.Sent.Clear();

        Say(server, alice, "/report mincraft Fan");

        Assert.Contains("@srv.report.sent:mincraft Fan", MessagesTo(transport, alice));
    }

    [Fact]
    public void Reporting_IsCapped_SoTheInboxCannotBeTheThingThatNeedsModerating()
    {
        var transport = new RecordingTransport();
        var server = NewServer("report_cap", transport);
        var (alice, _) = Pair(server);
        server.AddLocalPlayer("Carl");
        transport.Sent.Clear();

        for (int i = 0; i < 3; i++)
        {
            Say(server, alice, "/report Carl again");
        }

        Assert.Equal(3, MessagesTo(transport, alice).Count(m => m.StartsWith("@srv.report.sent", StringComparison.Ordinal)));

        Say(server, alice, "/report Carl and again");
        Assert.Contains("@srv.report.too_many", MessagesTo(transport, alice));

        // …and the window really is a window: it opens again once it has passed.
        server.AdvanceUptimeForTest(601);
        Say(server, alice, "/report Carl much later");
        Assert.Equal(4, MessagesTo(transport, alice).Count(m => m.StartsWith("@srv.report.sent", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheEvidenceBuffer_KeepsTheLastTwentyLinesOnly()
    {
        var transport = new RecordingTransport();
        var server = NewServer("report_buffer", transport);
        var (_, bob) = Pair(server);

        for (int i = 0; i < 25; i++)
        {
            // Three seconds apart: six lines inside ten seconds is a burst and earns an auto-mute (#1208),
            // which would stop the lines from ever reaching the buffer. A real conversation is not a burst.
            server.AdvanceUptimeForTest(3);
            Say(server, bob, "line " + i);
        }

        Assert.Equal(PlayerSession.MaxRecentChatLines, bob.RecentChatLines.Count);
        Assert.Equal("line 5", bob.RecentChatLines[0]);   // the first five aged out
        Assert.Equal("line 24", bob.RecentChatLines[^1]);
    }

    [Fact]
    public void TheEvidence_IsWhatTheOthersSaw_MaskedWordsIncluded()
    {
        // The excerpt is the relayed text, not the raw one: an operator is reviewing behaviour, not
        // collecting the unfiltered version of it.
        var transport = new RecordingTransport();
        var server = NewServer("report_masked", transport);
        var (alice, bob) = Pair(server);

        Say(server, bob, "you are an asshole");
        Say(server, alice, "/report Bob rude");

        string json = Assert.IsType<string>(server.LastPlayerReportJsonForTest);
        Assert.Contains("*******", json);
        Assert.DoesNotContain("asshole", json);
    }

    // ---------------- An admin pauses a chat (#1223) ----------------

    private static void Admin(SvGameServer server, PlayerSession who, string command, string target, int minutes = 0)
        => server.HandleForTest(who, new AdminCommandIntent { Command = command, StringArg = target, IntArg = minutes });

    [Fact]
    public void AnAdmin_PausesAndResumesAPlayersChat()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_ok", transport);
        var (alice, bob) = Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;
        transport.Sent.Clear();

        Admin(server, admin, "silence", "Bob", 5);

        Assert.Contains("@srv.admin.silenced:Bob", MessagesTo(transport, admin));
        // The player is told, with the same notice the automatic cool-down uses — silence with no
        // explanation reads as a broken game.
        Assert.Contains(MessagesTo(transport, bob), m => m.StartsWith("@srv.chat.muted_until:", StringComparison.Ordinal));
        Assert.True(server.IsChatMutedForTest("Bob"));

        transport.Sent.Clear();
        Say(server, bob, "can anyone hear me");
        Assert.Empty(ChatTo(transport, alice));

        Admin(server, admin, "unsilence", "Bob");
        Assert.Contains("@srv.admin.unsilenced:Bob", MessagesTo(transport, admin));
        Assert.Contains("@srv.chat.unmuted", MessagesTo(transport, bob));
        Assert.False(server.IsChatMutedForTest("Bob"));

        transport.Sent.Clear();
        Say(server, bob, "there we go");
        Assert.Equal(new[] { "there we go" }, ChatTo(transport, alice).Select(c => c.Text));
    }

    [Fact]
    public void APause_EndsByItself()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_expiry", transport);
        var (alice, bob) = Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;

        Admin(server, admin, "silence", "Bob", 5);
        Assert.True(server.IsChatMutedForTest("Bob"));

        server.AdvanceUptimeForTest(5 * 60 + 1);
        Assert.False(server.IsChatMutedForTest("Bob"));

        transport.Sent.Clear();
        Say(server, bob, "back again");
        Assert.Equal(new[] { "back again" }, ChatTo(transport, alice).Select(c => c.Text));
    }

    [Fact]
    public void OnlyAnAdminMaySilenceAnyone()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_role", transport);
        Pair(server);

        // Note the explicit role: the FIRST player on a fresh world becomes its WorldAdmin, so "the other
        // player" is not automatically an ordinary one.
        var guest = server.AddLocalPlayer("Guest");
        guest.State.Role = PlayerRole.Player;
        transport.Sent.Clear();

        Admin(server, guest, "silence", "Bob", 5);

        Assert.Contains("@srv.admin.not_admin", RejectionsTo(transport, guest));
        Assert.False(server.IsChatMutedForTest("Bob"));
    }

    [Fact]
    public void SilencingYourself_OrSomebodyWhoIsNotHere_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_target", transport);
        Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;
        transport.Sent.Clear();

        Admin(server, admin, "silence", "Chef", 5);
        Assert.Contains("@srv.admin.silence_self", RejectionsTo(transport, admin));

        Admin(server, admin, "silence", "Nobody", 5);
        Assert.Contains("@srv.admin.silence_no_target:Nobody", RejectionsTo(transport, admin));

        Admin(server, admin, "silence", string.Empty, 5);
        Assert.Contains("@srv.admin.usage_silence", RejectionsTo(transport, admin));
    }

    [Fact]
    public void ThePauseLength_IsClampedToSomethingAnAdminCanUndoBeforeBedtime()
    {
        // No number means the default; an absurd one is clamped to a day. Anything longer is a ban decision,
        // and bans live on the portal where the world's identity does.
        var transport = new RecordingTransport();
        var server = NewServer("silence_clamp", transport);
        Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;

        Admin(server, admin, "silence", "Bob"); // no minutes given
        Assert.True(server.IsChatMutedForTest("Bob"));
        server.AdvanceUptimeForTest(10 * 60 + 1);
        Assert.False(server.IsChatMutedForTest("Bob"));

        Admin(server, admin, "silence", "Bob", 99_999);
        server.AdvanceUptimeForTest(1440 * 60 + 1);
        Assert.False(server.IsChatMutedForTest("Bob"));
    }

    // ---------------- the pause follows the player, not the socket (#1294) ----------------

    [Fact]
    public void APause_SurvivesLeavingAndRejoining()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_rejoin", transport);
        var (alice, _) = Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;

        Admin(server, admin, "silence", "Bob", 5);
        Assert.True(server.IsChatMutedForTest("Bob"));

        server.DisconnectLocalPlayerForTest("Bob");
        Assert.True(server.IsChatMutedForTest("Bob")); // still on the books while they are away

        var bob2 = server.AddLocalPlayer("Bob");
        bob2.State.Inventory.Add("comm_radio", 1, 1);
        transport.Sent.Clear();

        Say(server, bob2, "back, can anyone hear me");
        Assert.Empty(ChatTo(transport, alice));
        Assert.Contains(MessagesTo(transport, bob2), m => m.StartsWith("@srv.chat.muted_until:", StringComparison.Ordinal));

        server.AdvanceUptimeForTest(5 * 60 + 1);
        Assert.False(server.IsChatMutedForTest("Bob"));

        transport.Sent.Clear();
        Say(server, bob2, "now?");
        Assert.Equal(new[] { "now?" }, ChatTo(transport, alice).Select(c => c.Text));
    }

    [Fact]
    public void AnAdmin_CanLiftAPauseWhileThePlayerIsAway()
    {
        var transport = new RecordingTransport();
        var server = NewServer("silence_offline_lift", transport);
        var (alice, _) = Pair(server);
        var admin = server.AddLocalPlayer("Chef");
        admin.State.Role = PlayerRole.Admin;

        Admin(server, admin, "silence", "Bob", 30);
        server.DisconnectLocalPlayerForTest("Bob");
        transport.Sent.Clear();

        Admin(server, admin, "unsilence", "bob"); // case-insensitive, like every name lookup
        Assert.Contains("@srv.admin.unsilenced:Bob", MessagesTo(transport, admin));
        Assert.False(server.IsChatMutedForTest("Bob"));

        // Lifting a pause nobody holds is still "no such player" — no phantom success.
        Admin(server, admin, "unsilence", "Nobody");
        Assert.Contains("@srv.admin.silence_no_target:Nobody", RejectionsTo(transport, admin));

        var bob2 = server.AddLocalPlayer("Bob");
        bob2.State.Inventory.Add("comm_radio", 1, 1);
        transport.Sent.Clear();
        Say(server, bob2, "free again");
        Assert.Equal(new[] { "free again" }, ChatTo(transport, alice).Select(c => c.Text));
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
