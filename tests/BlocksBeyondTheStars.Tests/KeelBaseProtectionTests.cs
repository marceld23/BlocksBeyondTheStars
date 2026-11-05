// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3198: a ship construction site grows under the protections of hand placement. Only the keel used to pass them — the
/// hull could grow into another player's base zone (whose owner then could not build there any more) and through walls
/// and terrain. #3221: so does a commissioned self-built ship its owner keeps building on while it is parked — while the
/// room inside its own box (and its own landing pad) stays free to furnish.
/// </summary>
public sealed class KeelBaseProtectionTests : IDisposable
{
    private const string Yard = "shipyard:Pilot";

    private readonly string _root;
    private readonly GameContent _content;

    public KeelBaseProtectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_keelbase_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo, string world = "keelbase",
        bool parkShips = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = parkShips, // #3221: a landing parks the ship on a pad only where ships are parked at all
            World = { TerrainGeneration = 0 }, // the keel goes down at a fixed column (see CustomShipTests)
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    /// <summary>The pilot lays a keel at (40, ground+1, 40) and grows a post one cell up from it — the hull row the tests
    /// extend runs one cell above the keel, through a cleared yard. Returns the keel's world cell.</summary>
    private static Vector3i KeelWithPost(SvGameServer server, PlayerSession pilot)
    {
        var core = new Vector3i(40, SurfaceY(server, 40, 40) + 1, 40);
        for (int x = core.X; x <= core.X + 8; x++)
        {
            server.World.SetBlock(new Vector3i(x, core.Y + 1, core.Z), BlockId.Air); // whatever grows there, it is not the subject
        }

        pilot.State.InstantBuild = true;
        pilot.State.Position = new Vector3f(core.X + 2.5f, core.Y + 1.5f, core.Z + 0.5f);
        server.PlaceShipCoreForTest("Pilot", core.X, core.Y, core.Z);
        Edit(server, 0, 1, 0);
        return core;
    }

    private static void Edit(SvGameServer server, int x, int y, int z)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent
        {
            StructureId = Yard,
            X = x,
            Y = y,
            Z = z,
            Mine = false,
            ItemKey = "iron_wall",
        });

    /// <summary>Grows the hull row at structure height 1 from x = 1 to <paramref name="toX"/>; returns how far it got.</summary>
    private static int GrowRow(SvGameServer server, int toX)
    {
        for (int x = 1; x <= toX; x++)
        {
            Edit(server, x, 1, 0);
            if (server.ConstructionBoundsForTest("Pilot")!.Value.Size.X != x + 1)
            {
                return x - 1;
            }
        }

        return toX;
    }

    /// <summary>Founds a base for <paramref name="owner"/> whose zone starts three cells east of the keel.</summary>
    private static void BaseEastOfKeel(SvGameServer server, PlayerSession owner, Vector3i keel)
        => server.PlaceBaseForTest(owner, new Vector3i(keel.X + 3 + WorldConstants.BaseZoneRadius, keel.Y + 1, keel.Z));

    [Fact]
    public void AHullCell_InAForeignBaseZone_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var neighbour = server.AddLocalPlayer("Neighbour");
            var pilot = server.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(server, pilot);
            BaseEastOfKeel(server, neighbour, keel);

            int reached = GrowRow(server, 6);

            Assert.Equal(2, reached); // local x 3 is world x keel+3, the first cell of the neighbour's zone
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.protect.base");
            Assert.True(server.BaseProtectedForTest(new Vector3i(keel.X + 3, keel.Y + 1, keel.Z), "Pilot"));
        }
    }

    [Fact]
    public void AHullCell_InTheBuildersOwnBase_OrAnAllysBase_IsAllowed()
    {
        var server = Started(new RecordingTransport(), out var repo);
        using (repo)
        {
            var ally = server.AddLocalPlayer("Ally");
            var pilot = server.AddLocalPlayer("Pilot");
            server.RequestAllianceForTest("Ally", "Pilot");
            server.RespondAllianceForTest("Pilot", "Ally", accept: true);
            Assert.True(server.AreAllied("Ally", "Pilot"));
            var keel = KeelWithPost(server, pilot);
            BaseEastOfKeel(server, ally, keel);

            Assert.Equal(6, GrowRow(server, 6)); // the ally's zone is open to the pilot, as for a hand-placed block
        }

        var own = Started(new RecordingTransport(), out var ownRepo, "keelbase_own");
        using (ownRepo)
        {
            var pilot = own.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(own, pilot);
            BaseEastOfKeel(own, pilot, keel);

            Assert.Equal(6, GrowRow(own, 6)); // and so is the builder's own base
        }
    }

    [Fact]
    public void AHullCell_InsideASolidWorldBlock_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(server, pilot);
            var wall = new Vector3i(keel.X + 2, keel.Y + 1, keel.Z);
            server.World.SetBlock(wall, _content.GetBlock("stone")!.NumericId); // a wall where the row would grow

            Assert.Equal(1, GrowRow(server, 4)); // the hull no longer grows through it
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.place.not_empty");
            Assert.Equal(_content.GetBlock("stone")!.NumericId, server.World.GetBlock(wall));
        }
    }

    // ---------------- #3221: the commissioned ship, parked ----------------

    /// <summary>The pilot builds the 5×4×5 hull of <see cref="CustomShipTests"/> on the keel column and commissions it; the
    /// ship stays parked where it was built. Returns its origin (the keel cell). On foot the hull grows by the layer over its
    /// roof (local y 4) — everything below is inside its own box.</summary>
    private static Vector3i ParkedSelfBuiltShip(SvGameServer server, PlayerSession pilot)
    {
        var core = CustomShipTests.LayKeel(server, pilot);
        var helm = CustomShipTests.BuildValidHull(server);
        pilot.State.Position = new Vector3f(core.X + helm.X + 0.5f, core.Y + helm.Y, core.Z + helm.Z + 0.5f);
        server.CommissionShipForTest("Pilot");
        Assert.True(pilot.Ships.Values.Single(s => s.IsCustom).Commissioned);
        Assert.Equal((core, new Vector3i(5, 4, 5)), server.LandedShipBoundsForTest("Pilot"));
        Assert.False(pilot.State.IsAdmin); // the world's first joiner is its admin, who passes every protection
        return core;
    }

    private static void EditShip(SvGameServer server, int x, int y, int z, bool mine = false)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent
        {
            StructureId = "ship:Pilot",
            X = x,
            Y = y,
            Z = z,
            Mine = mine,
            ItemKey = "iron_wall",
        });

    /// <summary>Whether the parked ship's design holds a block at structure-local (x, y, z).</summary>
    private static bool HullHas(PlayerSession pilot, int x, int y, int z)
        => pilot.Ships.Values.Single(s => s.IsCustom).BuiltCells.Split(';')
            .Any(c => c.StartsWith($"{x}:{y}:{z}:", StringComparison.Ordinal));

    /// <summary>Founds a base for <paramref name="owner"/> whose zone starts at the parked ship's local x 3 and reaches over
    /// its roof: the cabin cell (3, 2, 3) and the roof-top cell (3, 4, 3) lie in it, the corner (0, 4, 0) does not.</summary>
    private static void BaseOverTheShipsEastSide(SvGameServer server, PlayerSession owner, Vector3i origin)
        => server.PlaceBaseForTest(owner, new Vector3i(origin.X + 3 + WorldConstants.BaseZoneRadius, origin.Y + 2, origin.Z + 2));

    [Fact]
    public void AParkedShip_GrowingIntoAForeignBaseZone_IsRefused_WhileItsOwnBoxStaysFree()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo, "parked_foreign");
        using (repo)
        {
            var neighbour = server.AddLocalPlayer("Neighbour");
            var pilot = server.AddLocalPlayer("Pilot");
            var origin = ParkedSelfBuiltShip(server, pilot);
            BaseOverTheShipsEastSide(server, neighbour, origin);
            Assert.True(server.BaseProtectedForTest(new Vector3i(origin.X + 3, origin.Y + 4, origin.Z + 3), "Pilot"));

            EditShip(server, 3, 2, 3); // furnishing the cabin: the ship already holds that cell, zone or not
            Assert.True(HullHas(pilot, 3, 2, 3));

            EditShip(server, 3, 4, 3); // over the roof, into the neighbour's zone
            Assert.False(HullHas(pilot, 3, 4, 3));
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.protect.base");
            Assert.Equal(new Vector3i(5, 4, 5), server.LandedShipBoundsForTest("Pilot").Size);

            EditShip(server, 0, 4, 0); // over the roof outside the zone — the ship is not in its own way
            Assert.True(HullHas(pilot, 0, 4, 0));
            Assert.Equal(new Vector3i(5, 5, 5), server.LandedShipBoundsForTest("Pilot").Size);

            EditShip(server, 3, 2, 3, mine: true); // mining stays allowed, in the zone too
            Assert.False(HullHas(pilot, 3, 2, 3));
            Assert.DoesNotContain(transport.To<ActionRejected>(pilot), r => r.Reason == "@srv.place.no_ship_interior");
        }
    }

    [Fact]
    public void AParkedShip_GrowsIntoItsOwnersBase_AndAnAllysBase()
    {
        var server = Started(new RecordingTransport(), out var repo, "parked_ally");
        using (repo)
        {
            var ally = server.AddLocalPlayer("Ally");
            var pilot = server.AddLocalPlayer("Pilot");
            server.RequestAllianceForTest("Ally", "Pilot");
            server.RespondAllianceForTest("Pilot", "Ally", accept: true);
            Assert.True(server.AreAllied("Ally", "Pilot"));
            var origin = ParkedSelfBuiltShip(server, pilot);
            BaseOverTheShipsEastSide(server, ally, origin);

            EditShip(server, 3, 4, 3);
            Assert.True(HullHas(pilot, 3, 4, 3)); // the ally's zone is open to the pilot, as for a hand-placed block
        }

        var own = Started(new RecordingTransport(), out var ownRepo, "parked_own");
        using (ownRepo)
        {
            own.AddLocalPlayer("Host");
            var pilot = own.AddLocalPlayer("Pilot");
            var origin = ParkedSelfBuiltShip(own, pilot);
            BaseOverTheShipsEastSide(own, pilot, origin);

            EditShip(own, 3, 4, 3);
            Assert.True(HullHas(pilot, 3, 4, 3)); // and so is the pilot's own base
        }
    }

    [Fact]
    public void AParkedShip_GrowingThroughASolidWorldBlock_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo, "parked_solid");
        using (repo)
        {
            server.AddLocalPlayer("Host");
            var pilot = server.AddLocalPlayer("Pilot");
            var origin = ParkedSelfBuiltShip(server, pilot);
            var stone = _content.GetBlock("stone")!.NumericId;
            var rock = new Vector3i(origin.X + 2, origin.Y + 4, origin.Z + 2);
            server.World.SetBlock(rock, stone); // an overhang right over the roof

            EditShip(server, 2, 4, 2);

            Assert.False(HullHas(pilot, 2, 4, 2));
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.place.not_empty");
            Assert.Equal(stone, server.World.GetBlock(rock));
        }
    }

    /// <summary>A ship parked on a landing pad stands in the pad's landing volume, which nobody may build in. That volume is
    /// its own: the cabin is furnished and the hull grows taller over its own footprint as before.</summary>
    [Fact]
    public void AParkedShip_OnALandingPad_IsFurnishedAndGrownOverItsOwnFootprint()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo, "parked_pad", parkShips: true);
        using (repo)
        {
            server.AddLocalPlayer("Host");
            var pilot = server.AddLocalPlayer("Pilot");
            ParkedSelfBuiltShip(server, pilot);
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));
            server.LandOnCurrentBodyForTest(pilot);
            Assert.False(server.InSpace("Pilot"));
            var (origin, size) = server.LandedShipBoundsForTest("Pilot");
            Assert.Equal(new Vector3i(5, 4, 5), size);
            var pad = server.LandingPadForTest(pilot.AssignedPadIndex);
            Assert.Equal((pad.X - 2, pad.Z - 2), (origin.X, origin.Z)); // centred on the pad …
            Assert.True(server.IsOnLandingPadForTest(origin.X + 2, origin.Z + 2)); // … whose landing volume holds the hull
            pilot.State.Position = new Vector3f(origin.X + 2.5f, origin.Y + 1f, origin.Z + 1.5f);

            EditShip(server, 3, 2, 3);
            EditShip(server, 2, 4, 2);

            Assert.True(HullHas(pilot, 3, 2, 3));
            Assert.True(HullHas(pilot, 2, 4, 2));
            Assert.DoesNotContain(transport.To<ActionRejected>(pilot), r => r.Action == "structure");
        }
    }

    // ---------------- #3221: an authored ship, grown past its design box (#2120) ----------------

    /// <summary>The pilot's authored starter ship as it stands parked on their landing pad, the pilot building for free beside
    /// it. Returns its origin, its size and a cell just past its west side (local x −1) the ship may grow into: beside a hull
    /// cell above the floor, cleared to open air, inside the pad's landing volume.</summary>
    private static (Vector3i Origin, Vector3i Size, Vector3i Extension) ParkedStarterShip(SvGameServer server, PlayerSession pilot)
    {
        Assert.False(pilot.State.IsAdmin); // the world's first joiner is its admin, who passes every protection
        Assert.DoesNotContain(pilot.Ships.Values, s => s.IsCustom);
        pilot.State.InstantBuild = true;
        var (origin, size) = server.LandedShipBoundsForTest("Pilot");
        var design = server.BuildShipStructureForTest("Pilot");
        for (int y = 1; y < size.Y; y++)
        {
            for (int z = 0; z < size.Z; z++)
            {
                if (design.Get(new Vector3i(0, y, z)).IsAir)
                {
                    continue;
                }

                server.World.SetBlock(WorldAt(server, origin, new Vector3i(-1, y, z)), BlockId.Air);
                pilot.State.Position = new Vector3f(origin.X + 0.5f, origin.Y + y, origin.Z + z + 0.5f);
                Assert.True(server.IsOnLandingPadForTest(origin.X - 1, origin.Z + z)); // the pad the ship stands on
                return (origin, size, new Vector3i(-1, y, z));
            }
        }

        throw new InvalidOperationException("the starter design has no wall cell at local x 0");
    }

    private static Vector3i WorldAt(SvGameServer server, Vector3i origin, Vector3i local)
        => WorldConstants.CanonicalBlock(
            new Vector3i(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z), server.World.Circumference);

    /// <summary>Whether the pilot's ship (its design plus every persisted edit) holds a block at structure-local
    /// <paramref name="local"/>.</summary>
    private static bool ShipHas(SvGameServer server, Vector3i local)
        => !server.BuildShipStructureForTest("Pilot").Get(local).IsAir;

    private static void EditShip(SvGameServer server, Vector3i local) => EditShip(server, local.X, local.Y, local.Z);

    /// <summary>Founds a base for <paramref name="owner"/> whose zone ends at the ship's west side: it holds the extension
    /// cell (local x −1) and none of the design box.</summary>
    private static void BaseWestOfTheShip(SvGameServer server, PlayerSession owner, Vector3i origin, Vector3i extension)
        => server.PlaceBaseForTest(owner, WorldAt(server, origin,
            new Vector3i(-1 - WorldConstants.BaseZoneRadius, extension.Y, extension.Z)));

    [Fact]
    public void AnAuthoredShip_ExtendedIntoAForeignBaseZone_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo, "authored_foreign", parkShips: true);
        using (repo)
        {
            var neighbour = server.AddLocalPlayer("Neighbour");
            var pilot = server.AddLocalPlayer("Pilot");
            var (origin, _, extension) = ParkedStarterShip(server, pilot);
            BaseWestOfTheShip(server, neighbour, origin, extension);
            Assert.True(server.BaseProtectedForTest(WorldAt(server, origin, extension), "Pilot"));

            EditShip(server, extension);

            Assert.False(ShipHas(server, extension));
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.protect.base");
        }
    }

    [Fact]
    public void AnAuthoredShip_ExtendsIntoItsOwnersBase_AndAnAllysBase()
    {
        var server = Started(new RecordingTransport(), out var repo, "authored_ally", parkShips: true);
        using (repo)
        {
            var ally = server.AddLocalPlayer("Ally");
            var pilot = server.AddLocalPlayer("Pilot");
            server.RequestAllianceForTest("Ally", "Pilot");
            server.RespondAllianceForTest("Pilot", "Ally", accept: true);
            Assert.True(server.AreAllied("Ally", "Pilot"));
            var (origin, _, extension) = ParkedStarterShip(server, pilot);
            BaseWestOfTheShip(server, ally, origin, extension);

            EditShip(server, extension);
            Assert.True(ShipHas(server, extension)); // the ally's zone is open to the pilot, as for a hand-placed block
        }

        var own = Started(new RecordingTransport(), out var ownRepo, "authored_own", parkShips: true);
        using (ownRepo)
        {
            own.AddLocalPlayer("Host");
            var pilot = own.AddLocalPlayer("Pilot");
            var (origin, _, extension) = ParkedStarterShip(own, pilot);
            BaseWestOfTheShip(own, pilot, origin, extension);

            EditShip(own, extension);
            Assert.True(ShipHas(own, extension)); // and so is the pilot's own base
        }
    }

    /// <summary>The landing volume of the pad an authored ship is parked on is its own: the cabin is furnished, the ship
    /// grows past its side and over its roof as before (#2120) — for a player who is no admin too.</summary>
    [Fact]
    public void AnAuthoredShip_OnItsOwnPad_IsFurnishedAndExtended()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo, "authored_pad", parkShips: true);
        using (repo)
        {
            server.AddLocalPlayer("Host");
            var pilot = server.AddLocalPlayer("Pilot");
            var (_, size, extension) = ParkedStarterShip(server, pilot);
            var design = server.BuildShipStructureForTest("Pilot");
            var cabin = CabinFloorCell(design, size);
            var roof = RoofCell(design, size);

            EditShip(server, cabin);
            EditShip(server, extension);
            EditShip(server, roof);

            Assert.DoesNotContain(transport.To<ActionRejected>(pilot), r => r.Action == "structure");
            Assert.True(ShipHas(server, cabin));
            Assert.True(ShipHas(server, extension));
            Assert.True(ShipHas(server, roof));
        }
    }

    /// <summary>A free cell on the cabin floor: air inside the design box over a hull cell, away from every doorway.</summary>
    private static Vector3i CabinFloorCell(SpaceStructure design, Vector3i size)
    {
        for (int y = 1; y < size.Y - 1; y++)
            for (int x = 1; x < size.X - 1; x++)
                for (int z = 1; z < size.Z - 1; z++)
                {
                    var c = new Vector3i(x, y, z);
                    if (design.Get(c).IsAir && !design.Get(new Vector3i(x, y - 1, z)).IsAir
                        && !design.DoorCells.Any(d => Math.Abs(d.X - x) <= 1 && Math.Abs(d.Z - z) <= 1))
                    {
                        return c;
                    }
                }

        throw new InvalidOperationException("the starter design has no free cabin floor");
    }

    /// <summary>The cell over the first roof cell — on the layer that grows the ship's box.</summary>
    private static Vector3i RoofCell(SpaceStructure design, Vector3i size)
    {
        for (int x = 0; x < size.X; x++)
            for (int z = 0; z < size.Z; z++)
            {
                if (!design.Get(new Vector3i(x, size.Y - 1, z)).IsAir)
                {
                    return new Vector3i(x, size.Y, z);
                }
            }

        throw new InvalidOperationException("the starter design has no roof");
    }
}
