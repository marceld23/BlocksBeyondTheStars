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
using BlocksBeyondTheStars.Shared.Moderation;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The chat content filter wired into the server (#1207): profanity is masked and the sender told once, hate
/// terms drop the line with a notice, personal data is masked in Filtered and dropped in Safe, the operator's
/// <c>BBS_CHAT_FILTER</c> switch opens or hardens every world regardless of the world rule, a stricter launch
/// rule lifts an existing save, and every notice has EN+DE text.
/// </summary>
public sealed class ChatFilterTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ChatFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_chatfilter_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string name, RecordingTransport transport, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    /// <summary>Two players on the same world, the sender holding a comm radio; the sender's chat slot is
    /// re-armed before each line (the 700 ms rate limit is Environment.TickCount-based).</summary>
    private static (BlocksBeyondTheStars.GameServer.PlayerSession Alice, BlocksBeyondTheStars.GameServer.PlayerSession Bob) Pair(SvGameServer server)
    {
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.Inventory.Add("comm_radio", 1, 1);
        return (alice, bob);
    }

    private static void Say(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession who, string text)
    {
        who.LastChatTick = Environment.TickCount - 10_000;
        server.Chat(who.State.Name, text);
    }

    private static IEnumerable<ChatMessage> ChatTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ChatMessage>();

    private static int NoticesTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who, string key)
        => t.Sent.Count(s => s.Conn == who.ConnectionId && s.Msg is ServerMessage m && m.Text == key);

    [Fact]
    public void Filtered_MasksProfanity_AndTellsTheSenderOnce()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chat_mask", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "you are an asshole");
        Say(server, alice, "such a shit day");

        var heard = ChatTo(transport, bob).Select(c => c.Text).ToList();
        Assert.Equal(new[] { "you are an *******", "such a **** day" }, heard);
        Assert.Equal(1, NoticesTo(transport, alice, "@srv.chat.masked"));
        Assert.DoesNotContain(transport.Sent, s => s.Msg is ChatMessage c && c.Text.Contains("asshole"));
    }

    [Fact]
    public void Filtered_DropsHateTerm_AndTellsTheSender()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chat_block", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "h.i.t.l.e.r was right");

        Assert.Empty(ChatTo(transport, bob));
        Assert.Equal(1, NoticesTo(transport, alice, "@srv.chat.blocked"));
    }

    [Fact]
    public void OperatorOff_RelaysEverythingAsTyped()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chat_off", transport, c => c.ChatFilter = ChatFilterLevel.Off);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "you are an asshole");

        Assert.Equal(ChatMode.Open, server.EffectiveChatMode);
        Assert.Equal("you are an asshole", Assert.Single(ChatTo(transport, bob)).Text);
        Assert.Equal(0, NoticesTo(transport, alice, "@srv.chat.masked"));
    }

    [Fact]
    public void Filtered_MasksPersonalData_SafeDropsIt()
    {
        var transportA = new RecordingTransport();
        var filtered = NewServer("chat_pii_filtered", transportA);
        var (a1, b1) = Pair(filtered);
        transportA.Sent.Clear();
        Say(filtered, a1, "call me 0151 2345678 ok");
        var relayed = Assert.Single(ChatTo(transportA, b1)).Text;
        Assert.DoesNotContain("2345678", relayed);
        Assert.StartsWith("call me ", relayed);

        var transportB = new RecordingTransport();
        var safe = NewServer("chat_pii_safe", transportB, c => c.Rules.ChatMode = ChatMode.Safe);
        var (a2, b2) = Pair(safe);
        transportB.Sent.Clear();
        Say(safe, a2, "call me 0151 2345678 ok");
        Assert.Empty(ChatTo(transportB, b2));
        Assert.Equal(1, NoticesTo(transportB, a2, "@srv.chat.pii_blocked"));
    }

    [Fact]
    public void OperatorStrict_ForcesSafe_EvenWhenTheWorldRuleIsOpen()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chat_strict", transport, c =>
        {
            c.ChatFilter = ChatFilterLevel.Strict;
            c.Rules.ChatMode = ChatMode.Open;
        });
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "mail me at kid@example.com");

        Assert.Equal(ChatMode.Safe, server.EffectiveChatMode);
        Assert.Empty(ChatTo(transport, bob));
        Assert.Equal(1, NoticesTo(transport, alice, "@srv.chat.pii_blocked"));
    }

    [Fact]
    public void SlashCommands_AreNeverFiltered()
    {
        // /bump carries free text and is intercepted before the relay; the filter must not eat a debug snapshot.
        var transport = new RecordingTransport();
        var server = NewServer("chat_cmd", transport);
        var (alice, _) = Pair(server);
        transport.Sent.Clear();

        Say(server, alice, "/bump the shit hit the fan");

        Assert.Equal(0, NoticesTo(transport, alice, "@srv.chat.masked"));
        Assert.Equal(0, NoticesTo(transport, alice, "@srv.chat.blocked"));
    }

    [Fact]
    public void SafeLaunchRule_LiftsAnExistingFilteredSave()
    {
        var paths = new SaveGamePaths(_root, "chat_lift");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig { WorldName = "chat_lift", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            Assert.Equal(ChatMode.Filtered, server.EffectiveChatMode); // the default floor, baked into the save
        }

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var config2 = new ServerConfig { WorldName = "chat_lift", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
            config2.Rules.ChatMode = ChatMode.Safe; // e.g. the family preset or --chat-mode safe
            var server2 = new SvGameServer(config2, _content, st2, repo2);
            server2.Start();

            Assert.Equal(ChatMode.Safe, server2.EffectiveChatMode);
        }
    }

    [Fact]
    public void FamilyPresets_UseSafeChat()
    {
        Assert.Equal(ChatMode.Safe, ServerPresets.Get("family")!.ChatMode);
        Assert.Equal(ChatMode.Safe, ServerPresets.Get("peaceful-creative")!.ChatMode);
        Assert.Equal(ChatMode.Filtered, ServerPresets.Get("coop-survival")!.ChatMode);
        Assert.Equal(ChatMode.Filtered, new GameRules().ChatMode);
    }

    [Fact]
    public void Notices_HaveEnglishAndGermanText()
    {
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        foreach (var key in new[] { "srv.chat.blocked", "srv.chat.masked", "srv.chat.pii_blocked" })
        {
            Assert.True(en.Has(key), key + " (en)");
            Assert.True(de.Has(key), key + " (de)");
        }
    }

    public void Dispose()
    {
        foreach (var r in _repos)
        {
            try { r.Dispose(); } catch { }
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
