// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text.Json;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hosted-worlds foundations (fleet phase 0), against the REAL authoritative server:
///   1. idle shutdown — an empty server stops itself after IdleShutdownMinutes (and never when 0 / occupied),
///   2. the /status snapshot — live joined count + world identity, readable off the tick thread,
///   3. join tokens — with a JoinTokenSecret set, only a valid control-plane HMAC token gets in,
///   4. owner bootstrap — the token-verified owner account gets WorldAdmin even on a save whose
///      first-joiner WorldAdmin is someone else (the uploaded-singleplayer-save case).
/// </summary>
public sealed class HostedWorldsFoundationTests : IDisposable
{
    private const string Secret = "test-secret";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly System.Collections.Generic.List<SqliteWorldRepository> _repos = new();

    public HostedWorldsFoundationTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_hosted_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer Server, SqliteWorldRepository Repo) NewServer(string tag, LoopbackLink link, Action<ServerConfig>? mutate = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        mutate?.Invoke(config);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link), repo);
        server.Start();
        _repos.Add(repo);
        return (server, repo);
    }

    private static long FutureExpiry => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;

    /// <summary>Drives a real network join through the loopback transport and returns the outcome.</summary>
    private static (JoinAccepted? Accepted, JoinRejected? Rejected) TryJoin(SvGameServer server, LoopbackLink link, JoinRequest join)
    {
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
        client.Send(NetCodec.Encode(join), DeliveryMode.ReliableOrdered);
        for (int i = 0; i < 25 && accepted is null && rejected is null; i++)
        {
            server.Tick(0.1);
            client.Poll();
        }

        return (accepted, rejected);
    }

    // ---------------- HostedJoinToken (pure unit) ----------------

    [Fact]
    public void Token_RoundTrips_AndCarriesIdentity()
    {
        string token = HostedJoinToken.Create(Secret, "world_a", "acc-1", "Justus", FutureExpiry);

        Assert.True(HostedJoinToken.TryValidate(Secret, "world_a", token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            out var account, out var name, out var error), error);
        Assert.Equal("acc-1", account);
        Assert.Equal("Justus", name);
    }

    [Fact]
    public void Token_SurvivesSeparatorCharacters_InAccountAndName()
    {
        // base64url field encoding: '.', '|' and unicode in identities must not break the wire format.
        string token = HostedJoinToken.Create(Secret, "world_a", "acc.with|chars", "Jüstus.Pilot", FutureExpiry);

        Assert.True(HostedJoinToken.TryValidate(Secret, "world_a", token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            out var account, out var name, out _));
        Assert.Equal("acc.with|chars", account);
        Assert.Equal("Jüstus.Pilot", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1.only.three.parts")]
    public void Token_Malformed_IsRejected(string? token)
    {
        Assert.False(HostedJoinToken.TryValidate(Secret, "world_a", token, 0, out _, out _, out _));
    }

    [Fact]
    public void Token_ForAnotherWorld_IsRejected()
    {
        // The signature covers the world name: a token for world A must never open world B.
        string token = HostedJoinToken.Create(Secret, "world_a", "acc-1", "Justus", FutureExpiry);
        Assert.False(HostedJoinToken.TryValidate(Secret, "world_B", token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _, out _, out _));
    }

    [Fact]
    public void Token_Tampered_IsRejected()
    {
        // Swapping in a different (base64url) name invalidates the signature.
        string token = HostedJoinToken.Create(Secret, "world_a", "acc-1", "Justus", FutureExpiry);
        var parts = token.Split('.');
        parts[2] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Evil")).TrimEnd('=');
        Assert.False(HostedJoinToken.TryValidate(Secret, "world_a", string.Join(".", parts),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _, out _, out _));
    }

    [Fact]
    public void Token_Expired_IsRejected()
    {
        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        string token = HostedJoinToken.Create(Secret, "world_a", "acc-1", "Justus", past);
        Assert.False(HostedJoinToken.TryValidate(Secret, "world_a", token, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            out _, out _, out var error));
        Assert.Equal("token expired", error);
    }

    // ---------------- Idle shutdown ----------------

    [Fact]
    public void IdleShutdown_Fires_OnAnEmptyServer_AfterTheConfiguredMinutes()
    {
        var (server, _) = NewServer("idle_fire", new LoopbackLink(), c => c.IdleShutdownMinutes = 1);

        for (int i = 0; i < 59 && !server.IdleShutdownTriggered; i++)
        {
            server.Tick(1.0);
        }

        Assert.False(server.IdleShutdownTriggered); // 59s of a 60s budget: not yet

        server.Tick(1.0);
        server.Tick(1.0);
        Assert.True(server.IdleShutdownTriggered); // budget crossed on the next tick
    }

    [Fact]
    public void IdleShutdown_NeverFires_WhileAPlayerIsJoined_OrWhenDisabled()
    {
        // Disabled (the default 0): a long-empty server keeps running — self-hosting behaves as before.
        // The idle clock is a plain "+= deltaSeconds", so big ticks cover the same 120 s span cheaply.
        var (idleForever, _) = NewServer("idle_off", new LoopbackLink());
        for (int i = 0; i < 12; i++)
        {
            idleForever.Tick(10.0);
        }

        Assert.False(idleForever.IdleShutdownTriggered);

        // Enabled but occupied: a joined player pins the server up (and keeps resetting the countdown).
        var (occupied, _) = NewServer("idle_occupied", new LoopbackLink(), c => c.IdleShutdownMinutes = 1);
        occupied.AddLocalPlayer("Keeper");
        for (int i = 0; i < 12; i++)
        {
            occupied.Tick(10.0);
        }

        Assert.False(occupied.IdleShutdownTriggered);
    }

    // ---------------- /status snapshot ----------------

    [Fact]
    public void StatusJson_ReportsWorldIdentity_AndLiveJoinedCount()
    {
        var (server, _) = NewServer("status_w", new LoopbackLink(), c => { c.MaxPlayers = 12; c.IdleShutdownMinutes = 20; });
        server.AddLocalPlayer("P1");
        server.AddLocalPlayer("P2");
        server.Tick(1.1); // past the 1 s publish throttle

        using var doc = JsonDocument.Parse(server.StatusJson);
        var root = doc.RootElement;
        Assert.Equal("status_w", root.GetProperty("worldName").GetString());
        Assert.Equal(2, root.GetProperty("joinedPlayers").GetInt32());
        Assert.Equal(12, root.GetProperty("maxPlayers").GetInt32());
        Assert.Equal(Protocol.Version, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(20, root.GetProperty("idleShutdownMinutes").GetInt32());
        Assert.Equal(0, root.GetProperty("idleSeconds").GetInt64()); // players online → not idle
    }

    // ---------------- Join-token enforcement (real joins) ----------------

    [Fact]
    public void Join_WithoutToken_IsRejected_WhenSecretConfigured()
    {
        var link = new LoopbackLink();
        var (server, _) = NewServer("tok_missing", link, c => c.JoinTokenSecret = Secret);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest { PlayerName = "Pilot" });

        Assert.Null(accepted);
        Assert.NotNull(rejected);
        Assert.Contains("token", rejected!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Join_WithValidToken_IsAccepted()
    {
        var link = new LoopbackLink();
        var (server, _) = NewServer("tok_valid", link, c => c.JoinTokenSecret = Secret);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest
        {
            PlayerName = "Pilot",
            HostedToken = HostedJoinToken.Create(Secret, "tok_valid", "acc-1", "Pilot", FutureExpiry),
        });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
        Assert.Equal("Pilot", accepted!.PlayerId);
    }

    [Fact]
    public void Join_WithTokenForAnotherName_IsRejected()
    {
        var link = new LoopbackLink();
        var (server, _) = NewServer("tok_name", link, c => c.JoinTokenSecret = Secret);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest
        {
            PlayerName = "Impostor",
            HostedToken = HostedJoinToken.Create(Secret, "tok_name", "acc-1", "Pilot", FutureExpiry),
        });

        Assert.Null(accepted);
        Assert.NotNull(rejected);
    }

    [Fact]
    public void Join_WithoutSecretConfigured_IgnoresTokens_AsBefore()
    {
        // The default self-hosted/singleplayer path: no secret, token field absent — joins keep working.
        var link = new LoopbackLink();
        var (server, _) = NewServer("tok_off", link);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest { PlayerName = "Pilot" });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
    }

    // ---------------- Owner bootstrap ----------------

    [Fact]
    public void OwnerAccount_GetsWorldAdmin_EvenWhenFirstJoinerAlreadyHoldsIt()
    {
        // The uploaded-save scenario: "Founder" created the world (first joiner ⇒ WorldAdmin). The hosting
        // owner joins later, token-verified as the configured owner account — and must ALSO end up
        // WorldAdmin, or the uploader could never administer their own hosted world.
        var link = new LoopbackLink();
        var (server, repo) = NewServer("owner_boot", link, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "acc-owner";
        });

        server.AddLocalPlayer("Founder"); // first-ever player: becomes WorldAdmin (existing rule)
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Founder")!.Role);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest
        {
            PlayerName = "Owner",
            HostedToken = HostedJoinToken.Create(Secret, "owner_boot", "acc-owner", "Owner", FutureExpiry),
        });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Owner")!.Role);      // bootstrap fired + persisted
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Founder")!.Role);    // the founder keeps their role
    }

    [Fact]
    public void NonOwnerAccount_StaysRegularPlayer()
    {
        var link = new LoopbackLink();
        var (server, repo) = NewServer("owner_other", link, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "acc-owner";
        });

        server.AddLocalPlayer("Founder");

        var (accepted, _) = TryJoin(server, link, new JoinRequest
        {
            PlayerName = "Guest",
            HostedToken = HostedJoinToken.Create(Secret, "owner_other", "acc-guest", "Guest", FutureExpiry),
        });

        Assert.NotNull(accepted);
        Assert.Equal(PlayerRole.Player, repo.LoadPlayer("Guest")!.Role);
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
