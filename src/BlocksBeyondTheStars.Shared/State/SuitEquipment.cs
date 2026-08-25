// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Suit equipment effects derived from the gear a player <b>carries</b> — there are no equip slots: a
/// piece works as soon as it is anywhere in the backpack (#1270). One formula for both sides: the server
/// applies it to vitals/combat, the client shows the same numbers in the inventory's Suit tab and sizes
/// the HUD oxygen bar with it, so what the player reads can never drift from what the server does.
/// Data-driven via the item definitions (<c>armorResistance</c>, <c>oxygenBonus</c>,
/// <c>thermalInsulation</c>, <c>scanKnowledgeMultiplier</c>).
/// </summary>
public static class SuitEquipment
{
    /// <summary>Armour pieces add up, but never block everything.</summary>
    public const float MaxArmorResistance = 0.75f;

    /// <summary>Hard ceiling for thermal insulation — even the best rig never makes the suit free to run.</summary>
    public const float MaxThermalInsulation = 0.9f;

    /// <summary>Suit oxygen without any tank.</summary>
    public const float BaseOxygen = 100f;

    /// <summary>Total physical-damage resistance (0..0.75): every carried armour piece counts, summed.</summary>
    public static float ArmorResistance(IEnumerable<ItemDefinition> items, Func<string, bool> carried)
    {
        float sum = 0f;
        foreach (var item in items)
        {
            if (item.ArmorResistance > 0f && carried(item.Key))
            {
                sum += item.ArmorResistance;
            }
        }

        return Math.Min(MaxArmorResistance, sum);
    }

    /// <summary>Maximum suit oxygen — base 100 plus the best carried tank's bonus. Tanks are tiered
    /// (I/II/III), so only the highest bonus counts; carrying several does not stack.</summary>
    public static float MaxOxygen(IEnumerable<ItemDefinition> items, Func<string, bool> carried)
    {
        float bonus = 0f;
        foreach (var item in items)
        {
            if (item.OxygenBonus > bonus && carried(item.Key))
            {
                bonus = item.OxygenBonus;
            }
        }

        return BaseOxygen + bonus;
    }

    /// <summary>Best carried thermal insulation 0..0.9 (#669): the fraction of heat/cold/vacuum suit stress
    /// the gear absorbs. Like the oxygen tanks, only the BEST piece counts — liners are tiered.</summary>
    public static float ThermalInsulation(IEnumerable<ItemDefinition> items, Func<string, bool> carried)
    {
        float best = 0f;
        foreach (var item in items)
        {
            if (item.ThermalInsulation > best && carried(item.Key))
            {
                best = item.ThermalInsulation;
            }
        }

        return Math.Min(MaxThermalInsulation, best);
    }

    /// <summary>Best scanner knowledge multiplier from carried scanners (1 = no bonus).</summary>
    public static float ScanMultiplier(IEnumerable<ItemDefinition> items, Func<string, bool> carried)
    {
        float best = 1f;
        foreach (var item in items)
        {
            if (item.ScanKnowledgeMultiplier > best && carried(item.Key))
            {
                best = item.ScanKnowledgeMultiplier;
            }
        }

        return best;
    }

    /// <summary>Suit gear: armour / oxygen / insulation items plus the wearable suit modules (lamp, jetpack,
    /// extractors, stealth, teleporter, comms/scanners) — the crafting "Suit" filter and the inventory's
    /// Suit tab list exactly these.</summary>
    public static bool IsSuitGear(ItemDefinition def)
    {
        if (def.ArmorResistance > 0f || def.OxygenBonus > 0f || def.ThermalInsulation > 0f)
        {
            return true;
        }

        switch (def.Key)
        {
            case "suit_lamp":
            case "jetpack":
            case "oxygen_extractor":
            case "stealth_suit":
            case "suit_teleporter":
            case "comm_radio":
            case "radar_scanner":
                return true;
            default:
                return false;
        }
    }
}
