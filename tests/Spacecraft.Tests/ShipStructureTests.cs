using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Primitives;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>The starter ship is a real, indestructible structure on the planet (M23a).</summary>
public sealed class ShipStructureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipStructureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_ship_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(bool placeShip, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, placeShip ? "withship" : "noship"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = placeShip ? "withship" : "noship", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = placeShip };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host"); // ships are per-player now — a player must exist for one to be stamped
        return server;
    }

    [Fact]
    public void ShipHull_IsStamped_AndSolid()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock;
            // The floor cell at the anchor is solid ship structure.
            Assert.False(server.World.GetBlock(a).IsAir);
            Assert.True(server.IsProtectedShipBlock(a.X, a.Y, a.Z));
        }
    }

    [Fact]
    public void ShipKeepOut_ClearsTreesOnAndAboveTheHull()
    {
        // B31: vegetation in the ship's footprint (and just above/around it) is cleared, so trees never end up
        // standing on or poking through the landed hull.
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "keepout"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "keepout", Seed = 1, StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true,
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Host");

            var a = server.ShipAnchorBlock;
            var log = _content.GetBlock("wood_log")!.NumericId;
            var leaves = _content.GetBlock("tree_leaves")!.NumericId;

            // Plant a tree right on the ship: a trunk in the cabin footprint + leaves above the roof.
            var trunk = new Spacecraft.Shared.Geometry.Vector3i(a.X, a.Y + 1, a.Z);
            var crown = new Spacecraft.Shared.Geometry.Vector3i(a.X, a.Y + 6, a.Z);
            server.World.SetBlock(trunk, log);
            server.World.SetBlock(crown, leaves);

            server.ClearShipKeepOutForTest(a.X, a.Z, 2, 2, a.Y, 3);

            Assert.True(server.World.GetBlock(trunk).IsAir, "a trunk in the hull footprint must be cleared");
            Assert.True(server.World.GetBlock(crown).IsAir, "leaves above the roof must be cleared");
        }
    }

    [Fact]
    public void Ship_LandsOnDryLand_NotInWater()
    {
        // B36: on a watery world (seas + scattered upland ponds) the landing search must put the ship on DRY
        // land, never in a pond/lake. Jungle has both water and dry ground, so the chosen pad should be dry.
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "wet_world"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "wet_world", Seed = 1, StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true,
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            server.AddLocalPlayer("Host");

            Assert.True(server.LandingPadIsDry("Host"),
                "the ship's landing pad must sit on dry land, never in a sea or upland pond (B36)");
        }
    }

    [Fact]
    public void ShipInterior_IsHollow_NoTerrainIntrusion()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock;

            // The interior above the floor at the ship centre is cleared to air (no terrain/flora
            // intruding through the hull), while the floor below stays solid.
            Assert.True(server.World.GetBlock(new Spacecraft.Shared.Geometry.Vector3i(a.X, a.Y + 1, a.Z)).IsAir);
            Assert.False(server.World.GetBlock(a).IsAir);
        }
    }

    [Fact]
    public void ShipHatchDoor_SitsAtCabinFloor_NotOnTheRoof()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock; // (cx, y0 floor, cz)
            // The ship's hatch door (the energy door nearest the ship anchor) must sit at cabin-floor level —
            // one block above the floor anchor — never up at the hull roof (B27).
            var hatch = server.DoorSnapshots
                .Where(d => (d.Kind == "energy" || d.Kind == "slide")
                            && System.Math.Abs(d.Pos.X - a.X) < 6f && System.Math.Abs(d.Pos.Z - a.Z) < 8f)
                .OrderBy(d => System.Math.Abs(d.Pos.X - a.X) + System.Math.Abs(d.Pos.Z - a.Z))
                .FirstOrDefault();

            Assert.NotEqual(0, hatch.Id); // a ship hatch door exists
            Assert.InRange(hatch.Pos.Y - a.Y, 0.5f, 1.5f); // ~floor level, not the roof
        }
    }

    [Fact]
    public void ShipHatch_IsAnEnergyDoor_CentredOnTheHull()
    {
        // Item 35: the box ship's outer hatch is an energy door, and its 3-wide opening is centred on the hull
        // (cx+0.5), not half a block off-centre like the old 2-wide gap.
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock;
            var hatch = server.DoorSnapshots
                .Where(d => System.Math.Abs(d.Pos.X - a.X) < 6f && System.Math.Abs(d.Pos.Z - a.Z) < 8f)
                .OrderBy(d => System.Math.Abs(d.Pos.Z - a.Z))
                .FirstOrDefault();

            Assert.NotEqual(0, hatch.Id);
            Assert.Equal("energy", hatch.Kind);                         // the hatch is an energy door
            Assert.InRange(hatch.Pos.X - a.X, 0.25f, 0.75f);            // centred on the hull (cx+0.5), not cx-0.5
        }
    }

    [Fact]
    public void TwoPlayers_GetSeparateShips_AtDistinctStartPoints()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            // 'Host' was added by the helper; add a second player on the same planet.
            server.AddLocalPlayer("Mate");

            var hostAnchor = server.ShipAnchorOf("Host");
            var mateAnchor = server.ShipAnchorOf("Mate");

            // Each player's ship is stamped at its own anchor — they don't share a start point.
            Assert.NotEqual(default, hostAnchor);
            Assert.NotEqual(default, mateAnchor);
            Assert.NotEqual(hostAnchor, mateAnchor);

            // Both ships are mining-protected (each player's hull, not just the last-stamped one).
            Assert.True(server.IsProtectedShipBlock(hostAnchor.X, hostAnchor.Y, hostAnchor.Z));
            Assert.True(server.IsProtectedShipBlock(mateAnchor.X, mateAnchor.Y, mateAnchor.Z));
        }
    }

    [Fact]
    public void ShipHull_IsMiningProtected()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock;

            // A player standing inside the ship tries to mine the floor under them.
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.Position = new Spacecraft.Shared.Geometry.Vector3f(a.X + 0.5f, a.Y + 2f, a.Z + 0.5f);

            server.MineBlock("Pilot", a.X, a.Y, a.Z);

            Assert.False(server.World.GetBlock(a).IsAir); // hull survives
        }
    }

    [Fact]
    public void HealTank_HealsPlayer_WhenStandingAtIt()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var tank = server.StationPosition("medbay")!.Value;
            pilot.State.Position = tank; // standing at the heal-tank
            pilot.State.Health = 40f;
            pilot.State.AboardShip = true;

            server.UseStation("Pilot", "medbay");

            Assert.Equal(100f, pilot.State.Health);
        }
    }

    [Fact]
    public void Station_RejectsWhenTooFar()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            pilot.State.Health = 40f;
            pilot.State.Position = new Spacecraft.Shared.Geometry.Vector3f(1000, 64, 1000); // far away

            server.UseStation("Pilot", "medbay");

            Assert.Equal(40f, pilot.State.Health); // not healed
        }
    }

    [Fact]
    public void Quarters_SetsRespawnPoint()
    {
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var quarters = server.StationPosition("quarters")!.Value;
            pilot.State.Position = quarters;
            pilot.State.AboardShip = true;

            server.UseStation("Pilot", "quarters");

            Assert.Equal(quarters, pilot.State.RespawnPoint);
        }
    }

    [Fact]
    public void NoShip_WhenDisabled()
    {
        var server = Started(placeShip: false, out var repo);
        using (repo)
        {
            Assert.False(server.IsProtectedShipBlock(0, 64, 0));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
