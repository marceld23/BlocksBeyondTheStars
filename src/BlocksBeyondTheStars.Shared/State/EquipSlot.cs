// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// The suit's equipment slots (#2110, Justus' "Inventar wie in Minecraft", Marcel's decision): gear works only while it
/// is WORN in one of these, never while it merely rides in the backpack. The order is the slot index in
/// <see cref="PlayerState.Equipment"/> and the order the equipment row is drawn in; new slots go at the end.
/// </summary>
public enum EquipSlot
{
    Head = 0,     // helmet
    Chest = 1,    // chest armour or the stealth suit — one or the other
    Legs = 2,     // leg armour
    Feet = 3,     // boots
    Back = 4,     // jetpack
    Tank = 5,     // an oxygen tank
    Liner = 6,    // a suit liner
    Module1 = 7,  // any module: lamp, extractor, radio, radar, teleporter
    Module2 = 8,
}

public static class EquipSlots
{
    public const int Count = 9;

    /// <summary>The <c>equipSlot</c> names an item definition may carry.</summary>
    public const string Head = "head", Chest = "chest", Legs = "legs", Feet = "feet", Back = "back", Tank = "tank", Liner = "liner", Module = "module";

    public static bool IsModule(EquipSlot slot) => slot is EquipSlot.Module1 or EquipSlot.Module2;

    /// <summary>The slot an item name maps to (a module maps to the first module slot; the equip handler picks a free one).</summary>
    public static EquipSlot? Parse(string? name) => name?.ToLowerInvariant() switch
    {
        Head => EquipSlot.Head,
        Chest => EquipSlot.Chest,
        Legs => EquipSlot.Legs,
        Feet => EquipSlot.Feet,
        Back => EquipSlot.Back,
        Tank => EquipSlot.Tank,
        Liner => EquipSlot.Liner,
        Module => EquipSlot.Module1,
        _ => null,
    };

    /// <summary>Whether an item named for <paramref name="equipSlot"/> may sit in <paramref name="slot"/>.</summary>
    public static bool Accepts(EquipSlot slot, string? equipSlot)
    {
        var wanted = Parse(equipSlot);
        if (wanted is null)
        {
            return false;
        }

        return wanted == EquipSlot.Module1 ? IsModule(slot) : wanted == slot;
    }

    /// <summary>The locale key of a slot's label (<c>ui.equip.slot.&lt;name&gt;</c>).</summary>
    public static string LabelKey(EquipSlot slot) => "ui.equip.slot." + slot switch
    {
        EquipSlot.Head => Head,
        EquipSlot.Chest => Chest,
        EquipSlot.Legs => Legs,
        EquipSlot.Feet => Feet,
        EquipSlot.Back => Back,
        EquipSlot.Tank => Tank,
        EquipSlot.Liner => Liner,
        _ => Module,
    };
}
