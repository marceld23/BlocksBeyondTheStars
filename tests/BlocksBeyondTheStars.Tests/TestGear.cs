// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Puts suit gear ON a test player (#2110): since the equipment slots, gear works only while worn, so a test
/// that used to drop a helmet or a radio into the backpack wears it instead — into the slot its definition names (a
/// module into the first free module slot, else the first), replacing whatever was worn there.</summary>
public static class TestGear
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public static void Wear(PlayerState p, string key)
    {
        var def = Content.GetItem(key) ?? throw new System.ArgumentException("no such item: " + key, nameof(key));
        var slot = EquipSlots.Parse(def.EquipSlot) ?? throw new System.ArgumentException(key + " is not wearable", nameof(key));
        if (slot == EquipSlot.Module1 && p.Equipment.Slots[(int)EquipSlot.Module1] is { IsEmpty: false } && p.Equipment.Slots[(int)EquipSlot.Module2] is null)
        {
            slot = EquipSlot.Module2;
        }

        p.Equipment.SetSlot((int)slot, new ItemStack(key, 1));
    }

    /// <summary>Takes every piece of this key off.</summary>
    public static void TakeOff(PlayerState p, string key)
    {
        for (int i = 0; i < p.Equipment.SlotCount; i++)
        {
            if (p.Equipment.Slots[i] is { IsEmpty: false } s && s.Item == key)
            {
                p.Equipment.SetSlot(i, null);
            }
        }
    }
}
