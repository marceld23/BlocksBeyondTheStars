// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3200: a player is in one trade at a time. An invitation is checked again when it is answered, a trade that starts
/// withdraws every other invitation to or from its two partners, and a second invitation to somebody who is already asked
/// is refused instead of silently replacing the first.
/// </summary>
public sealed class TradeInvitationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TradeInvitationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tradeinvite_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A server with Alice, Bob and Carol standing together on foot.</summary>
    private SvGameServer ThreeTogether(RecordingTransport transport, out SqliteWorldRepository repo,
        out PlayerSession alice, out PlayerSession bob, out PlayerSession carol)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tradeinvite"));
        var config = new ServerConfig { WorldName = "tradeinvite", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        alice = server.AddLocalPlayer("Alice");
        bob = server.AddLocalPlayer("Bob");
        carol = server.AddLocalPlayer("Carol");
        float x = 0f;
        foreach (var p in new[] { alice, bob, carol })
        {
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(x, 64, 0); // all within the trade range of each other
            x += 1f;
        }

        return server;
    }

    [Fact]
    public void OneInvitesTwo_BothAccept_OnlyTheFirstTradeStarts()
    {
        var transport = new RecordingTransport();
        var server = ThreeTogether(transport, out var repo, out _, out _, out var carol);
        using (repo)
        {
            server.RequestTrade("Alice", "Bob");
            server.RequestTrade("Alice", "Carol");

            server.RespondTrade("Bob", true);
            server.RespondTrade("Carol", true); // the invitation was withdrawn the moment Alice started trading with Bob

            Assert.Equal("Bob", server.ActiveTrade("Alice")!.Other("Alice"));
            Assert.Null(server.ActiveTrade("Carol"));
            // Carol's invitation window closed with a reason instead of opening a second trade for Alice.
            Assert.Contains(transport.To<TradeClosed>(carol), c => !c.Completed && c.Reason == "@srv.trade.busy");
            Assert.DoesNotContain(transport.To<TradeUpdate>(carol), u => u.Partner == "Alice");
        }
    }

    [Fact]
    public void ASecondInvitation_ToSomeoneAlreadyAsked_IsRefused_AndTheFirstStands()
    {
        var transport = new RecordingTransport();
        var server = ThreeTogether(transport, out var repo, out _, out var bob, out var carol);
        using (repo)
        {
            server.RequestTrade("Alice", "Bob");
            server.RequestTrade("Carol", "Bob"); // used to replace Alice's invitation without a word

            Assert.Contains(transport.To<ActionRejected>(carol), r => r.Action == "trade" && r.Reason == "@srv.trade.busy");
            Assert.DoesNotContain(transport.To<TradeRequestNotice>(bob), n => n.Requester == "Carol");

            server.RespondTrade("Bob", true); // Bob answers the invitation on his screen: Alice's
            Assert.Equal("Alice", server.ActiveTrade("Bob")!.Other("Bob"));
            Assert.Null(server.ActiveTrade("Carol"));
        }
    }

    [Fact]
    public void ADecline_ReachesTheAsker_WithoutClosingTheInvitationTheyAreLookingAt()
    {
        var transport = new RecordingTransport();
        var server = ThreeTogether(transport, out var repo, out var alice, out _, out _);
        using (repo)
        {
            server.RequestTrade("Alice", "Bob");
            server.RequestTrade("Carol", "Alice"); // meanwhile Carol asks Alice
            transport.Sent.Clear();

            server.RespondTrade("Bob", false);

            Assert.Contains(transport.TextsTo(alice), t => t == "@srv.trade.declined");
            Assert.Empty(transport.To<TradeClosed>(alice)); // a TradeClosed would also have cleared Carol's invitation on her screen
            server.RespondTrade("Alice", true);
            Assert.Equal("Carol", server.ActiveTrade("Alice")!.Other("Alice"));
        }
    }

    [Fact]
    public void ATradeStarting_WithdrawsTheInvitationsToItsPartners_AndTellsTheAskers()
    {
        var transport = new RecordingTransport();
        var server = ThreeTogether(transport, out var repo, out _, out _, out var carol);
        using (repo)
        {
            server.RequestTrade("Alice", "Bob");
            server.RequestTrade("Carol", "Alice"); // Carol waits for Alice's answer
            transport.Sent.Clear();

            server.RespondTrade("Bob", true); // Alice now trades with Bob

            Assert.Contains(transport.TextsTo(carol), t => t == "@srv.trade.busy");
            server.RespondTrade("Alice", true); // the withdrawn invitation cannot start a second trade
            Assert.Equal("Bob", server.ActiveTrade("Alice")!.Other("Alice"));
            Assert.Null(server.ActiveTrade("Carol"));

            // Once that trade is over, Carol may ask again.
            server.CancelTrade("Alice");
            server.RequestTrade("Carol", "Alice");
            server.RespondTrade("Alice", true);
            Assert.Equal("Carol", server.ActiveTrade("Alice")!.Other("Alice"));
        }
    }
}
