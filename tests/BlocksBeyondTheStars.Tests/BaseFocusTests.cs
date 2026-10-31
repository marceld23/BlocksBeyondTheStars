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
/// A base's focus (#3036–#3040, Justus 2026-10-30): one per base, switched for free and saved with the core's store; travelling
/// merchants come only to a Market base; a Miner gathers by the depth of the players' shaft below the core, faster and from
/// deeper veins; a Lumberjack base wants a woodcutter first and gathers wood; a Radio base hears its whole star system.
/// </summary>
public sealed class BaseFocusTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BaseFocusTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_basefocus_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    private SvGameServer Started(out SqliteWorldRepository repo, string startPlanet = "rocky")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "focus"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "focus",
            Seed = 4242,
            StartPlanet = startPlanet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, st, repo);
        server.Start();
        return server;
    }

    private static Vector3i CoreNear(PlayerSession p)
        => new((int)Math.Floor(p.State.Position.X) + 3, (int)Math.Floor(p.State.Position.Y) + 4, (int)Math.Floor(p.State.Position.Z));

    private static YieldStatus SetFocus(SvGameServer server, PlayerSession p, Vector3i core, string focus)
    {
        server.YieldForTest(p, new YieldIntent
        {
            Action = YieldIntent.ActionFocus,
            Kind = YieldIntent.KindBase,
            BodyId = p.CurrentLocationId,
            CellX = core.X,
            CellY = core.Y,
            CellZ = core.Z,
            Focus = focus,
        });
        return server.BaseYieldStatusForTest(p, core);
    }

    [Fact]
    public void TheFocus_SwitchesForFree_AndIsSavedWithTheCore()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            Assert.Equal(BaseFocus.None, server.BaseFocusForTest(me, core));

            var status = SetFocus(server, me, core, BaseFocus.Market);
            Assert.Equal(BaseFocus.Market, status.Focus);
            Assert.Equal(BaseFocus.Market, server.BaseFocusForTest(me, core));
            Assert.Contains(repo.LoadMetadata()!.BaseYields.Values, s => s.Focus == BaseFocus.Market); // saved at once

            Assert.Equal(BaseFocus.Radio, SetFocus(server, me, core, BaseFocus.Radio).Focus); // and again, for free
            Assert.Equal(BaseFocus.None, SetFocus(server, me, core, "castle").Focus);          // unknown = none
        }
    }

    [Fact]
    public void AnotherPlayer_CannotSetMyBasesFocus()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var stranger = server.AddLocalPlayer("Stranger");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);

            SetFocus(server, stranger, core, BaseFocus.Market);

            Assert.Equal(BaseFocus.None, server.BaseFocusForTest(me, core));
        }
    }

    [Fact]
    public void OnlyAMarketBase_GetsTravellingMerchants()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            int baseId = server.BaseSnapshots.Single().Id;

            var plain = Enumerable.Range(0, 200).Select(_ => server.PickGuestKindForTest(baseId)).ToList();
            Assert.DoesNotContain("merchant", plain); // #3036: "bases should not spawn traders right away"

            SetFocus(server, me, core, BaseFocus.Market);
            var market = Enumerable.Range(0, 200).Select(_ => server.PickGuestKindForTest(baseId)).ToList();
            Assert.True(market.Count(k => k == "merchant") > 100, "at a Market base the merchant is the most common guest");
        }
    }

    [Fact]
    public void AMinerBase_GathersFasterTheDeeperTheShaftBelowItsCore()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            var flat = SetFocus(server, me, core, BaseFocus.Miner);
            Assert.Equal(0, flat.Depth);

            // A shaft 40 deep below the core (an owned edit — what a player's dig leaves); a server-internal one deeper down
            // (worldgen, regrowth) does not count.
            repo.SetBlock(me.CurrentLocationId, new Vector3i(core.X, core.Y - 40, core.Z + 2), 0, owner: me.State.PlayerId);
            repo.SetBlock(me.CurrentLocationId, new Vector3i(core.X, core.Y - 90, core.Z), 0, owner: string.Empty);
            server.ForgetBaseShaftsForTest();

            var deep = server.BaseYieldStatusForTest(me, core);
            Assert.Equal(40, deep.Depth);
            Assert.Equal((float)(BaseRules.YieldPerHour(deep.Helpers, BaseRules.YieldDefaultPercent) * BaseFocus.MinerRateFactor(40)), deep.PerHour, 3);
            Assert.True(deep.PerHour > flat.PerHour);
            Assert.True(deep.NextOre.Length == 0 || deep.NextOreDepth > deep.Depth, "the next ore lies deeper than the shaft");
        }
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(32, 2.0)]
    [InlineData(48, 2.5)]
    [InlineData(500, 3.0)]
    public void TheMinersRate_GrowsWithTheDepth_UpToItsCap(int depth, double factor)
    {
        Assert.Equal(factor, BaseFocus.MinerRateFactor(depth), 3);
        Assert.True(BaseFocus.MinerReaches(depth, 0));
        Assert.False(BaseFocus.MinerReaches(depth, depth + 10));
    }

    [Fact]
    public void ALumberjackBase_GivesAFreeResidentTheWoodcutterJobFirst()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Homesteader");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            int baseId = server.BaseSnapshots.Single().Id;
            SetFocus(server, me, core, BaseFocus.Lumberjack);

            // Three machines bring the first settler — a workbench would make a craftsman, the Lumberjack base wants wood first.
            var workbench = _content.GetBlock("workbench")!.NumericId;
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 2, core.Y, core.Z), workbench, 0, 0, 0, "Homesteader");
            server.World.SetBlock(new Vector3i(core.X + 1, core.Y, core.Z + 1), _content.GetBlock("forge")!.NumericId, 0, 0, 0, "Homesteader");
            server.ScanBaseLifeForTest();

            Assert.Contains(server.BaseResidentsForTest(baseId), r => r.Job == SvGameServer.BaseLumberjackJob);
        }
    }

    [Fact]
    public void ALumberjackBase_OnABarrenWorld_SaysSo_AndKeepsGatheringOre()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            var status = SetFocus(server, me, core, BaseFocus.Lumberjack);
            server.ShiftYieldClockForTest(3600);
            status = server.BaseYieldStatusForTest(me, core);

            if (status.NoTrees)
            {
                Assert.DoesNotContain(status.Items, i => i.Item == "wood_log");
            }
            else
            {
                Assert.All(status.Items, i => Assert.Contains(BaseFocus.LumberjackYield, w => w.Key == i.Item));
            }
        }
    }

    [Fact]
    public void ARadioBase_LetsItsOwnerHearTheirSystem_WithoutARadio()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var me = server.AddLocalPlayer("Me");
            var core = CoreNear(me);
            server.PlaceBaseForTest(me, core);
            string here = me.CurrentLocationId;
            Assert.False(server.NpcRadioReachesForTest(me, here)); // no radio worn

            SetFocus(server, me, core, BaseFocus.Radio);

            Assert.True(server.NpcRadioReachesForTest(me, here));
        }
    }
}
