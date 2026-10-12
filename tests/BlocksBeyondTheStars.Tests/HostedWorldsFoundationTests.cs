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
///      first-joiner WorldAdmin is someone else (the uploaded-singleplayer-save case),
///   5. who becomes admin on a hosted world (#2486) — with an owner configured the first joiner is just a
///      Player (portal friend, arcade guest), and an instance without player admins (the arcade pool) takes a
///      saved Admin/WorldAdmin role back at join while configured admins and fleet admins keep their powers.
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

    /// <summary>A server transport for several network clients at once (the loopback models exactly one): a test
    /// queues joins under its own connection ids, and the server's replies are kept per connection.</summary>
    private sealed class MultiJoinTransport : IServerTransport
    {
        private readonly System.Collections.Generic.Queue<(int Conn, byte[] Payload)> _inbox = new();
        private readonly System.Collections.Generic.List<(int Conn, byte[] Payload)> _sent = new();

        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public void Join(int connectionId, JoinRequest join) => _inbox.Enqueue((connectionId, NetCodec.Encode(join)));

        /// <summary>The join reply the server sent to one connection (decoded on demand — the join burst is large).</summary>
        public (JoinAccepted? Accepted, JoinRejected? Rejected) ReplyTo(int connectionId)
        {
            foreach (var (conn, payload) in _sent)
            {
                if (conn != connectionId)
                {
                    continue;
                }

                switch (NetCodec.Decode(payload))
                {
                    case JoinAccepted a: return (a, null);
                    case JoinRejected r: return (null, r);
                }
            }

            return (null, null);
        }

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode) => _sent.Add((connectionId, payload));

        public void Broadcast(byte[] payload, DeliveryMode mode) { }

        public void Poll()
        {
            _ = ClientDisconnected;
            while (_inbox.Count > 0)
            {
                var (conn, payload) = _inbox.Dequeue();
                ClientConnected?.Invoke(conn);
                PayloadReceived?.Invoke(conn, payload);
            }
        }

        public void Stop() { }

        public void Dispose() { }
    }

    private (SvGameServer Server, SqliteWorldRepository Repo) NewServer(string tag, LoopbackLink link, Action<ServerConfig>? mutate = null)
        => NewServer(tag, new LoopbackServerTransport(link), mutate);

    private (SvGameServer Server, SqliteWorldRepository Repo) NewServer(string tag, IServerTransport transport, Action<ServerConfig>? mutate = null)
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
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return (server, repo);
    }

    /// <summary>Stops a server the way a sleeping world stops (drain + save) and closes its save, so the next
    /// <see cref="NewServer(string, LoopbackLink, Action{ServerConfig}?)"/> on the same tag reopens it — the
    /// "save made elsewhere, opened by a hosted instance" shape.</summary>
    private static void StopAndClose(SvGameServer server, SqliteWorldRepository repo)
    {
        server.Stop();
        repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>A token-checked join request, as the WorldHost mints it for one account and player name.</summary>
    private static JoinRequest HostedJoin(string world, string accountId, string name) => new()
    {
        ContentFingerprint = TestJoin.Fingerprint,
        PlayerName = name,
        Token = "install-" + name, // the client's per-install name-claim secret
        HostedToken = HostedJoinToken.Create(Secret, world, accountId, name, FutureExpiry),
    };

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

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" });

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
            ContentFingerprint = TestJoin.Fingerprint,
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
            ContentFingerprint = TestJoin.Fingerprint,
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
        var (server, repo) = NewServer("tok_off", link);

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
        // #2486 changes nothing here: without a control-plane owner the first joiner still creates — and owns — the world.
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Pilot")!.Role);
        Assert.Equal(PlayerRole.Player, server.AddLocalPlayer("Friend").State.Role);
    }

    // ---------------- Owner bootstrap ----------------

    [Fact]
    public void OwnerAccount_GetsWorldAdmin_EvenWhenFirstJoinerAlreadyHoldsIt()
    {
        // The uploaded-save scenario: "Founder" created the world in singleplayer — no control-plane owner there,
        // so the first joiner became WorldAdmin. The save is uploaded; the hosted instance opens it with an owner
        // configured, and the owner joins token-verified — and must ALSO end up WorldAdmin, or the uploader could
        // never administer their own hosted world.
        var (singleplayer, spRepo) = NewServer("owner_boot", new LoopbackLink());
        singleplayer.AddLocalPlayer("Founder"); // first-ever player of an owner-less world: WorldAdmin
        Assert.Equal(PlayerRole.WorldAdmin, spRepo.LoadPlayer("Founder")!.Role);
        StopAndClose(singleplayer, spRepo);

        var link = new LoopbackLink();
        var (server, repo) = NewServer("owner_boot", link, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "acc-owner";
        });

        var (accepted, rejected) = TryJoin(server, link, new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = "Owner",
            HostedToken = HostedJoinToken.Create(Secret, "owner_boot", "acc-owner", "Owner", FutureExpiry),
        });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Owner")!.Role);      // bootstrap fired + persisted
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Founder")!.Role);    // the uploaded save keeps its row
    }

    // ---------------- Who becomes admin on a hosted world (#2486) ----------------

    [Fact]
    public void PortalWorld_NonOwnerJoiningFirst_StaysPlayer_AndTheOwnerStillGetsWorldAdmin()
    {
        // A friend reaches the fresh world before its owner does. With an owner configured the "first joiner
        // creates the world" rule is off — the world was created on the portal, by the owner.
        var transport = new MultiJoinTransport();
        var (server, repo) = NewServer("portal_first", transport, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "acc-owner";
        });

        transport.Join(11, HostedJoin("portal_first", "acc-friend", "Friend"));
        server.Tick(0.1);
        Assert.NotNull(transport.ReplyTo(11).Accepted);
        Assert.Equal(PlayerRole.Player, repo.LoadPlayer("Friend")!.Role);

        transport.Join(12, HostedJoin("portal_first", "acc-owner", "Owner"));
        server.Tick(0.1);
        Assert.NotNull(transport.ReplyTo(12).Accepted);
        Assert.Equal(PlayerRole.WorldAdmin, repo.LoadPlayer("Owner")!.Role);
        Assert.Equal(PlayerRole.Player, repo.LoadPlayer("Friend")!.Role); // still only a guest of the owner
    }

    [Theory]
    [InlineData(false)] // the general rule alone (owner configured → no first-joiner admin) already covers it
    [InlineData(true)]  // …and the arcade flag the WorldHost sets on top
    public void ArcadeWorld_FirstGuest_StaysPlayer(bool noPlayerAdmins)
    {
        // An arcade pool world exactly as the WorldHost starts it: the owner id is the channel name "glitch",
        // every guest's account id is "glitch:<install id>" — the owner rule can never match a guest.
        var transport = new MultiJoinTransport();
        var (server, repo) = NewServer("arcade_first_" + noPlayerAdmins, transport, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "glitch";
            c.NoPlayerAdmins = noPlayerAdmins;
        });

        transport.Join(21, HostedJoin("arcade_first_" + noPlayerAdmins, "glitch:install-1", "Nova3f2"));
        server.Tick(0.1);

        Assert.NotNull(transport.ReplyTo(21).Accepted);
        Assert.Equal(PlayerRole.Player, repo.LoadPlayer("Nova3f2")!.Role);
    }

    [Theory]
    [InlineData(true, PlayerRole.Player)]      // the arcade instance takes the saved role back at join
    [InlineData(false, PlayerRole.WorldAdmin)] // without the flag a saved row is never touched (uploaded saves)
    public void SavedGuestAdmin_IsDemotedAtJoin_OnlyUnderNoPlayerAdmins(bool noPlayerAdmins, PlayerRole expected)
    {
        // A pool world from before the fix: its first guest became WorldAdmin, and "Staff" is a configured admin.
        string tag = "arcade_demote_" + noPlayerAdmins;
        var (before, beforeRepo) = NewServer(tag, new LoopbackLink(), c => c.AdminPlayers = new() { "Staff" });
        before.AddLocalPlayer("Nova3f2");
        before.AddLocalPlayer("Staff");
        Assert.Equal(PlayerRole.WorldAdmin, beforeRepo.LoadPlayer("Nova3f2")!.Role);
        Assert.Equal(PlayerRole.Admin, beforeRepo.LoadPlayer("Staff")!.Role);
        StopAndClose(before, beforeRepo);

        var transport = new MultiJoinTransport();
        var (server, repo) = NewServer(tag, transport, c =>
        {
            c.JoinTokenSecret = Secret;
            c.WorldOwnerAccountId = "glitch";
            c.NoPlayerAdmins = noPlayerAdmins;
            c.AdminPlayers = new() { "Staff" };
            c.FleetAdminPlayers = new() { "Operator" };
        });

        transport.Join(31, HostedJoin(tag, "glitch:install-1", "Nova3f2"));
        server.Tick(0.1);

        Assert.NotNull(transport.ReplyTo(31).Accepted);
        Assert.Equal(expected, repo.LoadPlayer("Nova3f2")!.Role); // written straight back, not on the next save cycle

        // A configured admin name keeps its role under the flag — the operator's own list is the source of admin there.
        Assert.Equal(PlayerRole.Admin, server.AddLocalPlayer("Staff").State.Role);

        // Fleet admins are a session flag, not a role: their moderation powers do not depend on the flag.
        var op = server.AddLocalPlayer("Operator");
        Assert.True(op.IsFleetAdmin);
        Assert.Equal(PlayerRole.Player, op.State.Role);
        server.HandleForTest(op, new AdminCommandIntent { Command = "spectate", StringArg = "on" });
        Assert.True(op.Spectating);
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
            ContentFingerprint = TestJoin.Fingerprint,
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
