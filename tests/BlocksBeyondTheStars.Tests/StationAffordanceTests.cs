// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1070/#1072/#1074 — the server publishes which stations are in reach (the Tab menu's single source of
/// truth), answers "where is the nearest one?", and research is bound to the cockpit.
/// </summary>
public sealed class StationAffordanceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationAffordanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stations_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(bool placeShip, out SqliteWorldRepository repo)
    {
        string name = placeShip ? "st_ship" : "st_noship";
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = placeShip };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void StationsInReach_SeesAPlacedWorkbench_AndForge_OffShip()
    {
        // The client used to know only ship station markers, so a base workbench never lit the Crafting tab
        // (#1070). The server now publishes the set — per station, from the same box scan crafting uses.
        var server = Started(placeShip: false, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64f, 0.5f);

            var none = server.StationsInReachForTest(p.State.PlayerId);
            Assert.Empty(none.Available);
            Assert.False(none.ShipBuildOk);

            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("workbench")!.NumericId);
            var bench = server.StationsInReachForTest(p.State.PlayerId);
            Assert.Equal(new[] { "workshop" }, bench.Available);

            server.World.SetBlock(new Vector3i(-2, 64, 1), _content.GetBlock("forge")!.NumericId);
            var both = server.StationsInReachForTest(p.State.PlayerId);
            Assert.Contains("workshop", both.Available);
            Assert.Contains("refinery", both.Available);

            // Walk out of the ±3 box → gone again.
            p.State.Position = new Vector3f(20.5f, 64f, 0.5f);
            Assert.Empty(server.StationsInReachForTest(p.State.PlayerId).Available);
        }
    }

    [Fact]
    public void LocateStation_FindsTheNearestWorkbench_OrReportsNone()
    {
        var server = Started(placeShip: false, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Seeker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64f, 0.5f);

            var miss = server.LocateStationForTest(p.State.PlayerId, "workshop");
            Assert.False(miss.Found);

            server.World.SetBlock(new Vector3i(12, 64, 0), _content.GetBlock("workbench")!.NumericId);
            server.World.SetBlock(new Vector3i(5, 64, 3), _content.GetBlock("workbench")!.NumericId);
            var hit = server.LocateStationForTest(p.State.PlayerId, "workshop");
            Assert.True(hit.Found);
            Assert.Equal("block", hit.Kind);
            Assert.Equal("workbench", hit.BlockKey);
            Assert.Equal((5, 64, 3), (hit.X, hit.Y, hit.Z)); // the nearer of the two (canonical, wrap-safe coords)

            // A station name the server doesn't know → a clean "not found", never a throw.
            Assert.False(server.LocateStationForTest(p.State.PlayerId, "wibble").Found);
        }
    }

    [Fact]
    public void Research_IsBoundToTheCockpit_WhenAShipIsParked()
    {
        // #1074: HandleUnlock enforced nothing while the Tech tab claimed a lab no ship had. Now research
        // happens at the cockpit; StationsInReach.ResearchOk mirrors the same rule for the client.
        var server = Started(placeShip: true, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scientist");
            var bp = _content.GetBlueprint("detoxifier")!;
            foreach (var cost in bp.UnlockCost)
            {
                p.State.Inventory.Add(cost.Item, cost.Count, 99);
            }

            p.State.KnowledgePoints = bp.KnowledgeCost;
            var cockpit = server.StationPosition("cockpit");
            Assert.NotNull(cockpit);

            // Aboard but far from the cockpit (the parked hull is small — step well outside station reach).
            p.State.AboardShip = true;
            p.State.Position = cockpit!.Value + new Vector3f(0f, 0f, -12f);
            Assert.False(server.StationsInReachForTest(p.State.PlayerId).ResearchOk);
            server.UnlockBlueprint(p.State.PlayerId, "detoxifier");
            Assert.DoesNotContain("detoxifier", p.State.UnlockedBlueprints);

            // At the cockpit → research works, and the locator points at that cell.
            p.State.Position = cockpit.Value;
            Assert.True(server.StationsInReachForTest(p.State.PlayerId).ResearchOk);
            var where = server.LocateStationForTest(p.State.PlayerId, "research");
            Assert.True(where.Found);
            Assert.Equal("ship", where.Kind);
            server.UnlockBlueprint(p.State.PlayerId, "detoxifier");
            Assert.Contains("detoxifier", p.State.UnlockedBlueprints);
        }
    }

    [Fact]
    public void Research_StaysOpen_WithoutAParkedShip()
    {
        // No cockpit anywhere to walk to (worlds without a starter ship) → the gate must not dead-end.
        var server = Started(placeShip: false, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Nomad");
            p.State.AboardShip = false;
            Assert.True(server.StationsInReachForTest(p.State.PlayerId).ResearchOk);
        }
    }

    [Fact]
    public void StationVocabulary_NamesBlocks_InBothLanguages()
    {
        // #1071: every station gate the menu can show resolves to a block name + the new keys exist in en/de.
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        foreach (var block in new[] { "workbench", "forge", "detoxifier", "matter_forge", "algae_tank", "campfire", "factory_terminal", "data_cache" })
        {
            Assert.True(en.Has($"block.{block}.name"), block);
            Assert.True(de.Has($"block.{block}.name"), block);
        }

        foreach (var key in new[]
                 {
                     "ui.craft.need_block", "ui.craft.need_block_or_module", "ui.craft.need_research",
                     "ui.craft.hint_research", "ui.craft.hint_shipbuild_aboard", "ui.craft.hint_shipbuild_module",
                     "ui.craft.hint_hand_only", "ui.craft.hint_need_block", "ui.craft.in_reach", "ui.craft.in_reach_none",
                     "ui.craft.where", "ui.craft.where_ship", "ui.craft.where_none", "ui.craft.where_show",
                     "ui.craft.where_craft", "ui.craft.where_marked", "srv.unlock.cockpit",
                     "ui.station.block.workbench", "ui.station.block.forge", "ui.station.block.detoxifier",
                     "ui.station.block.matter_forge", "ui.station.block.algae_tank", "ui.station.block.campfire",
                 })
        {
            Assert.True(en.Has(key), $"missing '{key}' in en");
            Assert.True(de.Has(key), $"missing '{key}' in de");
        }

        // The retired phantom-lab wording is gone (#1074).
        Assert.False(en.Has("ui.craft.go_to_lab"));
        Assert.False(en.Has("srv.station.lab"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
