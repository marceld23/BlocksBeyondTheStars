using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Singleplayer "Creative" world options: unlock-all blueprints, own all ships, a curated starter kit —
/// persisted per world (reapplied on every load) while survival mechanics stay on. All-off = "Explorer".</summary>
public sealed class CreativeModeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreativeModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_creative_" + System.Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string name, out SqliteWorldRepository repo, bool creative)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true,
            CreativeUnlockAllBlueprints = creative,
            CreativeStartAllShips = creative,
            CreativeStarterKit = creative,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void CreativeWorld_UnlocksAllBlueprints_OwnsAllShips_AndGrantsKit()
    {
        var server = Start("creative", out var repo, creative: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");

            // Every blueprint is unlocked.
            Assert.Equal(_content.Blueprints.Count, p.State.UnlockedBlueprints.Count);
            Assert.True(_content.Blueprints.Count > 0);

            // Every ship type is owned (starter + the three unlockables).
            var types = server.OwnedShips.Values.Select(s => s.ShipType).ToHashSet();
            Assert.Contains("starter", types);
            Assert.Contains("hauler", types);
            Assert.Contains("scout", types);
            Assert.Contains("corvette", types);

            // The curated kit was granted (a generous stack of a key material reached the inventory).
            Assert.True(p.State.Inventory.CountOf("iron_ore") > 0, "the creative kit should grant materials");
        }
    }

    [Fact]
    public void ExplorerWorld_GrantsNothingExtra()
    {
        var server = Start("explorer", out var repo, creative: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Host");

            Assert.Empty(p.State.UnlockedBlueprints);                                  // nothing unlocked
            var types = server.OwnedShips.Values.Select(s => s.ShipType).ToHashSet();
            Assert.DoesNotContain("hauler", types);                                    // only the starter
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));                    // no creative kit
        }
    }

    [Fact]
    public void CreativeOptions_PersistAcrossRestart_AndKitIsGrantedOnce()
    {
        // Create the world Creative, note the kit amount, then reopen the SAME save with config flags OFF —
        // the persisted world options still apply (unlock-all + all-ships), and the one-time kit isn't refilled.
        int ironAfterFirst;
        {
            var s1 = Start("persist", out var repo1, creative: true);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Host");
                ironAfterFirst = p.State.Inventory.CountOf("iron_ore");
                Assert.True(ironAfterFirst > 0);
                repo1.Flush();
            }
        }

        // Fresh server, same save dir, but config says NOT creative — the world's saved options win.
        var s2 = Start("persist", out var repo2, creative: false);
        using (repo2)
        {
            var p = s2.AddLocalPlayer("Host");
            Assert.Equal(_content.Blueprints.Count, p.State.UnlockedBlueprints.Count); // still all unlocked
            Assert.Contains("corvette", s2.OwnedShips.Values.Select(s => s.ShipType)); // still owns all ships
            Assert.Equal(ironAfterFirst, p.State.Inventory.CountOf("iron_ore"));       // kit NOT granted again
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
