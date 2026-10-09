// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// The two-pilot findings of 2026-10-09: a parked ship stays visible while its pilot walks inside (#2431), an absurd ship
/// pose never reaches the other pilots (#2428), two ships launching into one orbit start beside each other (#2440), and a
/// crouching, downward-looking player shows exactly that in the presence the others get (#2435, #2434).
/// </summary>
public sealed class PilotVisibilityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public PilotVisibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pilots_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every per-connection server send so a test can assert who received what.</summary>
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
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static IEnumerable<T> To<T>(RecordingTransport t, PlayerSession s) => t.Sent.Where(x => x.Conn == s.ConnectionId).Select(x => x.Msg).OfType<T>();

    [Fact]
    public void ShipMove_DropsAPoseBeyondTheSanityBound_AndKeepsTheLastGoodOne()
    {
        var t = new RecordingTransport();
        var server = NewServer("pose", t);
        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");
        server.ShipMove("Alice", 10f, 0f, 0f);
        Assert.Contains(server.OtherSpacePlayers("Bob"), p => p.PlayerId == "Alice" && Math.Abs(p.X - 10f) < 0.01f);

        server.ShipMove("Alice", 5e8f, 0f, 0f);                        // the overflow range that crashed the lock readout
        server.ShipMove("Alice", 0f, -SvGameServer.ShipPoseSanityBound * 2f, 0f);
        Assert.Contains(server.OtherSpacePlayers("Bob"), p => p.PlayerId == "Alice" && Math.Abs(p.X - 10f) < 0.01f && Math.Abs(p.Y) < 0.01f);

        server.ShipMove("Alice", 2500f, 30f, -1200f);                   // a far but real flight pose still lands
        Assert.Contains(server.OtherSpacePlayers("Bob"), p => p.PlayerId == "Alice" && Math.Abs(p.X - 2500f) < 0.01f);
    }

    [Fact]
    public void AParkedShip_StaysVisibleToTheOtherPilot_WhileItsOwnerWalksInside()
    {
        var t = new RecordingTransport();
        var server = NewServer("parked", t);
        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");
        server.ShipMove("Alice", 0f, 0f, 0f, 90f);
        server.ShipMove("Bob", 20f, 2f, 5f, 45f);
        string? instance = server.SpaceInstanceIdForTest("Alice");
        Assert.NotNull(instance);

        server.EnterShipInterior("Bob");
        Assert.False(server.InSpace("Bob"));
        var seen = server.OtherSpacePlayers("Alice");
        var parked = Assert.Single(seen, p => p.PlayerId == "Bob");
        Assert.True(parked.Parked, "Bob's ship should float parked");
        Assert.Equal(20f, parked.X, 2);
        Assert.Equal(5f, parked.Z, 2);
        Assert.Equal(45f, parked.Yaw, 2);
        Assert.False(parked.Eva);
        Assert.DoesNotContain(server.OtherSpacePlayers("Bob"), p => p.PlayerId == "Bob"); // never his own ship

        // The instance outlives the last flying pilot while a parked ship floats in it.
        server.LeaveSpace("Alice");
        Assert.False(server.InSpace("Alice"));
        server.ExitShipToFlight("Bob");
        Assert.True(server.InSpace("Bob"), "Bob takes the helm again in the kept instance");
        Assert.Equal(instance, server.SpaceInstanceIdForTest("Bob"));

        server.EnterSpace("Alice");
        server.ShipMove("Bob", 20f, 2f, 5f, 45f);
        var flying = Assert.Single(server.OtherSpacePlayers("Alice"), p => p.PlayerId == "Bob");
        Assert.False(flying.Parked);
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 8f)]
    [InlineData(2, -8f)]
    [InlineData(3, 16f)]
    [InlineData(4, -16f)]
    public void LaunchOffset_AlternatesSides_EightUnitsApart(int ahead, float x) => Assert.Equal(x, SvGameServer.LaunchOffsetFor(ahead));

    [Fact]
    public void TheSecondShip_LaunchesBesideTheFirst()
    {
        var t = new RecordingTransport();
        var server = NewServer("launch", t);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        var first = To<SpaceState>(t, alice).Last();
        Assert.False(first.HasResumePose);
        Assert.False(first.SkipLaunch);

        server.EnterSpace("Bob");
        var second = To<SpaceState>(t, bob).Last();
        Assert.True(second.HasResumePose, "the second launch carries its column");
        Assert.False(second.SkipLaunch, "it is still a take-off, not a resumed flight");
        Assert.Equal(SvGameServer.LaunchOffsetFor(1), second.ResumeX);
        Assert.Equal(0f, second.ResumeY);
        Assert.Equal(0f, second.ResumeZ);
    }

    [Fact]
    public void Crouching_AndTheLookPitch_ReachTheOtherPlayersPresence()
    {
        var t = new RecordingTransport();
        var server = NewServer("crouch", t);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        var at = alice.State.Position;
        t.Sent.Clear();

        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new MoveIntent { X = at.X, Y = at.Y, Z = at.Z, Yaw = 10f, Pitch = -35f, Crouching = true }));
        server.Tick(0.3);

        Assert.True(alice.State.Crouching);
        var presence = To<PlayerPresence>(t, bob).Last(p => p.PlayerId == alice.State.PlayerId);
        Assert.True(presence.Crouching);
        Assert.Equal(-35f, presence.Pitch, 2);

        t.Sent.Clear();
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new MoveIntent { X = at.X, Y = at.Y, Z = at.Z, Yaw = 10f, Pitch = 20f, Crouching = false }));
        server.Tick(0.3);
        presence = To<PlayerPresence>(t, bob).Last(p => p.PlayerId == alice.State.PlayerId);
        Assert.False(presence.Crouching);
        Assert.Equal(20f, presence.Pitch, 2);
    }

    public void Dispose()
    {
        try
        {
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
