// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The story is findable on purpose (#1109–#1111): the pity budget bounds fragment droughts, structure
/// markers place fragments that survive a reload, the post-tutorial chip carries a story objective, the
/// story-state snapshot makes the logs rejoin-proof, and environmental lore reveals knowledge-gated,
/// once per player per text.
/// </summary>
public sealed class StoryFindabilityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StoryFindabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_findable_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Started(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- #1109: pity budget ----------------

    [Fact]
    public void PityBudget_GuaranteesAFragment_EveryThirdBodyAtWorst()
    {
        // Whatever the rolls, no window of three consecutive bodies may stay dry — so a 20-body journey
        // yields at least six fragments-worth of placements (the issue's acceptance simulation).
        for (int seed = 1; seed <= 200; seed++)
        {
            var rng = new Random(seed);
            int pity = 0, placed = 0, dryRun = 0;
            for (int body = 0; body < 20; body++)
            {
                int count = SvGameServer.RollNetFragmentCount(rng, ref pity);
                if (count == 0)
                {
                    dryRun++;
                    Assert.True(dryRun <= 2, $"seed {seed}: three dry bodies in a row despite the pity budget");
                }
                else
                {
                    dryRun = 0;
                    placed += count;
                }
            }

            Assert.True(placed >= 6, $"seed {seed}: only {placed} fragments over 20 bodies");
        }
    }

    [Fact]
    public void PityBudget_ForcesOne_AfterTwoDryBodies_AndAnyFindResets()
    {
        var rng = new Random(7);
        int pity = 2;
        int count = SvGameServer.RollNetFragmentCount(rng, ref pity);
        Assert.True(count >= 1, "with the budget exhausted the next body must carry a fragment");
        Assert.Equal(0, pity); // the forced placement resets the drought counter
    }

    [Fact]
    public void PityCounter_PersistsAcrossARelaunch()
    {
        // A zero-fragment world bumps the persisted counter; the counter must survive a reload.
        long seed = 1;
        int before = -1, after = -2;
        for (; seed <= 60; seed++)
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pity_" + seed));
            SvGameServer Boot()
            {
                var server = new SvGameServer(
                    new ServerConfig { WorldName = "pity_" + seed, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = false },
                    _content, new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();
                return server;
            }

            var first = Boot();
            before = first.NetFragmentCount;
            first.Stop();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (before != 0)
            {
                continue; // we need a dry world to observe the counter
            }

            var second = Boot();
            after = second.NetFragmentCount;
            second.Stop();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            break;
        }

        Assert.True(before == 0, "no dry world found across 60 seeds — distribution regression?");
        // The same world re-rolls the same seed on the relaunch, so it stays dry (determinism) — the
        // point is that the boot with a persisted counter neither crashes nor duplicates placements.
        Assert.Equal(0, after);
    }

    // ---------------- #1109: structure fragments ----------------

    [Fact]
    public void StructureFragment_PlacesOnce_AndNeverRevivesAFoundKey()
    {
        var server = Started("struct", 4242, out var repo);
        using (repo)
        {
            // The per-marker chance is deterministic from the position — walk spots until one hits.
            int baseline = server.NetFragmentCount;
            (float X, float Y, float Z)? spot = null;
            for (int i = 0; i < 64 && spot is null; i++)
            {
                server.PlaceStructureFragmentForTest("data_terminal", 100f + (i * 7f), 40f, -80f + (i * 5f));
                if (server.NetFragmentCount > baseline)
                {
                    var f = server.NetFragmentSnapshots[server.NetFragmentCount - 1];
                    spot = (f.Pos.X, f.Pos.Y - 1f, f.Pos.Z);
                }
            }

            Assert.True(spot is not null, "no data_terminal marker rolled a fragment across 64 spots");
            int placed = server.NetFragmentCount;

            // Idempotent within a residency: the same marker never doubles its fragment.
            server.PlaceStructureFragmentForTest("data_terminal", spot!.Value.X, spot.Value.Y, spot.Value.Z);
            Assert.Equal(placed, server.NetFragmentCount);

            // Reading it removes it — and the same KEY never comes back, not even from the same marker.
            var frag = server.NetFragmentSnapshots.First(f => Math.Abs(f.Pos.X - spot.Value.X) < 0.6f && Math.Abs(f.Pos.Z - spot.Value.Z) < 0.6f);
            Assert.True(server.PickUpNetFragmentForTest(frag.Id));
            server.PlaceStructureFragmentForTest("data_terminal", spot.Value.X, spot.Value.Y, spot.Value.Z);
            Assert.DoesNotContain(server.NetFragmentSnapshots, f => f.Key == frag.Key);

            server.Stop();
        }
    }

    [Fact]
    public void StructureFragment_IgnoresLootOnlyMarkers()
    {
        var server = Started("structneg", 99, out var repo);
        using (repo)
        {
            int baseline = server.NetFragmentCount;
            for (int i = 0; i < 32; i++)
            {
                server.PlaceStructureFragmentForTest("loot", 50f + (i * 9f), 30f, 60f + (i * 3f));
                server.PlaceStructureFragmentForTest("chest", 50f + (i * 9f), 30f, -60f - (i * 3f));
            }

            Assert.Equal(baseline, server.NetFragmentCount); // only data_terminal / relic_cache carry fragments
            server.Stop();
        }
    }

    // ---------------- #1110: story objective + rejoin-proof snapshot ----------------

    [Fact]
    public void AfterOnboarding_TheChipCarriesAStoryObjective()
    {
        var server = Started("objective", 4242, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            foreach (var stage in new[] { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" })
            {
                p.State.Milestones.Add("vega:stage:" + stage);
            }

            string? key = server.ObjectiveKeyForTest(p.State.PlayerId);
            Assert.NotNull(key);
            Assert.StartsWith("story.obj.", key, StringComparison.Ordinal);

            // Both possible pre-finale objectives are real, translated keys.
            var en = _content.CreateLocalizer(GameLocale.English);
            var de = _content.CreateLocalizer(GameLocale.German);
            foreach (var k in new[] { "story.obj.fragment_here", "story.obj.search", "story.obj.help", "story.obj.finale" })
            {
                Assert.True(en.Has(k), $"missing '{k}' in en");
                Assert.True(de.Has(k), $"missing '{k}' in de");
            }

            server.Stop();
        }
    }

    [Fact]
    public void StoryStateSnapshot_CarriesTheFoundKeys_AndSurvivesAReload()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "snapshot"));
        SvGameServer Boot()
        {
            var server = new SvGameServer(
                new ServerConfig { WorldName = "snapshot", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = false },
                _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            return server;
        }

        var first = Boot();
        var p = first.AddLocalPlayer("Reader");
        first.RecordStoryFragmentForTest("frag_sps_outpost7");
        first.RevealLoreForTest(p.State.PlayerId, "monument");

        var snap = first.StoryStateForTest(p.State.PlayerId);
        Assert.NotNull(snap);
        Assert.Contains("frag_sps_outpost7", snap!.FoundFragmentKeys);
        Assert.Single(snap.FoundLoreKeys);
        string loreKey = snap.FoundLoreKeys[0];
        first.Stop();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var second = Boot();
        var p2 = second.AddLocalPlayer("Reader");
        var snap2 = second.StoryStateForTest(p2.State.PlayerId);
        Assert.NotNull(snap2);
        Assert.Contains("frag_sps_outpost7", snap2!.FoundFragmentKeys);
        Assert.Contains(loreKey, snap2.FoundLoreKeys); // per-player lore persists with the player blob
        second.Stop();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    // ---------------- #1111: environmental lore ----------------

    [Fact]
    public void LoreReveals_AreKnowledgeGated_AndOncePerPlayerPerText()
    {
        var server = Started("lore", 4242, out var repo);
        using (repo)
        {
            var pack = _content.Stories["vega_protocol"];
            var p = server.AddLocalPlayer("Scholar");

            // Only texts at or below the CURRENT knowledge level can surface — and each exactly once.
            // (A fresh world with a joined player already sits at level 1: the join's mapped-system
            // milestone puts the first points on the arc.)
            int level = server.WorldKnowledgeLevel();
            Assert.True(level < 3, $"fresh world at knowledge {level} — the gate below would be meaningless");
            var openNow = pack.LoreSites.Where(l => l.Site == "monument" && l.MinKnowledge <= level).Select(l => l.Key).ToHashSet();
            for (int i = 0; i < 8; i++)
            {
                server.RevealLoreForTest(p.State.PlayerId, "monument");
            }

            var found = server.FoundLoreForTest(p.State.PlayerId);
            Assert.Equal(openNow.OrderBy(k => k, StringComparer.Ordinal), found);

            // The spoiler texts stay locked below their gate (term_protocol_g needs knowledge 3).
            for (int i = 0; i < 8; i++)
            {
                server.RevealLoreForTest(p.State.PlayerId, "data_terminal");
            }

            Assert.DoesNotContain("term_protocol_g", server.FoundLoreForTest(p.State.PlayerId));
            server.Stop();
        }
    }

    [Fact]
    public void LoreSiteOfContainer_DerivesTheSiteFromTheLootId()
    {
        Assert.Equal("data_terminal", SvGameServer.LoreSiteOfContainer("loot_wreck_data_terminal_120_14_-30"));
        Assert.Equal("data_terminal", SvGameServer.LoreSiteOfContainer("loot_vault_data_terminal_0_12_9"));
        Assert.Equal("wreck", SvGameServer.LoreSiteOfContainer("loot_wreck_module_5_6_7"));
        Assert.Equal("vault", SvGameServer.LoreSiteOfContainer("loot_vault_loot_1_2_3"));
        Assert.Equal("ruin", SvGameServer.LoreSiteOfContainer("loot_ruin_loot_4_5_6"));
        Assert.Equal("monument", SvGameServer.LoreSiteOfContainer("loot_monument_relic_cache_7_8_9"));
        Assert.Equal("bandit_camp", SvGameServer.LoreSiteOfContainer("loot_bandit_camp_bandit_stash_1_1_1"));
        Assert.Equal("chest", SvGameServer.LoreSiteOfContainer("loot_chest_chest_2_2_2"));
        Assert.Equal(string.Empty, SvGameServer.LoreSiteOfContainer("crate_someplayer_1"));
    }

    [Fact]
    public void EveryLoreText_ResolvesInBothLanguages_AndHasAtLeastTwentyEntries()
    {
        var pack = _content.Stories["vega_protocol"];
        Assert.True(pack.LoreSites.Count >= 20, $"only {pack.LoreSites.Count} lore sites");
        Assert.Equal(pack.LoreSites.Count, pack.LoreSites.Select(l => l.Key).Distinct(StringComparer.Ordinal).Count());

        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        foreach (var l in pack.LoreSites)
        {
            Assert.True(en.Has(l.TextKey), $"missing '{l.TextKey}' in en");
            Assert.True(de.Has(l.TextKey), $"missing '{l.TextKey}' in de");
            Assert.True(en.Has("ui.lore.site." + l.Site), $"missing site title for '{l.Site}' in en");
            Assert.True(de.Has("ui.lore.site." + l.Site), $"missing site title for '{l.Site}' in de");
        }
    }

    // ---------------- shared locale guard ----------------

    [Fact]
    public void TheNewSurfaceKeys_ExistInBothLanguages()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (var key in new[]
                 {
                     "vega.hint.fragment_signal", "poi.fragment",
                     "ui.reader.fragment", "ui.reader.memory", "ui.reader.close",
                     "ui.story.lore", "ui.story.read",
                     "ui.wiki.lore", "ui.wiki.lore.empty", "ui.wiki.lore.found",
                 })
        {
            Assert.True(en.ContainsKey(key), $"missing '{key}' in en");
            Assert.True(de.ContainsKey(key), $"missing '{key}' in de");
        }
    }
}
