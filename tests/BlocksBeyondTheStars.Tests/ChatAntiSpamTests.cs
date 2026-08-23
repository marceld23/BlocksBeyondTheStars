// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Anti-spam + temporary auto-mute (#1208). The 700 ms per-line limit only stops a held key; these two
/// sliding windows stop a burst of distinct lines and someone who keeps tripping the content filter (#1207).
/// The clock is faked through <c>AdvanceUptimeForTest</c> so the ten-minute cool-down can be exercised
/// without waiting for it.
/// </summary>
public sealed class ChatAntiSpamTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ChatAntiSpamTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_antispam_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
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
        var server = new SvGameServer(
            new ServerConfig
            {
                WorldName = name,
                Seed = 7,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
            },
            _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static (BlocksBeyondTheStars.GameServer.PlayerSession Alice, BlocksBeyondTheStars.GameServer.PlayerSession Bob) Pair(SvGameServer server)
    {
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.Inventory.Add("comm_radio", 1, 1);
        return (alice, bob);
    }

    /// <summary>Says one line, re-arming the 700 ms slot first — that limit is TickCount-based and is not
    /// what these tests are about.</summary>
    private static void Say(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession who, string text)
    {
        who.LastChatTick = Environment.TickCount - 10_000;
        server.Chat(who.State.Name, text);
    }

    private static IEnumerable<ChatMessage> ChatTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ChatMessage>();

    /// <summary>Mute notices carry the remaining minutes as an ":arg" tail, so they are matched by prefix.</summary>
    private static int MuteNoticesTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Count(s => s.Conn == who.ConnectionId && s.Msg is ServerMessage m
                             && m.Text.StartsWith("@srv.chat.muted_until", StringComparison.Ordinal));

    // ---------------- the burst window ----------------

    [Fact]
    public void SevenLinesInTenSeconds_MutesTheSender_AndTheSeventhNeverLands()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_burst", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 7; i++)
        {
            Say(server, alice, "line " + i);
        }

        Assert.True(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(6, ChatTo(transport, bob).Count()); // the seventh is part of the flood, so it is dropped
        Assert.Equal(1, MuteNoticesTo(transport, alice));

        Say(server, alice, "let me back in");
        Assert.Equal(6, ChatTo(transport, bob).Count());
    }

    [Fact]
    public void TheSameSevenLinesSpreadOut_AreNotSpam()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_paced", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 7; i++)
        {
            server.AdvanceUptimeForTest(3.0); // 3 s apart — never more than 6 inside a 10 s window
            Say(server, alice, "line " + i);
        }

        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(7, ChatTo(transport, bob).Count());
        Assert.Equal(0, MuteNoticesTo(transport, alice));
    }

    // ---------------- the filter-hit window ----------------

    [Fact]
    public void FourFilterHitsInFiveMinutes_MutesTheSender()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_filter", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        // Well apart in time, so the BURST window can never be what fires — this is the filter-hit rule.
        for (int i = 0; i < 3; i++)
        {
            server.AdvanceUptimeForTest(30.0);
            Say(server, alice, "h.i.t.l.e.r was right");
        }

        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));

        server.AdvanceUptimeForTest(30.0);
        Say(server, alice, "h.i.t.l.e.r was right");

        Assert.True(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(1, MuteNoticesTo(transport, alice));
    }

    [Fact]
    public void FilterHitsOutsideTheWindow_DoNotAccumulate()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_filter_paced", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 6; i++)
        {
            server.AdvanceUptimeForTest(200.0); // > 5 min apart: each hit has aged out before the next
            Say(server, alice, "h.i.t.l.e.r was right");
        }

        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(0, MuteNoticesTo(transport, alice));
    }

    [Fact]
    public void OrdinaryLines_AreNotFilterHits()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_clean", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 12; i++)
        {
            server.AdvanceUptimeForTest(30.0);
            Say(server, alice, "found some iron over here");
        }

        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(12, ChatTo(transport, bob).Count());
    }

    // ---------------- the cool-down ends ----------------

    [Fact]
    public void TheMuteExpires_AndTheSenderCanTalkAgain()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_expiry", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 7; i++)
        {
            Say(server, alice, "line " + i);
        }

        Assert.True(server.IsChatMutedForTest(alice.State.PlayerId));

        server.AdvanceUptimeForTest(599.0);
        Assert.True(server.IsChatMutedForTest(alice.State.PlayerId));

        server.AdvanceUptimeForTest(2.0); // past the ten minutes
        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));

        Say(server, alice, "sorry about that");
        Assert.Contains(ChatTo(transport, bob), c => c.Text == "sorry about that");
    }

    [Fact]
    public void AMutedSenderIsToldOnce_NotOncePerAttempt()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_notice", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 7; i++)
        {
            Say(server, alice, "line " + i);
        }

        for (int i = 0; i < 5; i++)
        {
            Say(server, alice, "hello?");
        }

        Assert.Equal(1, MuteNoticesTo(transport, alice));
    }

    [Fact]
    public void ASecondMuteEarnsAFreshNotice()
    {
        var transport = new RecordingTransport();
        var server = NewServer("spam_renotice", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        for (int i = 0; i < 7; i++)
        {
            Say(server, alice, "line " + i);
        }

        server.AdvanceUptimeForTest(601.0); // the first mute runs out
        Assert.False(server.IsChatMutedForTest(alice.State.PlayerId));

        for (int i = 0; i < 7; i++)
        {
            Say(server, alice, "again " + i);
        }

        Assert.True(server.IsChatMutedForTest(alice.State.PlayerId));
        Assert.Equal(2, MuteNoticesTo(transport, alice));
    }

    // ---------------- the notice is readable ----------------

    [Fact]
    public void TheMuteNotice_HasEnAndDeTextWithTheMinutesSlot()
    {
        const string key = "srv.chat.muted_until";
        foreach (var locale in new[] { _content.CreateLocalizer(GameLocale.English), _content.CreateLocalizer(GameLocale.German) })
        {
            Assert.True(locale.Has(key), key);
            Assert.Contains("{name}", locale.Get(key)); // the generic ":arg" slot the server token resolver fills
        }
    }
}
