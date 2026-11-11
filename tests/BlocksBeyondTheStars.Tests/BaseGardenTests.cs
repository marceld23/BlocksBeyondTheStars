// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gardener's farm around the base (#3477, a tester's garden beside his base: fruit trees on one side, pumpkins and
/// crops on the other). The base gardener (#1868) also picks pumpkins and the fruit of the trees the owner grew, works the
/// owner's own plantings within the Lumberjack reach of the core inside or outside the walls — never a field beyond it,
/// never wild flora, never another player's field — and with no crate banks the harvest into the core's yield store, up to
/// its capacity.
/// </summary>
public sealed class BaseGardenTests
{
    private const string Stranger = "Neighbour";
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static StoredContainer Crate(NpcLifeWorld w)
    {
        int x = NpcLifeWorld.Cx - 4, z = NpcLifeWorld.Cz - 3;
        w.Set(x, w.Feet, z, "crate");
        var crate = new StoredContainer
        {
            Id = "garden_crate",
            Planet = w.Server.ActiveLocationId,
            Kind = "crate",
            Position = new Vector3i(x, w.Feet, z),
        };
        w.Server.AddContainerForTest(crate);
        return crate;
    }

    private static int CountIn(NpcLifeWorld w, StoredContainer crate, params string[] items)
        => w.Server.Containers.Single(c => c.Id == crate.Id).Items.Where(s => items.Contains(s.Item)).Sum(s => s.Count);

    /// <summary>A plant on a soil cell of the pad, both set by <paramref name="owner"/> (empty: a server write — the world's own).</summary>
    private static Vector3i Plant(NpcLifeWorld w, int x, int z, string plant, string owner = NpcLifeWorld.Owner)
    {
        w.Server.World.SetBlock(new Vector3i(x, w.PadY, z), w.Block("dirt"), 0, 0, 0, owner);
        var cell = new Vector3i(x, w.Feet, z);
        w.Server.World.SetBlock(cell, w.Block(plant), 0, 0, 0, owner);
        return cell;
    }

    /// <summary>A fruit sapling planted by <paramref name="owner"/> and grown into its tree; the fruit cells it hangs.</summary>
    private static List<Vector3i> FruitTree(NpcLifeWorld w, int x, int z, string owner = NpcLifeWorld.Owner)
    {
        Plant(w, x, z, "flora_sapling_fruit_round", owner);
        Assert.True(w.Server.TryGrowTreeForTest(x, w.Feet, z), "the fruit sapling did not grow");
        var fruit = w.Block("flora_fruit_round").Value;
        var cells = new List<Vector3i>();
        for (int dx = -3; dx <= 3; dx++)
            for (int dz = -3; dz <= 3; dz++)
                for (int y = w.Feet; y <= w.Feet + 8; y++)
                {
                    var c = new Vector3i(x + dx, y, z + dz);
                    if (w.Server.World.GetBlock(c).Value == fruit)
                    {
                        cells.Add(c);
                    }
                }

        Assert.InRange(cells.Count, 3, 5); // #2977: a fruit sapling's tree always bears 3–5
        return cells;
    }

    private static void Gardener(NpcLifeWorld w, int baseId)
    {
        w.Server.ScanBaseLifeForTest();
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Job == "gardener");
    }

    [Fact]
    public void APumpkinInTheBase_IsHarvestedIntoTheCrate()
    {
        using var w = new NpcLifeWorld(_content, "garden_pumpkin");
        int baseId = w.FoundBase();
        var crate = Crate(w);
        var pumpkin = Plant(w, NpcLifeWorld.Cx + 4, NpcLifeWorld.Cz + 3, FloraCatalog.PumpkinKey);
        Gardener(w, baseId);
        Assert.Contains(pumpkin, w.Server.OwnerGardenForTest(baseId));

        w.TickAt(0.45, 90, until: () => w.Server.World.GetBlock(pumpkin).IsAir);

        Assert.True(w.Server.World.GetBlock(pumpkin).IsAir, "the pumpkin was never harvested");
        Assert.True(CountIn(w, crate, "pumpkin", "toxic_pumpkin") >= 1, "the pumpkin did not reach the crate");
        Assert.True(w.Server.FloraRegrowTimerForTest(pumpkin.X, pumpkin.Y, pumpkin.Z) > 0, "the harvested pumpkin does not regrow");
    }

    [Fact]
    public void TheFruitOfATreeTheOwnerGrew_IsPicked_AndGrowsBackOnItsLeaf()
    {
        using var w = new NpcLifeWorld(_content, "garden_fruit");
        int baseId = w.FoundBase();
        var crate = Crate(w);
        var fruit = FruitTree(w, NpcLifeWorld.Cx + 14, NpcLifeWorld.Cz + 2); // outside the core zone, no walls: outside the base
        Gardener(w, baseId);
        Assert.Equal(fruit.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z),
            w.Server.OwnerGardenForTest(baseId).OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z));

        var fruitId = w.Block("flora_fruit_round");
        w.TickAt(0.45, 120, until: () => fruit.Any(c => w.Server.World.GetBlock(c).IsAir));
        var picked = fruit.FirstOrDefault(c => w.Server.World.GetBlock(c).IsAir);
        Assert.True(w.Server.World.GetBlock(picked).IsAir, "no fruit was picked");
        Assert.True(CountIn(w, crate, "fruit_round", "toxic_fruit_round") >= 1, "the fruit did not reach the crate");
        Assert.True(w.Server.FloraRegrowTimerForTest(picked.X, picked.Y, picked.Z) > 0, "the picked fruit does not regrow");

        // Off duty at night, so nobody picks it again: it comes back on its leaf in the tree's colour (#2038).
        double plantTime = BlocksBeyondTheStars.WorldGeneration.FruitRules.RegrowSeconds / w.Server.WeatherRegrowFactorForTest(picked);
        w.TickAt(0.9, plantTime * 2, dt: 1.0, until: () => w.Server.World.GetBlock(picked) == fruitId);
        Assert.Equal(fruitId.Value, w.Server.World.GetBlock(picked).Value);
    }

    [Fact]
    public void TheOwnersFieldOutsideTheBase_WithinReach_IsWorked()
    {
        using var w = new NpcLifeWorld(_content, "garden_outside");
        int baseId = w.FoundBase();
        var crate = Crate(w);
        var crop = Plant(w, NpcLifeWorld.Cx + 20, NpcLifeWorld.Cz, "flora_cropgrain"); // open field, 20 from the core
        Gardener(w, baseId);
        Assert.Empty(w.Server.BaseIndexForTest(baseId)!.Crops); // not inside the base: before #3477 nobody worked it
        Assert.Equal(new[] { crop }, w.Server.OwnerGardenForTest(baseId));

        w.TickAt(0.45, 120, until: () => w.Server.World.GetBlock(crop).IsAir);

        Assert.True(w.Server.World.GetBlock(crop).IsAir, "the field outside the base was never harvested");
        Assert.True(CountIn(w, crate, "grain") >= 2, "the grain did not reach the crate");
    }

    [Fact]
    public void AFieldBeyondReach_WildFlora_AndAnotherPlayersField_AreLeftAlone()
    {
        using var w = new NpcLifeWorld(_content, "garden_strangers", padHalf: 40);
        int baseId = w.FoundBase();
        Crate(w);
        int cx = NpcLifeWorld.Cx, cz = NpcLifeWorld.Cz;
        var mine = Plant(w, cx + 20, cz - 2, "flora_cropberry");
        var far = Plant(w, cx + 34, cz, "flora_cropberry");                       // the owner's, but beyond the 32 reach
        var wild = Plant(w, cx + 12, cz + 12, FloraCatalog.PumpkinKey, owner: ""); // the world's own (no editor)
        var theirs = Plant(w, cx - 12, cz + 12, "flora_cropberry", owner: Stranger);
        var wildTree = FruitTree(w, cx - 14, cz - 14, owner: "");                  // a tree nobody planted
        var theirTree = FruitTree(w, cx + 2, cz - 22, owner: Stranger);
        Gardener(w, baseId);

        Assert.Equal(new[] { mine }, w.Server.OwnerGardenForTest(baseId));

        w.TickAt(0.45, 120, until: () => w.Server.World.GetBlock(mine).IsAir);
        Assert.True(w.Server.World.GetBlock(mine).IsAir, "the owner's own field was never worked");
        foreach (var cell in new[] { far, wild, theirs }.Concat(wildTree).Concat(theirTree))
        {
            Assert.False(w.Server.World.GetBlock(cell).IsAir, $"the gardener picked {cell}, which is not the owner's garden");
        }
    }

    [Fact]
    public void WithoutACrate_TheHarvestLandsInTheCoreStore_UpToItsCapacity()
    {
        using var w = new NpcLifeWorld(_content, "garden_core");
        int baseId = w.FoundBase();
        var first = Plant(w, NpcLifeWorld.Cx + 20, NpcLifeWorld.Cz, "flora_cropberry");
        Gardener(w, baseId);

        w.TickAt(0.45, 120, until: () => w.Server.World.GetBlock(first).IsAir);
        Assert.True(w.Server.World.GetBlock(first).IsAir, "without a crate the crop was never harvested");
        var store = w.Server.BaseCoreStoreForTest(baseId);
        Assert.True(store.TryGetValue("berries", out int berries) && berries >= 2, "the berries did not reach the core's store");

        // A full core takes nothing more: the next plant stays standing, the store stays at its capacity.
        int ready = w.Server.BaseCoreStoreForTest(baseId).Values.Sum();
        w.Server.FillBaseCoreStoreForTest(baseId, "stone", BaseRules.YieldCapacity - ready);
        var second = Plant(w, NpcLifeWorld.Cx + 22, NpcLifeWorld.Cz + 4, "flora_cropberry");
        w.Server.ScanBaseLifeForTest();
        w.Server.DueHarvestForTest();
        w.TickAt(0.45, 90);

        Assert.False(w.Server.World.GetBlock(second).IsAir, "a plant was picked with the core full and no crate");
        Assert.Equal(BaseRules.YieldCapacity, w.Server.BaseCoreStoreForTest(baseId).Values.Sum());
    }
}
