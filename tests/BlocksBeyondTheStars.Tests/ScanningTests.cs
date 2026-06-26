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

    [Fact]
    public void Blueprint_RequiresKnowledge_InAdditionToMaterials()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eng");
            // detoxifier unlockCost: data_fragment x2, iron_plate x12, cable x4; knowledgeCost 6.
            p.State.Inventory.Add("data_fragment", 2, 99);
            p.State.Inventory.Add("iron_plate", 12, 99);
            p.State.Inventory.Add("cable", 4, 99);

            // No knowledge yet → rejected even with the materials.
            server.UnlockBlueprint("Eng", "detoxifier");
            Assert.DoesNotContain("detoxifier", p.State.UnlockedBlueprints);

            // Research enough, then it unlocks. Knowledge is a permanent THRESHOLD (item 11): it gates the
            // unlock but is NOT spent — only the research materials are consumed.
            p.State.KnowledgePoints = 6;
            server.UnlockBlueprint("Eng", "detoxifier");
            Assert.Contains("detoxifier", p.State.UnlockedBlueprints);
            Assert.Equal(6, p.State.KnowledgePoints);              // knowledge never goes away
            Assert.Equal(0, p.State.Inventory.CountOf("cable"));   // but the materials are spent
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
