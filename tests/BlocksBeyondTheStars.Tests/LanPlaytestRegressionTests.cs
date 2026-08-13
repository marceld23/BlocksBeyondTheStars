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
/// Regressions from the 2026-08-12 two-player LAN playtest (#954–#958): the silently dropped empty-body-id
/// pad request that froze the E-landing chooser, the landing-pad bookkeeping that let a joiner be assigned
/// the pad a pilot in space still holds, the same-body landing path that skipped the ship-placement/door
/// resync, and SwitchShip replacing OTHER players' own hulls.
/// </summary>
public sealed class LanPlaytestRegressionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LanPlaytestRegressionTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_lanreg_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Transport recording every server send so a test can assert who received what message.</summary>
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

    private SvGameServer NewServer(string name, RecordingTransport transport, bool placeShip = true)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
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

    private readonly List<SqliteWorldRepository> _repos = new();

    // ---------------- #956: the E-landing chooser hangs on "Reading landing pads…" ----------------

    [Fact]
    public void RequestLandingPads_WithEmptyBodyId_RepliesWithTheCurrentBodysPads()
    {
        // The home body is registered client-side with an EMPTY id ("" = current body). The handler used to
        // FindBody("") → null → silent return: no reply, no timeout — the pad chooser froze forever.
        var transport = new RecordingTransport();
        var server = NewServer("pads_empty", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");

        transport.Sent.Clear();
        server.RequestLandingPadsForTest(pilot, string.Empty);

        var reply = transport.Sent
            .Where(x => x.Conn == pilot.ConnectionId)
            .Select(x => x.Msg).OfType<LandingPadList>().FirstOrDefault();
        Assert.NotNull(reply);
        Assert.Equal(string.Empty, reply!.BodyId); // must ECHO the requested id — the client gates on it
        Assert.True(reply.Pads.Length > 0, "the home body's real pads must be listed");
    }

    [Fact]
    public void RequestLandingPads_ForAnUnknownBody_StillReplies()
    {
        // Every request gets an answer (empty pad list for an unknown body) — a silent server drop must
        // never be able to freeze the client's chooser state again.
        var transport = new RecordingTransport();
        var server = NewServer("pads_unknown", transport);
        var pilot = server.AddLocalPlayer("Pilot");

        transport.Sent.Clear();
        server.RequestLandingPadsForTest(pilot, "no-such-body");

        var reply = transport.Sent
            .Where(x => x.Conn == pilot.ConnectionId)
            .Select(x => x.Msg).OfType<LandingPadList>().FirstOrDefault();
        Assert.NotNull(reply);
        Assert.Equal("no-such-body", reply!.BodyId);
        Assert.Empty(reply.Pads);
    }

    // ---------------- #957: pad bookkeeping + the same-body landing resync ----------------

    [Fact]
    public void PadHeldByAPilotInSpace_IsNotHandedToAJoiner()
    {
        // Ann parks on her pad and launches to space. Her pad used to count as free the moment she was in
        // space (without releasing her AssignedPadIndex) — and a fresh joiner's default pad index of 0 was
        // never re-validated — so Ben's ship was stamped onto Ann's origin: two ships on one pad.
        var transport = new RecordingTransport();
        var server = NewServer("pad_reserved", transport);

        var ann = server.AddLocalPlayer("Ann");
        int annPad = ann.AssignedPadIndex;
        Assert.True(annPad >= 0, "joining with a ship parks it on a pad");
        server.EnterSpace("Ann");

        var ben = server.AddLocalPlayer("Ben");
        Assert.NotEqual(annPad, ben.AssignedPadIndex); // Ann's pad stays reserved while she is in space
        Assert.NotEqual(server.ShipAnchorOf("Ann"), server.ShipAnchorOf("Ben"));
    }

    [Fact]
    public void LandingBackOnTheSameBody_ResyncsShipMarkersDoorsAndChunks()
    {
        // The same-body landing path (RelocateToAssignedPad) used to skip the ship-placement/stations/door
        // messages the cross-body travel path sends — the HUD compass and world-map ship markers kept
        // pointing at the previous pad — and never re-streamed chunks, so everything that changed on the
        // body while the player was away stayed stale on their client (the playtest's "ghost blocks").
        var transport = new RecordingTransport();
        var server = NewServer("land_resync", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        pilot.SentChunks.Add(new BlocksBeyondTheStars.Shared.World.ChunkCoord(0, 4, 0)); // simulate a streamed chunk

        transport.Sent.Clear();
        server.LandOnCurrentBodyForTest(pilot);

        object[] toPilot = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).ToArray();
        Assert.Contains(toPilot, m => m is ShipPlacement);
        Assert.Contains(toPilot, m => m is DoorList);
        Assert.Contains(toPilot, m => m is ShipCombatStatus);
        Assert.Empty(pilot.SentChunks); // cleared → the world re-streams fresh (stale-view self-heal)
    }

    // ---------------- #971: the same-body landing must also MOVE the player ----------------

    /// <summary>Lands the pilot back on a pad that is NOT the one they launched from and returns both the
    /// pad they left and the messages the server sent them, so a test can assert where they ended up.</summary>
    private static (int LaunchPad, int ChosenPad, object[] ToPilot) LandOnAnotherPad(
        SvGameServer server, RecordingTransport transport, PlayerSession pilot)
    {
        int launchPad = pilot.AssignedPadIndex;
        int chosenPad = launchPad == 0 ? 1 : 0; // the chooser's "some other pad on this planet"
        server.EnterSpace(pilot.State.PlayerId);

        transport.Sent.Clear();
        server.LandOnCurrentBodyForTest(pilot, chosenPad);

        return (launchPad, chosenPad,
            transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).ToArray());
    }

    [Fact]
    public void LandingBackOnTheSameBody_SnapsThePlayerOntoTheClaimedPad()
    {
        // Landing back on the same planet parked the ship on the chosen pad but left the PLAYER standing at
        // the pad they launched from, thousands of blocks away: "I landed and my ship isn't there" (#971).
        // The position must ride the RespawnNotice snap channel — the client discards a position that
        // arrives on PlayerStateUpdate (#414 N17), and there is no WorldReset here to re-arm its spawn snap.
        var transport = new RecordingTransport();
        var server = NewServer("land_snap", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        Assert.True(server.LandingPadCount >= 2, "the same-body landing bug needs a second pad to choose");

        var (launchPad, chosenPad, toPilot) = LandOnAnotherPad(server, transport, pilot);
        Assert.NotEqual(launchPad, chosenPad);

        var snap = toPilot.OfType<RespawnNotice>().LastOrDefault();
        Assert.NotNull(snap);
        Assert.False(snap!.Died); // a touchdown, not a death — no red flash on the client

        // The snap and the authoritative position agree, and both sit on the ship that just parked.
        var anchor = server.ShipAnchorOf("Pilot");
        Assert.Equal(pilot.State.Position.X, snap.X);
        Assert.Equal(pilot.State.Position.Y, snap.Y);
        Assert.Equal(pilot.State.Position.Z, snap.Z);
        Assert.InRange(snap.X, anchor.X - 32, anchor.X + 32);
        Assert.InRange(snap.Z, anchor.Z - 32, anchor.Z + 32);
        Assert.True(pilot.State.AboardShip);
    }

    [Fact]
    public void AfterLandingBackOnTheSameBody_TheStalePreLaunchPoseIsDropped()
    {
        // The client keeps streaming its pre-launch pose for a beat after the landing. Without the #865
        // spawn-adoption gate the server TRUSTED it and dragged the player straight back to the old pad —
        // which is the position the checkpoint save then persisted (the savegame that reported #971).
        var transport = new RecordingTransport();
        var server = NewServer("land_stale", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        var preLaunch = pilot.State.Position; // standing in the ship on the pad they are about to leave

        // The pilot has been playing for a while: their client adopted the join spawn long ago, so the gate
        // is clear and the server trusts them — exactly the state a real launch happens from.
        server.HandlePayloadForTest(pilot.ConnectionId, NetCodec.Encode(
            new MoveIntent { X = preLaunch.X, Y = preLaunch.Y, Z = preLaunch.Z }));
        Assert.False(pilot.AwaitingSpawnAdopt);

        LandOnAnotherPad(server, transport, pilot);
        var landed = pilot.State.Position;

        server.HandlePayloadForTest(pilot.ConnectionId, NetCodec.Encode(
            new MoveIntent { X = preLaunch.X, Y = preLaunch.Y, Z = preLaunch.Z }));

        Assert.Equal(landed.X, pilot.State.Position.X); // the authoritative touchdown stands
        Assert.Equal(landed.Z, pilot.State.Position.Z);

        // …and once the client has adopted the snap, normal movement is trusted again.
        server.HandlePayloadForTest(pilot.ConnectionId, NetCodec.Encode(
            new MoveIntent { X = landed.X + 1f, Y = landed.Y, Z = landed.Z }));
        Assert.Equal(landed.X + 1f, pilot.State.Position.X);
    }

    // ---------------- #954: SwitchShip must not replace OTHER players' own hulls ----------------

    [Fact]
    public void SwitchShipInSpace_SendsOthersTheDesignAsShipRemote()
    {
        var transport = new RecordingTransport();
        var server = NewServer("switch_remote", transport);

        var ann = server.AddLocalPlayer("Ann");
        var ben = server.AddLocalPlayer("Ben");
        server.EnterSpace("Ann");
        server.EnterSpace("Ben"); // same body → same instance

        server.RequestLandingPadsForTest(ann, ann.CurrentLocationId); // serves Ann → the ship cursor points at her
        transport.Sent.Clear();
        Assert.True(server.SwitchShip("default")); // re-selecting the starter rebuilds + re-sends the design

        // Ann gets her (rebuilt) hull as her OWN ship; Ben must get it as a remote silhouette — kind
        // "ship" replaced HIS own hull with Ann's on every switch.
        Assert.Contains(transport.Sent, x => x.Conn == ann.ConnectionId
            && x.Msg is SpaceShipDesign d && d.Id == "ship:Ann" && d.Kind == "ship");
        Assert.Contains(transport.Sent, x => x.Conn == ben.ConnectionId
            && x.Msg is SpaceShipDesign d && d.Id == "ship:Ann" && d.Kind == "ship_remote");
        Assert.DoesNotContain(transport.Sent, x => x.Conn == ben.ConnectionId
            && x.Msg is SpaceShipDesign d && d.Id == "ship:Ann" && d.Kind == "ship");
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — a straggling handle on Windows must not fail the suite
        }
    }
}
