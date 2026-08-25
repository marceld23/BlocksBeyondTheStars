// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The one suit-gear formula (#1270): gear works while carried, armour stacks up to a cap, of tanks and
/// liners only the best carried piece counts. Shared by the server (vitals/combat) and the client (Suit
/// tab readout, HUD oxygen bar), so a drift between the two would show up here.
/// </summary>
public sealed class SuitEquipmentTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static System.Func<string, bool> Carrying(params string[] keys) => key => keys.Contains(key);

    [Fact]
    public void Armour_StacksAcrossPieces_AndIsCapped()
    {
        var items = _content.Items.Values;
        Assert.Equal(0f, SuitEquipment.ArmorResistance(items, Carrying()));
        Assert.Equal(0.25f, SuitEquipment.ArmorResistance(items, Carrying("armor_chest")), 3);
        Assert.Equal(0.55f, SuitEquipment.ArmorResistance(items, Carrying("armor_chest", "armor_legs", "helmet")), 3);

        var heavy = new[]
        {
            new ItemDefinition { Key = "a", ArmorResistance = 0.5f },
            new ItemDefinition { Key = "b", ArmorResistance = 0.5f },
        };
        Assert.Equal(SuitEquipment.MaxArmorResistance, SuitEquipment.ArmorResistance(heavy, Carrying("a", "b")));
    }

    [Fact]
    public void OxygenTanks_OnlyTheBestCarriedCounts()
    {
        var items = _content.Items.Values;
        Assert.Equal(100f, SuitEquipment.MaxOxygen(items, Carrying()));
        Assert.Equal(150f, SuitEquipment.MaxOxygen(items, Carrying("oxygen_tank_1")));
        Assert.Equal(300f, SuitEquipment.MaxOxygen(items, Carrying("oxygen_tank_1", "oxygen_tank_2", "oxygen_tank_3")));
    }

    [Fact]
    public void SuitLiners_OnlyTheBestCounts_AndArmourInsulatesALittle()
    {
        var items = _content.Items.Values;
        Assert.Equal(0.15f, SuitEquipment.ThermalInsulation(items, Carrying("armor_chest")), 3);
        Assert.Equal(0.85f, SuitEquipment.ThermalInsulation(items, Carrying("suit_liner_1", "suit_liner_3", "armor_chest")), 3);

        var rig = new[] { new ItemDefinition { Key = "x", ThermalInsulation = 1f } };
        Assert.Equal(SuitEquipment.MaxThermalInsulation, SuitEquipment.ThermalInsulation(rig, Carrying("x")));
    }

    [Fact]
    public void SuitGear_CoversStatItems_AndTheWearableModules()
    {
        foreach (var key in new[] { "armor_chest", "helmet", "oxygen_tank_2", "suit_liner_1", "jetpack", "suit_lamp", "stealth_suit" })
        {
            Assert.True(SuitEquipment.IsSuitGear(_content.GetItem(key)!), key);
        }

        Assert.False(SuitEquipment.IsSuitGear(_content.GetItem("iron_ore")!));
        Assert.False(SuitEquipment.IsSuitGear(_content.GetItem("basic_drill")!));
    }
}
