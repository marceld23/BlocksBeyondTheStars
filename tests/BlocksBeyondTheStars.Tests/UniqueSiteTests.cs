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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// One-of-a-kind sites + peaceful space encounters (#1129, D6): the Singing Shrine and the Sealed
/// Observatory stand on seed-pure bodies of the galaxy's FIXED prefix (growth never moves them), trusted
/// NPCs share their legend, "The Long Quiet" derelict is boardable and holds salvage, a drifting life pod
/// is rescued by flying close, and an anomaly pays knowledge + lore on scan. Everything here is peaceful —
/// no encounter is gated by (or gates) any combat rule.
/// </summary>
public sealed class UniqueSiteTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public UniqueSiteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_unique_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, long seed, out SqliteWorldRepository repo, bool growth = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.World.GalaxyGrowth = growth;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Flies a pilot to a body the regular way (temporary jump generator for cross-system hops).</summary>
    private static void TravelTo(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession pilot, string bodyId)
    {
        if (pilot.CurrentLocationId == bodyId)
        {
            return;
        }

        if (!server.Ship.Modules.Contains("jump_generator"))
        {
            server.Ship.Modules.Add("jump_generator");
        }

        server.Travel(pilot.State.PlayerId, bodyId);
        Assert.Equal(bodyId, pilot.CurrentLocationId);
    }

    // ---------------- Site selection ----------------

    [Fact]
    public void SiteBodies_AreSeedPure_Distinct_AndUnmovedByGalaxyGrowth()
    {
        var s1 = NewServer("unique_pick_a", 11, out var repo1, growth: true);
        string shrine, observatory, derelictHost;
        using (repo1)
        {
            shrine = s1.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey)!;
            observatory = s1.UniqueSiteBodyForTest(SvGameServer.ObservatorySiteKey)!;
            derelictHost = s1.UniqueSiteBodyForTest("derelict_host")!;
            Assert.False(string.IsNullOrEmpty(shrine));
            Assert.False(string.IsNullOrEmpty(observatory));
            Assert.False(string.IsNullOrEmpty(derelictHost));
            Assert.NotEqual(shrine, observatory); // two surface legends never share one world

            // Growth appends systems — the picks must not move (the sites are stamped ground by then).
            var scout = s1.AddLocalPlayer("Scout");
            var edge = s1.Galaxy.Systems.First(sys => s1.IsEdgeSystemForTest(sys.Id));
            scout.State.KnownSystems.Remove(edge.Id);
            s1.TryGrowGalaxyForTest(scout, edge.Id);
            Assert.Equal(shrine, s1.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey));
            Assert.Equal(observatory, s1.UniqueSiteBodyForTest(SvGameServer.ObservatorySiteKey));
            s1.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Pure function of the seed: a second save with the same seed picks the same bodies.
        var s2 = NewServer("unique_pick_b", 11, out var repo2);
        using (repo2)
        {
            Assert.Equal(shrine, s2.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey));
            Assert.Equal(observatory, s2.UniqueSiteBodyForTest(SvGameServer.ObservatorySiteKey));
            Assert.Equal(derelictHost, s2.UniqueSiteBodyForTest("derelict_host"));
            s2.Stop();
        }
    }

    // ---------------- The shrine ----------------

    [Fact]
    public void TheShrine_Stamps_WithKeepersAndARelicCache_AndPinsItsSpot()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = NewServer($"unique_shrine_{seed}", seed, out var repo);
            using (repo)
            {
                string body = server.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey)!;
                var pilot = server.AddLocalPlayer("Pilgrim");
                TravelTo(server, pilot, body);

                var record = server.PlacementRecordsForTest.FirstOrDefault(r =>
                    r.Kind == "unique:" + SvGameServer.ShrineSiteKey);
                Assert.NotNull(record); // decided either way — placed or an explicit skip
                if (!record!.Placed)
                {
                    server.Stop();
                    continue; // an all-lava/ocean surface — try the next seed for the full assertions
                }

                Assert.True(server.ShrineKeepersForTest >= 6, "the shrine keeps its keepers");
                Assert.Contains(server.Containers, c =>
                    c.Id.StartsWith("loot_alien_shrine_relic_cache", StringComparison.Ordinal));
                server.Stop();
                return;
            }
        }

        Assert.Fail("no seed in 1..40 placed the shrine — the search should almost always succeed");
    }

    // ---------------- Hints ----------------

    [Fact]
    public void ATrustedFriend_SharesEachLegendOnce()
    {
        var server = NewServer("unique_hint", 7, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Confidant");

            string first = server.UniqueSiteHintForTest("Confidant");
            Assert.False(string.IsNullOrEmpty(first), "the first legend should be shared");
            string second = server.UniqueSiteHintForTest("Confidant");
            Assert.False(string.IsNullOrEmpty(second), "the second legend should follow");
            Assert.Equal(string.Empty, server.UniqueSiteHintForTest("Confidant")); // all shared now

            Assert.Contains(server.Metadata.RevealedPois, k => k.EndsWith("|uniquesite:" + SvGameServer.ShrineSiteKey, StringComparison.Ordinal));
            Assert.Contains(server.Metadata.RevealedPois, k => k.EndsWith("|uniquesite:" + SvGameServer.ObservatorySiteKey, StringComparison.Ordinal));
            server.Stop();
        }
    }

    // ---------------- The Long Quiet ----------------

    [Fact]
    public void TheLongQuiet_IsBoardableByAnyone_AndHoldsSalvage()
    {
        var server = NewServer("unique_derelict", 5, out var repo);
        using (repo)
        {
            string host = server.UniqueSiteBodyForTest("derelict_host")!;
            var pilot = server.AddLocalPlayer("Salvager");
            TravelTo(server, pilot, host);
            server.EnterSpace("Salvager");

            var contact = server.SpaceEntitiesFor("Salvager").FirstOrDefault(e => e.Id == SvGameServer.DerelictStationId);
            Assert.NotNull(contact);
            Assert.Equal(SvGameServer.DerelictName, contact!.Name);
            Assert.False(contact.Hostile);

            server.ShipMove("Salvager", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            server.BoardStation("Salvager", SvGameServer.DerelictStationId);
            Assert.True(server.InStation("Salvager"));

            var salvage = server.Containers.Where(c => c.Id.StartsWith("loot_derelict_", StringComparison.Ordinal)).ToList();
            Assert.True(salvage.Count >= 3, $"the wreck should hold salvage (found {salvage.Count})");
            Assert.Contains(salvage, c => c.Items.Count > 0);

            // The wreck speaks with its own lore voice, not as a generic terminal.
            Assert.Equal("derelict", SvGameServer.LoreSiteOfContainer(salvage[0].Id));
            server.Stop();
        }
    }

    // ---------------- Space encounters ----------------

    [Fact]
    public void ADriftingPod_IsRescuedByFlyingClose_AndTheSurvivorKnowsYou()
    {
        var server = NewServer("unique_pod", 3, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Rescuer");
            server.EnterSpace("Rescuer");
            string podId = server.SpawnEncounterForTest("Rescuer", 1);
            Assert.False(string.IsNullOrEmpty(podId));

            var pod = server.SpaceEntitiesFor("Rescuer").First(e => e.Id == podId);
            Assert.False(pod.Hostile);
            string survivor = pod.Name;

            server.ShipMove("Rescuer", pod.Position.X, pod.Position.Y, pod.Position.Z - 4f);
            server.TickSpaceEncountersForTest("Rescuer");

            Assert.DoesNotContain(server.SpaceEntitiesFor("Rescuer"), e => e.Id == podId); // taken aboard
            Assert.Equal(2, pilot.State.Inventory.CountOf("gold_ingot")); // the small thank-you
            var rel = Assert.Single(pilot.State.NpcMemory.Values.Where(r => r.Name == survivor));
            Assert.True(rel.Value >= 25, "a rescue makes you KNOWN in one act");
            server.Stop();
        }
    }

    [Fact]
    public void AnAnomaly_PaysKnowledgeAndLore_OnScan()
    {
        var server = NewServer("unique_anomaly", 3, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Surveyor");
            server.EnterSpace("Surveyor");
            string anomalyId = server.SpawnEncounterForTest("Surveyor", 2);
            Assert.False(string.IsNullOrEmpty(anomalyId));

            var result = server.ScanSpaceEntity("Surveyor", anomalyId);
            Assert.True(result.FirstTime);
            Assert.True(result.KnowledgeGained > 0);
            Assert.Equal("anomaly", result.Kind);
            Assert.Contains(pilot.State.Milestones, m => m.StartsWith("lore:anomaly", StringComparison.Ordinal));

            // Once per save per player: a second anomaly still scans, but pays no second windfall.
            var again = server.ScanSpaceEntity("Surveyor", anomalyId);
            Assert.False(again.FirstTime);
            Assert.Equal(0, again.KnowledgeGained);
            server.Stop();
        }
    }

    // ---------------- Content consistency ----------------

    [Fact]
    public void EverySiteLoreText_AndLocaleKey_Resolves()
    {
        var pack = _content.Stories["vega_protocol"];
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);

        foreach (var siteKind in new[] { "alien_shrine", "observatory", "derelict", "anomaly" })
        {
            Assert.Contains(pack.LoreSites, l => l.Site == siteKind); // each site has a voice
            Assert.True(en.Has("ui.lore.site." + siteKind) && de.Has("ui.lore.site." + siteKind),
                $"missing 'ui.lore.site.{siteKind}'");
        }

        foreach (var l in pack.LoreSites)
        {
            Assert.True(en.Has(l.TextKey) && de.Has(l.TextKey), $"missing '{l.TextKey}'");
        }

        foreach (var key in new[]
        {
            "poi.alien_shrine", "poi.observatory", "npc.hint.site", "npc.hint.site_here", "npc.call.rescue",
            "srv.encounter.pod", "srv.encounter.anomaly", "srv.encounter.rescued", "ui.scan.anomaly",
        })
        {
            Assert.True(en.Has(key) && de.Has(key), $"missing '{key}'");
        }
    }
}
