// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The radios on the client side (#2567/#2569): comms work only while a radio is WORN, so the radio tab and the voice
/// indicator ask which one is worn (the widest wins), what it reaches, and which carried one "Put on radio" wears.
/// </summary>
public sealed class RadioGearTests
{
    [Fact]
    public void Worn_IsTheWidestWornRadio_OrNull()
    {
        var worn = new HashSet<string> { "comm_radio", "system_radio" };

        Assert.Equal("system_radio", RadioGear.Worn(worn.Contains));
        Assert.Null(RadioGear.Worn(new HashSet<string> { "suit_lamp" }.Contains)); // a lamp is no radio
        Assert.Null(RadioGear.Worn(null));
    }

    [Fact]
    public void Reach_FollowsTheTier()
    {
        Assert.Equal("ui.equip.effect.radio_planet", RadioGear.ReachLabelKey("comm_radio"));
        Assert.Equal("ui.equip.effect.radio_system", RadioGear.ReachLabelKey("system_radio"));
        Assert.Equal("ui.equip.effect.radio_galaxy", RadioGear.ReachLabelKey("galaxy_radio"));
    }

    [Fact]
    public void Carried_PrefersTheBackpack_ThenTheHold_TheWidestFirst()
    {
        var personal = new[]
        {
            new NetItemStack { Slot = 0, Item = "pickaxe", Count = 1 },
            new NetItemStack { Slot = 4, Item = "comm_radio", Count = 1 },
            new NetItemStack { Slot = 7, Item = "system_radio", Count = 1 },
        };
        var cargo = new[] { new NetItemStack { Slot = 2, Item = "galaxy_radio", Count = 1 } };

        var fromPack = RadioGear.Carried(personal, cargo);
        Assert.NotNull(fromPack);
        Assert.Equal(7, fromPack!.Value.Stack.Slot); // the system radio beats the comm radio
        Assert.False(fromPack.Value.FromCargo);

        var fromHold = RadioGear.Carried(new[] { personal[0] }, cargo);
        Assert.NotNull(fromHold);
        Assert.Equal(2, fromHold!.Value.Stack.Slot);
        Assert.True(fromHold.Value.FromCargo);

        Assert.Null(RadioGear.Carried(new[] { personal[0] }, null)); // none carried (or the hold unknown off board)
    }

    [Fact]
    public void ALabChangedRadio_IsStillARadio()
    {
        Assert.True(RadioGear.IsRadio("comm_radio#t00ff00"));
        Assert.False(RadioGear.IsRadio("suit_lamp"));
        Assert.False(RadioGear.IsRadio(null));
    }
}
