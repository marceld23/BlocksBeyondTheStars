// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gatherer's work core (#2642, wilderness folk #2639): wilderness NPCs pick wild plants and their own plot the
/// gardener's way — air, a <c>BlockChanged</c>, the regrow — and bank exactly the drops a player would get. Crops and
/// the species this world did not roll toxic count; a toxic species, a plant a player planted or touched, and a plant in
/// a protected area (here: a player base) never do; the search reads loaded chunks only. The cadence (one harvest per
/// minute per NPC) is the caller's (#2641/#2646) and tested there.
/// </summary>
public sealed class NpcGatheringTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    /// <summary>A lush world, so the roster has edible and toxic wild species to choose from.</summary>
    private NpcLifeWorld Arena(string name) => new(_content, name, configure: c => c.StartPlanet = "meadowlands");

    /// <summary>A plant on its own ground tile of the pad — written without an owner (as worldgen does), or with one
    /// (as a player's planting does).</summary>
    private static Vector3i Plant(NpcLifeWorld w, int x, int z, string key, string ground = "grass", string owner = "")
    {
        w.Server.World.SetBlock(new Vector3i(x, w.PadY, z), w.Block(ground));
        var cell = new Vector3i(x, w.Feet, z);
        w.Server.World.SetBlock(cell, w.Block(key), 0, 0, 0, owner);
        return cell;
    }

    /// <summary>A wild land species this world rolled edible and one it rolled toxic.</summary>
    private static (string Edible, string Toxic) WildPair(NpcLifeWorld w)
    {
        string? edible = null, toxic = null;
        foreach (var sp in FloraCatalog.All)
        {
            if (sp.Cultivated || sp.Aquatic || sp.Hanging || sp.Authored || sp.Habitat == FloraHabitat.Cave
                || w.Server.FloraSpeciesForBlock(sp.Key) is not { } species)
            {
                continue;
            }

            if (species.Toxic)
            {
                toxic ??= sp.Key;
            }
            else
            {
                edible ??= sp.Key;
            }
        }

        Assert.True(edible is not null && toxic is not null, "the test world rolled no edible/toxic pair");
        return (edible!, toxic!);
    }

    private static Vector3f Center(NpcLifeWorld w, int x = Cx, int z = Cz) => new(x + 0.5f, w.Feet, z + 0.5f);

    [Fact]
    public void FindsEdibleWildFloraAndPlotCrops_NearestFirst_AndSkipsTheToxicSpecies()
    {
        using var w = Arena("gather_find");
        var (edible, toxic) = WildPair(w);
        var crop = Plant(w, Cx + 2, Cz, "flora_cropberry", ground: "dirt");
        var wild = Plant(w, Cx + 5, Cz, edible);
        Plant(w, Cx + 3, Cz + 1, toxic);

        var sites = w.Server.FindNpcGatherSitesForTest(Center(w), 12, 8);

        Assert.Equal(new[] { crop, wild }, sites);
        Assert.Single(w.Server.FindNpcGatherSitesForTest(Center(w), 12, 1)); // the cap holds
    }

    [Fact]
    public void SkipsPlayerPlantedPlants_AndPlantsInAPlayersBase()
    {
        using var w = Arena("gather_skip");
        var (edible, _) = WildPair(w);
        w.FoundBase(); // the core at the pad's centre: its zone is protected
        var inBase = Plant(w, Cx + 3, Cz + 3, edible);
        var planted = Plant(w, Cx - 9, Cz + 2, edible, owner: NpcLifeWorld.Owner);
        var wild = Plant(w, Cx - 12, Cz, edible);

        var sites = w.Server.FindNpcGatherSitesForTest(Center(w, Cx - 6), 12, 8);
        Assert.Equal(new[] { wild }, sites);

        int npc = w.Server.AddNpcForTest(Center(w, Cx - 6));
        var banked = new List<(string Item, int Count)>();
        Assert.False(w.Server.NpcHarvestFloraForTest(npc, planted, banked), "a player's planting was picked");
        Assert.False(w.Server.NpcHarvestFloraForTest(npc, inBase, banked), "a plant in a player's base was picked");
        Assert.Empty(banked);
        Assert.Equal(w.Block(edible), w.Server.World.GetBlock(planted));
        Assert.Equal(w.Block(edible), w.Server.World.GetBlock(inBase));
    }

    [Fact]
    public void AHarvest_ClearsTheCell_BanksThePlayersDrops_AndThePlantRegrows()
    {
        using var w = Arena("gather_harvest");
        var crop = Plant(w, Cx + 2, Cz, "flora_cropberry", ground: "dirt");
        int npc = w.Server.AddNpcForTest(Center(w));
        var banked = new List<(string Item, int Count)>();
        int sentBefore = w.Transport.Sent.Count;

        Assert.True(w.Server.NpcHarvestFloraForTest(npc, crop, banked));

        Assert.True(w.Server.World.GetBlock(crop).IsAir, "the harvested cell is not air");
        Assert.True(w.Server.FloraRegrowTimerForTest(crop.X, crop.Y, crop.Z) > 0, "no regrow was scheduled");
        foreach (var drop in _content.GetBlock("flora_cropberry")!.Drops)
        {
            Assert.True(banked.Where(b => b.Item == drop.Item).Sum(b => b.Count) >= drop.Count, $"{drop.Item} did not reach the bank");
        }

        Assert.Contains(w.Transport.Sent.Skip(sentBefore), s => s.Msg is BlockChanged { Block: 0 } m && m.X == crop.X && m.Y == crop.Y && m.Z == crop.Z);
        Assert.False(w.Server.NpcHarvestFloraForTest(npc, crop, banked), "an empty cell was harvested twice");
        Assert.False(w.Server.IsBlockPlayerEditedForTest(crop), "an NPC's harvest was recorded as a player's edit");

        w.TickAt(0.45, 150, dt: 1.0, until: () => !w.Server.World.GetBlock(crop).IsAir);
        Assert.Equal(w.Block("flora_cropberry"), w.Server.World.GetBlock(crop));
        Assert.Contains(crop, w.Server.FindNpcGatherSitesForTest(Center(w), 6, 8)); // and it is the gatherer's again
    }

    [Fact]
    public void AFruitUnderACrown_IsPicked_AndRegrowsInItsColour()
    {
        using var w = Arena("gather_fruit");
        string? fruit = FloraCatalog.FruitKeys().FirstOrDefault(k => w.Server.FloraSpeciesForBlock(k) is { Toxic: false });
        Assert.True(fruit is not null, "the test world's fruit is toxic");

        // A leaf two blocks over the pad, the fruit hanging under it (ground + 2), coloured like its tree kind.
        int x = Cx + 4, z = Cz;
        w.Server.World.SetBlock(new Vector3i(x, w.Feet + 2, z), w.Block("tree_leaves"));
        var cell = new Vector3i(x, w.Feet + 1, z);
        const int tint = 0x33AA55;
        w.Server.World.SetBlock(cell, w.Block(fruit!), tint);

        Assert.Contains(cell, w.Server.FindNpcGatherSitesForTest(Center(w), 8, 8));
        int npc = w.Server.AddNpcForTest(Center(w));
        var banked = new List<(string Item, int Count)>();
        Assert.True(w.Server.NpcHarvestFloraForTest(npc, cell, banked));
        Assert.True(w.Server.World.GetBlock(cell).IsAir);
        Assert.NotEmpty(banked);
        Assert.Equal(tint, w.Server.FloraRegrowTintForTest(cell));
    }

    [Fact]
    public void TheSearch_NeverLoadsAChunk()
    {
        using var w = Arena("gather_unloaded");
        int loaded = w.Server.World.LoadedChunkCount;
        var far = new Vector3f(Cx + w.Server.World.Circumference / 2 + 0.5f, w.Feet, Cz + 0.5f); // the far side of the world

        Assert.Empty(w.Server.FindNpcGatherSitesForTest(far, 16, 8));
        Assert.Equal(loaded, w.Server.World.LoadedChunkCount);
    }
}
