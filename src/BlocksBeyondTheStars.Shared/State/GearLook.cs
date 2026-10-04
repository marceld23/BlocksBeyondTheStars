// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// What the worn gear looks like on an avatar, as a bitmask (the <c>Gear</c> field of the presence). One mask for both
/// sides: the server sends it for every other player, the client builds the same mask for its own body, so a piece
/// draws the same on you and on everybody else. The low bits are the kinds every avatar draws (any helmet, any chest
/// plate, …) and keep their meaning for older clients; the tier-2 pieces add a bit of their own on top (#2294–#2297),
/// so a newer client can draw the titanium look while an older one still draws a plain helmet.
/// </summary>
public static class GearLook
{
    /// <summary>Any helmet (the plain one or the titanium helmet).</summary>
    public const int Helmet = 1;

    /// <summary>Any chest piece: armour plating, the stealth suit or the titanium chest plate.</summary>
    public const int Chest = 2;

    /// <summary>Any leg plates (plain or titanium).</summary>
    public const int Legs = 4;

    /// <summary>The jetpack on the back.</summary>
    public const int Jetpack = 8;

    /// <summary>The suit lamp.</summary>
    public const int Lamp = 16;

    /// <summary>Any boots (plain or spring boots).</summary>
    public const int Boots = 32;

    /// <summary>An oxygen tank.</summary>
    public const int Tank = 64;

    /// <summary>The climbing gloves (#2192).</summary>
    public const int ClimbingGloves = 128;

    /// <summary>The climbing claws (#2192).</summary>
    public const int ClimbingClaws = 256;

    /// <summary>The titanium helmet (#2294) — set together with <see cref="Helmet"/>.</summary>
    public const int TitanHelmet = 512;

    /// <summary>The titanium chest plate (#2294) — set together with <see cref="Chest"/>.</summary>
    public const int TitanChest = 1024;

    /// <summary>The titanium leg plates (#2294) — set together with <see cref="Legs"/>.</summary>
    public const int TitanLegs = 2048;

    /// <summary>The spring boots (#2295) — set together with <see cref="Boots"/>.</summary>
    public const int SpringBoots = 4096;

    /// <summary>The glider on the back (#2296) — it takes the jetpack's place, so it never comes with <see cref="Jetpack"/>.</summary>
    public const int Glider = 8192;

    /// <summary>The suit battery (#2297).</summary>
    public const int SuitBattery = 16384;

    /// <summary>The mask of the gear <paramref name="worn"/> says is worn (by base key — a piece the bio lab changed is
    /// still the same piece).</summary>
    public static int Mask(Func<string, bool> worn)
    {
        int g = 0;
        g |= Bit(worn("helmet"), Helmet);
        g |= Bit(worn("titan_helmet"), Helmet | TitanHelmet);
        g |= Bit(worn("armor_chest") || worn("stealth_suit"), Chest);
        g |= Bit(worn("titan_chest"), Chest | TitanChest);
        g |= Bit(worn("armor_legs"), Legs);
        g |= Bit(worn("titan_legs"), Legs | TitanLegs);
        g |= Bit(worn("jetpack"), Jetpack);
        g |= Bit(worn("glider"), Glider);
        g |= Bit(worn("suit_lamp"), Lamp);
        g |= Bit(worn("boots"), Boots);
        g |= Bit(worn("spring_boots"), Boots | SpringBoots);
        g |= Bit(worn("oxygen_tank_1") || worn("oxygen_tank_2") || worn("oxygen_tank_3"), Tank);
        g |= Bit(worn("climbing_gloves"), ClimbingGloves);
        g |= Bit(worn("climbing_claws"), ClimbingClaws);
        g |= Bit(worn("suit_battery"), SuitBattery);
        return g;
    }

    /// <summary>Whether <paramref name="mask"/> carries every bit of <paramref name="bit"/>.</summary>
    public static bool Has(int mask, int bit) => (mask & bit) == bit;

    private static int Bit(bool on, int bits) => on ? bits : 0;
}
