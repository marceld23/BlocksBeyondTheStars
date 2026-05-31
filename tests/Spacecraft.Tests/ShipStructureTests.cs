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
