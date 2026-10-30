// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Several bases per world (#2996), base discovery (#2997), the planet scan's settlers (#2998) and the base yield (#2999):
/// the world rule caps the cores per body and player, touching bases are fine, a thing inside two bases of one group counts
/// for the nearest core, the residents are capped per body, renaming goes by the core cell; a stranger's base is in nobody's
/// list until found, an ally's always; the settlers sort most bases first; the yield fills from the wall clock up to the
/// capacity and is collected at the core.
/// </summary>
public sealed class SeveralBasesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SeveralBasesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bases_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string tag = "bases", int basesPerWorld = BaseRules.BasesPerWorldDefault,
        int yieldPercent = BaseRules.YieldDefaultPercent)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.BasesPerWorld = basesPerWorld;
        config.Rules.BaseYieldPercent = yieldPercent;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Builder(SvGameServer server, string name = "Builder")
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.Inventory.Add("base_core", 16, 16);
        return p;
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAlliance(a, b);
        server.RespondAlliance(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    // ---------------- #2996 several bases ----------------

    [Fact]
    public void FiveTouchingCores_FoundFiveBases_TheSixthIsRefused()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Builder(server);
            for (int i = 0; i < 6; i++)
            {
                server.PlaceBlock("Builder", 1 + i, 200, 0, "base_core"); // side by side: touching is fine
            }

            Assert.Equal(BaseRules.BasesPerWorldDefault, server.BaseSnapshots.Count);
            Assert.True(server.World.GetBlock(new Vector3i(6, 200, 0)).IsAir); // the sixth was refused before anything was placed
            Assert.Equal(BaseRules.BasesPerWorldDefault, repo.ListAllBases().Count);
        }
    }

    [Fact]
    public void RenamingByTheCoreCell_RenamesThatBase()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            server.PlaceBlock("Builder", 3, 200, 0, "base_core");

            server.RenameBaseAtForTest(p, new Vector3i(3, 200, 0), "Second");

            Assert.Equal(new[] { "Builder's Base", "Second" }, server.BaseSnapshots.OrderBy(b => b.Id).Select(b => b.Name).ToArray());
        }
    }

    [Fact]
    public void AnOldRenameWithoutACell_StillRenamesTheFirstBase()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            server.PlaceBlock("Builder", 3, 200, 0, "base_core");

            server.SetBaseNameForTest(p, server.ActiveLocationId, "Home");

            Assert.Equal("Home", server.BaseSnapshots.OrderBy(b => b.Id).First().Name);
        }
    }

    [Fact]
    public void AThingBetweenTwoOwnCores_CountsForTheNearest_AStrangersCoreTakesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var stranger = server.AddLocalPlayer("Stranger");
            var a = new Vector3i(0, 200, 0);
            var b = new Vector3i(20, 200, 0);
            server.PlaceBaseForTest(me, a);
            server.PlaceBaseForTest(me, b);
            var nearB = new Vector3i(15, 200, 0);

            Assert.True(server.BaseOwnsCellForTest(b, nearB));
            Assert.False(server.BaseOwnsCellForTest(a, nearB));

            server.PlaceBaseForTest(stranger, new Vector3i(14, 200, 0));
            Assert.True(server.BaseOwnsCellForTest(b, nearB)); // a stranger's core beside it never pulls the bed away
        }
    }

    [Fact]
    public void ABedOutsideTheNearerBasesIndex_StaysWithTheBaseThatSeesIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var low = new Vector3i(0, 64, 0);
            var tower = new Vector3i(40, 100, 0);
            server.PlaceBaseForTest(me, low);
            server.PlaceBaseForTest(me, tower);
            var bed = new Vector3i(38, 66, 0); // nearer to the tower's core, but 34 below it — outside its ±32 index window

            Assert.True(server.BaseOwnsCellForTest(low, bed)); // the low base keeps it: the tower never reads that cell
        }
    }

    [Fact]
    public void ResidentsAreCappedPerBody_EachBaseKeepsItsFounder()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var a = new Vector3i(0, 200, 0);
            var b = new Vector3i(60, 200, 0);
            server.PlaceBaseForTest(me, a);
            server.PlaceBaseForTest(me, b);

            server.SeedBasePlanForTest(a, 20);
            Assert.Equal(BaseRules.ResidentCapPerBody - 20, server.BaseResidentRoomForTest(b));

            server.SeedBasePlanForTest(a, BaseRules.ResidentCapPerBody);
            Assert.Equal(1, server.BaseResidentRoomForTest(b)); // the founder always comes
        }
    }

    // ---------------- #2997 discovery ----------------

    [Fact]
    public void AStrangersBase_IsHidden_UntilWalkedNear_AndStaysFound()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var guest = server.AddLocalPlayer("Guest");
            server.PlaceBaseForTest(owner, new Vector3i(0, 200, 0));
            guest.State.Position = new Vector3f(400, 200, 400);
            server.RunBaseDiscoveryForTest();
            Assert.Empty(server.BaseListForTest(guest));

            guest.State.Position = new Vector3f(30, 200, 20);
            server.RunBaseDiscoveryForTest();

            var seen = Assert.Single(server.BaseListForTest(guest));
            Assert.Equal(NetBase.RelationOther, seen.Relation);
            Assert.Equal(0, seen.YieldReady);                       // the yield readout is the owner's only
            Assert.Single(guest.State.DiscoveredBases);
            Assert.Single(repo.LoadPlayer("Guest")!.DiscoveredBases); // saved: a reconnect keeps it
            Assert.Equal(NetBase.RelationOwn, Assert.Single(server.BaseListForTest(owner)).Relation);
        }
    }

    [Fact]
    public void AnAlliesBase_IsAlwaysListed_AndLeavesWithTheAlliance()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var friend = server.AddLocalPlayer("Friend");
            server.PlaceBaseForTest(owner, new Vector3i(0, 200, 0));
            friend.State.Position = new Vector3f(900, 200, 900);
            Ally(server, "Owner", "Friend");

            Assert.Equal(NetBase.RelationAlly, Assert.Single(server.BaseListForTest(friend)).Relation);

            server.DissolveAllianceForTest("Owner", "Friend");
            Assert.Empty(server.BaseListForTest(friend));
        }
    }

    [Fact]
    public void AQuantumScan_FindsEveryBaseOfTheBody()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            var guest = server.AddLocalPlayer("Guest");
            server.PlaceBaseForTest(owner, new Vector3i(0, 200, 0));
            server.PlaceBaseForTest(owner, new Vector3i(90, 200, 0));
            guest.State.Position = new Vector3f(900, 200, 900);

            Assert.Equal(2, server.DiscoverAllBasesOnForTest(guest, server.ActiveLocationId));
            Assert.Equal(2, server.BaseListForTest(guest).Count);
            Assert.Equal(0, server.DiscoverAllBasesOnForTest(guest, server.ActiveLocationId)); // nothing new the second time
        }
    }

    // ---------------- #2998 settlers ----------------

    [Fact]
    public void Settlers_SortMostBasesFirst_AndMarkTheViewer()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var lyx = server.AddLocalPlayer("Lyx");
            var jus = server.AddLocalPlayer("Jus");
            server.PlaceBaseForTest(jus, new Vector3i(0, 200, 0));
            server.PlaceBaseForTest(lyx, new Vector3i(50, 200, 0));
            server.PlaceBaseForTest(lyx, new Vector3i(100, 200, 0));
            server.PlaceBaseForTest(lyx, new Vector3i(150, 200, 0));

            var settlers = server.PlanetSettlersForTest(server.ActiveLocationId, "Jus");

            Assert.Equal(new[] { "Lyx", "Jus" }, settlers.Select(s => s.Name).ToArray());
            Assert.Equal(3, settlers[0].Bases);
            Assert.Equal(NetBase.RelationOther, settlers[0].Relation);
            Assert.Equal(NetBase.RelationOwn, settlers[1].Relation);
            Assert.Empty(server.PlanetSettlersForTest("no-such-body", "Jus"));
        }
    }

    // ---------------- #2999 yield ----------------

    [Fact]
    public void AnHourFillsTheStore_AtTheBaseRate()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = new Vector3i(0, 200, 0);
            server.PlaceBaseForTest(me, core);
            Assert.Equal(0, server.BaseYieldStatusForTest(me, core).Ready);

            server.ShiftYieldClockForTest(3600);

            var status = server.BaseYieldStatusForTest(me, core);
            Assert.Equal((int)BaseRules.YieldBasePerHour, status.Ready);
            Assert.Equal(BaseRules.YieldCapacity, status.Capacity);
            Assert.All(status.Items, i => Assert.NotNull(_content.GetItem(i.Item)));
        }
    }

    [Fact]
    public void AFullStoreStops_AtTheCapacity()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = new Vector3i(0, 200, 0);
            server.PlaceBaseForTest(me, core);

            server.ShiftYieldClockForTest(100 * 3600);

            Assert.Equal(BaseRules.YieldCapacity, server.BaseYieldStatusForTest(me, core).Ready);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 12)]
    public void TheWorldRuleScalesTheYield(int percent, int afterAnHour)
    {
        var server = Started(out var repo, "yield" + percent, yieldPercent: percent);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = new Vector3i(0, 200, 0);
            server.PlaceBaseForTest(me, core);

            server.ShiftYieldClockForTest(3600);

            Assert.Equal(afterAnHour, server.BaseYieldStatusForTest(me, core).Ready);
        }
    }

    [Fact]
    public void Collecting_MovesTheItemsIntoTheBackpack_OnlyForTheOwner()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var other = server.AddLocalPlayer("Other");
            var core = new Vector3i(0, 200, 0);
            me.State.Position = new Vector3f(1.5f, 200, 1.5f);
            other.State.Position = new Vector3f(1.5f, 200, 1.5f);
            server.PlaceBaseForTest(me, core);
            server.ShiftYieldClockForTest(3600);
            var collect = new YieldIntent
            {
                Action = YieldIntent.ActionCollect,
                Kind = YieldIntent.KindBase,
                BodyId = server.ActiveLocationId,
                CellX = 0,
                CellY = 200,
                CellZ = 0,
            };

            server.YieldForTest(other, collect);
            Assert.Equal(6, server.BaseYieldStatusForTest(me, core).Ready); // a stranger takes nothing

            int before = me.State.Inventory.Slots.Where(s => s != null).Sum(s => s!.Count);
            server.YieldForTest(me, collect);
            int after = me.State.Inventory.Slots.Where(s => s != null).Sum(s => s!.Count);

            Assert.Equal(before + 6, after);
            Assert.Equal(0, server.BaseYieldStatusForTest(me, core).Ready);
        }
    }

    [Fact]
    public void MiningTheCore_HandsTheRestToTheOwner()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "base_core");
            server.ShiftYieldClockForTest(3600);
            int before = p.State.Inventory.Slots.Where(s => s != null).Sum(s => s!.Count);

            server.MineBlock("Builder", 1, 200, 0);

            int after = p.State.Inventory.Slots.Where(s => s != null).Sum(s => s!.Count);
            Assert.Equal(before + 1 + 6, after); // the core itself and the six gathered items
        }
    }

    [Fact]
    public void ChangingTheYieldRule_NeverPaysBackwards()
    {
        var server = Started(out var repo, "yieldrule", yieldPercent: 0);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = new Vector3i(0, 200, 0);
            server.PlaceBaseForTest(me, core);
            server.ShiftYieldClockForTest(3600); // an hour with the yield off

            server.SetWorldRulesForTest(me, new SetWorldRulesIntent { BaseYieldPercent = 100 });
            Assert.Equal(0, server.BaseYieldStatusForTest(me, core).Ready);

            server.ShiftYieldClockForTest(3600);
            Assert.Equal((int)BaseRules.YieldBasePerHour, server.BaseYieldStatusForTest(me, core).Ready);
        }
    }

    [Theory]
    [InlineData("--base-yield", "300")]
    [InlineData("--base-yield", "-1")]
    [InlineData("--bases-per-world", "4")]
    [InlineData("--bases-per-world", "0")]
    public void LaunchOptions_TakeOnlyTheOfferedSteps(string option, string value)
    {
        var config = new ServerConfig();
        Assert.Empty(config.ApplyCommandLine(new[] { option, value }));
        Assert.Equal(BaseRules.BasesPerWorldDefault, config.Rules.BasesPerWorld);
        Assert.Equal(BaseRules.YieldDefaultPercent, config.Rules.BaseYieldPercent);
    }

    // ---------------- BaseRules ----------------

    [Fact]
    public void Accrue_StartsTheClock_CarriesTheFraction_AndNeverGoesBack()
    {
        var store = new BlocksBeyondTheStars.Shared.State.BaseYieldStore();
        Assert.Equal(0, BaseRules.Accrue(store, 1000, 6, _ => "stone"));       // the first read starts the clock
        Assert.Equal(0, BaseRules.Accrue(store, 1000 + 300, 6, _ => "stone")); // 5 min at 6/h = 0.5
        Assert.Equal(1, BaseRules.Accrue(store, 1000 + 600, 6, _ => "stone")); // another 0.5 → the first item
        Assert.Equal(0, BaseRules.Accrue(store, 500, 6, _ => "stone"));        // a clock that went back credits nothing
        Assert.Equal(1, store.CountReady());
    }

    [Fact]
    public void PickWeighted_IsDeterministic_AndFollowsTheWeights()
    {
        var table = BaseRules.StationYield;
        var first = Enumerable.Range(0, 200).Select(i => BaseRules.PickWeighted(table, "seed", i)).ToArray();
        var again = Enumerable.Range(0, 200).Select(i => BaseRules.PickWeighted(table, "seed", i)).ToArray();
        Assert.Equal(first, again);
        Assert.True(first.Count(x => x == "ice") > first.Count(x => x == "data_fragment"));
    }
}
