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
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// NPC story threads (#1112): a settlement NPC who KNOWS something hands over a fragment or rumour on
/// greeting — gated by relationship + world knowledge, once per player, deterministic (works with AI off).
/// </summary>
public sealed class NpcThreadTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcThreadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npcthread_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>An inhabited settlement with a vendor, the player standing at them (AI off).</summary>
    private SvGameServer StartedWithVendor(out SqliteWorldRepository repo, out BlocksBeyondTheStars.GameServer.PlayerSession player)
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, $"thread_{seed}"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = $"thread_{seed}",
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = true,
                PlaceWrecks = false,
                AiLevel = AiLevel.Off,
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            if (server.HasSettlement && !server.SettlementRuined
                && server.NpcSnapshots.Any(n => n.Role == "vendor"))
            {
                player = server.AddLocalPlayer("Friend");
                player.State.Position = server.NpcSnapshots.First(n => n.Role == "vendor").Home;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with a vendor found across 120 seeds.");
    }

    /// <summary>Makes the vendor "know" the player at the relationship value the thread requires — written
    /// through the same npcKey the greeting resolves, exactly as repeated trades would produce it.</summary>
    private static void Befriend(SvGameServer server, PlayerState p, int value)
    {
        string key = server.NpcKeyForTest(p.PlayerId, "vendor")!;
        Assert.NotNull(key);
        p.NpcMemory[key] = new NpcRelationship { Role = "vendor", Value = value };
    }

    [Fact]
    public void TheSettlerLegend_FiresOnce_AtKnownStanding_AndHandsOverItsFragment()
    {
        var server = StartedWithVendor(out var repo, out var player);
        using (repo)
        {
            var p = player.State;

            // A fresh world with a joined player already sits at knowledge level 1 (the join's
            // mapped-system milestone), which is exactly the settler legend's gate — the RELATIONSHIP
            // is what still holds it back (see AStranger_GetsNoThread for that half).
            Assert.True(server.WorldKnowledgeLevel() >= 1);
            Befriend(server, p, 20); // tier "known"
            var (_, fragmentsBefore, _, _, _, _) = server.StorySnapshot;

            string line = server.NpcThreadLineForTest(p.PlayerId, "vendor")!;
            Assert.False(string.IsNullOrEmpty(line), "the settler-legend thread should fire");

            var (_, fragmentsAfter, _, _, _, _) = server.StorySnapshot;
            Assert.Equal(fragmentsBefore + 1, fragmentsAfter); // frag_settler_legend was handed over

            // Once per player: the same vendor (and any other) never repeats the thread.
            Assert.Equal(string.Empty, server.NpcThreadLineForTest(p.PlayerId, "vendor"));
            server.Stop();
        }
    }

    [Fact]
    public void AStranger_GetsNoThread_RegardlessOfKnowledge()
    {
        var server = StartedWithVendor(out var repo, out var player);
        using (repo)
        {
            Assert.True(server.WorldKnowledgeLevel() >= 1); // the gate the stranger DOES clear
            Assert.Equal(string.Empty, server.NpcThreadLineForTest(player.State.PlayerId, "vendor"));
            server.Stop();
        }
    }

    [Fact]
    public void ThePack_DeclaresTheThreads_AndTheirTextsResolve()
    {
        var pack = _content.Stories["vega_protocol"];
        Assert.True(pack.NpcThreads.Count >= 3, $"only {pack.NpcThreads.Count} npc threads");
        var en = _content.CreateLocalizer(Shared.Localization.GameLocale.English);
        var de = _content.CreateLocalizer(Shared.Localization.GameLocale.German);
        foreach (var t in pack.NpcThreads)
        {
            Assert.True(en.Has(t.TextKey), $"missing '{t.TextKey}' in en");
            Assert.True(de.Has(t.TextKey), $"missing '{t.TextKey}' in de");
            if (!string.IsNullOrEmpty(t.FragmentKey))
            {
                Assert.Contains(pack.Fragments, f => f.Key == t.FragmentKey);
            }
        }
    }
}
