using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Task 5 Stage 3b — storage crate: a placed crate is a persistent container you can stash loose materials
/// into (tools/weapons stay with you) and take back, and mining it returns its stored contents.
/// </summary>
public sealed class CrateStorageTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrateStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crate_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "crate"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "crate", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static SvGameServer WithBuilder(SvGameServer server, out BlocksBeyondTheStars.Shared.State.PlayerState state)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cell is empty
        p.State.Inventory.Add("crate", 1, 99);
        p.State.Inventory.Add("iron_ore", 10, 99);
        p.State.Inventory.Add("machete", 1, 1); // a tool — must NOT be stashed
        state = p.State;
        return server;
    }

    [Fact]
    public void Crate_StashesMaterials_ButNotTools_AndGivesThemBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            WithBuilder(server, out var p);

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.FirstOrDefault(c => c.Kind == "crate");
            Assert.NotNull(crate);

            int machetesBefore = p.Inventory.CountOf("machete"); // (the starter kit already carries one)
            server.DepositToContainer("Builder", crate!.Id);
            Assert.Equal(0, p.Inventory.CountOf("iron_ore"));            // loose material stashed
            Assert.Equal(machetesBefore, p.Inventory.CountOf("machete")); // tools are never stashed
            Assert.Contains(server.Containers.First(c => c.Id == crate.Id).Items, s => s.Item == "iron_ore" && s.Count == 10);

            server.LootContainer("Builder", crate.Id);        // take it back out (G)
            Assert.Equal(10, p.Inventory.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void MiningACrate_ReturnsItsStoredContents()
    {
        var server = Started(out var repo);
        using (repo)
        {
            WithBuilder(server, out var p);
            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");
            server.DepositToContainer("Builder", crate.Id);
            Assert.Equal(0, p.Inventory.CountOf("iron_ore"));

            server.MineBlock("Builder", 1, 200, 0); // break the crate

            Assert.DoesNotContain(server.Containers, c => c.Id == crate.Id); // container gone
            Assert.Equal(10, p.Inventory.CountOf("iron_ore"));               // contents returned
            Assert.Equal(1, p.Inventory.CountOf("crate"));                   // and the crate block drops back
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
