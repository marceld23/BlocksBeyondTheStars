// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// Per-crate stash filter (#1032): the player dedicates a crate to specific items; a stash (H) then only
/// moves what the whitelist allows. Matching is on the base item key (dyed variants still fit), the server
/// sanitizes the list (unknown keys / non-stashable categories dropped), and the filter survives a save.
/// </summary>
public sealed class ContainerFilterTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ContainerFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cratefilter_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cratefilter"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "cratefilter", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FilteredCrate_OnlyStashesWhitelistedItems_DyedVariantIncluded()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Builder");
            var p = session.State;
            p.Position = new Vector3f(0, 200, 0);
            p.Inventory.Add("crate", 1, 99);
            p.Inventory.Add("iron_ore", 10, 99);
            p.Inventory.Add("carbon", 5, 99);
            p.Inventory.Add("copper_ore#tff0000", 4, 99); // dyed variant — must match a "copper_ore" whitelist entry

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");

            server.SetContainerFilterForTest(session, crate.Id, new[] { "iron_ore", "copper_ore" });
            server.DepositToContainer("Builder", crate.Id);

            Assert.Equal(0, p.Inventory.CountOf("iron_ore"));            // whitelisted → stashed
            Assert.Equal(0, p.Inventory.CountOf("copper_ore#tff0000"));  // dyed variant of a whitelisted base → stashed
            Assert.Equal(5, p.Inventory.CountOf("carbon"));              // not on the list → stays with the player

            var items = server.Containers.First(c => c.Id == crate.Id).Items;
            Assert.Contains(items, s => s.Item == "iron_ore" && s.Count == 10);
            Assert.Contains(items, s => s.Item == "copper_ore#tff0000" && s.Count == 4);
            Assert.DoesNotContain(items, s => s.Item == "carbon");
        }
    }

    [Fact]
    public void FullyBlockedStash_MovesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Builder");
            var p = session.State;
            p.Position = new Vector3f(0, 200, 0);
            p.Inventory.Add("crate", 1, 99);
            p.Inventory.Add("carbon", 5, 99);

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");

            server.SetContainerFilterForTest(session, crate.Id, new[] { "iron_ore" });
            server.DepositToContainer("Builder", crate.Id);

            Assert.Equal(5, p.Inventory.CountOf("carbon")); // nothing matched → nothing moved
            Assert.Empty(server.Containers.First(c => c.Id == crate.Id).Items);
        }
    }

    [Fact]
    public void ClearingTheFilter_AllowsEverythingAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Builder");
            var p = session.State;
            p.Position = new Vector3f(0, 200, 0);
            p.Inventory.Add("crate", 1, 99);
            p.Inventory.Add("carbon", 5, 99);

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");

            server.SetContainerFilterForTest(session, crate.Id, new[] { "iron_ore" });
            server.SetContainerFilterForTest(session, crate.Id, Array.Empty<string>());
            server.DepositToContainer("Builder", crate.Id);

            Assert.Equal(0, p.Inventory.CountOf("carbon")); // empty filter = no filter
        }
    }

    [Fact]
    public void SetFilter_DropsUnknownAndNonStashableKeys()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Builder");
            var p = session.State;
            p.Position = new Vector3f(0, 200, 0);
            p.Inventory.Add("crate", 1, 99);

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");

            // "machete" is a tool (never stashed → pointless on a whitelist), "no_such_item" doesn't exist,
            // "stone" is a block (H never stashes those), and the dyed key must be stored stripped to its base.
            server.SetContainerFilterForTest(session, crate.Id, new[] { "iron_ore", "machete", "no_such_item", "stone", "copper_ore#tff0000" });

            var filter = server.Containers.First(c => c.Id == crate.Id).Filter;
            Assert.Equal(new[] { "copper_ore", "iron_ore" }, filter.OrderBy(k => k).ToArray());
        }
    }

    [Fact]
    public void Filter_SurvivesPersistenceRoundtrip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Builder");
            session.State.Position = new Vector3f(0, 200, 0);
            session.State.Inventory.Add("crate", 1, 99);

            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            var crate = server.Containers.First(c => c.Kind == "crate");
            server.SetContainerFilterForTest(session, crate.Id, new[] { "iron_ore" });

            var reloaded = repo.ListContainers(crate.Planet).First(c => c.Id == crate.Id);
            Assert.Equal(new[] { "iron_ore" }, reloaded.Filter.ToArray());

            // And a crate saved without a filter comes back with an empty (allow-everything) one.
            server.SetContainerFilterForTest(session, crate.Id, Array.Empty<string>());
            reloaded = repo.ListContainers(crate.Planet).First(c => c.Id == crate.Id);
            Assert.Empty(reloaded.Filter);
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
