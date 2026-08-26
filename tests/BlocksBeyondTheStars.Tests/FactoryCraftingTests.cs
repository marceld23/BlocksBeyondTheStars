// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Factory crafting (Phase 0 foundations): a factory recipe turns cheaper, less-rare raw materials
/// into the same output as a base recipe, but only at a <c>factory_terminal</c> block (off the ship,
/// inside a spawned factory). Factory-made items must not be disassembled back through the cheaper
/// factory recipe (anti-exploit). These tests stand a terminal block up by hand — the spawned factory
/// structures arrive in a later phase.
/// </summary>
public sealed class FactoryCraftingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FactoryCraftingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_factory_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "factory"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "factory",
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // Note: crafting a roster recipe at a real factory terminal is covered end-to-end by
    // FactoryStructureTests (a spawned factory enforces its roster). These tests cover the data layer:
    // station gating without a terminal, factory recipes being unavailable aboard ship, and the
    // disassembly exclusion.

    [Fact]
    public void FactoryRecipe_Unavailable_WithoutTerminal()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Maker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(40.5f, 50.5f, 40.5f); // no terminal nearby
            p.State.Inventory.Add("iron_ore", 6, 99);

            server.Craft("Maker", "factory_iron_plate", 1);

            Assert.Equal(0, p.State.Inventory.CountOf("iron_plate")); // station not available -> no craft
            Assert.Equal(6, p.State.Inventory.CountOf("iron_ore"));   // raw untouched
        }
    }

    [Fact]
    public void FactoryRecipe_NotAvailableAboardShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Aboard ship there is no factory module — factories are world structures, never on a ship.
            var p = server.AddLocalPlayer("Maker"); // aboard by default
            p.State.Inventory.Add("iron_ore", 6, 99);

            server.Craft("Maker", "factory_iron_plate", 1);

            Assert.Equal(0, p.State.Inventory.CountOf("iron_plate"));
        }
    }

    [Fact]
    public void FactoryMadeItem_DisassemblesViaBaseRecipe_NotFactoryRecipe()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // iron_plate has a base workshop recipe (iron_ingot x2) AND a factory recipe (iron_ore x6).
            // Disassembly must pick the base recipe and refund iron_ingot — never the cheaper factory raw.
            var p = server.AddLocalPlayer("Maker"); // aboard, has a workshop module
            p.State.Inventory.Add("iron_plate", 1, 99);

            server.Disassemble("Maker", "iron_plate");

            Assert.True(p.State.Inventory.CountOf("iron_ingot") >= 1); // base recipe inputs recovered
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));    // factory recipe was excluded
        }
    }

    // ---------------- Roster pinning (#1299) ----------------
    // A factory's roster is rolled from the factory recipe set. That set grows with every content release,
    // and the roll walks the whole set — so without a pin, a factory a player claimed for its steel plates
    // would make something else after the next update. The roll is frozen into the factory's placement
    // record at first stamp and wins over every later roll.

    private static long? _cachedFactorySeed;

    private SvGameServer StartFactoryWorld(long seed, string world, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceFactories = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>The first seed that stamps a factory (probed once per test run — the probe is the slow part).</summary>
    private long FirstFactorySeed()
    {
        if (_cachedFactorySeed is { } cached)
        {
            return cached;
        }

        for (long seed = 1; seed <= 80; seed++)
        {
            var s = StartFactoryWorld(seed, "probe" + seed, out var repo);
            using (repo)
            {
                if (s.FactoryCount > 0)
                {
                    _cachedFactorySeed = seed;
                    return seed;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No factory across 80 seeds.");
    }

    /// <summary>The placement record behind the first stamped factory (records are in roll order, so the
    /// first PLACED factory record is <c>FactoriesForTest[0]</c>).</summary>
    private static StructurePlacementRecord FirstFactoryRecord(IEnumerable<StructurePlacementRecord> records)
        => records.Where(r => r.Kind == "factory" && r.Placed).OrderBy(r => r.Index).First();

    /// <summary>Rewrites the persisted metadata of a world between two loads — the way a save from an older
    /// build (or a hand-edited one) reaches the loader.</summary>
    private void EditMetadata(string world, Action<WorldMetadata> edit)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize(); // a bare repository has no connection yet — the server normally does this on Start()
        var meta = repo.LoadMetadata() ?? throw new Xunit.Sdk.XunitException("The first load saved no metadata.");
        edit(meta);
        repo.SaveMetadata(meta);
    }

    private List<string> AllFactoryRecipeKeys()
        => _content.Recipes.Values.Where(r => r.Station == CraftingStation.Factory).Select(r => r.Key).ToList();

    [Fact]
    public void Roster_IsPinnedAtFirstStamp_AndSurvivesAReload()
    {
        long seed = FirstFactorySeed();
        const string world = "roster_reload";

        List<string> first;
        var server = StartFactoryWorld(seed, world, out var repo);
        using (repo)
        {
            first = server.FactoriesForTest[0].Roster.ToList();
            var rec = FirstFactoryRecord(server.PlacementRecordsForTest);
            Assert.NotNull(rec.Roster);
            Assert.Equal(first, rec.Roster);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var reloaded = StartFactoryWorld(seed, world, out var repo2);
        using (repo2)
        {
            Assert.Equal(first, reloaded.FactoriesForTest[0].Roster.ToList());
            Assert.Equal(first, FirstFactoryRecord(reloaded.PlacementRecordsForTest).Roster);
        }
    }

    [Fact]
    public void PinnedRoster_WinsOverThisLoadsRoll_AndTheMachineCountFollowsIt()
    {
        // Stands in for a grown recipe set: the record says one thing, the fresh roll says another.
        long seed = FirstFactorySeed();
        const string world = "roster_pinned";

        List<string> rolled;
        var server = StartFactoryWorld(seed, world, out var repo);
        using (repo)
        {
            rolled = server.FactoriesForTest[0].Roster.ToList();
        }

        string alternative = AllFactoryRecipeKeys().First(k => k != rolled[0]);
        var pinned = new List<string> { alternative };
        Assert.NotEqual(rolled, pinned);
        EditMetadata(world, meta => FirstFactoryRecord(meta.Placements).Roster = pinned);

        var reloaded = StartFactoryWorld(seed, world, out var repo2);
        using (repo2)
        {
            var f = reloaded.FactoriesForTest[0];
            Assert.Equal(pinned, f.Roster.ToList());
            Assert.Equal(1, f.MachineCount); // one machine bay per roster entry
            Assert.Equal(pinned, FirstFactoryRecord(reloaded.PlacementRecordsForTest).Roster); // the pin is never overwritten
        }
    }

    [Fact]
    public void PinnedRoster_DropsARecipeKeyThatNoLongerExists()
    {
        // A recipe removed from content must not leave a machine that makes nothing — but the rest of the
        // pinned roster stays exactly as it was.
        long seed = FirstFactorySeed();
        const string world = "roster_vanished";

        var server = StartFactoryWorld(seed, world, out var repo);
        using (repo)
        {
            Assert.True(server.FactoryCount > 0);
        }

        string kept = AllFactoryRecipeKeys()[0];
        EditMetadata(world, meta => FirstFactoryRecord(meta.Placements).Roster =
            new List<string> { kept, "factory_recipe_that_no_longer_exists" });

        var reloaded = StartFactoryWorld(seed, world, out var repo2);
        using (repo2)
        {
            Assert.Equal(new[] { kept }, reloaded.FactoriesForTest[0].Roster);
        }
    }

    [Fact]
    public void RecordFromBeforePinning_GetsTheCurrentRollPinnedOnFirstLoad()
    {
        // A save from before #1299 has factory records without a roster. The first load after the change
        // freezes that load's roll into the record — the same roll the world always had for this seed.
        long seed = FirstFactorySeed();
        const string world = "roster_legacy";

        List<string> rolled;
        var server = StartFactoryWorld(seed, world, out var repo);
        using (repo)
        {
            rolled = server.FactoriesForTest[0].Roster.ToList();
        }

        EditMetadata(world, meta => FirstFactoryRecord(meta.Placements).Roster = null);

        var reloaded = StartFactoryWorld(seed, world, out var repo2);
        using (repo2)
        {
            var rec = FirstFactoryRecord(reloaded.PlacementRecordsForTest);
            Assert.NotNull(rec.Roster);
            Assert.Equal(rolled, rec.Roster);
            Assert.Equal(rolled, reloaded.FactoriesForTest[0].Roster.ToList());
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
