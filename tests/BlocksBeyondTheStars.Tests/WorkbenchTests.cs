using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Task 5 Stage 3 — buildable world objects: a placed workbench enables workshop crafting (and a forge,
/// refinery crafting) on a world without being aboard the ship, so players can set up a base.
/// </summary>
public sealed class WorkbenchTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorkbenchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bench_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "bench"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "bench", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Workbench_EnablesWorkshopCrafting_OnAWorld_WithoutTheShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;          // standing on a world, not aboard the ship
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("iron_ore", 4, 99);

            // No workbench nearby → a workshop recipe can't be crafted off-ship.
            server.Craft("Builder", "iron_ingot");
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ingot"));

            // Place a workbench next to the player → the workshop is now available here.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("workbench")!.NumericId);
            server.Craft("Builder", "iron_ingot");
            Assert.Equal(1, p.State.Inventory.CountOf("iron_ingot"));
        }
    }

    [Fact]
    public void Forge_EnablesRefineryCrafting_OnAWorld()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Smith");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("titanium_ore", 2, 99);

            // titanium_plate is a refinery recipe — blocked without a forge here.
            server.Craft("Smith", "titanium_plate");
            Assert.Equal(0, p.State.Inventory.CountOf("titanium_plate"));

            server.World.SetBlock(new Vector3i(0, 64, 2), _content.GetBlock("forge")!.NumericId);
            server.Craft("Smith", "titanium_plate");
            Assert.Equal(1, p.State.Inventory.CountOf("titanium_plate"));
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
