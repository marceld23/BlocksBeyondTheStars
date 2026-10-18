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
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The two-pilot findings of 2026-10-09: a parked ship stays visible while its pilot walks inside (#2431), an absurd ship
/// pose never reaches the other pilots (#2428), two ships launching into one orbit start beside each other (#2440), and a
/// crouching, downward-looking player shows exactly that in the presence the others get (#2435, #2434). Also #2737: the
/// pilots of the system's OTHER flights reach a pilot as <see cref="SpaceState.SystemPilots"/>, converted into its frame.
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

    private SvGameServer NewServer(string name, RecordingTransport transport, bool placeShip = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = placeShip,
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

    [Fact]
    public void ASpacewalkingPartner_IsSentWithTheSuitPose_AndTheFloatingShipPose()
    {
        // #2709: a partner on a spacewalk used to reach the others as a suit pose only — their ship vanished while they
        // were outside, and the suit was a bare cube. The space state now carries the floating ship and the suit look.
        var t = new RecordingTransport();
        var server = NewServer("evapartner", t);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");
        server.ShipMove("Alice", -120f, 2f, 4f, 30f); // the ship, west of the origin like in the report

        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new SetEvaIntent { Active = true }));
        Assert.True(alice.State.InEva);
        server.ShipMove("Alice", -124f, 3f, 6f, 90f); // the suit floats beside the ship

        t.Sent.Clear();
        server.Tick(0.3); // the pilot beat re-broadcasts the poses to everyone in the instance
        var state = To<SpaceState>(t, bob).Last();
        var partner = Assert.Single(state.Players, p => p.PlayerId == "Alice");
        Assert.True(partner.Eva);
        Assert.Equal(-124f, partner.X, 2);
        Assert.Equal(6f, partner.Z, 2);
        Assert.Equal(90f, partner.Yaw, 2);
        Assert.True(partner.HasShipPose, "the floating ship must reach the partner while its pilot is outside");
        Assert.Equal(-120f, partner.ShipX, 2);
        Assert.Equal(2f, partner.ShipY, 2);
        Assert.Equal(4f, partner.ShipZ, 2);
        Assert.Equal(30f, partner.ShipYaw, 2);
        Assert.Equal(alice.SkinColor, partner.Skin);
        Assert.Equal(alice.TorsoColor, partner.Torso);
        Assert.Equal(alice.ArmColor, partner.Arms);
        Assert.Equal(alice.LegColor, partner.Legs);
        Assert.Equal("Alice", partner.Name);

        // Back aboard, the pose is the ship again — no second ship pose.
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new SetEvaIntent { Active = false }));
        server.ShipMove("Alice", -120f, 2f, 4f, 30f);
        var aboard = Assert.Single(server.OtherSpacePlayers("Bob"), p => p.PlayerId == "Alice");
        Assert.False(aboard.Eva);
        Assert.False(aboard.HasShipPose);
        Assert.Equal(-120f, aboard.X, 2);
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

    /// <summary>#2737: puts the player on <paramref name="body"/> by a real (quick) travel — with a jump generator fitted,
    /// since the fresh start world may still run under its legacy planet-type key — and launches from it.</summary>
    private static void LaunchFrom(SvGameServer server, PlayerSession session, CelestialBody body)
    {
        if (session.CurrentLocationId != body.Id)
        {
            server.RequestLandingPadsForTest(session, session.CurrentLocationId); // serves the player → server.Ship is theirs
            server.Ship.Modules.Add("jump_generator");
            Assert.True(server.QuickTravelForTest(session.State.PlayerId, body.Id), $"{session.State.PlayerId} must reach {body.Id}");
        }

        server.EnterSpace(session.State.PlayerId);
        Assert.True(server.InSpace(session.State.PlayerId));
    }

    [Fact]
    public void PilotsOfTheSystemsOtherFlights_AreSystemPilots_InTheReceiversFrame()
    {
        var t = new RecordingTransport();
        var server = NewServer("system_pilots", t, placeShip: true);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        var carol = server.AddLocalPlayer("Carol");
        server.SetInstantTravelForTest(true);

        var landable = server.Galaxy.AllBodies()
            .Where(b => b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType)
                && _content.GetPlanet(b.PlanetType!) is not null && b.SystemId != SvGameServer.GuardianFinaleSystemId)
            .ToList();
        var system = landable.GroupBy(b => b.SystemId).First(g => g.Count() >= 2).ToArray();
        var (aliceBody, bobBody) = (system[0], system[1]);
        var carolBody = landable.First(b => b.SystemId != aliceBody.SystemId);

        LaunchFrom(server, alice, aliceBody);
        LaunchFrom(server, bob, bobBody);
        LaunchFrom(server, carol, carolBody);
        Assert.NotEqual(server.SpaceInstanceIdForTest("Alice"), server.SpaceInstanceIdForTest("Bob"));

        server.ShipMove("Alice", 10f, 0f, 0f);
        server.ShipMove("Bob", 5f, 3f, -7f, 30f);
        server.ShipMove("Carol", 1f, 1f, 1f);

        // Another flight: not a ship in Alice's scene …
        Assert.DoesNotContain(server.OtherSpacePlayers("Alice"), p => p.PlayerId == "Bob");
        // … but a navigation target at the converted position, labelled with the body Bob flies near.
        var seen = Assert.Single(server.SystemSpacePilots("Alice"));
        Assert.Equal("Bob", seen.PlayerId);
        Assert.Equal(bobBody.Id, seen.AnchorBodyId);
        Assert.Equal(5f + (bobBody.SystemX - aliceBody.SystemX) * SystemBodyLayout.FlightViewScale, seen.X, 2);
        Assert.Equal(3f, seen.Y, 2);
        Assert.Equal(-7f + (bobBody.SystemZ - aliceBody.SystemZ) * SystemBodyLayout.FlightViewScale, seen.Z, 2);
        Assert.Equal(aliceBody.Id, Assert.Single(server.SystemSpacePilots("Bob")).AnchorBodyId);
        Assert.Empty(server.SystemSpacePilots("Carol")); // another star system: nobody

        // Alice flies alone, yet hears about Bob about once a second …
        t.Sent.Clear();
        for (int i = 0; i < 6; i++)
        {
            server.Tick(0.25);
        }

        var state = To<SpaceState>(t, alice).LastOrDefault();
        Assert.NotNull(state);
        Assert.Contains(state!.SystemPilots, p => p.PlayerId == "Bob" && p.AnchorBodyId == bobBody.Id);
        Assert.DoesNotContain(state.SystemPilots, p => p.PlayerId == "Carol");
        Assert.DoesNotContain(state.Players, p => p.PlayerId == "Bob"); // never a ghost ship in her scene

        // … and once more after Bob has landed, so her lock on him is released.
        server.LeaveSpace("Bob");
        t.Sent.Clear();
        for (int i = 0; i < 6; i++)
        {
            server.Tick(0.25);
        }

        state = To<SpaceState>(t, alice).LastOrDefault();
        Assert.NotNull(state);
        Assert.Empty(state!.SystemPilots);
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
