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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// 12-player smoke test for the raised multiplayer cap (default <see cref="ServerConfig.MaxPlayers"/> = 12).
/// Locks in the two parts of that change against the REAL authoritative server:
///   1. landing-pad supply scales to the cap, so 12 players each get their OWN pad (no pad-0 clustering) under
///      the default-on <see cref="GameRules.PersonalLandingZones"/> rule, and
///   2. the join cap rejects the 13th player once 12 are online.
/// </summary>
public sealed class MaxPlayersTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MaxPlayersTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_maxp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string tag, LoopbackLink link, int maxPlayers = 12)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            StartPlanet = "rocky",       // a full-size planet: pad supply is floored to MaxPlayers
            AutoSaveIntervalMinutes = 9999,
            MaxPlayers = maxPlayers,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link), repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private readonly List<SqliteWorldRepository> _repos = new();

    [Fact]
    public void DefaultMaxPlayers_IsTwelve()
    {
        // Guards the default bump (4 -> 12). A new self-hosted world allows 12 without any config.
        Assert.Equal(12, new ServerConfig().MaxPlayers);
    }

    [Fact]
    public void TwelvePlayers_EachClaimTheirOwnLandingPad_OnAFullPlanet()
    {
        var server = NewServer("pads12", new LoopbackLink());
        var players = new List<PlayerSession>();
        for (int i = 0; i < 12; i++)
        {
            players.Add(server.AddLocalPlayer("P" + i));
        }

        // The pad floor (PadCountFor) scales a full-size planet's supply up to the player cap, so a full
        // 12-player server never runs out of pads.
        Assert.True(server.LandingPadCount >= 12,
            $"a full planet must offer at least one pad per player (got {server.LandingPadCount} for 12 players).");

        // Each player lands and claims the first free pad, exactly like a real touchdown (holding it via
        // AssignedPadIndex, which is how live occupancy is tracked). No two may land on the same pad.
        var claimed = new List<int>();
        foreach (var p in players)
        {
            var (chosen, reason) = server.TryClaimPadForTest(p, -1);
            Assert.True(chosen >= 0, $"{p.State.Name} found no free pad: '{reason}'.");
            p.AssignedPadIndex = chosen; // hold the pad, as a landed player does
            claimed.Add(chosen);
        }

        Assert.Equal(12, claimed.Distinct().Count()); // every player got their OWN pad — no clustering on pad 0
    }

    [Fact]
    public void ThirteenthPlayer_IsRejectedAsFull_WhenTwelveAreOnline()
    {
        var link = new LoopbackLink();
        var server = NewServer("full13", link);

        // Bring twelve players online (each session is Joined == true, which is what the cap counts).
        for (int i = 0; i < 12; i++)
        {
            server.AddLocalPlayer("P" + i);
        }

        // A thirteenth player attempts a REAL join through the transport — it must be refused as full.
        JoinAccepted? accepted = null;
        JoinRejected? rejected = null;
        var client = new LoopbackClientTransport(link);
        client.PayloadReceived += payload =>
        {
            switch (NetCodec.Decode(payload))
            {
                case JoinAccepted a: accepted = a; break;
                case JoinRejected r: rejected = r; break;
            }
        };

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "P13" }), DeliveryMode.ReliableOrdered);
        for (int i = 0; i < 25 && accepted is null && rejected is null; i++)
        {
            server.Tick(0.1);
            client.Poll();
        }

        Assert.Null(accepted);
        Assert.NotNull(rejected);
        Assert.Contains("full", rejected!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
