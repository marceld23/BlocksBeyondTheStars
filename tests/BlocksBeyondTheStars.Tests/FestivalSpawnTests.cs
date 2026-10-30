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

    /// <summary>A breathable world (the spawner never lays a pumpkin into vacuum) with a grass pad around the player.</summary>
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
                w.Set(NpcLifeWorld.Cx + dx, w.PadY, NpcLifeWorld.Cz + dz, "grass");
            }
        }

        return w;
    }

    private static ushort Id(string key) => Content.Value.GetBlock(key)!.NumericId.Value;

    [Fact]
    public void DuringAHarvestFestival_PumpkinsAppearOnTheGrassNearThePlayer_WithinTheBudget()
    {
        using var w = World("festival_spawn_grow");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30)); // Halloween week: flora_pumpkin, 6 per pass
        int sentBefore = w.Transport.Sent.Count(s => s.Msg is BlockChanged);

        w.Server.RunFestivalSpawnPassForTest();
        var cells = w.Server.FestivalDepositCellsForTest.ToList();
        Assert.InRange(cells.Count, 1, 6);
        foreach (var cell in cells)
        {
            Assert.Equal(Id("flora_pumpkin"), w.Server.World.GetBlock(cell).Value);
            Assert.Equal(Id("grass"), w.Server.World.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)).Value); // on a host
            Assert.Equal(w.PadY + 1, cell.Y);
        }

        Assert.Equal(sentBefore + cells.Count, w.Transport.Sent.Count(s => s.Msg is BlockChanged)); // every placement broadcast

        for (int i = 0; i < 20; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.InRange(w.Server.FestivalDepositCellsForTest.Count, cells.Count, 6 * 21);

        // The ledger survives a reload.
        int count = w.Server.FestivalDepositCellsForTest.Count;
        w.Restart();
        Assert.Equal(count, w.Server.FestivalDepositCellsForTest.Count);
    }

    [Fact]
    public void AHarvestedDeposit_LeavesTheLedger_AndNeverRegrows()
    {
        using var w = World("festival_spawn_harvest");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        w.Server.RunFestivalSpawnPassForTest();
        var cell = w.Server.FestivalDepositCellsForTest.First();
        w.Player.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 1.5f); // within arm's reach

        for (int i = 0; i < 40 && w.Server.World.GetBlock(cell).Value != 0; i++)
        {
            // Like a player holding the mine button: a hit, then a tick in which the progress accrues.
            w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
            w.Server.Tick(0.1);
        }

        var refusals = w.Transport.Sent.Select(s => s.Msg).OfType<ActionRejected>().Where(r => r.Action == "mine").Select(r => r.Reason).ToList();
        Assert.True(w.Server.World.GetBlock(cell).Value == 0, "the pumpkin should be harvested; refusals: " + string.Join(", ", refusals));
        Assert.False(w.Server.IsFestivalDepositForTest(cell));
        Assert.DoesNotContain(w.Transport.Sent.Select(s => s.Msg).OfType<FloraRegrowStarted>(), m => m.X == cell.X && m.Y == cell.Y && m.Z == cell.Z);
        Assert.Contains(w.Player.State.Inventory.Slots, s => s?.Item is "pumpkin" or "toxic_pumpkin");

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
        for (int i = 0; i < 3; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

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
}
