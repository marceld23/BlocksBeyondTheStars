// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Achievements, asked for by a player: "Ich möchte, dass es Erfolge gibt wie 'Baue 5 Eisen ab' und dafür gibt's
/// eine Belohnung." Counters tally on the player, the data table declares the goals and rewards, and a reward
/// that cannot be handed over must DEFER the unlock rather than lose it.
/// </summary>
public sealed class AchievementTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AchievementTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_achv_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "achv")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 5,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TheDataFileLoads_AndEveryEntryIsUsable()
    {
        Assert.NotEmpty(_content.Achievements);
        foreach (var a in _content.Achievements)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Key));
            Assert.False(string.IsNullOrWhiteSpace(a.Counter));
            Assert.True(a.Target >= 1);

            // Every reward must be a real item, or the unlock would hand out nothing.
            foreach (var r in a.Rewards)
            {
                Assert.NotNull(_content.GetItem(r.Item));
                Assert.True(r.Count > 0);
            }
        }
    }

    /// <summary>Every achievement needs a translated name + description in BOTH locales, or the panel shows
    /// raw keys to the player.</summary>
    [Fact]
    public void EveryAchievementIsTranslated()
    {
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = _content.CreateLocalizer(locale);
            foreach (var a in _content.Achievements)
            {
                Assert.NotEqual($"achv.{a.Key}.name", loc.Get($"achv.{a.Key}.name"));
                Assert.NotEqual($"achv.{a.Key}.desc", loc.Get($"achv.{a.Key}.desc"));
            }
        }
    }

    [Fact]
    public void MiningIronAdvancesAndUnlocks_AndPaysTheReward()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

            // "Baue 5 Eisen ab" — iron_5 wants 5 iron_ore and pays 2 iron_plate.
            for (int i = 0; i < 5; i++)
            {
                var pos = new Vector3i(20 + i, 60, 20);
                p.State.Position = new Vector3f(pos.X + 1.2f, pos.Y + 0.5f, pos.Z + 0.5f);
                server.World.SetBlock(pos, _content.GetBlock("iron_ore")!.NumericId);
                server.MineBlock("Justus", pos.X, pos.Y, pos.Z);
            }

            Assert.Contains("iron_5", p.State.Achievements);
            Assert.True(p.State.Inventory.CountOf("iron_plate") >= 2); // the reward was handed over
            Assert.Equal(5, p.State.AchievementCounters["mine:iron_ore"]);
            Assert.Equal(5, p.State.AchievementCounters["mine:any"]); // one event feeds both counters
        }
    }

    [Fact]
    public void AnAchievementIsNeverAwardedTwice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

            for (int i = 0; i < 12; i++)
            {
                var pos = new Vector3i(30 + i, 60, 30);
                p.State.Position = new Vector3f(pos.X + 1.2f, pos.Y + 0.5f, pos.Z + 0.5f);
                server.World.SetBlock(pos, _content.GetBlock("iron_ore")!.NumericId);
                server.MineBlock("Justus", pos.X, pos.Y, pos.Z);
            }

            // Well past the target of 5 — the reward must have been paid exactly once (2 plates, not 2 per swing).
            Assert.Contains("iron_5", p.State.Achievements);
            Assert.Equal(2, p.State.Inventory.CountOf("iron_plate"));
        }
    }

    [Fact]
    public void AnUnaffordableReward_DefersTheUnlockInsteadOfLosingIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;

            // Fill every slot but one, and keep the drill in that one — mining works, but a NEW item type
            // (the iron_plate reward) has nowhere to go.
            var inv = p.State.Inventory;
            int max = _content.MaxStackOf("stone");
            for (int i = 1; i < inv.SlotCount; i++)
            {
                inv.SetSlot(i, new ItemStack("stone", max));
            }

            inv.SetSlot(0, new ItemStack("basic_drill", 1));

            // Mining itself is refused with a full inventory, so drive the counter directly instead.
            p.State.AchievementCounters["mine:iron_ore"] = 5;
            server.SettleAchievementsForTest(p);

            // Not marked earned, and nothing was destroyed.
            Assert.DoesNotContain("iron_5", p.State.Achievements);
            Assert.Equal(0, inv.CountOf("iron_plate"));

            // Make room → the next settle pays out and marks it earned.
            inv.SetSlot(5, null);
            server.SettleAchievementsForTest(p);

            Assert.Contains("iron_5", p.State.Achievements);
            Assert.Equal(2, inv.CountOf("iron_plate"));
        }
    }

    [Fact]
    public void ProgressAndUnlocksSurviveAReload()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AchievementCounters["mine:any"] = 7;
            p.State.Achievements.Add("first_build");
            server.SaveNow();
        }

        var server2 = Started(out var repo2);
        using (repo2)
        {
            var p = server2.AddLocalPlayer("Justus");
            Assert.Equal(7, p.State.AchievementCounters["mine:any"]);
            Assert.Contains("first_build", p.State.Achievements);
        }
    }

    [Fact]
    public void RevisitingTheSameBody_DoesNotFarmTheExplorerAchievements()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            string here = server.ActiveLocationId;

            // First arrival counts…
            p.State.LandedBodies.Remove(here);
            server.MarkArrivedOnBodyForTest(p, here);
            int afterFirst = p.State.AchievementCounters.TryGetValue("visit:body", out int v1) ? v1 : 0;

            // …arriving again does not (hopping between two planets must not farm the explorer achievements).
            server.MarkArrivedOnBodyForTest(p, here);
            server.MarkArrivedOnBodyForTest(p, here);
            int afterRepeat = p.State.AchievementCounters.TryGetValue("visit:body", out int v2) ? v2 : 0;

            Assert.Equal(afterFirst, afterRepeat);
        }
    }

    // --- Late-game counters (#1102) ------------------------------------------------------------------

    /// <summary>The research ladder can never demand more blueprints than the tree holds, and every new
    /// category has its section title translated — both are data-authoring mistakes the panel would show.</summary>
    [Fact]
    public void ResearchTargetsFitTheTree_AndEveryCategoryIsTranslated()
    {
        int blueprints = _content.Blueprints.Count;
        foreach (var a in _content.Achievements.Where(a => a.Counter == AchievementCounters.ResearchAny))
        {
            Assert.True(a.Target <= blueprints, $"'{a.Key}' wants {a.Target} blueprints but the tree has {blueprints}");
        }

        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = _content.CreateLocalizer(locale);
            foreach (var cat in _content.Achievements.Select(a => a.Category).Distinct())
            {
                Assert.NotEqual($"achv.category.{cat}", loc.Get($"achv.category.{cat}"));
            }
        }
    }

    /// <summary>A blueprint researched at the cockpit bumps <c>research:any</c> — the "Researcher" ladder.</summary>
    [Fact]
    public void ResearchingABlueprint_AdvancesTheResearchCounter()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var bp = _content.GetBlueprint("machete")!; // 3 KP, iron plates, no prerequisites
            p.State.KnowledgePoints = bp.KnowledgeCost;
            foreach (var cost in bp.UnlockCost)
            {
                p.State.Inventory.Add(cost.Item, cost.Count, 99);
            }

            server.UnlockBlueprint("Justus", bp.Key);

            Assert.Contains(bp.Key, p.State.UnlockedBlueprints);
            Assert.Equal(1, p.State.AchievementCounters[AchievementCounters.ResearchAny]);
        }
    }

    /// <summary>A first-time scan bumps <c>scan:any</c>; scanning the same subject again does not — the ledger
    /// that gates the knowledge also gates the tally, so the "Scholar" ladder can't be farmed on one rock.</summary>
    [Fact]
    public void FirstScansAdvanceTheScanCounter_RescansDoNot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));

            server.ScanSubject("Justus", "block", "iron_ore");
            server.ScanSubject("Justus", "block", "iron_ore");
            server.ScanSubject("Justus", "block", "stone");

            Assert.Equal(2, p.State.AchievementCounters[AchievementCounters.ScanAny]);
        }
    }

    /// <summary>The counters travel with the achievement list, unclamped, so the Progress page can show the
    /// raw "blocks mined" figure rather than the capped bar value.</summary>
    [Fact]
    public void TheAchievementList_CarriesTheRawCounters()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
            for (int i = 0; i < 12; i++)
            {
                var pos = new Vector3i(40 + i, 60, 40);
                p.State.Position = new Vector3f(pos.X + 1.2f, pos.Y + 0.5f, pos.Z + 0.5f);
                server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);
                server.MineBlock("Justus", pos.X, pos.Y, pos.Z);
            }

            var list = server.AchievementListForTest(p);
            Assert.Equal(12, list.Counters["mine:any"]);                                   // raw, not capped at 10
            Assert.Equal(10, list.Items.Single(a => a.Key == "first_blocks").Progress); // the bar value is capped
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
