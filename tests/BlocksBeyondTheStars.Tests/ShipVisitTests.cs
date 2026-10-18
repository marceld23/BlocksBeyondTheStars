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
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The docking visit (#2723): a docked guest walks over into the host's ship while both float in space — the guest's own
/// ship stays parked where it floated, the guest looks only, and is sent home before the host's ship can move.
/// </summary>
public sealed class ShipVisitTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ShipVisitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_visit_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private sealed record Docked(SvGameServer Server, RecordingTransport T, PlayerSession Host, PlayerSession Guest, string Instance);

    /// <summary>"Host" and "Guest" launch into one orbit, the ships 12 apart, and dock. With <paramref name="hostInside"/>
    /// the host then walks inside their ship (it floats parked). <paramref name="third"/> adds a third pilot, "Carl".</summary>
    private Docked TwoDockedShips(string world, bool hostInside = true, bool third = false, bool dock = true, DockingMode mode = DockingMode.Free,
        bool nearStation = false)
    {
        var t = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = nearStation ? 1 : 9,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        if (!nearStation)
        {
            config.StartPlanet = "rocky";
        }

        config.Rules.ShipDocking = mode;
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, t, repo);
        server.Start();
        var host = server.AddLocalPlayer("Host");
        var guest = server.AddLocalPlayer("Guest");
        if (third)
        {
            server.AddLocalPlayer("Carl");
        }

        foreach (var s in server.Sessions.Values)
        {
            s.Ships[s.ActiveShipId].Modules.Add("docking_module");
        }

        server.EnterSpace("Host");
        server.EnterSpace("Guest");
        // Off the system station (well out of its boarding range) when asked for one, else at the origin.
        var at = nearStation
            ? server.SpaceEntitiesFor("Host").First(e => e.Kind == CombatEntityKind.SpaceStation).Position + new Vector3f(40f, 0f, -150f)
            : new Vector3f(0f, 0f, 0f);
        server.ShipMove("Host", at.X, at.Y, at.Z, 0f);
        server.ShipMove("Guest", at.X + 12f, at.Y, at.Z, 45f);
        if (third)
        {
            server.EnterSpace("Carl");
            server.ShipMove("Carl", -20f, 0f, 0f);
        }

        string instance = server.SpaceInstanceIdForTest("Host")!;
        if (dock)
        {
            server.RequestDock("Guest", "Host");
            Assert.True(server.AreDocked("Guest", "Host"));
        }

        if (hostInside)
        {
            server.EnterShipInterior("Host");
            Assert.True(server.InShipInterior("Host"));
        }

        return new Docked(server, t, host, guest, instance);
    }

    private static IEnumerable<T> To<T>(RecordingTransport t, PlayerSession s) => t.Sent.Where(x => x.Conn == s.ConnectionId).Select(x => x.Msg).OfType<T>();

    private static bool Told(RecordingTransport t, PlayerSession s, string text) => To<ServerMessage>(t, s).Any(m => m.Text == text);

    private static bool Refused(RecordingTransport t, PlayerSession s, string action, string reason)
        => To<ActionRejected>(t, s).Any(r => r.Action == action && r.Reason == reason);

    private static void Visit(Docked d)
    {
        d.Server.VisitShip("Guest", "Host");
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));
    }

    /// <summary>The guest is back in their own floating ship: their interior, their own walkabout, no visit.</summary>
    private static void AssertGuestHome(Docked d)
    {
        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.Null(d.Server.ShipGuestOf("Host"));
        Assert.Equal("shipint:Guest", d.Guest.CurrentLocationId);
        Assert.True(d.Server.InShipInterior("Guest"), "home means the own interior, the parked ship's record kept");
        Assert.Equal(string.Empty, To<WorldReset>(d.T, d.Guest).Last().ShipOwnerId);
    }

    [Fact]
    public void TheGuest_StandsInTheHostsHull_TheirOwnShipFloatsParked_AndTheOrbitStaysLoaded()
    {
        var d = TwoDockedShips("visit_basic", third: true);
        Assert.All(To<DockStatus>(d.T, d.Host), s => Assert.True(s.CanVisit)); // this server can do visits

        Visit(d);

        Assert.Equal("Guest", d.Server.ShipGuestOf("Host"));
        Assert.False(d.Server.InSpace("Guest"));
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
        var reset = To<WorldReset>(d.T, d.Guest).Last();
        Assert.Equal("ship_interior", reset.PlanetType);
        Assert.Equal("Host", reset.ShipOwnerId);
        Assert.True(Told(d.T, d.Host, "@srv.visit.guest_arrived:Guest"));

        d.Server.Tick(0.2);
        Assert.True(d.Guest.State.AboardShip, "the guest arrives inside the host's hull");
        Assert.False(d.Guest.State.InEva);
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest")); // no walk-out at the arrival spot

        // A third pilot still sees the guest's ship, parked where it floated.
        var parked = Assert.Single(d.Server.OtherSpacePlayers("Carl"), p => p.PlayerId == "Guest");
        Assert.True(parked.Parked);
        Assert.Equal(12f, parked.X, 2);

        // With nobody flying any more, the parked hulls keep the orbit loaded.
        d.Server.LeaveSpace("Carl");
        Assert.True(d.Server.SpaceInstanceLoadedForTest(d.Instance));
    }

    [Fact]
    public void APilot_DocksWithAFriendWalkingTheirParkedShip_AndGoesOver()
    {
        // "Step inside, I'm coming over": the host already walks inside the floating ship when the guest docks.
        var d = TwoDockedShips("visit_dock_inside", dock: false);
        Assert.True(d.Server.InShipInterior("Host"));

        d.Server.RequestDock("Guest", "Host");
        Assert.True(d.Server.AreDocked("Guest", "Host"), "the parked ship's pose counts for the friend walking inside it");

        Visit(d);
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
    }

    [Fact]
    public void TwoFriendsWalkingTheirOwnFloatingShips_Dock_ButNotFromFarAway_AndNotFromASurface()
    {
        var d = TwoDockedShips("visit_dock_both_inside", dock: false);
        d.Server.EnterShipInterior("Guest");
        d.Server.RequestDock("Host", "Guest");
        Assert.True(d.Server.AreDocked("Host", "Guest"));
        d.Server.Undock("Host");

        // The guest takes the helm and flies off: out of range of the parked ship.
        d.Server.ExitShipToFlight("Guest");
        d.Server.ShipMove("Guest", 300f, 0f, 0f);
        d.Server.RequestDock("Guest", "Host");
        Assert.False(d.Server.AreDocked("Guest", "Host"));

        // Down on the planet: never docked with a ship in orbit, however close the numbers.
        d.Server.LeaveSpace("Guest");
        d.Guest.State.Position = new Vector3f(0f, 0f, 0f);
        d.Server.RequestDock("Guest", "Host");
        Assert.False(d.Server.AreDocked("Guest", "Host"));
    }

    [Fact]
    public void TheAccept_RechecksAFriendWalkingTheirParkedShip()
    {
        var t = TwoDockedShips("visit_dock_accept", dock: false, mode: DockingMode.RequestRequired);
        t.Server.RequestDock("Guest", "Host"); // next to the parked ship — the request goes through

        t.Server.ShipMove("Guest", 300f, 0f, 0f); // the guest flies off before the host accepts
        t.Server.RespondDock("Host", "Guest", accept: true);
        Assert.False(t.Server.AreDocked("Guest", "Host"));

        t.Server.ShipMove("Guest", 12f, 0f, 0f);
        t.Server.RequestDock("Guest", "Host");
        t.Server.RespondDock("Host", "Guest", accept: true);
        Assert.True(t.Server.AreDocked("Guest", "Host"));
    }

    [Fact]
    public void AVisit_IsRefused_WithoutADocking()
    {
        var d = TwoDockedShips("visit_undocked");
        d.Server.Undock("Guest");

        d.Server.VisitShip("Guest", "Host");

        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.True(Refused(d.T, d.Guest, "visit", "@srv.visit.not_docked"));
    }

    [Fact]
    public void AVisit_IsRefused_WhenTheShipsAreTooFarApart()
    {
        var d = TwoDockedShips("visit_far");
        d.Server.ShipMove("Guest", 300f, 0f, 0f);

        d.Server.VisitShip("Guest", "Host");

        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.True(d.Server.InSpace("Guest"));
        Assert.True(Refused(d.T, d.Guest, "visit", "@srv.visit.too_far"));
    }

    [Fact]
    public void AVisit_IsRefused_WhileTheHostIsAtTheHelm_AndTheHostIsTold()
    {
        var d = TwoDockedShips("visit_helm", hostInside: false);

        d.Server.VisitShip("Guest", "Host");

        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.True(d.Server.InSpace("Guest"));
        Assert.True(Refused(d.T, d.Guest, "visit", "@srv.visit.host_flying"));
        Assert.True(Told(d.T, d.Host, "@srv.visit.host_flying_notice:Guest"));
    }

    [Fact]
    public void TheGuest_LooksOnly_NoBuildingMiningPaintingStationsOrBioLab()
    {
        var d = TwoDockedShips("visit_lookonly");
        d.Guest.Ships[d.Guest.ActiveShipId].Modules.Add("bio_lab");
        d.Host.Ships[d.Host.ActiveShipId].Modules.Add("bio_lab");
        Visit(d);
        var at = d.Guest.State.Position;
        int x = (int)Math.Floor(at.X), y = (int)Math.Floor(at.Y), z = (int)Math.Floor(at.Z);
        int conn = d.Guest.ConnectionId;

        d.Server.HandlePayloadForTest(conn, NetCodec.Encode(new MineBlockIntent { X = x, Y = y - 1, Z = z }));
        d.Server.HandlePayloadForTest(conn, NetCodec.Encode(new PlaceBlockIntent { X = x, Y = y + 1, Z = z + 1, ItemKey = "iron_wall" }));
        d.Server.HandlePayloadForTest(conn, NetCodec.Encode(new PaintBlockIntent { X = x, Y = y - 1, Z = z }));
        d.Server.HandlePayloadForTest(conn, NetCodec.Encode(new StructureEditIntent { StructureId = "ship:Host", X = 1, Y = 1, Z = 1, Mine = true }));
        d.Server.HandlePayloadForTest(conn, NetCodec.Encode(new PlaceBlockIntent { X = x + 200, Y = y, Z = z, ItemKey = "iron_wall" })); // the void outside

        Assert.True(Refused(d.T, d.Guest, "mine", "@srv.visit.look_only"));
        Assert.True(Refused(d.T, d.Guest, "paint", "@srv.visit.look_only"));
        Assert.True(Refused(d.T, d.Guest, "structure", "@srv.visit.look_only"));
        Assert.Equal(2, To<ActionRejected>(d.T, d.Guest).Count(r => r.Action == "place" && r.Reason == "@srv.visit.look_only"));

        // The host's helm is no way out for the guest — and no other station either.
        d.Server.UseStation("Guest", "cockpit");
        d.Server.UseStation("Guest", "medbay");
        Assert.Equal(2, To<ActionRejected>(d.T, d.Guest).Count(r => r.Action == "station" && r.Reason == "@srv.visit.look_only"));
        Assert.True(d.Server.IsVisitingShip("Guest"));
        Assert.False(d.Server.InSpace("Host"));

        // The guest's own lab module stays in the guest's own ship; the host's works for the host.
        d.Server.BioLabForTest(d.Guest, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = 12345u });
        d.Server.BioLabForTest(d.Host, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = 12345u });
        Assert.Equal("srv.bio.need_lab", To<BioLabResult>(d.T, d.Guest).Last().MessageKey);
        Assert.NotEqual("srv.bio.need_lab", To<BioLabResult>(d.T, d.Host).Last().MessageKey);
    }

    [Fact]
    public void TheGuest_BreathesInTheHull_ButNotInAnUnsealedExtension()
    {
        var d = TwoDockedShips("visit_air");
        d.Host.State.InstantBuild = true;
        var (origin, size) = d.Server.LandedShipBoundsForTest("Host"); // the host's interior world is the active one
        var hatch = d.Server.DoorFits.Single(door => (int)Math.Floor(door.Pos.Z) == origin.Z);
        int hatchX = (int)Math.Floor(hatch.Pos.X) - origin.X;
        BuildOpenRoomBehindTheHatch(d.Server, size.X, size.Y - 1);
        Visit(d);

        d.Guest.State.Oxygen = 50f;
        d.Server.Tick(0.5);
        Assert.True(d.Guest.State.AboardShip);
        Assert.True(d.Guest.State.Oxygen > 50f, "the host's cabin breathes for the guest too");

        // Through the hatch into the host's open extension: helmet on — but still inside the ship, no walk-out.
        d.Guest.State.Position = new Vector3f(origin.X + hatchX + 0.5f, origin.Y + 1f, origin.Z - 2.5f);
        d.Guest.State.Oxygen = 50f;
        d.Server.Tick(0.5);

        Assert.True(d.Server.IsVisitingShip("Guest"));
        Assert.False(d.Guest.State.AboardShip);
        Assert.True(d.Guest.State.Oxygen < 50f, "out here there is no air");
    }

    /// <summary>A roofless room behind the host's rear hatch (as in ShipExtensionTests): floor, side walls, back wall.</summary>
    private static void BuildOpenRoomBehindTheHatch(SvGameServer server, int width, int top)
    {
        void Build(int x, int y, int z) => server.HandleStructureEditForTest("Host", new StructureEditIntent { StructureId = "ship:Host", X = x, Y = y, Z = z, ItemKey = "iron_wall" });
        const int Depth = 4;
        for (int z = -1; z >= -Depth - 1; z--)
        {
            for (int x = 0; x < width; x++) { Build(x, 0, z); }
            Build(-1, 0, z);
            Build(width, 0, z);
        }

        for (int y = 1; y <= top; y++)
        {
            for (int z = -1; z >= -Depth - 1; z--)
            {
                Build(-1, y, z);
                Build(width, y, z);
            }

            for (int x = 0; x < width; x++) { Build(x, y, -Depth - 1); }
        }
    }

    [Fact]
    public void GoingHome_PutsTheGuestInTheirOwnInterior_AndTheHelmResumesAtTheParkedPose()
    {
        var d = TwoDockedShips("visit_home");
        Visit(d);

        d.Server.EnterShipInterior("Guest"); // "back to my ship" (EnterShipIntent) while visiting

        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Host, "@srv.visit.guest_left:Guest"));
        Assert.Contains(To<PlayerLeft>(d.T, d.Host), l => l.PlayerId == "Guest");

        d.Server.ExitShipToFlight("Guest");
        Assert.True(d.Server.InSpace("Guest"));
        var flight = To<SpaceState>(d.T, d.Guest).Last();
        Assert.True(flight.SkipLaunch);
        Assert.True(flight.HasResumePose);
        Assert.Equal(12f, flight.ResumeX, 2);
        Assert.Equal(45f, flight.ResumeYaw, 2);
    }

    [Fact]
    public void TheGuestsOwnHelm_EndsTheVisit_StraightIntoTheirFlight()
    {
        var d = TwoDockedShips("visit_ownhelm");
        Visit(d);

        d.Server.ExitShipToFlight("Guest"); // ExitShipIntent (or a launch) while visiting

        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.True(d.Server.InSpace("Guest"));
        Assert.False(d.Server.InShipInterior("Guest"));
        Assert.Equal(12f, To<SpaceState>(d.T, d.Guest).Last().ResumeX, 2);
        Assert.True(Told(d.T, d.Host, "@srv.visit.guest_left:Guest"));
    }

    [Fact]
    public void TheHostTakingTheHelm_SendsTheGuestHomeFirst()
    {
        var d = TwoDockedShips("visit_hosthelm");
        Visit(d);

        d.Server.ExitShipToFlight("Host");

        Assert.True(d.Server.InSpace("Host"));
        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Guest, "@srv.visit.host_moving:Host"));
    }

    [Fact]
    public void TheHostUndocking_SendsTheGuestHome()
    {
        var d = TwoDockedShips("visit_hostundock");
        Visit(d);

        d.Server.Undock("Host"); // U is the host's "send home"

        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Guest, "@srv.visit.sent_home:Host"));
    }

    [Fact]
    public void TheGuestUndocking_GoesHome()
    {
        var d = TwoDockedShips("visit_guestundock");
        Visit(d);

        d.Server.Undock("Guest");

        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Guest, "@srv.visit.undocked"));
    }

    [Fact]
    public void TheHostQuitting_SendsTheGuestHome()
    {
        var d = TwoDockedShips("visit_hostquit");
        Visit(d);

        d.Server.DisconnectLocalPlayerForTest("Host");

        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Guest, "@srv.visit.host_left:Host"));
    }

    [Fact]
    public void TheHostDying_SendsTheGuestHome()
    {
        var d = TwoDockedShips("visit_hostdies");
        Visit(d);

        d.Server.KillPlayerForTest(d.Host, "test");

        AssertGuestHome(d);
        Assert.True(Told(d.T, d.Guest, "@srv.visit.host_left:Host"));
    }

    [Fact]
    public void TheGuestWalkingOutOfTheHostsHull_GoesHome_NoSpacewalk()
    {
        var d = TwoDockedShips("visit_walkout");
        Visit(d);
        var p = d.Guest.State;

        p.Position = new Vector3f(p.Position.X, p.Position.Y, p.Position.Z - 40f); // out of the hatch, into the void
        d.Server.Tick(0.1);

        AssertGuestHome(d);
        Assert.False(p.InEva);
        Assert.False(d.Server.InSpace("Guest"));
        Assert.True(Told(d.T, d.Guest, "@srv.visit.back_home"));
    }

    [Fact]
    public void TheGuestQuitting_EndsTheVisit()
    {
        var d = TwoDockedShips("visit_guestquit");
        Visit(d);

        d.Server.DisconnectLocalPlayerForTest("Guest");

        Assert.Null(d.Server.ShipGuestOf("Host"));
        Assert.False(d.Server.InShipInterior("Guest")); // #2722: and no walkabout outlives the session
        Assert.Contains(To<PlayerLeft>(d.T, d.Host), l => l.PlayerId == "Guest");
        Assert.True(d.Server.InShipInterior("Host"), "the host stays in their ship");
    }

    [Fact]
    public void TheGuestDying_EndsTheVisit()
    {
        var d = TwoDockedShips("visit_guestdies");
        Visit(d);

        d.Server.KillPlayerForTest(d.Guest, "test");

        Assert.False(d.Server.IsVisitingShip("Guest"));
        Assert.Null(d.Server.ShipGuestOf("Host"));
        Assert.NotEqual("shipint:Host", d.Guest.CurrentLocationId); // the respawn took the guest back to their ship
    }

    [Fact]
    public void AHostOnASpacewalk_CanBeVisited_AndBothSeeEachOtherOnceTheHostIsBackIn()
    {
        var d = TwoDockedShips("visit_eva", hostInside: false);
        // Both hold the same pad index — after a hyperjump or a wormhole everybody's interior parks the hull on pad 0.
        d.Host.AssignedPadIndex = 0;
        d.Guest.AssignedPadIndex = 0;
        d.Server.HandlePayloadForTest(d.Host.ConnectionId, NetCodec.Encode(new SetEvaIntent { Active = true }));
        Assert.True(d.Host.State.InEva);
        d.Server.ShipMove("Host", 3f, 1f, 0f); // the suit floats beside the ship

        Visit(d);
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
        var (hull, _) = d.Server.LandedShipBoundsForTest("Host"); // the host's interior world is the active one
        d.Server.Tick(0.2);
        Assert.True(d.Guest.State.AboardShip, "the host's ship was placed for the guest");

        d.Server.EnterShipInterior("Host"); // the host boards from the spacewalk
        Assert.Equal("shipint:Host", d.Host.CurrentLocationId);
        Assert.Equal(hull, d.Server.LandedShipBoundsForTest("Host").Origin); // the hull stays where the guest stands in it
        Assert.Equal(0, d.Host.AssignedPadIndex);                            // and the host's pad is the host's

        d.T.Sent.Clear();
        d.Server.Tick(0.6);
        d.Server.Tick(0.6); // past the 1 Hz visit check
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
        Assert.True(d.Guest.State.AboardShip);
        Assert.Contains(To<PlayerPresence>(d.T, d.Host), p => p.PlayerId == "Guest");
        Assert.Contains(To<PlayerPresence>(d.T, d.Guest), p => p.PlayerId == "Host");
    }

    [Fact]
    public void TheHostSteppingOutOfTheAirlock_KeepsTheGuestAboard_AndComingBackFindsThemThere()
    {
        var d = TwoDockedShips("visit_airlock");
        Visit(d);

        d.Server.StartEvaFromShip("Host"); // out through the hatch: the ship stays parked

        Assert.True(d.Host.State.InEva);
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));
        Assert.DoesNotContain(To<ServerMessage>(d.T, d.Guest), m => m.Text.StartsWith("@srv.visit.host_", StringComparison.Ordinal));
        d.Server.Tick(0.6);
        d.Server.Tick(0.6); // past the 1 Hz visit check: a host on a spacewalk keeps the ship parked
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));

        d.Server.EnterShipInterior("Host");
        d.Server.Tick(0.6);
        d.Server.Tick(0.6);
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));
        Assert.Equal("shipint:Host", d.Host.CurrentLocationId);
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
    }

    [Fact]
    public void AHostOnASpacewalkBoardingAStation_SendsTheGuestHomeAtOnce()
    {
        var d = TwoDockedShips("visit_station", nearStation: true);
        Visit(d);
        d.Server.StartEvaFromShip("Host");
        var station = d.Server.SpaceEntitiesFor("Host").First(e => e.Kind == CombatEntityKind.SpaceStation);
        d.Server.ShipMove("Host", station.Position.X, station.Position.Y, station.Position.Z); // the suit floats over

        d.Server.BoardStation("Host", station.Id);

        Assert.True(d.Server.InStation("Host"));
        AssertGuestHome(d); // not only at the next 1 Hz check
        Assert.True(Told(d.T, d.Guest, "@srv.visit.host_left:Host"));
    }

    [Fact]
    public void AStalePoseFromTheFriendsShip_NeverThrowsTheGuestOntoASpacewalk()
    {
        var d = TwoDockedShips("visit_stale");
        Visit(d);
        int hostWorld = To<WorldReset>(d.T, d.Guest).Last(r => r.ShipOwnerId == "Host").WorldId;
        d.Server.Undock("Host"); // sent home
        AssertGuestHome(d);
        var p = d.Guest.State;
        var home = p.Position;
        int homeWorld = To<WorldReset>(d.T, d.Guest).Last().WorldId;
        Assert.NotEqual(hostWorld, homeWorld);
        // Both interiors share their coordinates: a pose from the friend's ship (its open extension, say) lies a few
        // blocks from the own heal tank — inside the spawn gate's radius, but outside the own hull.
        var stale = new Vector3f(home.X, home.Y, home.Z - 30f);

        // A current client stamps the pose with the world it was taken in — the friend's ship, which it has left.
        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new MoveIntent { X = stale.X, Y = stale.Y, Z = stale.Z, WorldId = hostWorld }));
        Assert.Equal(home, p.Position);
        // An older client sends no world: just after the switch a pose from outside the hull is taken as stale too.
        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new MoveIntent { X = stale.X, Y = stale.Y, Z = stale.Z }));
        Assert.Equal(home, p.Position);
        d.Server.Tick(0.1);
        Assert.False(p.InEva);
        Assert.False(d.Server.InSpace("Guest"));
        Assert.True(d.Server.InShipInterior("Guest"));

        // A moment later the real walk out of the own hatch still starts a spacewalk.
        d.Server.Tick(2.0);
        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new MoveIntent { X = stale.X, Y = stale.Y, Z = stale.Z, WorldId = homeWorld }));
        d.Server.Tick(0.1);
        Assert.True(p.InEva);
        Assert.True(d.Server.InSpace("Guest"));
    }

    [Fact]
    public void TheHostHearsAFriendWantingToComeOver_AtMostEveryFewSeconds()
    {
        var d = TwoDockedShips("visit_ask", hostInside: false);

        d.Server.VisitShip("Guest", "Host");
        d.Server.VisitShip("Guest", "Host");
        d.Server.VisitShip("Guest", "Host");
        Assert.Equal(3, To<ActionRejected>(d.T, d.Guest).Count(r => r.Reason == "@srv.visit.host_flying"));
        Assert.Single(To<ServerMessage>(d.T, d.Host), m => m.Text == "@srv.visit.host_flying_notice:Guest");

        d.Server.Tick(11.0);
        d.Server.VisitShip("Guest", "Host");
        Assert.Equal(2, To<ServerMessage>(d.T, d.Host).Count(m => m.Text == "@srv.visit.host_flying_notice:Guest"));
    }

    [Fact]
    public void BothVisitingEachOtherAtOnce_EndsTogetherInOneShip()
    {
        var d = TwoDockedShips("visit_both");
        d.Server.EnterShipInterior("Guest"); // both walk their own ships

        d.Server.VisitShip("Guest", "Host");
        d.Server.VisitShip("Host", "Guest"); // pressed at the same moment

        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));
        Assert.False(d.Server.IsVisitingShip("Host"));
        Assert.True(Refused(d.T, d.Host, "visit", "@srv.visit.already_here:Guest"));
        Assert.Equal("shipint:Host", d.Host.CurrentLocationId);
        Assert.Equal("shipint:Host", d.Guest.CurrentLocationId);
    }

    [Theory]
    [InlineData("srv.visit.already_here", true)]
    [InlineData("srv.visit.arrived", true)]
    [InlineData("srv.visit.back_home", false)]
    [InlineData("srv.visit.go_home_first", false)]
    [InlineData("srv.visit.guest_arrived", true)]
    [InlineData("srv.visit.guest_left", true)]
    [InlineData("srv.visit.host_flying", false)]
    [InlineData("srv.visit.host_flying_notice", true)]
    [InlineData("srv.visit.host_left", true)]
    [InlineData("srv.visit.host_moving", true)]
    [InlineData("srv.visit.look_only", false)]
    [InlineData("srv.visit.not_docked", false)]
    [InlineData("srv.visit.sent_home", true)]
    [InlineData("srv.visit.space_only", false)]
    [InlineData("srv.visit.too_far", false)]
    [InlineData("srv.visit.undocked", false)]
    public void EveryVisitLine_ExistsInEnglishAndGerman_WithTheNameWhereItIsSent(string key, bool named)
    {
        foreach (string language in new[] { "en", "de" })
        {
            var table = TestLocales.Load(language);
            Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{language}: {key} is missing");
            Assert.Equal(named, text!.Contains("{name}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheVisitIntent_TravelsTheWire_AndAGuestCannotTravelAwayWithoutGoingHome()
    {
        var d = TwoDockedShips("visit_wire");

        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new VisitShipIntent { HostId = "Host" }));
        Assert.Equal("Host", d.Server.VisitedHostOf("Guest"));

        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new TravelIntent { DestinationBodyId = d.Instance.Substring("space:".Length) }));
        Assert.True(Refused(d.T, d.Guest, "travel", "@srv.visit.go_home_first"));
        Assert.True(d.Server.IsVisitingShip("Guest"));

        // The pause menu's "Back to my ship" is the way home while visiting.
        d.Server.HandlePayloadForTest(d.Guest.ConnectionId, NetCodec.Encode(new ReturnToShipIntent()));
        AssertGuestHome(d);
    }
}
