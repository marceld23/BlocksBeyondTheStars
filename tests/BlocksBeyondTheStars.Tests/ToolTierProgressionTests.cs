// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Guards the drill-tier bootstrap. Tier-2 ore (titanium, cobalt, tungsten, platinum, uranium, neodymium)
/// can only be mined with the Tier-2 <c>titanium_drill</c>. So the FIRST titanium_drill must be craftable
/// without mining any Tier-2 ore — otherwise the whole Tier-2 economy is unreachable in survival. (It used
/// to require <c>carbide</c>, which needs tungsten+platinum, which need the titanium_drill: a hard deadlock.
/// The drill now uses steel instead; carbide stays the gate for the advanced tools you reach afterwards.)
/// </summary>
public sealed class ToolTierProgressionTests
{
    private readonly GameContent _c = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Items reachable WITHOUT mining any Tier-2 ore: basic-drill block drops + fauna + starter kit,
    /// grown by every Hand / Workshop / Refinery / Market recipe (all available before a Tier-2 drill — the
    /// refinery unlocks from Tier-1 materials and the market barters Tier-1 ore into titanium ore).</summary>
    private HashSet<string> ReachableWithoutTier2Mining()
    {
        var available = new HashSet<string> { "creature_meat", "toxic_gland", "basic_drill", "hand_scanner", "scrap_pistol" };
        foreach (var b in _c.Blocks.Values.Where(b => b.MinToolTier <= 1))
        {
            foreach (var d in b.Drops) available.Add(d.Item);
        }

        var stations = new[] { CraftingStation.Hand, CraftingStation.Workshop, CraftingStation.Refinery, CraftingStation.Market };
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var r in _c.Recipes.Values.Where(r => stations.Contains(r.Station)))
            {
                if (r.Inputs.All(i => available.Contains(i.Item)))
                {
                    foreach (var o in r.Outputs)
                    {
                        changed |= available.Add(o.Item);
                    }
                }
            }
        }

        return available;
    }

    [Fact]
    public void FirstTier2Drill_IsCraftable_WithoutTier2Mining()
    {
        var reachable = ReachableWithoutTier2Mining();
        Assert.True(reachable.Contains("titanium_drill"),
            "the first titanium_drill (only Tier-2 miner) can't be built without already mining Tier-2 ore — "
            + "progression deadlock. Its recipe must not depend on tungsten/platinum/carbide.");
    }

    [Fact]
    public void TitaniumDrillRecipe_DoesNotDependOnTier2Materials()
    {
        var r = _c.Recipes["titanium_drill"];
        string[] tier2Only = { "carbide", "tungsten_ingot", "platinum_ingot", "cobalt_ingot", "uranium", "neodymium" };
        var offenders = r.Inputs.Select(i => i.Item).Intersect(tier2Only).ToList();
        Assert.True(offenders.Count == 0,
            "titanium_drill must not require Tier-2-mined materials: " + string.Join(", ", offenders));
    }

    /// <summary>The fix must not orphan carbide — it still gates the advanced tools you build after the
    /// titanium_drill (those genuinely sit behind Tier-2 mining, so requiring carbide there is correct).</summary>
    [Fact]
    public void Carbide_StillGatesTheAdvancedTools()
    {
        bool consumed = _c.Recipes.Values.Any(r => r.Inputs.Any(i => i.Item == "carbide"));
        Assert.True(consumed, "carbide is now unused — it should still gate diamond_drill / mining_beam / terrain_blaster");
    }
}
