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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The grown structure-template pools (#1115): four settlement and four station layouts, per-planet-type
/// restriction, and — the load-safety half — template PINNING: a stamped instance replays its exact
/// template forever, and structures from before pinning existed replay against ONLY the legacy pool,
/// which reproduces the old selection stream draw-for-draw. A bigger pool must never morph an existing
/// world's layout under its stamped blocks.
/// </summary>
public sealed class StructureTemplatePoolTests : IDisposable
{
    private static readonly string[] KnownMarkers =
    {
        "vendor", "mission_board", "hangar", "heal_tank", "quarters", "npc", "spawn", "loot",
        "door_slide", "door_hinge", "data_terminal", "bandit_stash", "relic_cache", "chest", "module",
        "greenhouse",
    };

    private readonly string _root;
    private readonly GameContent _content;

    public StructureTemplatePoolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tmplpool_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void Pools_CarryTheNewTemplates_AndEveryCellResolves()
    {
        Assert.True(_content.SettlementTemplates.Count >= 4, "settlement pool should hold river_hamlet + 3 new");
        Assert.True(_content.StationTemplates.Count >= 4, "station pool should hold hub_outpost + 3 new");

        // Tier coverage: a rolled tier should have a real chance of a template on both sides.
        foreach (var tier in new[] { "hamlet", "village", "town" })
        {
            Assert.Contains(_content.SettlementTemplates, t => t.Tier == tier);
        }

        foreach (var tier in new[] { "small", "medium", "large" })
        {
            Assert.Contains(_content.StationTemplates, t => t.Tier == tier);
        }

        // Exactly ONE legacy template per pool — the frozen pre-#1115 selection universe.
        Assert.Equal("river_hamlet", Assert.Single(_content.SettlementTemplates.Where(t => t.LegacyPool)).Key);
        Assert.Equal("hub_outpost", Assert.Single(_content.StationTemplates.Where(t => t.LegacyPool)).Key);

        foreach (var t in _content.SettlementTemplates.Concat(_content.StationTemplates))
        {
            Assert.True(t.Cells.Count > 0, t.Key + " has no cells");
            foreach (var c in t.Cells)
            {
                if (c.Kind == "marker")
                {
                    Assert.Contains(c.Id, KnownMarkers);
                }
                else
                {
                    Assert.NotNull(_content.GetBlock(c.Id)); // a typo'd block key must fail loudly here
                }
            }
        }
    }

    [Fact]
    public void LegacyOnlyPick_ReturnsExactlyThePreExpansionUniverse()
    {
        var rng = new Random(1);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal("river_hamlet", _content.PickSettlementTemplate("village", null, rng, null, legacyOnly: true)!.Key);
            Assert.Null(_content.PickSettlementTemplate("hamlet", null, rng, null, legacyOnly: true));
            Assert.Null(_content.PickSettlementTemplate("town", null, rng, null, legacyOnly: true));
            Assert.Equal("hub_outpost", _content.PickStationTemplate("medium", null, rng, legacyOnly: true)!.Key);
            Assert.Null(_content.PickStationTemplate("small", null, rng, legacyOnly: true));
        }
    }

    [Fact]
    public void PlanetTypeRestriction_FiltersTheStiltHamlet()
    {
        var rng = new Random(7);
        for (int i = 0; i < 60; i++)
        {
            var picked = _content.PickSettlementTemplate("village", null, rng, "desert");
            Assert.NotNull(picked);
            Assert.NotEqual("stilt_hamlet", picked!.Key); // wet-world template never lands in the dunes
        }

        bool seenOnJungle = false;
        for (int i = 0; i < 200 && !seenOnJungle; i++)
        {
            seenOnJungle = _content.PickSettlementTemplate("village", null, rng, "jungle")?.Key == "stilt_hamlet";
        }

        Assert.True(seenOnJungle, "the stilt hamlet should be pickable on its listed worlds");
    }

    [Fact]
    public void StampedTemplate_IsPinned_AndSurvivesARestart()
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            string world = $"tmplpin_{seed}";
            string pinnedTemplate;
            {
                var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
                var st = new LoopbackServerTransport(new LoopbackLink());
                var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
                config.World.SettlementTemplateUse = Shared.World.Frequency.Frequent; // make hits common for the scan
                var server = new SvGameServer(config, _content, st, repo);
                server.Start();
                using (repo)
                {
                    var rec = server.PlacementRecordsForTest.FirstOrDefault(r =>
                        r.Kind == "settlement" && r.Placed && !string.IsNullOrEmpty(r.Template));
                    if (rec is null)
                    {
                        server.Stop();
                        continue; // no template settlement on this world — next seed
                    }

                    pinnedTemplate = rec.Template;
                    Assert.NotNull(_content.SettlementTemplateByKey(pinnedTemplate)); // the pin points at a real layout
                    server.Stop();
                }
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // The restart replays the very same template (the pin, not a fresh roll against the pool).
            {
                var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
                var st = new LoopbackServerTransport(new LoopbackLink());
                var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
                config.World.SettlementTemplateUse = Shared.World.Frequency.Frequent;
                var server = new SvGameServer(config, _content, st, repo);
                server.Start();
                using (repo)
                {
                    var again = server.PlacementRecordsForTest.First(r =>
                        r.Kind == "settlement" && r.Placed && !string.IsNullOrEmpty(r.Template));
                    Assert.Equal(pinnedTemplate, again.Template);
                    server.Stop();
                }
            }

            return;
        }

        Assert.Fail("no seed in 1..60 produced a template settlement at Frequent — the pool should make this common");
    }
}
