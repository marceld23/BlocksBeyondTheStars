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
/// Interactions on stamped structures: wreck/ruin loot markers become scavengeable containers
/// (looted via the normal container flow, and never respawning on reload), and a settlement vendor
/// enables market barter while standing next to it.
/// </summary>
public sealed class StructureInteractionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StructureInteractionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_structint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string planet, long seed, bool wrecks, bool settlements, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + "_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = planet + "_" + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceWrecks = wrecks,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- Wreck loot ----------------

    [Fact]
    public void Wreck_SpawnsScavengeableLootContainers()
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Start("rocky", seed, wrecks: true, settlements: false, out var repo);
            if (!server.HasWreck) { repo.Dispose(); continue; }
            using (repo)
            {
                Assert.NotEmpty(server.Containers); // loot/module/terminal markers became containers

                var p = server.AddLocalPlayer("Scavenger");
                var container = server.Containers.First();
                p.State.Position = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
                int before = p.State.Inventory.Slots.Count(s => s is { IsEmpty: false });

                server.LootContainer("Scavenger", container.Id);

                int after = p.State.Inventory.Slots.Count(s => s is { IsEmpty: false });
                Assert.True(after > before, "Looting a wreck cache should add items to the inventory.");
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No wreck found across 60 seeds.");
    }

    [Fact]
    public void StructureLoot_DoesNotRespawn_OnReload()
    {
        // Find a wreck seed, loot all its containers, then restart the same world and confirm the
        // looted caches stay gone (the GeneratedLoot guard prevents re-spawning).
        long wreckSeed = -1;
        for (long seed = 1; seed <= 60; seed++)
        {
            var probe = Start("rocky", seed, wrecks: true, settlements: false, out var probeRepo);
            bool has = probe.HasWreck;
            probeRepo.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            if (has) { wreckSeed = seed; break; }
        }

        Assert.True(wreckSeed > 0, "Expected to find a wreck seed.");

        // First run: loot every container.
        var server = Start("rocky", wreckSeed, wrecks: true, settlements: false, out var repo1);
        var p = server.AddLocalPlayer("Scavenger");
        foreach (var container in server.Containers.ToList())
        {
            p.State.Position = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
            server.LootContainer("Scavenger", container.Id);
        }

        Assert.Empty(server.Containers); // all emptied + removed
        server.Stop();
        repo1.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Reload the same world: the looted caches must not come back.
        var reloaded = Start("rocky", wreckSeed, wrecks: true, settlements: false, out var repo2);
        using (repo2)
        {
            Assert.Empty(reloaded.Containers);
        }
    }

    // ---------------- Settlement vendor → market ----------------

    [Fact]
    public void Market_IsAvailable_NextToASettlementVendor()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = Start("jungle", seed, wrecks: false, settlements: true, out var repo);
            var vendor = server.HasSettlement
                ? server.SettlementMarkers.FirstOrDefault(m => m.Type == "vendor")
                : default;
            if (vendor.Type != "vendor") { repo.Dispose(); continue; }

            using (repo)
            {
                var p = server.AddLocalPlayer("Trader");
                p.State.AboardShip = false;                 // not on the ship — must rely on the vendor
                p.State.Position = vendor.Pos;              // standing at the vendor
                p.State.Inventory.Add("iron_ore", 5, 99);

                server.Craft("Trader", "market_iron_to_titanium"); // market recipe at the vendor

                Assert.Equal(1, p.State.Inventory.CountOf("titanium_ore"));
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No settlement with a vendor found across 40 seeds.");
    }

    [Fact]
    public void Market_NotAvailable_AwayFromShipAndVendor()
    {
        var server = Start("rocky", 1, wrecks: false, settlements: false, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Trader");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(500, 64, 500); // nowhere near ship or a vendor
            p.State.Inventory.Add("iron_ore", 5, 99);

            server.Craft("Trader", "market_iron_to_titanium");

            Assert.Equal(0, p.State.Inventory.CountOf("titanium_ore")); // no market here
            Assert.Equal(5, p.State.Inventory.CountOf("iron_ore"));
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
