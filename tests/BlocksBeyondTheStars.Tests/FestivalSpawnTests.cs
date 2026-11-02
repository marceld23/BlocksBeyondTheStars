// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The festivals' deposit layer (#3006): pumpkins appear near a player on a breathable world while a harvest
/// festival is on — on host blocks under the sky, within the per-pass budget and the per-world cap, broadcast and persisted;
/// a harvested one never regrows; when the festival ends the layer clears its own cells and nothing else.</summary>
public sealed class FestivalSpawnTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A breathable world (the spawner never lays a pumpkin into vacuum) with a grass pad around the player — natural
    /// ground with no owner, since #3020 keeps festival plants off what players built.</summary>
    private static NpcLifeWorld World(string name)
    {
        string planet = Content.Value.Planets.Values.First(p => string.Equals(p.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase) && !p.Void).Key;
        var w = new NpcLifeWorld(Content.Value, name, padHalf: 16, configure: c =>
        {
            c.StartPlanet = planet;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
        });
        for (int dx = -14; dx <= 14; dx++)
        {
            for (int dz = -14; dz <= 14; dz++)
            {
                w.Server.World.SetBlock(new Vector3i(NpcLifeWorld.Cx + dx, w.PadY, NpcLifeWorld.Cz + dz), Content.Value.GetBlock("grass")!.NumericId);
            }
        }

        return w;
    }

    private static ushort Id(string key) => Content.Value.GetBlock(key)!.NumericId.Value;

    /// <summary>#3083: one pumpkin per lay and a wide radius, so a single pass may miss the test's grass pad — run passes
    /// until the next pumpkin stands (a miss does not reset the player's clock, so this takes a pass or two).</summary>
    private static void PassUntilAPumpkin(NpcLifeWorld w, int maxPasses = 40)
    {
        int before = w.Server.FestivalDepositCellsForTest.Count;
        for (int i = 0; i < maxPasses && w.Server.FestivalDepositCellsForTest.Count == before; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.True(w.Server.FestivalDepositCellsForTest.Count > before, $"no festival pumpkin within {maxPasses} passes");
    }

    /// <summary>Mines a cell like a player holding the mine button — a hit, then a tick in which the progress accrues —
    /// standing right beside it.</summary>
    private static void Harvest(NpcLifeWorld w, Vector3i cell)
    {
        w.Player.State.Position = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 1.5f); // within arm's reach
        for (int i = 0; i < 40 && w.Server.World.GetBlock(cell).Value != 0; i++)
        {
            w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
            w.Server.Tick(0.1);
        }

        var refusals = w.Transport.Sent.Select(s => s.Msg).OfType<ActionRejected>().Where(r => r.Action == "mine").Select(r => r.Reason).ToList();
        Assert.True(w.Server.World.GetBlock(cell).Value == 0, "the pumpkin should be harvested; refusals: " + string.Join(", ", refusals));
    }

    private static readonly string[] PumpkinHosts = { "grass", "dirt", "mud" };

    [Fact]
    public void DuringAHarvestFestival_PumpkinsAppearOnTheGrassNearThePlayer_WithinTheBudget()
    {
        using var w = World("festival_spawn_grow");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30)); // Halloween week: flora_pumpkin, one at a time
        int sentBefore = w.Transport.Sent.Count(s => s.Msg is BlockChanged);

        PassUntilAPumpkin(w);
        var cells = w.Server.FestivalDepositCellsForTest.ToList();
        var cell = Assert.Single(cells); // #3083: one pumpkin per lay
        Assert.Equal(Id("flora_pumpkin"), w.Server.World.GetBlock(cell).Value);
        Assert.Contains(PumpkinHosts, h => Id(h) == w.Server.World.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)).Value); // on a host
        Assert.Equal(sentBefore + 1, w.Transport.Sent.Count(s => s.Msg is BlockChanged)); // every placement broadcast
        Assert.Equal(1, w.Player.State.FestivalSpawnsLaid["halloween:2031"]); // booked on the player's budget

        // #3083: the player's own clock — nothing more until the interval (30 s = ten passes of 3 s) has run down.
        for (int i = 0; i < 8; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.Single(w.Server.FestivalDepositCellsForTest);
        PassUntilAPumpkin(w);
        Assert.Equal(2, w.Server.FestivalDepositCellsForTest.Count);

        // The ledger and the budget survive a reload.
        int count = w.Server.FestivalDepositCellsForTest.Count;
        w.Restart();
        Assert.Equal(count, w.Server.FestivalDepositCellsForTest.Count);
        Assert.Equal(2, w.Player.State.FestivalSpawnsLaid["halloween:2031"]);
    }

    [Fact]
    public void AHarvestedDeposit_LeavesTheLedger_AndNeverRegrows()
    {
        using var w = World("festival_spawn_harvest");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        PassUntilAPumpkin(w);
        var cell = w.Server.FestivalDepositCellsForTest.First();
        Harvest(w, cell);
        Assert.False(w.Server.IsFestivalDepositForTest(cell));
        Assert.DoesNotContain(w.Transport.Sent.Select(s => s.Msg).OfType<FloraRegrowStarted>(), m => m.X == cell.X && m.Y == cell.Y && m.Z == cell.Z);
        Assert.Contains(w.Player.State.Inventory.Slots, s => s?.Item == "pumpkin");

        // No regrow: with the festival over (so the spawner does not simply lay a new pumpkin there) the cell stays bare
        // long after a wild plant's regrow would have brought one back.
        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        w.TickAt(0.4, seconds: 200, dt: 1.0);
        Assert.Equal(0, w.Server.World.GetBlock(cell).Value);
    }

    [Fact]
    public void WhenTheFestivalEnds_TheLayerClearsItsOwnCells_AndOnlyThose()
    {
        using var w = World("festival_spawn_clear");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        PassUntilAPumpkin(w);

        var cells = w.Server.FestivalDepositCellsForTest.ToList();
        Assert.NotEmpty(cells);
        // The player's own pumpkin, next to the pad: not in the ledger, never touched.
        var own = new Vector3i(NpcLifeWorld.Cx + 15, w.PadY + 1, NpcLifeWorld.Cz + 15);
        w.Server.World.SetBlock(new Vector3i(own.X, own.Y - 1, own.Z), Content.Value.GetBlock("grass")!.NumericId);
        w.Server.World.SetBlock(own, Content.Value.GetBlock("flora_pumpkin")!.NumericId);

        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15)); // no festival
        for (int i = 0; i < 10; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.Empty(w.Server.FestivalDepositCellsForTest);
        Assert.All(cells, c => Assert.Equal(0, w.Server.World.GetBlock(c).Value));
        Assert.Equal(Id("flora_pumpkin"), w.Server.World.GetBlock(own).Value);
    }

    [Fact]
    public void NothingAppears_WithoutAFestival_OrOnAnAirlessWorld()
    {
        using var w = World("festival_spawn_none");
        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        w.Server.RunFestivalSpawnPassForTest();
        Assert.Empty(w.Server.FestivalDepositCellsForTest);

        using var rocky = new NpcLifeWorld(Content.Value, "festival_spawn_airless", padHalf: 8); // rocky: a toxic atmosphere
        rocky.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        rocky.Server.RunFestivalSpawnPassForTest();
        Assert.Empty(rocky.Server.FestivalDepositCellsForTest);
    }

    /// <summary>#3083 (Justus, 2026-11-01: "the pumpkins should not grow back and should be rarer"; Marcel: the event must not
    /// be that easy): each player has a budget per festival and year. Once it is laid, nothing more appears — a harvested
    /// pumpkin leaves a gap that stays — and next year's festival starts fresh.</summary>
    [Fact]
    public void TheBudget_EndsTheSpawn_AHarvestedFieldStaysEmpty_AndNextYearStartsFresh()
    {
        using var w = World("festival_spawn_budget");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        int budget = Content.Value.FindFestival("halloween")!.Spawn!.Budget;
        var laid = w.Player.State.FestivalSpawnsLaid;
        for (int i = 0; i < budget * 40 && laid.GetValueOrDefault("halloween:2031") < budget; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.Equal(budget, laid["halloween:2031"]);
        Assert.Equal(budget, w.Server.FestivalDepositCellsForTest.Count);

        foreach (var cell in w.Server.FestivalDepositCellsForTest.Take(3).ToList())
        {
            Harvest(w, cell);
        }

        for (int i = 0; i < 60; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.Equal(budget - 3, w.Server.FestivalDepositCellsForTest.Count); // nothing filled the gaps
        Assert.Equal(budget, laid["halloween:2031"]);

        w.Server.SetFestivalClockForTest(Utc(2032, 10, 30)); // next year's Halloween
        PassUntilAPumpkin(w);
        Assert.Equal(1, laid["halloween:2032"]);
        Assert.False(laid.ContainsKey("halloween:2031"), "last year's entry is forgotten");
    }

    /// <summary>#3084 (Justus' inventory held 66 toxic pumpkins and not one plain one): a festival pumpkin is a present, not
    /// the world's wild species — it drops the plain fruit and scans as edible even where the roster made pumpkins toxic.
    /// A wild pumpkin of the same world stays toxic.</summary>
    [Fact]
    public void AFestivalPumpkin_IsPlainFruit_EvenWhereTheWorldsPumpkinsAreToxic()
    {
        using var w = World("festival_spawn_plain");
        w.Server.MakeFloraToxicForTest("flora_pumpkin");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        PassUntilAPumpkin(w);
        var cell = w.Server.FestivalDepositCellsForTest.First();

        var scan = w.Server.ScanSubject(w.Player.State.PlayerId, "block", "flora_pumpkin", null, cell);
        Assert.Equal("ui.scan.threat.edible", scan.ThreatKey);
        Harvest(w, cell);
        var inv = w.Player.State.Inventory;
        Assert.True(inv.CountOf("pumpkin") >= 1, "the festival pumpkin gives the plain fruit");
        Assert.Equal(0, inv.CountOf("toxic_pumpkin"));

        // Control: a pumpkin that is no festival present follows the world's roster.
        var wild = Enumerable.Range(0, 20).Select(i => new Vector3i(NpcLifeWorld.Cx + 10 - i, w.PadY + 1, NpcLifeWorld.Cz - 10))
            .First(c => w.Server.World.GetBlock(c).Value == 0 && !w.Server.IsFestivalDepositForTest(c));
        w.Server.World.SetBlock(wild, Content.Value.GetBlock("flora_pumpkin")!.NumericId);
        Assert.Equal("ui.scan.threat.toxic", w.Server.ScanSubject(w.Player.State.PlayerId, "block", "flora_pumpkin", null, wild).ThreatKey);
        Harvest(w, wild);
        Assert.True(inv.CountOf("toxic_pumpkin") >= 1, "a wild pumpkin of a toxic roster stays toxic");
    }
}
