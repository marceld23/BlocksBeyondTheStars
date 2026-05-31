using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Flowing fluids: water/lava spread down + sideways with level decay (World systems).</summary>
public sealed class FluidTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FluidTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_fluid_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fluid"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fluid", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void WaterAndLava_BlocksExist()
    {
        Assert.True(_content.Blocks.ContainsKey("water"));
        Assert.True(_content.Blocks.ContainsKey("lava"));
        Assert.False(_content.GetBlock("water")!.Mineable);
    }

    [Fact]
    public void Water_FlowsDownIntoAir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // High in the air column above the surface = guaranteed empty cells.
            var src = new Vector3i(0, 130, 0);
            var below = new Vector3i(0, 129, 0);
            Assert.True(server.World.GetBlock(below).IsAir);

            server.PlaceFluidSource("water", src.X, src.Y, src.Z);
            server.Tick(0.3); // > fluid interval → one flow step

            Assert.False(server.World.GetBlock(below).IsAir); // water fell down
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(below).Value);
        }
    }

    [Fact]
    public void Water_SpreadsSidewaysOnFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Build a small solid floor in the air, place a source on it, let it spread.
            var stone = _content.GetBlock("stone")!.NumericId;
            int y = 130;
            for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, y - 1, z), stone);
            }

            server.PlaceFluidSource("water", 0, y, 0);
            for (int i = 0; i < 6; i++)
            {
                server.Tick(0.3); // several flow steps
            }

            // Water should have reached a side cell on the floor.
            var side = new Vector3i(2, y, 0);
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(side).Value);
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
