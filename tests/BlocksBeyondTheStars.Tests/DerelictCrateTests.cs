// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2633: a derelict station's old supply crates — filled once ever on the first boarding, with medpacks, rations, now and
/// then spoiled food, scrap and one or two log entries; the logs are written from a seed (one story per station, a
/// different writer and job per log), stable across a restart, readable in English and German, and a taken log lands in
/// the Discoveries ledger. Spoiled food is edible and hurts a little; the ration dispenser refuses harmful food.
/// </summary>
public sealed class DerelictCrateTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DerelictCrateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_derelict_crates_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, long seed = 7)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Flies the player up to the start system's guaranteed derelict and boards it; returns its id.</summary>
    private static string BoardGuaranteed(SvGameServer server, PlayerSession p)
    {
        string system = server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId;
        string id = DerelictPlacer.BodyId(system, 0);
        string pid = p.State.PlayerId;
        server.EnterSpace(pid);
        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        var layout = server.DerelictLayoutForTest(id)!;
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (layout.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid));
        return id;
    }

    private static List<string> LogsIn(IEnumerable<ItemStack> items)
        => items.Where(s => DerelictLogs.IsLogItem(s.Item)).Select(s => s.Item).ToList();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryCrateSpot_StandsOnTheDeck_NeverOnScrapAPostOrAnotherCrate(bool proceduralOnly)
    {
        var placed = new HashSet<ushort>(new[] { "scrap_pile", "scrap_metal", "station_vendor", SvGameServer.GuardianBayBlockKey }
            .Select(k => _content.GetBlock(k)!.NumericId.Value));
        for (long seed = 1; seed <= 12; seed++)
        {
            var l = DerelictGenerator.Generate(seed * 7919, _content, proceduralOnly);
            Assert.InRange(l.CrateSpots.Count, 1, 6);
            foreach (var c in l.CrateSpots)
            {
                ushort below = l.Get(c.X, c.Y - 1, c.Z);
                Assert.NotEqual(0, below); // a floor under it…
                Assert.DoesNotContain(below, placed); // …not a scrap pile, an old stall or the guardian bay
                Assert.DoesNotContain(new Vector3i(c.X, c.Y - 1, c.Z), l.CrateSpots); // …nor another crate
            }
        }
    }

    [Fact]
    public void Crates_FillOnce_HoldSuppliesAndLogs_AndStayAcrossARestart()
    {
        string id;
        Vector3i secondSpot;
        List<(string Item, int Count)> secondContents;
        int crateCount;
        {
            var s1 = NewServer("drl_crates", out var repo1);
            using (repo1)
            {
                var p = s1.AddLocalPlayer("Justus");
                id = BoardGuaranteed(s1, p);
                var layout = s1.DerelictLayoutForTest(id)!;
                var spots = s1.DerelictCrateSpotsForTest(id);
                var crates = s1.DerelictCratesForTest(id);
                crateCount = spots.Count;
                Assert.InRange(crateCount, 2, 6);
                Assert.Equal(crateCount, crates.Count);

                // Every crate stands as a crate block — in the interior AND in the build (the hull and the row keep it).
                ushort crateBlock = _content.GetBlock("crate")!.NumericId.Value;
                foreach (var spot in spots)
                {
                    Assert.Equal(crateBlock, s1.World.GetBlock(spot).Value);
                    Assert.Contains(crates, c => c.Position.Equals(spot));
                }

                Assert.Equal(crateBlock, s1.StationCellsForTest(id)[layout.CrateSpots[0]].Value);

                // Supplies: the first crate always has a medpack; every crate holds one or two logs, all different.
                var first = crates.Single(c => c.Position.Equals(spots[0]));
                Assert.Contains(first.Items, s => s.Item == "medpack");
                var allLogs = new List<string>();
                foreach (var c in crates)
                {
                    var logs = LogsIn(c.Items);
                    Assert.InRange(logs.Count, 1, 2);
                    allLogs.AddRange(logs);
                }

                Assert.Equal(allLogs.Count, allLogs.Distinct(StringComparer.Ordinal).Count());

                // Take crate 0: it empties away, its logs are in the backpack and in the Discoveries ledger.
                var firstLogs = LogsIn(first.Items);
                p.State.Position = new Vector3f(spots[0].X + 0.5f, spots[0].Y, spots[0].Z + 1.5f);
                s1.LootContainer("Justus", first.Id);
                Assert.DoesNotContain(s1.DerelictCratesForTest(id), c => c.Id == first.Id);
                foreach (var log in firstLogs)
                {
                    Assert.Equal(1, p.State.Inventory.CountOf(log));
                    string ledger = DerelictLogs.LedgerKey(DerelictLogs.SeedOfItem(log));
                    Assert.Contains(ledger, p.State.Scanned);
                    Assert.Equal(DerelictLogs.Compose(DerelictLogs.SeedOfItem(log), _content.DerelictLogs)!.Writer, p.State.ScannedNames[ledger]);
                    Assert.Equal(id, p.State.ScannedWhere[ledger].BodyId);
                }

                // Leave and board again: the looted crate never comes back, the others are untouched.
                secondSpot = spots[1];
                secondContents = crates.Single(c => c.Position.Equals(secondSpot)).Items.Select(s => (s.Item, s.Count)).ToList();
                s1.LeaveStation("Justus");
                BoardGuaranteed(s1, p);
                Assert.Equal(crateCount - 1, s1.DerelictCratesForTest(id).Count);
                s1.Stop();
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("drl_crates", out var repo2);
        using (repo2)
        {
            var p = s2.AddLocalPlayer("Justus");
            s2.LeaveStation("Justus"); // the rejoin may have put the player back aboard
            BoardGuaranteed(s2, p);
            var crates = s2.DerelictCratesForTest(id);
            Assert.Equal(crateCount - 1, crates.Count); // filled once ever — a restart refills nothing
            var second = crates.Single(c => c.Position.Equals(secondSpot));
            Assert.Equal(secondContents, second.Items.Select(s => (s.Item, s.Count)).ToList());
            Assert.Equal("crate", _content.BlockById(s2.World.GetBlock(secondSpot))!.Key);
            s2.Stop();
        }
    }

    [Fact]
    public void CrateLoot_IsSeedPure_AndSometimesHoldsSpoiledFood()
    {
        var server = NewServer("drl_loot", out var repo);
        using (repo)
        {
            int withSpoiled = 0, withMedpack = 0, total = 0;
            for (int s = 0; s < 30; s++)
            {
                for (int crate = 0; crate < 6; crate++)
                {
                    string station = "derelict:sys" + s + "-0";
                    var items = server.DerelictCrateLootForTest(station, crate);
                    Assert.Equal(items.Select(i => (i.Item, i.Count)), server.DerelictCrateLootForTest(station, crate).Select(i => (i.Item, i.Count)));
                    Assert.InRange(LogsIn(items).Count, 1, 2);
                    Assert.All(items, i => Assert.NotNull(_content.GetItem(i.Item)));
                    total++;
                    withSpoiled += items.Any(i => i.Item == "spoiled_food") ? 1 : 0;
                    withMedpack += items.Any(i => i.Item == "medpack") ? 1 : 0;
                    if (crate == 0)
                    {
                        Assert.Contains(items, i => i.Item == "medpack");
                    }
                }
            }

            Assert.InRange(withSpoiled, 1, total - 1); // sometimes — not never, not always
            Assert.True(withMedpack > total / 2, $"medpacks in {withMedpack} of {total} crates");
            server.Stop();
        }
    }

    [Fact]
    public void Logs_TellOneStoryPerStation_DifferPerWriter_AndAreStable()
    {
        var fragments = _content.DerelictLogs;
        Assert.False(fragments.IsEmpty);
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");

        foreach (string station in new[] { "derelict:sys0-0", "derelict:sys4-1", "derelict:sys9-2" })
        {
            var logs = Enumerable.Range(0, 12).Select(i => DerelictLogs.Compose(DerelictLogs.SeedFor(7, station, i), fragments)!).ToList();

            // One station, one story: the same disaster, the same way out, the same day — told by twelve different people.
            Assert.Single(logs.Select(l => l.IncidentKey.Substring(0, l.IncidentKey.LastIndexOf('.'))).Distinct());
            Assert.Single(logs.Select(l => l.EscapeKey).Distinct());
            Assert.Single(logs.Select(l => l.Day).Distinct());
            Assert.Equal(logs.Count, logs.Select(l => l.JobKey).Distinct().Count());

            var texts = logs.Select(l => l.Render(k => en[k])).ToList();
            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
            foreach (var log in logs)
            {
                // Stable: the seed alone writes the same log again (a restart, another client).
                var again = DerelictLogs.Compose(log.Seed, fragments)!;
                Assert.Equal(log.Render(k => en[k]), again.Render(k => en[k]));
                Assert.Equal(log.Seed, DerelictLogs.SeedOfItem(DerelictLogs.ItemFor(log.Seed)));
                Assert.Equal(log.Seed, DerelictLogs.SeedOfLedgerKey(DerelictLogs.LedgerKey(log.Seed)));

                foreach (var table in new[] { en, de })
                {
                    string text = log.Render(k => table.TryGetValue(k, out var v) ? v : k);
                    Assert.DoesNotContain("{", text);
                    Assert.DoesNotContain("derelict.log.", text);
                    Assert.Contains(log.Writer, text);
                }
            }
        }

        // Another world, another story for the same station id (almost always — pinned for these seeds).
        Assert.NotEqual(DerelictLogs.Compose(DerelictLogs.SeedFor(7, "derelict:sys0-0", 0), fragments)!.Render(k => en[k]),
            DerelictLogs.Compose(DerelictLogs.SeedFor(8, "derelict:sys0-0", 0), fragments)!.Render(k => en[k]));
        Assert.Null(DerelictLogs.Compose(0, fragments));
        Assert.Null(DerelictLogs.Compose(DerelictLogs.SeedFor(7, "derelict:sys0-0", 0), new DerelictLogDefinition()));
    }

    [Fact]
    public void EveryLogText_ExistsInEnglishAndGerman()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var keys = _content.DerelictLogs.AllTextKeys().ToList();
        Assert.True(keys.Count > 40, $"only {keys.Count} log fragments");
        keys.AddRange(new[]
        {
            "item.spoiled_food.name", "item.spoiled_food.desc", "item.derelict_log.name", "item.derelict_log.desc",
            "srv.derelict.log_found", "ui.derelict_log.faded", "ui.wiki.discoveries.log", "srv.ration.harmful",
        });

        foreach (var key in keys)
        {
            foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                bool header = key == DerelictLogDefinition.HeaderKey;
                bool named = key == "srv.derelict.log_found";
                Assert.Equal(header || named, text!.Contains("{0}"));
                Assert.Equal(header, text.Contains("{1}"));
            }
        }
    }

    [Fact]
    public void SpoiledFood_IsEdible_HurtsALittle_AndStaysOutOfTheRationDispenser()
    {
        var food = _content.GetItem("spoiled_food");
        Assert.NotNull(food);
        Assert.Equal(ItemCategory.Consumable, food!.Category);
        Assert.True(food.ConsumeHunger > 0f, "spoiled food still fills a little");
        Assert.True(food.ConsumeHealth < 0f, "spoiled food hurts");
        Assert.True(food.ConsumeHealth > _content.GetItem("toxic_meat")!.ConsumeHealth, "…but less than contaminated meat");
        Assert.True(HerdRules.IsFoodForCreatures(_content.GetItem("berries")) && !HerdRules.IsFoodForCreatures(food),
            "harmful food is never fed to the herd");

        var server = NewServer("drl_food", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.AboardShip = false;
            p.State.Health = 80f;
            p.State.Hunger = 50f;
            p.State.Inventory.SetSlot(3, new ItemStack("spoiled_food", 3));

            server.ConsumeItem("Spacer", "spoiled_food");
            Assert.Equal(80f + food.ConsumeHealth, p.State.Health, 3);
            Assert.Equal(50f + food.ConsumeHunger, p.State.Hunger, 3);
            Assert.Equal(2, p.State.Inventory.CountOf("spoiled_food"));

            server.LoadRation("Spacer", "spoiled_food", 2);
            Assert.Equal(0, p.State.RationStore.CountOf("spoiled_food"));
            Assert.Equal(2, p.State.Inventory.CountOf("spoiled_food"));
            server.Stop();
        }
    }
}
