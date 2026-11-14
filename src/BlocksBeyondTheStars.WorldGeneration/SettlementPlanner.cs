// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>What a planner gets to plan one settlement (#3586, #3587, #3589).</summary>
public sealed class SettlementPlanRequest
{
    /// <summary>The settlement's size tier: hamlet, village, town or city.</summary>
    public string Tier { get; set; } = "village";

    /// <summary>The instance seed — every draw of the plan comes from it.</summary>
    public long Seed { get; set; }

    /// <summary>The planned kit (its layout, forms, streets, spacing, extent and module entries).</summary>
    public StructureKit Kit { get; set; } = new();

    /// <summary>The modules the kit's entries name, resolved from every pack (the kit module pool).</summary>
    public IReadOnlyList<StructureTemplate> Pool { get; set; } = System.Array.Empty<StructureTemplate>();

    /// <summary>The ground of the site (#3589): world height of a box column (local x, z), or null for flat ground.</summary>
    public System.Func<int, int, int>? Ground { get; set; }

    /// <summary>The box side the nearest fresh water lies beyond (0 −Z, 1 +Z, 2 −X, 3 +X), −1 for none — the bank village's
    /// hint.</summary>
    public int WaterSide { get; set; } = -1;

    /// <summary>A form the caller insists on (the editor's preview), "" = drawn from the kit's forms.</summary>
    public string Form { get; set; } = string.Empty;

    /// <summary>The box side the caller insists on (a grown city's suburb, #3599); 0 = the kit's or the tier's.</summary>
    public int Extent { get; set; }

    /// <summary>The axis a street village's main road must run along — "x" or "z" (a suburb's road leaves its city's gate,
    /// #3599); "" = drawn.</summary>
    public string Axis { get; set; } = string.Empty;

    /// <summary>A main road without bends (a suburb's road is the city's gate road running on).</summary>
    public bool Straight { get; set; }
}

/// <summary>
/// The settlement planners of terrain generation 41 (#3586 grown grid, #3587 road first): from a kit, a seed and the ground
/// of the site to a <see cref="SettlementPlan"/> — roads first, then the kit's buildings grown along them (central functions
/// at the plaza, farms at the edge, every door turned to its road), then the heights that follow the land (#3590). Pure
/// and deterministic: the same request always plans the same settlement; the plan is pinned, so a later change to the
/// planners never moves a settlement that stands.
/// </summary>
public static class SettlementPlanner
{
    /// <summary>The most a planned settlement's floors rise above its lowest one (#3590): NPC routes search eight blocks
    /// up and down.</summary>
    public const int MaxRise = 6;

    /// <summary>The box side of a planned settlement: the kit's <see cref="StructureKit.Extent"/>, else the tier's.</summary>
    public static int ExtentFor(StructureKit kit, string tier)
        => kit.Extent > 0 ? System.Math.Clamp(kit.Extent, 32, 128) : tier switch
        {
            "hamlet" => 40,
            "town" => 72,
            "city" => 88,
            _ => 56,
        };

    /// <summary>Whether a planned settlement's people are aliens — a hash of its seed (the plan does not store it: the
    /// planner picks the modules of that style, the baker reads the same answer).</summary>
    public static bool IsAlien(long seed) => StableHashing.Unit("settlement:inhabitants", seed) < 0.5;

    /// <summary>Plans one settlement.</summary>
    public static SettlementPlan Plan(SettlementPlanRequest request)
    {
        var b = new SettlementPlanBuilder(request);
        b.Build();
        return b.Plan;
    }

    /// <summary>How many buildings a tier grows to (#3587): the kit's module bounds when set, else the tier's.</summary>
    internal static (int Min, int Max) LotCount(StructureKit kit, string tier)
    {
        if (kit.ModulesMin > 0 || kit.ModulesMax > 0)
        {
            var (min, max) = kit.EffectiveBounds();
            return (System.Math.Max(1, min), System.Math.Max(System.Math.Max(1, min), max));
        }

        return tier switch
        {
            "hamlet" => (3, 5),
            "town" => (11, 16),
            "city" => (16, 24),
            _ => (7, 11),
        };
    }

    /// <summary>
    /// The modules a planned settlement grows, in the order the planner places them (#3587): every required entry's minimum
    /// copies, then weighted draws up to a drawn count — only modules of the settlement's style (village or town) and
    /// inhabitants — sorted by <see cref="StructureRoles.Ring"/> (the plaza's first, the farms last), each ring in draw order.
    /// </summary>
    internal static List<StructureTemplate> PickModules(SettlementPlanRequest r, bool alien, System.Random rng)
    {
        bool town = StructureRoles.IsTownStyleTier(r.Tier);
        var entries = new List<(KitEntry Entry, StructureTemplate Module)>();
        foreach (var e in r.Kit.Entries)
        {
            var m = ModuleByKey(r.Pool, e.Module);
            if (m != null && m.Width > 0 && m.Length > 0 && StructureRoles.IsTownStyleTier(m.Tier) == town && m.IsAlienStyle == alien)
            {
                entries.Add((e, m));
            }
        }

        var picked = new List<StructureTemplate>();
        var counts = new int[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            for (; counts[i] < entries[i].Entry.MinOrRequired; counts[i]++)
            {
                picked.Add(entries[i].Module);
            }
        }

        var (min, max) = LotCount(r.Kit, r.Tier);
        int target = System.Math.Max(picked.Count, rng.Next(min, max + 1));
        while (picked.Count < target && DrawEntry(entries, counts, rng) is int pick)
        {
            counts[pick]++;
            picked.Add(entries[pick].Module);
        }

        var ordered = new List<StructureTemplate>(picked.Count);
        for (int ring = 0; ring <= 2; ring++)
        {
            ordered.AddRange(picked.FindAll(m => StructureRoles.Ring(m.FunctionOrRole) == ring));
        }

        return ordered;
    }

    private static int? DrawEntry(List<(KitEntry Entry, StructureTemplate Module)> entries, int[] counts, System.Random rng)
    {
        int total = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (counts[i] < System.Math.Max(entries[i].Entry.Max, entries[i].Entry.MinOrRequired))
            {
                total += System.Math.Max(1, entries[i].Entry.Weight);
            }
        }

        if (total == 0)
        {
            return null;
        }

        int roll = rng.Next(total);
        for (int i = 0; i < entries.Count; i++)
        {
            if (counts[i] >= System.Math.Max(entries[i].Entry.Max, entries[i].Entry.MinOrRequired))
            {
                continue;
            }

            roll -= System.Math.Max(1, entries[i].Entry.Weight);
            if (roll < 0)
            {
                return i;
            }
        }

        return null;
    }

    internal static StructureTemplate? ModuleByKey(IReadOnlyList<StructureTemplate> pool, string key)
    {
        foreach (var m in pool)
        {
            if (m.Key == key)
            {
                return m;
            }
        }

        return null;
    }
}
