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

    /// <summary>#3234: the suit lamp is switched on (set together with <see cref="Lamp"/>) — only the server's presence
    /// sets it, from the lamp state the client reports; other clients light their world with the beam and make the lamp
    /// glow. A bit of the mask rather than a field of its own: a presence must stay under the compression threshold.
    /// <see cref="Mask"/> never sets it (it is a state of the lamp, not a piece of gear).</summary>
    public const int LampLit = 32768;

    // #3290–#3292: which SET the piece in a slot belongs to — three bits per slot on top of the generic Helmet / Chest /
    // Legs bit (which stays set, so an older client still draws a plain piece there). 0 = the classic look (the plain
    // plates and the titanium ones, told apart by their own bits above), 1 = the guardian set, 2 = the robber disguise,
    // 3 = the flower set, 4 = the Ressaw diving suit (#3337, the first set with boots of its own — the boots got the
    // fourth slot, bits 25–27, on top of the generic Boots bit), 5–7 reserved. A mask that uses them is above 0xFFFF and
    // costs a wearer two bytes more on the wire; the presence gains no field (a minimal presence must stay under the
    // compression threshold).

    /// <summary>The first bit of the head piece's style (bits 16–18).</summary>
    public const int HeadStyleShift = 16;

    /// <summary>The first bit of the chest piece's style (bits 19–21).</summary>
    public const int ChestStyleShift = 19;

    /// <summary>The first bit of the leg piece's style (bits 22–24).</summary>
    public const int LegsStyleShift = 22;

    /// <summary>#3337: the first bit of the boots' style (bits 25–27).</summary>
    public const int BootsStyleShift = 25;

    /// <summary>The three bits of one slot's style, before the shift.</summary>
    public const int StyleBits = 7;

    /// <summary>The classic look (plain or titanium plates).</summary>
    public const int StyleClassic = 0;

    /// <summary>#3290: the guardian set — dark plates with small red lights.</summary>
    public const int StyleGuardian = 1;

    /// <summary>#3291: the robber disguise — the mask with its breathing filters, the black jacket.</summary>
    public const int StyleRobber = 2;

    /// <summary>#3292: the flower set — the blossom wreath, the flower bracelets.</summary>
    public const int StyleFlower = 3;

    /// <summary>#3337: the Ressaw diving suit — milky white with the purple and red G.D.S. trim, the round visor helmet.</summary>
    public const int StyleRessaw = 4;

    /// <summary>The style of the head piece in <paramref name="mask"/> (0 = classic).</summary>
    public static int HeadStyle(int mask) => (mask >> HeadStyleShift) & StyleBits;

    /// <summary>The style of the chest piece in <paramref name="mask"/> (0 = classic).</summary>
    public static int ChestStyle(int mask) => (mask >> ChestStyleShift) & StyleBits;

    /// <summary>The style of the leg pieces in <paramref name="mask"/> (0 = classic).</summary>
    public static int LegsStyle(int mask) => (mask >> LegsStyleShift) & StyleBits;

    /// <summary>#3337: the style of the boots in <paramref name="mask"/> (0 = classic: the plain or the spring boots).</summary>
    public static int BootsStyle(int mask) => (mask >> BootsStyleShift) & StyleBits;

    /// <summary>The bits of <paramref name="style"/> in the slot that starts at <paramref name="shift"/>.</summary>
    public static int Style(int shift, int style) => (style & StyleBits) << shift;

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
        g |= SetPieces(worn);
        g |= Bit(worn("jetpack"), Jetpack);
        g |= Bit(worn("glider"), Glider);
        g |= Bit(worn("suit_lamp") || worn("ressaw_lamp"), Lamp); // #3339: every lamp the lamp key switches (SuitEquipment.IsLamp)
        g |= Bit(worn("boots"), Boots);
        g |= Bit(worn("spring_boots"), Boots | SpringBoots);
        g |= Bit(worn("oxygen_tank_1") || worn("oxygen_tank_2") || worn("oxygen_tank_3"), Tank);
        g |= Bit(worn("climbing_gloves"), ClimbingGloves);
        g |= Bit(worn("climbing_claws"), ClimbingClaws);
        g |= Bit(worn("suit_battery"), SuitBattery);
        return g;
    }

    /// <summary>#3290–#3292, #3337: the pieces of the guardian, robber and flower sets and of the Ressaw diving suit — the
    /// slot's generic bit plus its style.</summary>
    private static int SetPieces(Func<string, bool> worn)
    {
        int g = 0;
        g |= Bit(worn("guardian_helmet"), Helmet | Style(HeadStyleShift, StyleGuardian));
        g |= Bit(worn("robber_mask"), Helmet | Style(HeadStyleShift, StyleRobber));
        g |= Bit(worn("flower_wreath"), Helmet | Style(HeadStyleShift, StyleFlower));
        g |= Bit(worn("ressaw_helmet"), Helmet | Style(HeadStyleShift, StyleRessaw));
        g |= Bit(worn("guardian_chest"), Chest | Style(ChestStyleShift, StyleGuardian));
        g |= Bit(worn("robber_jacket"), Chest | Style(ChestStyleShift, StyleRobber));
        g |= Bit(worn("flower_bracelets"), Chest | Style(ChestStyleShift, StyleFlower));
        g |= Bit(worn("ressaw_suit"), Chest | Style(ChestStyleShift, StyleRessaw));
        g |= Bit(worn("guardian_legs"), Legs | Style(LegsStyleShift, StyleGuardian));
        g |= Bit(worn("ressaw_leggings"), Legs | Style(LegsStyleShift, StyleRessaw));
        g |= Bit(worn("ressaw_boots"), Boots | Style(BootsStyleShift, StyleRessaw));
        return g;
    }

    /// <summary>Whether <paramref name="mask"/> carries every bit of <paramref name="bit"/>.</summary>
    public static bool Has(int mask, int bit) => (mask & bit) == bit;

    private static int Bit(bool on, int bits) => on ? bits : 0;
}
