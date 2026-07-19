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
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.WorldHost;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Protocol/join/transport hardening (#424, audit findings S8–S14): the join path is flood-gated and
/// re-join-proof, untrusted MessagePack is decoded with security limits, the docker CLI wrapper cannot
/// hang forever, secret compares are fixed-time, and the shared world generator's per-world mode state
/// is always configured completely.
/// </summary>
public sealed class ProtocolHardeningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ProtocolHardeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hardening_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>A transport the test drives directly: it raises connect/payload/disconnect into the server
    /// and records every message the server sends back, per connection.</summary>
    private sealed class DrivenTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Connect(int id) => ClientConnected?.Invoke(id);
        public void Disconnect(int id) => ClientDisconnected?.Invoke(id);
        public void Receive(int id, object message) => PayloadReceived?.Invoke(id, NetCodec.Encode(message));

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, DrivenTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private readonly List<SqliteWorldRepository> _repos = new();

    // ---------------- S8 — join re-join guard + flood gate ----------------

    [Fact]
    public void Join_OnAlreadyJoinedConnection_IsDroppedWithoutReply()
    {
        var transport = new DrivenTransport();
        var server = NewServer("s8_rejoin", transport);

        transport.Connect(1);
        transport.Receive(1, new JoinRequest { ProtocolVersion = Protocol.Version, PlayerName = "Pilot" });
        Assert.Single(transport.Sent.Where(x => x.Msg is JoinAccepted));

        // The re-join must neither re-run the join burst (amplifier) nor answer at all (an answer feeds it).
        transport.Sent.Clear();
        transport.Receive(1, new JoinRequest { ProtocolVersion = Protocol.Version, PlayerName = "Pilot" });
        Assert.Empty(transport.Sent);

        server.Stop();
    }

    [Fact]
    public void Join_Flood_IsRateLimitedPerConnection()
    {
        var transport = new DrivenTransport();
        var server = NewServer("s8_flood", transport);

        // 20 protocol-mismatch joins in one burst: only the join budget's worth may even be processed
        // (each would otherwise do rejection work; a valid flood would do DB + world work).
        transport.Connect(2);
        for (int i = 0; i < 20; i++)
        {
            transport.Receive(2, new JoinRequest { ProtocolVersion = Protocol.Version + 999, PlayerName = "Flood" });
        }

        int rejected = transport.Sent.Count(x => x.Conn == 2 && x.Msg is JoinRejected);
        Assert.True(rejected is > 0 and <= 5, $"expected 1..5 processed join attempts, got {rejected}");

        // A reconnect starts with a fresh budget — a legitimate client retrying after a rejection
        // (new connection) is never locked out.
        transport.Disconnect(2);
        transport.Connect(2);
        transport.Sent.Clear();
        transport.Receive(2, new JoinRequest { ProtocolVersion = Protocol.Version + 999, PlayerName = "Flood" });
        Assert.Single(transport.Sent.Where(x => x.Conn == 2 && x.Msg is JoinRejected));

        server.Stop();
    }

    // ---------------- S10 — untrusted MessagePack decoding ----------------

    [Fact]
    public void Decode_DeeplyNestedMaliciousPayload_IsHandledBenignly()
    {
        // A JoinRequest body that is a map with one unknown key whose value is a million nested arrays
        // (one byte per level — maximal nesting inside the packet cap): the contractless formatter must
        // Skip it. Whatever the library does with it (skip, or reject via the UntrustedData limits this
        // codebase now sets — #424 S10), the contract here is: no exception escapes Decode, no crash, no
        // hang; the result is either dropped or a plain default-shaped message.
        const int Depth = 1_000_000;
        var body = new List<byte> { 0x81, 0xA1, (byte)'x' }; // fixmap(1) { "x": ... }
        for (int i = 0; i < Depth; i++)
        {
            body.Add(0x91); // fixarray(1) — one more nesting level
        }

        body.Add(0xC0); // nil terminator at the bottom

        var payload = new byte[body.Count + 1];
        payload[0] = 1; // JoinRequest tag
        body.CopyTo(payload, 1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        object? decoded = NetCodec.Decode(payload);
        sw.Stop();

        Assert.True(decoded is null or JoinRequest, $"unexpected decode result: {decoded?.GetType().Name}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"decoding a hostile payload must stay cheap (took {sw.Elapsed})");
    }

    [Fact]
    public void Decode_NormalJoinRequest_StillRoundTrips()
    {
        // Regression guard: the security limits must not reject legitimate traffic.
        var encoded = NetCodec.Encode(new JoinRequest { ProtocolVersion = Protocol.Version, PlayerName = "Pilot" });
        var decoded = Assert.IsType<JoinRequest>(NetCodec.Decode(encoded));
        Assert.Equal("Pilot", decoded.PlayerName);
    }

    // ---------------- S11 — docker CLI wrapper timeout ----------------

    [Fact]
    public void RunProcess_KillsAHungCliOnTimeout()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (exitCode, _, _) = OperatingSystem.IsWindows()
            ? DockerCliLauncher.RunProcess("cmd", new List<string> { "/c", "ping -n 60 127.0.0.1 > nul" }, 1_500)
            : DockerCliLauncher.RunProcess("/bin/sh", new List<string> { "-c", "sleep 60" }, 1_500);
        sw.Stop();

        Assert.Equal(-1, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"a hung CLI must be killed at the timeout, not waited out (took {sw.Elapsed})");
    }

    [Fact]
    public void RunProcess_CapturesOutputOfAWellBehavedCli()
    {
        var (exitCode, stdout, _) = OperatingSystem.IsWindows()
            ? DockerCliLauncher.RunProcess("cmd", new List<string> { "/c", "echo hello" }, 30_000)
            : DockerCliLauncher.RunProcess("/bin/sh", new List<string> { "-c", "echo hello" }, 30_000);

        Assert.Equal(0, exitCode);
        Assert.Equal("hello", stdout.Trim());
    }

    // ---------------- S14 — fixed-time secret compares ----------------

    [Fact]
    public void TokenEquals_MatchesOnlyTheExactSecret()
    {
        Assert.True(BasicAuth.TokenEquals("s3cret", "s3cret"));
        Assert.False(BasicAuth.TokenEquals("s3crex", "s3cret"));
        Assert.False(BasicAuth.TokenEquals("s3cret-longer", "s3cret"));
        Assert.False(BasicAuth.TokenEquals(null, "s3cret"));
        Assert.False(BasicAuth.TokenEquals(string.Empty, "s3cret"));

        // An unconfigured secret must never match anything — the gate stays closed, not open.
        Assert.False(BasicAuth.TokenEquals(string.Empty, string.Empty));
        Assert.False(BasicAuth.TokenEquals("anything", string.Empty));
    }

    [Fact]
    public void SecretCompare_FixedTimeEquals_Basics()
    {
        Assert.True(SecretCompare.FixedTimeEquals("pass", "pass"));
        Assert.False(SecretCompare.FixedTimeEquals("pass", "pasS"));
        Assert.False(SecretCompare.FixedTimeEquals("pass", "password"));
        Assert.True(SecretCompare.FixedTimeEquals(null, string.Empty)); // callers gate empty-config themselves
    }

    // ---------------- S13 — shared generator mode state ----------------

    [Fact]
    public void SharedGenerator_InterleavedWorlds_StayDeterministic()
    {
        var planet = _content.GetPlanet("rocky");
        Assert.NotNull(planet);
        var coord = new ChunkCoord(0, 3, 0);

        // One shared generator, alternating between a small cratered moon and a full-size world — like
        // two resident worlds generating on demand. The moon's chunk must be identical to what a fresh,
        // single-world generator produces (no mode bleed-through from the other world).
        var shared = new WorldGenerator(worldSeed: 42, _content);
        shared.SetWorldMode(1008, cratered: true, landingPads: null);
        _ = shared.Generate(planet!, coord);
        shared.SetWorldMode(WorldConstants.Circumference, cratered: false, landingPads: null);
        _ = shared.Generate(planet!, coord);
        shared.SetWorldMode(1008, cratered: true, landingPads: null);
        var interleaved = shared.Generate(planet!, coord);

        var fresh = new WorldGenerator(worldSeed: 42, _content);
        fresh.SetWorldMode(1008, cratered: true, landingPads: null);
        var baseline = fresh.Generate(planet!, coord);

        int cs = WorldConstants.ChunkSize;
        for (int x = 0; x < cs; x++)
            for (int y = 0; y < cs; y++)
                for (int z = 0; z < cs; z++)
                {
                    Assert.Equal(baseline.Get(x, y, z), interleaved.Get(x, y, z));
                }
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
