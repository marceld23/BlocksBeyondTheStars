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
/// #2425 (open): "online we couldn't see each other's skins after we had been in the ship or had died" — the other player
/// showed the standard colours without the painted face. This pins what the server does today around those trips: the
/// suit colours ride every presence the other player gets, and the painted face reaches them on join. If the players'
/// trigger is ever found, the failing step goes here.
/// </summary>
public sealed class AppearanceAfterTripsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public AppearanceAfterTripsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_appearance_" + Guid.NewGuid().ToString("N"));
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
        config.Rules.FreeSpaceFlight = true;
        config.Rules.KeepInventoryOnDeath = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static IEnumerable<T> To<T>(RecordingTransport t, PlayerSession s) => t.Sent.Where(x => x.Conn == s.ConnectionId).Select(x => x.Msg).OfType<T>();

    private static PlayerPresence? LastPresenceOf(RecordingTransport t, PlayerSession viewer, PlayerSession subject)
        => To<PlayerPresence>(t, viewer).LastOrDefault(p => p.PlayerId == subject.State.PlayerId);

    /// <summary>Reports the player's own position with a fresh yaw, so the next presence beat has a change to send (an
    /// unchanged pose is only re-sent every few beats), and answers a pending death-screen choice first.</summary>
    private static void Move(SvGameServer server, PlayerSession s, float yaw)
    {
        if (s.RespawnChoiceDeadline > 0)
        {
            server.HandlePayloadForTest(s.ConnectionId, NetCodec.Encode(new RespawnChoiceIntent { UseCustomSpawn = false }));
        }

        var at = s.State.Position;
        server.HandlePayloadForTest(s.ConnectionId, NetCodec.Encode(new MoveIntent { X = at.X, Y = at.Y, Z = at.Z, Yaw = yaw }));
    }

    [Fact]
    public void Colours_AndTheFace_SurviveASpaceTrip_ADeath_AndAWalkInsideTheShip()
    {
        const int skin = 0x112233, torso = 0x445566, arms = 0x778899, legs = 0xAABBCC;
        string face = new string('a', 32 * 32);
        var t = new RecordingTransport();
        var server = NewServer("looks", t);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new SetAppearanceIntent { Skin = skin, Torso = torso, Arms = arms, Legs = legs }));
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new SetFaceIntent { Pixels = face }));
        Move(server, alice, 10f);
        server.Tick(0.6);

        var seen = LastPresenceOf(t, bob, alice);
        Assert.NotNull(seen);
        Assert.Equal(skin, seen!.Skin);
        Assert.Equal(legs, seen.Legs);
        Assert.Contains(To<PlayerFace>(t, bob), f => f.PlayerId == alice.State.PlayerId && f.Pixels == face);

        // Up into space and back down: the presence picks up again with the same colours.
        t.Sent.Clear();
        server.EnterSpace("Alice");
        server.ShipMove("Alice", 1f, 0f, 0f);
        server.Tick(0.6);
        server.LeaveSpace("Alice");
        Move(server, alice, 20f);
        server.Tick(0.6);
        seen = LastPresenceOf(t, bob, alice);
        Assert.NotNull(seen);
        Assert.Equal(skin, seen!.Skin);
        Assert.Equal(torso, seen.Torso);
        Assert.False(seen.Stealthed, "back on the ground the orbiter mark is gone");

        // A death on the same world: a heal-tank snap, the session keeps its look.
        t.Sent.Clear();
        server.KillPlayerForTest(alice, "test");
        server.Tick(0.1);
        Move(server, alice, 30f);
        server.Tick(0.6);
        seen = LastPresenceOf(t, bob, alice);
        Assert.NotNull(seen);
        Assert.Equal(skin, seen!.Skin);
        Assert.Equal(arms, seen.Arms);

        // A walk inside the floating ship and back.
        t.Sent.Clear();
        server.EnterSpace("Alice");
        Assert.True(server.InSpace("Alice"), "Alice should be in flight after the respawn");
        server.ShipMove("Alice", 1f, 0f, 0f);
        server.EnterShipInterior("Alice");
        Assert.True(server.InShipInterior("Alice"), "Alice should be walking inside her ship");
        server.ExitShipToFlight("Alice");
        Assert.True(server.InSpace("Alice"), "Alice should be back at the helm");
        server.LandOnBody("Alice", bob.CurrentLocationId); // the real way down: a landing relocates to a pad
        Assert.False(server.InSpace("Alice"));
        Assert.Equal(bob.CurrentLocationId, alice.CurrentLocationId);
        Move(server, alice, 40f);
        server.Tick(0.6);
        seen = LastPresenceOf(t, bob, alice);
        Assert.True(seen != null, $"no presence of Alice reached Bob — Alice at {alice.State.Position}, Bob at {bob.State.Position}, Alice aboard {alice.State.AboardShip}, stealthed {alice.State.Stealthed}");
        Assert.Equal(skin, seen!.Skin);
        Assert.Equal(legs, seen.Legs);
        Assert.Equal(face, alice.State.FacePixels); // the painted face is still the session's
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
