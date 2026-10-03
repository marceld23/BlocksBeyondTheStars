// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// What the lab changed on one tool or one piece of gear: two gains and one drawback, each a stat with a level 1..3.
/// It rides in the item's key after the <c>u</c> tag (six hex digits), so the changed item is a distinct item for
/// inventory, network and saves with no instance channel — tools and gear never stack anyway.
/// <para>
/// Three things a change never touches: the tool <b>tier</b> (the progression gate), the <b>mining radius</b> (27 blocks
/// per swing is a server and a balance cost) and <b>ignition</b>. And there is no wear: a change can be overwritten or
/// washed off, never lost to use.
/// </para>
/// </summary>
public readonly struct ItemMods : IEquatable<ItemMods>
{
    /// <summary>The key tag and the payload length.</summary>
    public const char Tag = 'u';
    public const int PayloadLength = 6;

    /// <summary>The most a set of heavy gear may slow its wearer.</summary>
    public const float MaxWeight = 0.15f;

    public ItemMods(ModStat first, int firstLevel, ModStat second, int secondLevel, ModStat drawback, int drawbackLevel)
    {
        First = firstLevel > 0 ? first : ModStat.None;
        FirstLevel = First == ModStat.None ? 0 : Math.Clamp(firstLevel, 1, 3);
        Second = secondLevel > 0 ? second : ModStat.None;
        SecondLevel = Second == ModStat.None ? 0 : Math.Clamp(secondLevel, 1, 3);
        Drawback = drawbackLevel > 0 ? drawback : ModStat.None;
        DrawbackLevel = Drawback == ModStat.None ? 0 : Math.Clamp(drawbackLevel, 1, 3);
    }

    public ModStat First { get; }
    public int FirstLevel { get; }
    public ModStat Second { get; }
    public int SecondLevel { get; }
    public ModStat Drawback { get; }
    public int DrawbackLevel { get; }

    public bool IsEmpty => First == ModStat.None && Second == ModStat.None && Drawback == ModStat.None;

    /// <summary>The gain level of a stat (both slots add up, capped at 3).</summary>
    public int Gain(ModStat stat)
        => Math.Min(3, (First == stat ? FirstLevel : 0) + (Second == stat ? SecondLevel : 0));

    /// <summary>The drawback level of a stat.</summary>
    public int Loss(ModStat stat) => Drawback == stat ? DrawbackLevel : 0;

    /// <summary>The changes an item key carries (empty for a plain item or a malformed payload). A payload that names a
    /// value this version does not know — a hand-typed key, a key of a newer version — is empty as a whole: the item then
    /// acts as the plain one, never as half a change.</summary>
    public static ItemMods Of(string? itemKey)
    {
        string payload = ItemKey.GetTag(itemKey, Tag);
        if (payload.Length != PayloadLength
            || !int.TryParse(payload, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
        {
            return default;
        }

        var first = (ModStat)((v >> 20) & 0xF);
        var second = (ModStat)((v >> 12) & 0xF);
        var drawback = (ModStat)((v >> 4) & 0xF);
        if (!BioRules.IsKnown(first) || !BioRules.IsKnown(second) || !BioRules.IsKnown(drawback))
        {
            return default;
        }

        return new ItemMods(first, (v >> 16) & 0xF, second, (v >> 8) & 0xF, drawback, v & 0xF);
    }

    /// <summary>The item key with these changes (the plain base key for an empty set).</summary>
    public string ApplyTo(string itemKey)
    {
        if (IsEmpty)
        {
            return ItemKey.SetTag(itemKey, Tag, string.Empty);
        }

        int v = ((int)First << 20) | (FirstLevel << 16) | ((int)Second << 12) | (SecondLevel << 8) | ((int)Drawback << 4) | DrawbackLevel;
        return ItemKey.SetTag(itemKey, Tag, v.ToString("x6", CultureInfo.InvariantCulture));
    }

    public bool Equals(ItemMods other)
        => First == other.First && FirstLevel == other.FirstLevel && Second == other.Second && SecondLevel == other.SecondLevel
           && Drawback == other.Drawback && DrawbackLevel == other.DrawbackLevel;

    public override bool Equals(object? obj) => obj is ItemMods other && Equals(other);

    public override int GetHashCode()
        => ((int)First << 20) | (FirstLevel << 16) | ((int)Second << 12) | (SecondLevel << 8) | ((int)Drawback << 4) | DrawbackLevel;

    public static bool operator ==(ItemMods left, ItemMods right) => left.Equals(right);

    public static bool operator !=(ItemMods left, ItemMods right) => !left.Equals(right);
}

/// <summary>The effective values of a changed tool — THE place tool values are read through once an item key is known.</summary>
public static class ToolMods
{
    /// <summary>Per level: mining power and damage.</summary>
    public const float PowerPerLevel = 0.10f;

    /// <summary>Per level: suit energy per use.</summary>
    public const float EnergyPerLevel = 0.15f;

    /// <summary>Per level: cooldown and range.</summary>
    public const float CooldownPerLevel = 0.08f;
    public const float RangePerLevel = 0.08f;

    /// <summary>What a drawback on power costs per level.</summary>
    public const float PowerLossPerLevel = 0.05f;

    // Changed values per definition and key. Keyed on the definition's own tool object, so a reloaded content set (or a
    // test with its own items) never reads another set's numbers, and dropped with it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolProperties, Dictionary<string, ToolProperties>> Cache = new();

    /// <summary>The tool values of an item key: the definition's own for a plain key, the changed ones for a key with a
    /// <c>u</c> payload (cached per key — this is asked on every swing).</summary>
    public static ToolProperties? Effective(ItemDefinition? def, string? itemKey)
    {
        if (def?.Tool is not { } tool)
        {
            return null;
        }

        if (string.IsNullOrEmpty(itemKey) || itemKey!.IndexOf(ItemKey.Separator) < 0)
        {
            return tool;
        }

        var mods = ItemMods.Of(itemKey);
        if (mods.IsEmpty)
        {
            return tool;
        }

        var perKey = Cache.GetOrCreateValue(tool);
        lock (perKey)
        {
            if (!perKey.TryGetValue(itemKey, out var changed))
            {
                changed = Apply(tool, mods);
                perKey[itemKey] = changed;
            }

            return changed;
        }
    }

    /// <summary>The changed values. Tier, mining radius and ignition are copied untouched.</summary>
    public static ToolProperties Apply(ToolProperties tool, ItemMods mods)
    {
        float power = 1f + PowerPerLevel * mods.Gain(ModStat.Power) - PowerLossPerLevel * mods.Loss(ModStat.Power);
        float energy = 1f - EnergyPerLevel * mods.Gain(ModStat.Energy) + EnergyPerLevel * mods.Loss(ModStat.Energy);
        float cooldown = 1f - CooldownPerLevel * mods.Gain(ModStat.Cooldown) + CooldownPerLevel * mods.Loss(ModStat.Cooldown);
        float range = 1f + RangePerLevel * mods.Gain(ModStat.Range) - RangePerLevel * mods.Loss(ModStat.Range);
        return new ToolProperties
        {
            Kind = tool.Kind,
            Tier = tool.Tier,
            MiningRadius = tool.MiningRadius,
            Ignites = tool.Ignites,
            Fx = tool.Fx,
            MiningPower = tool.MiningPower * power,
            Damage = tool.Damage * power,
            EnergyPerUse = tool.EnergyPerUse * energy,
            CooldownSeconds = tool.CooldownSeconds * cooldown,
            Range = tool.Range * range,
        };
    }
}

/// <summary>What a changed piece of worn gear adds to the suit formulas, per level.</summary>
public static class GearMods
{
    public const float InsulationPerLevel = 0.05f;
    public const float ArmorPerLevel = 0.04f;
    public const float CorrosionPerLevel = 0.05f;
    public const float FallPerLevel = 0.06f;
    public const float GripPerLevel = 0.05f;
    public const float OxygenPerLevel = 10f;
    public const float WeightPerLevel = 0.02f;

    /// <summary>What every changed piece among <paramref name="wornKeys"/> adds to one stat — the caller adds it to the
    /// gear formula and applies that formula's cap. <see cref="ModStat.Weight"/> is the slow-down of the wearer.</summary>
    public static float Bonus(IEnumerable<string?> wornKeys, ModStat stat)
    {
        float per = stat switch
        {
            ModStat.Insulation => InsulationPerLevel,
            ModStat.Armor => ArmorPerLevel,
            ModStat.Corrosion => CorrosionPerLevel,
            ModStat.Fall => FallPerLevel,
            ModStat.Grip => GripPerLevel,
            ModStat.Oxygen => OxygenPerLevel,
            ModStat.Weight => WeightPerLevel,
            _ => 0f,
        };
        if (per <= 0f)
        {
            return 0f;
        }

        float sum = 0f;
        foreach (string? key in wornKeys)
        {
            if (string.IsNullOrEmpty(key) || key!.IndexOf(ItemKey.Separator) < 0)
            {
                continue;
            }

            var mods = ItemMods.Of(key);
            sum += per * (stat == ModStat.Weight ? mods.Loss(stat) : mods.Gain(stat) - mods.Loss(stat));
        }

        return stat == ModStat.Weight ? Math.Min(ItemMods.MaxWeight, sum) : sum;
    }
}

/// <summary>
/// The lab's item changer: a tool or a piece of gear + a mineral sample + a coating → changed values. The material's
/// fixed traits decide <i>which</i> value changes; the deposit's origin values and the coating decide <i>how much</i>.
/// Every change beyond a small one comes with a drawback.
/// </summary>
public static class ItemModRules
{
    /// <summary>Which value a material trait changes on a tool or weapon.</summary>
    public static ModStat ToolStat(MatTrait trait) => trait switch
    {
        MatTrait.Conductive => ModStat.Energy,
        MatTrait.Hard => ModStat.Power,
        MatTrait.HeatProof => ModStat.Cooldown,
        MatTrait.ColdProof => ModStat.Energy,
        MatTrait.Magnetic => ModStat.Range,
        MatTrait.Unstable => ModStat.Power,
        MatTrait.Light => ModStat.Cooldown,
        MatTrait.Heavy => ModStat.Power,
        _ => ModStat.None,
    };

    /// <summary>Which value a material trait changes on a worn piece.</summary>
    public static ModStat GearStat(MatTrait trait) => trait switch
    {
        MatTrait.HeatProof => ModStat.Insulation,
        MatTrait.ColdProof => ModStat.Insulation,
        MatTrait.Hard => ModStat.Armor,
        MatTrait.Heavy => ModStat.Armor,
        MatTrait.Light => ModStat.Fall,
        MatTrait.Magnetic => ModStat.Grip,
        MatTrait.Conductive => ModStat.Oxygen,
        MatTrait.Unstable => ModStat.Corrosion,
        _ => ModStat.None,
    };

    /// <summary>Which value a coating's effect adds as the second gain.</summary>
    public static ModStat CoatingStat(BioEffect effect, bool tool) => effect switch
    {
        BioEffect.Strength or BioEffect.Mining => tool ? ModStat.Power : ModStat.Armor,
        BioEffect.Reflex or BioEffect.Speed => tool ? ModStat.Cooldown : ModStat.None,
        BioEffect.Energy => tool ? ModStat.Energy : ModStat.Oxygen,
        BioEffect.Perception or BioEffect.NightSight => tool ? ModStat.Range : ModStat.None,
        BioEffect.HeatWard or BioEffect.ColdWard => tool ? ModStat.Cooldown : ModStat.Insulation,
        BioEffect.ToxinWard => tool ? ModStat.None : ModStat.Corrosion,
        BioEffect.FeatherFall or BioEffect.Jump => tool ? ModStat.None : ModStat.Fall,
        BioEffect.Grip => tool ? ModStat.None : ModStat.Grip,
        BioEffect.Breath => tool ? ModStat.Energy : ModStat.Oxygen,
        BioEffect.Shield or BioEffect.Regeneration => tool ? ModStat.None : ModStat.Armor,
        _ => ModStat.None,
    };

    /// <summary>
    /// Computes the changes. Returns an empty set when the material has no trait that means anything for the item.
    /// <paramref name="tool"/> is true for tools and weapons, false for worn gear; <paramref name="hasCooldown"/> and
    /// <paramref name="hasRange"/> say whether the tool has those values at all (a laser has no cooldown to shorten).
    /// </summary>
    public static ItemMods Compute(bool tool, bool hasCooldown, bool hasRange, bool usesEnergy, MaterialProfile material, Compound? coating)
    {
        bool Usable(ModStat stat) => stat != ModStat.None
            && (!tool || ((stat != ModStat.Cooldown || hasCooldown) && (stat != ModStat.Range || hasRange) && (stat != ModStat.Energy || usesEnergy)));

        ModStat StatOf(MatTrait trait) => tool ? ToolStat(trait) : GearStat(trait);

        var lead = material.Strongest(t => t != MatTrait.None && Usable(StatOf(t)));
        if (lead == MatTrait.None)
        {
            return default;
        }

        // How much: the trait's level on this world, the deposit's purity, and what the coating adds.
        int coatingLevel = coating is { Failed: false } ? coating.Level : 0;
        int score = material.LevelOf(lead) * 2 + material.Purity + coatingLevel / 3;
        int firstLevel = score >= 12 ? 3 : score >= 8 ? 2 : 1;
        var first = StatOf(lead);

        var second = ModStat.None;
        int secondLevel = 0;
        if (coating is { Failed: false })
        {
            var stat = CoatingStat(coating.Effect, tool);
            if (Usable(stat))
            {
                second = stat;
                secondLevel = coating.Level >= 11 ? 3 : coating.Level >= 6 ? 2 : 1;
            }
        }

        // The price: anything beyond a small change costs something. A heavy or unstable material names its own price,
        // a deposit's trace trait may; otherwise a tool gets slower to use and gear gets heavier.
        int total = firstLevel + secondLevel;
        var drawback = ModStat.None;
        int drawbackLevel = 0;
        if (total >= 3)
        {
            drawbackLevel = Math.Min(3, total - 2);
            if (!tool)
            {
                // A light material carries its own weight: gear made with it pays nothing.
                drawback = material.LevelOf(MatTrait.Light) > 0 ? ModStat.None : ModStat.Weight;
                drawbackLevel = drawback == ModStat.None ? 0 : drawbackLevel;
            }
            else
            {
                drawback = material.LevelOf(MatTrait.Unstable) > 0 && hasRange && first != ModStat.Range && second != ModStat.Range ? ModStat.Range
                    : hasCooldown && first != ModStat.Cooldown && second != ModStat.Cooldown ? ModStat.Cooldown
                    : usesEnergy && first != ModStat.Energy && second != ModStat.Energy ? ModStat.Energy
                    : first != ModStat.Power && second != ModStat.Power ? ModStat.Power
                    : ModStat.None;
                if (drawback == ModStat.None)
                {
                    // No value left to pay with: the change stays small instead.
                    drawbackLevel = 0;
                    firstLevel = secondLevel > 0 ? 1 : 2;
                    secondLevel = secondLevel > 0 ? 1 : 0;
                }
            }
        }

        return new ItemMods(first, firstLevel, second, secondLevel, drawback, drawbackLevel);
    }
}
