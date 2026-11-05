// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The 700 ms chat slot and its clock (#3187). The slot's "last line" started at 0 and was compared against the 32-bit
/// <c>Environment.TickCount</c>, which is negative from 24.9 to 49.7 days of host uptime: on such a host every chat line and
/// every <c>/report</c> counted as too fast, forever, without a word to the player. The clock is pinned through
/// <c>SetChatClockForTest</c>, so these tests do not depend on how long the machine running them has been up.
/// </summary>
public sealed class ChatThrottleClockTests : IDisposable
{
    /// <summary>What the old 32-bit clock read on a host that had been up for about 30 days.</summary>
    private const long OldClockAfterThirtyDays = -1_900_000_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_chatclock_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var server = new SvGameServer(
            new ServerConfig { WorldName = name, Seed = 7, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false },
            _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static (PlayerSession Alice, PlayerSession Bob) Pair(SvGameServer server)
    {
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        TestGear.Wear(alice.State, "comm_radio");
        return (alice, bob);
    }

    [Theory]
    [InlineData(OldClockAfterThirtyDays)]
    [InlineData(0L)]
    [InlineData(100L)] // a host up for a tenth of a second: 100 − 0 < 700 refused the first line as well
    public void TheFirstChatLine_AlwaysGoesThrough(long clock)
    {
        var transport = new RecordingTransport();
        var server = NewServer("chatclock_first_" + (clock < 0 ? "neg" : clock.ToString(System.Globalization.CultureInfo.InvariantCulture)), transport);
        var (alice, bob) = Pair(server);
        server.SetChatClockForTest(clock);
        transport.Sent.Clear();

        server.Chat(alice.State.Name, "hello");

        Assert.Single(transport.To<ChatMessage>(bob));
    }

    [Fact]
    public void TheFirstReport_GoesThrough_OnTheOldNegativeClock()
    {
        // /report is the in-game way for a child to ask for help — it must not fall silent on a long-running server.
        var transport = new RecordingTransport();
        var server = NewServer("chatclock_report", transport);
        var (alice, _) = Pair(server);
        server.SetChatClockForTest(OldClockAfterThirtyDays);
        transport.Sent.Clear();

        server.Chat(alice.State.Name, "/report");

        Assert.Contains("@srv.report.usage", transport.TextsTo(alice));
    }

    [Fact]
    public void TheThrottle_StillDropsALineWithin700ms()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chatclock_throttle", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        server.SetChatClockForTest(5_000);
        server.Chat(alice.State.Name, "one");
        server.SetChatClockForTest(5_699);
        server.Chat(alice.State.Name, "two");   // 699 ms later: a held key — dropped
        server.SetChatClockForTest(5_700);
        server.Chat(alice.State.Name, "three"); // 700 ms after the last accepted line: through

        Assert.Equal(new[] { "one", "three" }, transport.To<ChatMessage>(bob).Select(m => m.Text));
    }

    [Fact]
    public void ACommand_SpendsTheSameSlot_AsAChatLine()
    {
        var transport = new RecordingTransport();
        var server = NewServer("chatclock_shared", transport);
        var (alice, bob) = Pair(server);
        transport.Sent.Clear();

        server.SetChatClockForTest(10_000);
        server.Chat(alice.State.Name, "/report");
        server.SetChatClockForTest(10_200);
        server.Chat(alice.State.Name, "right after"); // the report took the slot 200 ms ago

        Assert.Single(transport.TextsTo(alice), t => t == "@srv.report.usage");
        Assert.Empty(transport.To<ChatMessage>(bob));
    }
}
