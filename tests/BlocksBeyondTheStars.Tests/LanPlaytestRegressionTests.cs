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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
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

    // ---------------- #977: your OWN reserved pad must stay selectable ----------------

    /// <summary>Asks the server for a body's pads on behalf of one session and returns the reply's entry for
    /// one pad index — the exact data the flight chooser and the world map draw from.</summary>
    private static NetLandingPad PadAsSeenBy(
        SvGameServer server, RecordingTransport transport, PlayerSession viewer, int padIndex)
    {
        transport.Sent.Clear();
        server.RequestLandingPadsForTest(viewer, viewer.CurrentLocationId);
        var reply = transport.Sent
            .Where(x => x.Conn == viewer.ConnectionId)
            .Select(x => x.Msg).OfType<LandingPadList>().Last();
        return reply.Pads.Single(p => p.Index == padIndex);
    }

    [Fact]
    public void PadChooser_ShowsYourOwnReservedPadAsYoursNotAsTaken()
    {
        // A pilot in space keeps their pad reserved (#957) — and that reservation was reported back to the
        // reserving pilot as plain "occupied", labelled with their own name. The client only offers FREE
        // pads, so the pilot could not land back on their own ship's pad at all.
        var transport = new RecordingTransport();
        var server = NewServer("pad_mine", transport);

        var ann = server.AddLocalPlayer("Ann");
        int annPad = ann.AssignedPadIndex;
        Assert.True(annPad >= 0, "joining with a ship parks it on a pad");
        server.EnterSpace("Ann");

        var mine = PadAsSeenBy(server, transport, ann, annPad);
        Assert.False(mine.Occupied); // selectable in the chooser
        Assert.True(mine.Mine);      // …and drawn as hers
        Assert.Equal("Ann", mine.Occupant);

        // The server agrees: landing back on the reserved pad is accepted and parks her ship there.
        server.LandOnCurrentBodyForTest(ann, annPad);
        Assert.Equal(annPad, ann.AssignedPadIndex);
    }

    [Fact]
    public void PadChooser_StillShowsAnotherPilotsReservedPadAsTaken()
    {
        // The other half of #977: excluding the viewer must not weaken the reservation for anyone else.
        var transport = new RecordingTransport();
        var server = NewServer("pad_theirs", transport);

        var ann = server.AddLocalPlayer("Ann");
        int annPad = ann.AssignedPadIndex;
        server.EnterSpace("Ann");
        var ben = server.AddLocalPlayer("Ben");

        var theirs = PadAsSeenBy(server, transport, ben, annPad);
        Assert.True(theirs.Occupied); // Ben cannot pick it
        Assert.False(theirs.Mine);
        Assert.Equal("Ann", theirs.Occupant);
    }

    // ---------------- #981: a trade request must be answerable ----------------

    [Fact]
    public void ATradeRequest_ArrivesAsAnAnswerableInvitation()
    {
        // The invitation used to be a chat line only, and NOTHING in the client ever sent the response
        // intent — so pressing T looked dead on both sides and the trade could never open.
        var transport = new RecordingTransport();
        var server = NewServer("trade_notice", transport);
        var ann = server.AddLocalPlayer("Ann");
        var ben = server.AddLocalPlayer("Ben");
        ben.State.Position = ann.State.Position; // standing together (pads are far apart)

        transport.Sent.Clear();
        server.RequestTrade("Ann", "Ben");

        Assert.Contains(transport.Sent, x => x.Conn == ben.ConnectionId
            && x.Msg is TradeRequestNotice n && n.Requester == "Ann");
        Assert.Contains(transport.Sent, x => x.Conn == ann.ConnectionId // the asker sees it went out
            && x.Msg is ServerMessage m && m.Text.StartsWith("@srv.trade.request_sent", StringComparison.Ordinal));

        // …and the answer the client can now give opens the trade for both.
        server.RespondTrade("Ben", true);
        Assert.Contains(transport.Sent, x => x.Conn == ann.ConnectionId && x.Msg is TradeUpdate);
        Assert.Contains(transport.Sent, x => x.Conn == ben.ConnectionId && x.Msg is TradeUpdate);
    }

    // ---------------- #982: painted avatars must travel BOTH ways ----------------

    [Fact]
    public void EnteringAWorld_ExchangesPaintedAvatarsBothWays()
    {
        // Faces and body paintings are out-of-band one-shot messages: only the arriving player was ever
        // sent them, so everybody already in the world kept rendering the newcomer as a blank avatar.
        var transport = new RecordingTransport();
        var server = NewServer("paint_sync", transport);
        var ann = server.AddLocalPlayer("Ann");
        var ben = server.AddLocalPlayer("Ben");
        string pixels = new string('7', BodyPaint.ExpectedLength(BodyPaint.Torso));
        server.SetBodyPaintForTest(ann, BodyPaint.Torso, pixels);
        server.SetBodyPaintForTest(ben, BodyPaint.Torso, pixels);
        server.EnterSpace("Ann");

        transport.Sent.Clear();
        server.LandOnCurrentBodyForTest(ann); // a world entry — the same sync every arrival runs

        Assert.Contains(transport.Sent, x => x.Conn == ann.ConnectionId
            && x.Msg is PlayerBodyPaint p && p.PlayerId == "Ben"); // the arriving player sees the others…
        Assert.Contains(transport.Sent, x => x.Conn == ben.ConnectionId
            && x.Msg is PlayerBodyPaint p && p.PlayerId == "Ann"); // …and the others see them (the missing half)
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

    // ---------------- 2026-08-13 audit follow-ups (#996/#997/#999) ----------------

    [Fact]
    public void TheStarMapsFreePadCount_DoesNotCountYourOwnReservationAgainstYou()
    {
        // #999: a pilot in space keeps their pad reserved (#957) and the chooser already excludes that
        // reservation for its holder (#977) — but the star map's free-pad count did not, so the last pad
        // being your OWN made the map say "pads full" one screen before the chooser happily offered it.
        var transport = new RecordingTransport();
        var server = NewServer("pad_count", transport);

        var ann = server.AddLocalPlayer("Ann");
        Assert.True(ann.AssignedPadIndex >= 0);
        int freeBefore = server.FreePadCountForTest();
        server.EnterSpace("Ann"); // the reservation is held while she is up in space (#957)

        Assert.Equal(freeBefore, server.FreePadCountForTest());        // neutral: still reserved
        Assert.Equal(freeBefore + 1, server.FreePadCountForTest("Ann")); // as seen by the holder (#999)
    }

    [Fact]
    public void AnObserverLandingBackOnTheSameBody_ClaimsNoPadAndNoAnchor()
    {
        // #996: HandleTravel has exempted spectators from the pad/ship half of a landing since #487 —
        // the same-body path did not: it claimed a communal pad for the invisible observer and set
        // AboardShip/RespawnPoint as if they had a ship parked there.
        var transport = new RecordingTransport();
        var server = NewServer("observer_land", transport, placeShip: false);

        var watcher = server.AddLocalPlayer("Watcher");
        watcher.Spectating = true;
        watcher.State.AboardShip = false;
        var anchorBefore = watcher.State.RespawnPoint;
        server.EnterSpace("Watcher");
        int freeBefore = server.FreePadCountForTest();

        server.LandOnCurrentBodyForTest(watcher, 1); // an explicit pad pick, like the chooser sends

        Assert.Equal(freeBefore, server.FreePadCountForTest()); // no pad claimed by the observer
        Assert.False(watcher.State.AboardShip);
        Assert.Equal(anchorBefore.X, watcher.State.RespawnPoint.X); // anchor untouched
        Assert.Equal(anchorBefore.Z, watcher.State.RespawnPoint.Z);
    }

    [Fact]
    public void ANewPlayersRespawnAnchor_IsTheirOwnSpawn_NotTheLastServedShips()
    {
        // #997: CreateNewPlayer runs BEFORE the new session exists, so _shipPlaced/_healTank still
        // resolve under the last served player's ship cursor. With PlaceStarterShip=false nothing
        // overwrites the anchor afterwards — a brand-new player persisted the HOST's heal tank and
        // respawned inside the host's ship.
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "respawn_anchor"));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = "respawn_anchor",
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        var host = server.AddLocalPlayer("Host"); // ship parked → cursor + heal tank point at the host
        config.PlaceStarterShip = false;          // a shipwright world for everyone joining after (#949)
        var newbie = server.AddLocalPlayer("Newbie");

        // The newbie's anchor is at their own pad, not at the host's heal tank (pads sit far apart).
        Assert.InRange(newbie.State.RespawnPoint.X, newbie.State.Position.X - 8, newbie.State.Position.X + 8);
        Assert.InRange(newbie.State.RespawnPoint.Z, newbie.State.Position.Z - 8, newbie.State.Position.Z + 8);
        Assert.True(
            System.Math.Abs(newbie.State.RespawnPoint.X - host.State.RespawnPoint.X) > 8
            || System.Math.Abs(newbie.State.RespawnPoint.Z - host.State.RespawnPoint.Z) > 8,
            "the new player's respawn anchor must not be the host's heal tank");
    }

    // ---------------- #1020: dying to an AI tick respawned you inside ANOTHER player's ship ----------------

    [Fact]
    public void DyingToAnAiTick_RecoversToYourOwnShipsWorld_NotTheLastServedOnes()
    {
        // The AI damage ticks (creatures/bandits/machines/speeders) call RespawnPlayer with the ship cursor
        // still on whoever the server served last. RecoverToShip read THAT player's ship to pick the
        // recovery world (and could even re-home that ship there) — the host died and came back at the
        // OTHER player's ship instead of his own.
        var transport = new RecordingTransport();
        var server = NewServer("death_cursor", transport);
        var host = server.AddLocalPlayer("Host");
        var mary = server.AddLocalPlayer("Mary");
        server.SetInstantTravelForTest(true);

        // Park the two ships on two DIFFERENT galaxy bodies (real travels, so both ship records carry a
        // proper body id — the fresh start world still runs under its legacy planet-type key).
        var planets = server.Galaxy.AllBodies().Where(b =>
                b.Kind == CelestialKind.Planet
                && !string.IsNullOrEmpty(b.PlanetType)
                && _content.GetPlanet(b.PlanetType!) is not null
                && b.Id != host.CurrentLocationId)
            .Take(2).ToArray();
        var (hostBody, maryBody) = (planets[0], planets[1]);

        server.RequestLandingPadsForTest(host, host.CurrentLocationId); // serves the host → server.Ship is his
        server.Ship.Modules.Add("jump_generator"); // the destinations may sit in another system (as in TravelTests)
        Assert.True(server.QuickTravelForTest("Host", hostBody.Id));

        server.EnterSpace("Host"); // die away from the surface → the RecoverToShip (world-transition) path

        server.RequestLandingPadsForTest(mary, mary.CurrentLocationId); // serves Mary → server.Ship is hers
        server.Ship.Modules.Add("jump_generator");
        Assert.True(server.QuickTravelForTest("Mary", maryBody.Id)); // the travel serves her → cursor on Mary

        server.KillPlayerForTest(host, "@srv.death.wildlife");

        // The host must recover to HIS ship's world and heal tank — not to the body Mary's ship parks on.
        Assert.Equal(hostBody.Id, host.CurrentLocationId);
        Assert.True(server.HasShip, "the victim's own ship must be re-parked for the respawn");
        Assert.Equal(server.HealTank.X, host.State.Position.X);
        Assert.Equal(server.HealTank.Z, host.State.Position.Z);
        Assert.Equal(maryBody.Id, mary.CurrentLocationId); // and Mary was not dragged anywhere either
    }

    [Fact]
    public void VoidRescue_RecoversToYourOwnShip_NotTheLastServedOnes()
    {
        // SafeSpawnPoint took a playerId and ignored it in the ship branch (_shipPlaced/_healTank = the
        // cursor): the void-rescue tick, which never pins the cursor, teleported a falling player into
        // whichever ship the server touched last.
        var transport = new RecordingTransport();
        var server = NewServer("void_cursor", transport);
        var host = server.AddLocalPlayer("Host");
        var mary = server.AddLocalPlayer("Mary");
        var hostAnchor = host.State.RespawnPoint;
        var maryAnchor = mary.State.RespawnPoint;

        server.RequestLandingPadsForTest(mary, mary.CurrentLocationId); // serves Mary → the ship cursor points at her

        // Carve a deep air shaft below the host and drop him in (the world has a bedrock floor, so the
        // void has to be made, not found) — the same setup SpawnSafetyTests uses.
        int bx = (int)System.Math.Floor(host.State.Position.X), bz = (int)System.Math.Floor(host.State.Position.Z);
        int top = (int)host.State.Position.Y;
        for (int y = top; y > top - 160; y--)
        {
            server.World.SetBlock(new Vector3i(bx, y, bz), BlockId.Air);
        }

        host.State.Position = new Vector3f(bx + 0.5f, top - 50, bz + 0.5f);
        Assert.True(server.IsInVoidForTest(host.State.Position), "the host must start in the void for the rescue to fire");

        server.RunVoidRescueForTest();

        Assert.InRange(host.State.Position.X, hostAnchor.X - 8, hostAnchor.X + 8);
        Assert.InRange(host.State.Position.Z, hostAnchor.Z - 8, hostAnchor.Z + 8);
        Assert.True(
            System.Math.Abs(host.State.Position.X - maryAnchor.X) > 8
            || System.Math.Abs(host.State.Position.Z - maryAnchor.Z) > 8,
            "the rescued player must not be teleported into the other player's ship");
    }

    // ---------------- #1020: a pad claimed after your arrival never showed as occupied ----------------

    [Fact]
    public void ALandingClaimingAPad_RepublishesOccupancyToPlayersAlreadyOnTheBody()
    {
        // LandingPadList was a world-entry snapshot: whoever was already on the body kept seeing a later
        // lander's pad as free/anonymous forever ("we couldn't see her landing pad").
        var transport = new RecordingTransport();
        var server = NewServer("pad_republish", transport);
        var host = server.AddLocalPlayer("Host");
        var justus = server.AddLocalPlayer("Justus");
        var mary = server.AddLocalPlayer("Mary");

        server.EnterSpace("Mary");
        transport.Sent.Clear();
        server.LandOnCurrentBodyForTest(mary, 3); // an explicit free pad pick, like the chooser sends

        foreach (var bystander in new[] { host, justus })
        {
            var list = transport.Sent
                .Where(x => x.Conn == bystander.ConnectionId)
                .Select(x => x.Msg).OfType<LandingPadList>().LastOrDefault();
            Assert.NotNull(list);
            var pad = list!.Pads.Single(p => p.Index == 3);
            Assert.True(pad.Occupied, "the freshly claimed pad must be published as occupied");
            Assert.Equal("Mary", pad.Occupant);
        }

        // The lander's own list marks the pad as hers (receiver-relative Mine, #977).
        var maryList = transport.Sent
            .Where(x => x.Conn == mary.ConnectionId)
            .Select(x => x.Msg).OfType<LandingPadList>().LastOrDefault();
        Assert.NotNull(maryList);
        Assert.True(maryList!.Pads.Single(p => p.Index == 3).Mine);
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
