// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3341 + the crate loot of the buried G.D.S. ruins (#3334, #3337): the G.D.S. logs are written from a seed like the
/// derelict logs (one story per buried city, a different writer and job per log, stable, readable in English and German,
/// every fragment in both, and in the tone the maintainer set — curious, a little sad, never frightening); a
/// <c>gds_cache:&lt;n&gt;</c> crate holds suit piece n for n &lt; 4, Ressaw crystals and one or two logs; and a log taken
/// out of a crate lands in the Discoveries ledger (the Codex) under its own set.
/// </summary>
public sealed class GdsLogTests : IDisposable
{
    private static readonly string[] SuitPieces = { "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots" };

    private readonly string _root;
    private readonly GameContent _content;

    public GdsLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_gds_logs_" + Guid.NewGuid().ToString("N"));
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
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static List<string> LogsIn(IEnumerable<ItemStack> items)
        => items.Where(s => LogSets.Gds.IsLogItem(s.Item)).Select(s => s.Item).ToList();

    [Fact]
    public void Logs_TellOneStoryPerCity_DifferPerWriter_AndAreStableInBothLanguages()
    {
        var fragments = _content.GdsLogs;
        Assert.False(fragments.IsEmpty);
        Assert.Equal("gds.log.header", fragments.HeaderKey);
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");

        foreach (string city in new[] { "sys0-p1", "sys3-p0", "sys7-p2" })
        {
            var logs = Enumerable.Range(0, 12)
                .Select(i => LogSets.Gds.Compose(GdsLogs.SeedFor(7, city, "crate" + (i / 2), i), _content)!).ToList();

            // One buried city, one story: the same hall cracked first, the same way out, the same cycle — twelve writers.
            Assert.Single(logs.Select(l => l.IncidentKey.Substring(0, l.IncidentKey.LastIndexOf('.'))).Distinct());
            Assert.Single(logs.Select(l => l.EscapeKey).Distinct());
            Assert.Single(logs.Select(l => l.Day).Distinct());
            Assert.Equal(logs.Count, logs.Select(l => l.JobKey).Distinct().Count());
            Assert.All(logs, l => Assert.All(l.TextKeys, k => Assert.StartsWith("gds.log.", k, StringComparison.Ordinal)));

            var texts = logs.Select(l => l.Render(k => en[k])).ToList();
            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
            foreach (var log in logs)
            {
                // Stable: the seed alone writes the same log again (a restart, another client).
                var again = LogSets.Gds.Compose(log.Seed, _content)!;
                Assert.Equal(log.Render(k => en[k]), again.Render(k => en[k]));
                string item = GdsLogs.ItemFor(log.Seed);
                Assert.Equal("gds_log#x" + log.Seed.ToString("x8", System.Globalization.CultureInfo.InvariantCulture), item);
                Assert.Same(LogSets.Gds, LogSets.OfItem(item));
                Assert.Equal(log.Seed, LogSets.Gds.SeedOfItem(item));
                Assert.Equal(log.Seed, LogSets.Gds.SeedOfLedgerKey(LogSets.Gds.LedgerKey(log.Seed)));

                // The sets never claim each other's items or ledger entries.
                Assert.False(DerelictLogs.IsLogItem(item));
                Assert.Equal(0u, DerelictLogs.SeedOfLedgerKey(LogSets.Gds.LedgerKey(log.Seed)));
                Assert.Equal(0u, LogSets.Gds.SeedOfLedgerKey(DerelictLogs.LedgerKey(log.Seed)));

                string english = log.Render(k => en.TryGetValue(k, out var v) ? v : k);
                string german = log.Render(k => de.TryGetValue(k, out var v) ? v : k);
                Assert.NotEqual(english, german);
                foreach (string text in new[] { english, german })
                {
                    Assert.DoesNotContain("{", text);
                    Assert.DoesNotContain("gds.log.", text);
                    Assert.Contains(log.Writer, text);
                    Assert.Contains("G.D.S.", text);
                }
            }
        }

        // Another world, another story for the same body id; no seed, no fragments → no log.
        Assert.NotEqual(LogSets.Gds.Compose(GdsLogs.SeedFor(7, "sys0-p1", "a", 0), _content)!.Render(k => en[k]),
            LogSets.Gds.Compose(GdsLogs.SeedFor(8, "sys0-p1", "a", 0), _content)!.Render(k => en[k]));
        Assert.Null(LogSets.Gds.Compose(0, _content));
        Assert.Null(DerelictLogs.Compose(GdsLogs.SeedFor(7, "sys0-p1", "a", 0), new Shared.Definitions.DerelictLogDefinition()));
    }

    [Fact]
    public void EveryGdsLogText_ExistsInEnglishAndGerman_AndKeepsTheTone()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var fragments = _content.GdsLogs.AllTextKeys().ToList();
        Assert.True(fragments.Count >= 40, $"only {fragments.Count} G.D.S. log fragments");
        var keys = fragments.Concat(new[]
        {
            "item.gds_log.name", "item.gds_log.desc", "srv.gds.log_found", "ui.wiki.discoveries.gds_log",
        }).ToList();

        // The tone rule (#3341): curious and a little sad — nobody is hurt on the page, the G.D.S. were reckless, never evil.
        var dark = new Regex(@"\b(dead|died|dies|dying|death|blood\w*|hurt\w*|injur\w*|kill\w*|drown\w*|scream\w*|monster\w*|experiment\w*|evil|corpse\w*|bones?)\b",
            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));
        foreach (var key in keys)
        {
            foreach (var (lang, table) in new[] { ("en", en), ("de", de) })
            {
                Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                bool header = key == _content.GdsLogs.HeaderKey;
                bool named = key == "srv.gds.log_found";
                Assert.Equal(header || named, text!.Contains("{0}"));
                Assert.Equal(header, text.Contains("{1}"));
            }

            Assert.NotEqual(en[key], de[key]); // translated, not copied
            Assert.False(dark.IsMatch(en[key]), $"'{key}' leaves the tone: {en[key]}");
        }
    }

    [Fact]
    public void GdsCaches_HoldTheirSuitPiece_Crystals_AndOneOrTwoLogs()
    {
        var server = NewServer("gds_loot", out var repo);
        using (repo)
        {
            int withTwoLogs = 0, withData = 0, total = 0;
            foreach (var cell in new[] { new Vector3i(10, 40, -7), new Vector3i(-120, 12, 300), new Vector3i(4, 70, 4) })
            {
                var ruinLogs = new List<string>();
                for (int n = 0; n < 8; n++)
                {
                    var at = new Vector3i(cell.X + n * 3, cell.Y, cell.Z);
                    var items = server.GdsCacheLootForTest(n, at);
                    Assert.Equal(items.Select(i => (i.Item, i.Count)), server.GdsCacheLootForTest(n, at).Select(i => (i.Item, i.Count)));
                    Assert.All(items, i => Assert.NotNull(_content.GetItem(i.Item)));

                    // Crate n < 4 holds suit piece n — so a ruin's first four crates hold the whole set; later ones none.
                    var pieces = items.Where(i => SuitPieces.Contains(i.Item)).ToList();
                    if (n < SuitPieces.Length)
                    {
                        Assert.Equal(SuitPieces[n], Assert.Single(pieces).Item);
                        Assert.Equal(1, pieces[0].Count);
                    }
                    else
                    {
                        Assert.Empty(pieces);
                    }

                    Assert.InRange(items.Single(i => i.Item == "ressaw_crystal").Count, 1, 3);
                    var logs = LogsIn(items);
                    Assert.InRange(logs.Count, 1, 2);
                    ruinLogs.AddRange(logs);
                    total++;
                    withTwoLogs += logs.Count == 2 ? 1 : 0;
                    withData += items.Any(i => i.Item == "data_fragment") ? 1 : 0;
                }

                // Every log of a ruin is a different note, and they all tell the same city's story.
                Assert.Equal(ruinLogs.Count, ruinLogs.Distinct(StringComparer.Ordinal).Count());
                Assert.Single(ruinLogs.Select(l => LogSets.Gds.Compose(LogSets.Gds.SeedOfItem(l), _content)!.EscapeKey).Distinct());
            }

            Assert.InRange(withTwoLogs, 1, total - 1); // sometimes two — not never, not always
            Assert.InRange(withData, 1, total - 1);
            server.Stop();
        }
    }

    [Fact]
    public void AGdsCacheMarker_SpawnsOnce_AndATakenLogLandsInTheCodex()
    {
        var server = NewServer("gds_codex", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var feet = p.State.Position;
            var cell = new Vector3i((int)Math.Floor(feet.X) + 1, (int)Math.Floor(feet.Y), (int)Math.Floor(feet.Z));
            var pos = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

            var crate = server.SpawnGdsCacheForTest("gds_ruin", "gds_cache:0", pos);
            Assert.NotNull(crate);
            Assert.Equal("salvage", crate!.Kind);
            Assert.Equal(cell, crate.Position); // exactly the marker's cell — also below zero (truncating would miss by one)
            var below = server.SpawnGdsCacheForTest("gds_ruin", "gds_cache:1", new Vector3f(-4.5f, cell.Y + 0.5f, -9.5f));
            Assert.Equal(new Vector3i(-5, cell.Y, -10), below!.Position);
            Assert.Contains(crate.Items, i => i.Item == "ressaw_helmet");
            var logs = LogsIn(crate.Items);
            Assert.InRange(logs.Count, 1, 2);
            Assert.Equal(server.GdsCacheLootForTest(0, cell).Select(i => (i.Item, i.Count)), crate.Items.Select(i => (i.Item, i.Count)));

            p.State.Position = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 1.5f);
            server.LootContainer("Justus", crate.Id);
            Assert.DoesNotContain(server.Containers, c => c.Id == crate.Id); // looted empty → gone
            Assert.Equal(1, p.State.Inventory.CountOf("ressaw_helmet"));
            foreach (var log in logs)
            {
                Assert.Equal(1, p.State.Inventory.CountOf(log));
                uint seed = LogSets.Gds.SeedOfItem(log);
                string ledger = LogSets.Gds.LedgerKey(seed);
                Assert.StartsWith("gdslog:", ledger, StringComparison.Ordinal);
                Assert.Contains(ledger, p.State.Scanned);
                Assert.Equal(LogSets.Gds.Compose(seed, _content)!.Writer, p.State.ScannedNames[ledger]);
                Assert.Single(LogSets.Gds.FoundIn(p.State.Scanned, _content), f => f.LedgerKey == ledger); // the Codex re-reads it
                Assert.Empty(LogSets.Derelict.FoundIn(new[] { ledger }, _content)); // …under its own set only
            }

            // Once ever: the same marker never fills a second crate, not even after the first was emptied.
            Assert.Null(server.SpawnGdsCacheForTest("gds_ruin", "gds_cache:0", pos));
            server.Stop();
        }
    }

    [Theory]
    [InlineData("gds_cache:0", true, 0)]
    [InlineData("gds_cache:3", true, 3)]
    [InlineData("gds_cache:17", true, 17)]
    [InlineData("gds_cache:", false, -1)]
    [InlineData("gds_cache:-1", false, -1)]
    [InlineData("gds_cache:x", false, -1)]
    [InlineData("gds_cache", false, -1)]
    [InlineData("loot", false, -1)]
    public void TheCacheMarker_ParsesItsNumber(string marker, bool ok, int expected)
    {
        Assert.Equal(ok, SvGameServer.TryParseGdsCache(marker, out int n));
        if (ok)
        {
            Assert.Equal(expected, n);
        }
    }
}
