// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Scanning &amp; research: a first-time scan grants knowledge points (re-scans don't), the handheld
/// scanner reports a threat, the ship scanner reveals asteroid resources, and blueprints require
/// knowledge in addition to materials.
/// </summary>
public sealed class ScanningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ScanningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_scan_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, Action<GameRules>? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = planet, Seed = 4242, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FirstScan_GrantsKnowledge_RescanDoesNot()
    {
        var server = Started("jungle", out var repo); // "many" creatures
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");
            var speciesId = server.SpeciesRoster.First().Id;

            var first = server.ScanSubject("Scout", "creature", speciesId);
            Assert.True(first.FirstTime);
            Assert.True(first.KnowledgeGained > 0);
            Assert.Equal(first.KnowledgeGained, p.State.KnowledgePoints);

            var again = server.ScanSubject("Scout", "creature", speciesId);
            Assert.False(again.FirstTime);
            Assert.Equal(0, again.KnowledgeGained);
            Assert.Equal(first.KnowledgeGained, p.State.KnowledgePoints); // unchanged
        }
    }

    [Fact]
    public void ScanCreature_ReportsThreat()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Scout");
            var result = server.ScanSubject("Scout", "creature", server.SpeciesRoster.First().Id);
            Assert.Contains(result.Threat, new[] { "Safe", "Provokable", "Hostile" });
        }
    }

    [Fact]
    public void ScanBlock_GrantsKnowledge()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");
            var result = server.ScanSubject("Scout", "block", "iron_ore");
            Assert.True(result.FirstTime);
            Assert.True(result.KnowledgeGained >= 1);
            Assert.True(p.State.KnowledgePoints >= 1);
        }
    }

    [Fact]
    public void ScanTree_ReportsNamedSpecies_AndTrunkLeavesShareOneDiscovery()
    {
        var server = Started("jungle", out var repo); // a treed world
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");

            // The trunk scans as this world's coined, edible/toxic tree species — not the raw block key.
            var trunk = server.ScanSubject("Scout", "block", "wood_log");
            Assert.True(trunk.FirstTime);
            Assert.True(trunk.KnowledgeGained >= 1);
            Assert.NotEqual("wood_log", trunk.Subject);
            Assert.Contains(trunk.Threat, new[] { "Edible", "Toxic" });

            // The leaves are the SAME tree → already discovered, so no further knowledge.
            var leaves = server.ScanSubject("Scout", "block", "tree_leaves");
            Assert.False(leaves.FirstTime);
            Assert.Equal(0, leaves.KnowledgeGained);
            Assert.Equal(trunk.Subject, leaves.Subject);          // same coined name
            Assert.Equal(trunk.KnowledgeGained, p.State.KnowledgePoints); // unchanged
        }
    }

    [Fact]
    public void ScanMicroFauna_GrantsKnowledgeOnce_RejectsUnknownKind_AndListsInLedger()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");

            // Critters are client-local (#757): the server validates only that the kind exists — the
            // same existence-only trust level creature scans run at.
            var first = server.ScanSubject("Scout", "microfauna", "wisp");
            Assert.True(first.FirstTime);
            Assert.Equal("microfauna", first.Kind);
            Assert.Equal("wisp", first.SubjectKey);
            Assert.True(first.KnowledgeGained > 0);
            Assert.Equal("wisp", p.State.ScannedNames["microfauna:wisp"]);

            var again = server.ScanSubject("Scout", "microfauna", "wisp");
            Assert.False(again.FirstTime);
            Assert.Equal(0, again.KnowledgeGained);

            // A kind the catalogue doesn't know earns nothing — the ledger can't be spammed with junk.
            var bogus = server.ScanSubject("Scout", "microfauna", "dragon");
            Assert.Equal(0, bogus.KnowledgeGained);
            Assert.Equal("ui.scan.unknown", bogus.InfoKey);
            Assert.DoesNotContain("microfauna:dragon", p.State.Scanned);
        }
    }

    [Fact]
    public void MicroFaunaCatalog_KeysAreDistinct_AndKnownLookupWorks()
    {
        Assert.Equal(MicroFaunaCatalog.Keys.Length, MicroFaunaCatalog.Keys.Distinct().Count());
        Assert.All(MicroFaunaCatalog.Keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
        Assert.True(MicroFaunaCatalog.IsKnown("firefly"));
        Assert.True(MicroFaunaCatalog.IsKnown("wisp"));     // the skyray's replacement (#752)
        Assert.False(MicroFaunaCatalog.IsKnown("skyray"));  // retired key must stay retired
        Assert.False(MicroFaunaCatalog.IsKnown(null));
    }

    [Fact]
    public void ScanAsteroid_RevealsResources_AndGrantsKnowledge()
    {
        var server = Started("rocky", out var repo, r => r.FreeSpaceFlight = true);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var asteroid = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);

            var result = server.ScanSpaceEntity("Pilot", asteroid.Id);
            Assert.Contains("Resources", result.Info);
            Assert.True(result.KnowledgeGained > 0);
        }
    }

    // --- #484: the readout travels as locale keys + structured data, never as English prose ---

    // NOTE ON TEST COUNT: each Started(...) generates a whole world (planet, settlements, NPCs, SQLite), so
    // these assertions are grouped per world rather than split one-per-fact. Splitting them doubled the
    // server starts in this file, which starved the 2-core CI runner enough to tip a timing-sensitive
    // loopback-HTTP test in WebSocketTransportTests over the 120 s fast-tier budget. Facets of ONE scan
    // belong in one test anyway.

    [Fact]
    public void ScanCreature_SendsLocaleKeys_RecordsName_AndSurvivesASave()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");
            var speciesId = server.SpeciesRoster.First().Id;
            var result = server.ScanSubject("Scout", "creature", speciesId);

            Assert.Equal("creature", result.Kind);
            // Habitat / activity / temperament as keys the client localizes — these used to be raw C# enum
            // names, so the German build read "Forest · Nocturnal · Territorial".
            Assert.Equal(3, result.TraitKeys.Length);
            Assert.StartsWith("ui.scan.habitat.", result.TraitKeys[0]);
            Assert.StartsWith("ui.scan.activity.", result.TraitKeys[1]);
            Assert.StartsWith("ui.scan.temperament.", result.TraitKeys[2]);
            Assert.Contains(result.ThreatKey, new[] { "ui.scan.threat.safe", "ui.scan.threat.provokable", "ui.scan.threat.hostile" });

            // The coined species name is remembered at scan time: species are generated PER WORLD, so it
            // could not be resolved again from another planet for the Codex "Discoveries" list.
            Assert.Equal(result.Subject, p.State.ScannedNames[$"creature:{speciesId}"]);

            // …and it has to survive a save, or the Codex empties on reload.
            var restored = StateMapper.FromSnapshot(StateMapper.ToSnapshot(p.State));
            Assert.Equal(p.State.ScannedNames, restored.ScannedNames);
        }
    }

    [Fact]
    public void ScanBlock_SendsStructuredDrops_AndUnknownSubjectSendsALocaleKey()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Scout");
            var ore = server.ScanSubject("Scout", "block", "iron_ore");

            // Drops are structured now, so the client can show localized names ("Eisenerz ×1") instead of
            // the raw key the server used to bake into an English "Yields: iron_ore×1" string.
            Assert.NotEmpty(ore.Drops);
            Assert.All(ore.Drops, d => Assert.False(string.IsNullOrEmpty(d.Item)));
            Assert.All(ore.Drops, d => Assert.True(d.Count > 0));
            Assert.Empty(ore.InfoKey); // it has a yield, so no whole-line remark

            Assert.Equal("ui.scan.unknown", server.ScanSubject("Scout", "block", "not_a_real_block").InfoKey);
        }
    }

    [Fact]
    public void ScanAsteroid_SendsResourceTypesWithoutCounts()
    {
        var server = Started("rocky", out var repo, r => r.FreeSpaceFlight = true);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var asteroid = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);

            var result = server.ScanSpaceEntity("Pilot", asteroid.Id);
            Assert.Equal("asteroid", result.Kind);
            Assert.NotEmpty(result.Drops);
            Assert.All(result.Drops, d => Assert.Equal(0, d.Count)); // type only — the client omits "×n"
        }
    }

    [Fact]
    public void DiscoveryLog_RoundTripsThroughTheCodec()
    {
        var log = new BlocksBeyondTheStars.Networking.Messages.DiscoveryLog
        {
            Entries = new[] { "creature:sp0", "block:iron_ore" },
            Names = new[] { "Sky Grazer", "iron_ore" },
            Full = true,
        };

        var decoded = Assert.IsType<BlocksBeyondTheStars.Networking.Messages.DiscoveryLog>(
            BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(log)));
        Assert.Equal(log.Entries, decoded.Entries);
        Assert.Equal(log.Names, decoded.Names);
        Assert.True(decoded.Full);
    }

    [Fact]
    public void ScanResult_StructuredFields_RoundTripThroughTheCodec()
    {
        var sent = new BlocksBeyondTheStars.Networking.Messages.ScanResult
        {
            Subject = "Sky Grazer",
            SubjectKey = "sp0",
            Kind = "creature",
            ThreatKey = "ui.scan.threat.safe",
            TraitKeys = new[] { "ui.scan.habitat.land", "ui.scan.activity.diurnal", "ui.scan.temperament.passive" },
            Drops = new[] { new BlocksBeyondTheStars.Networking.Messages.NetTradeItem { Item = "hide", Count = 2 } },
            InfoKey = string.Empty,
        };

        var decoded = Assert.IsType<BlocksBeyondTheStars.Networking.Messages.ScanResult>(
            BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(sent)));
        Assert.Equal(sent.SubjectKey, decoded.SubjectKey);
        Assert.Equal(sent.Kind, decoded.Kind);
        Assert.Equal(sent.ThreatKey, decoded.ThreatKey);
        Assert.Equal(sent.TraitKeys, decoded.TraitKeys);
        Assert.Equal("hide", decoded.Drops.Single().Item);
        Assert.Equal(2, decoded.Drops.Single().Count);
    }

    [Fact]
    public void EveryScanLocaleKey_ExistsInBothLanguages()
    {
        // The whole point of #484: the readout is keys, so a missing key would show "[ui.scan.…]" in game.
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");

        var required = new List<string>
        {
            "ui.scan.yield", "ui.scan.resources", "ui.scan.no_yield", "ui.scan.foliage",
            "ui.scan.flora_harvest", "ui.scan.barren", "ui.scan.unknown", "ui.scan.no_scanner",
            "ui.scan.not_scannable", "ui.scan.subject.asteroid", "ui.scan.knowledge_gain",
            "ui.scan.ore.found", "ui.scan.ore.none", "ui.settings.ui_scale",
            "ui.wiki.discoveries", "ui.wiki.discoveries.empty", "ui.wiki.discoveries.count",
        };
        foreach (var kind in new[] { "creature", "tree", "flora", "block", "asteroid" })
        {
            required.Add("ui.wiki.discoveries." + kind);
        }

        foreach (var t in Enum.GetNames<Shared.Definitions.CreatureHabitat>())
        {
            required.Add("ui.scan.habitat." + t.ToLowerInvariant());
        }

        foreach (var t in Enum.GetNames<Shared.Definitions.CreatureActivity>())
        {
            required.Add("ui.scan.activity." + t.ToLowerInvariant());
        }

        foreach (var t in Enum.GetNames<Shared.Definitions.CreatureTemperament>())
        {
            required.Add("ui.scan.temperament." + t.ToLowerInvariant());
        }

        required.AddRange(new[]
        {
            "ui.scan.threat.hostile", "ui.scan.threat.provokable", "ui.scan.threat.safe",
            "ui.scan.threat.toxic", "ui.scan.threat.edible",
        });

        var missingEn = required.Where(k => !en.ContainsKey(k)).ToList();
        var missingDe = required.Where(k => !de.ContainsKey(k)).ToList();
        Assert.Empty(missingEn);
        Assert.Empty(missingDe);
    }

    [Fact]
    public void Blueprint_RequiresKnowledge_InAdditionToMaterials()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eng");
            // detoxifier unlockCost: data_fragment x2, iron_plate x12, cable x4; knowledgeCost 16.
            p.State.Inventory.Add("data_fragment", 2, 99);
            p.State.Inventory.Add("iron_plate", 12, 99);
            p.State.Inventory.Add("cable", 4, 99);

            // No knowledge yet → rejected even with the materials.
            server.UnlockBlueprint("Eng", "detoxifier");
            Assert.DoesNotContain("detoxifier", p.State.UnlockedBlueprints);

            // Research enough, then it unlocks. Knowledge is a permanent THRESHOLD (item 11): it gates the
            // unlock but is NOT spent — only the research materials are consumed.
            p.State.KnowledgePoints = 16;
            server.UnlockBlueprint("Eng", "detoxifier");
            Assert.Contains("detoxifier", p.State.UnlockedBlueprints);
            Assert.Equal(16, p.State.KnowledgePoints);             // knowledge never goes away
            Assert.Equal(0, p.State.Inventory.CountOf("cable"));   // but the materials are spent
        }
    }

    [Fact]
    public void MinigameKnowledge_PaysPerStarOncePerGame()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Gamer");

            // First 2-star run pays 10; replaying the same game at the same rating teaches nothing (#767).
            server.ReportMinigameResultForTest("Gamer", "asteroid_dodger", rating: 2);
            Assert.Equal(10, p.State.KnowledgePoints);
            server.ReportMinigameResultForTest("Gamer", "asteroid_dodger", rating: 2);
            Assert.Equal(10, p.State.KnowledgePoints);

            // Improving the best rating pays only the newly-earned star; a worse re-run pays nothing.
            server.ReportMinigameResultForTest("Gamer", "asteroid_dodger", rating: 3);
            Assert.Equal(15, p.State.KnowledgePoints);
            server.ReportMinigameResultForTest("Gamer", "asteroid_dodger", rating: 1);
            Assert.Equal(15, p.State.KnowledgePoints);

            // A different game has its own ledger; incomplete runs and missing keys never pay.
            server.ReportMinigameResultForTest("Gamer", "star_racer", rating: 1);
            Assert.Equal(20, p.State.KnowledgePoints);
            server.ReportMinigameResultForTest("Gamer", "star_racer", rating: 3, completed: false);
            Assert.Equal(20, p.State.KnowledgePoints);
            server.ReportMinigameResultForTest("Gamer", "", rating: 3);
            Assert.Equal(20, p.State.KnowledgePoints);
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
