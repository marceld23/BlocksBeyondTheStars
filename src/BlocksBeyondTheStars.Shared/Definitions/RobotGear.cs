// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Bio;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The hunter robot's gear (#3398/#3399, Justus' "Robot upgrade": "a jetpack or weapons that it uses — just NO armour!"):
/// one weapon and one jetpack, given with E while the item is held, taken back in the Companions tab. The rules the server
/// and the client share — which item is which piece, and what a weapon makes of the robot's shot — live here, read from the
/// item data (a weapon's damage, range and FX colour), so a new weapon in <c>data/items.json</c> needs no code. There is
/// deliberately no armour piece.
/// </summary>
public static class RobotGear
{
    /// <summary>The weapon piece (an item whose tool kind is <see cref="ToolKind.Weapon"/>).</summary>
    public const string WeaponPiece = "weapon";

    /// <summary>The jetpack piece.</summary>
    public const string JetpackPiece = "jetpack";

    /// <summary>The jetpack's item key.</summary>
    public const string JetpackItem = "jetpack";

    /// <summary>A companion's share of a weapon's damage per shot.</summary>
    public const float WeaponDamageShare = 0.6f;

    /// <summary>The shortest reach a weapon gives the robot (a melee weapon: it hits what stands this close).</summary>
    public const float MinReach = 4f;

    /// <summary>The longest reach a weapon gives the robot.</summary>
    public const float MaxReach = 20f;

    /// <summary><c>NetCreature.GearFlags</c>: the robot wears a jetpack.</summary>
    public const int FlagJetpack = 1;

    /// <summary><c>NetCreature.GearFlags</c>: the robot is flying on its jetpack right now (the client shows the flame).</summary>
    public const int FlagFlying = 2;

    /// <summary>Which piece of gear an item is: <see cref="WeaponPiece"/>, <see cref="JetpackPiece"/> or empty (the robot
    /// takes nothing else — no armour).</summary>
    public static string PieceOf(ItemDefinition? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (item.Tool is { Kind: ToolKind.Weapon })
        {
            return WeaponPiece;
        }

        return string.Equals(item.Key, JetpackItem, StringComparison.Ordinal) ? JetpackPiece : string.Empty;
    }

    /// <summary>Whether <paramref name="piece"/> names a piece of gear.</summary>
    public static bool IsPiece(string? piece) => piece is WeaponPiece or JetpackPiece;

    /// <summary>The robot's damage per shot with <paramref name="weapon"/>: <see cref="WeaponDamageShare"/> of the weapon's
    /// damage, never below <paramref name="baseDamage"/> (its own shot without a weapon). <paramref name="itemKey"/> is the
    /// key of the very item the robot carries (see <see cref="WeaponValues"/>).</summary>
    public static float ShotDamage(ItemDefinition? weapon, float baseDamage, string? itemKey = null)
        => WeaponValues(weapon, itemKey) is { } tool ? Math.Max(baseDamage, WeaponDamageShare * tool.Damage) : baseDamage;

    /// <summary>The robot's reach with <paramref name="weapon"/>: the weapon's range clamped to <see cref="MinReach"/> ..
    /// <see cref="MaxReach"/>; <paramref name="baseReach"/> without one. <paramref name="itemKey"/> as in
    /// <see cref="ShotDamage"/>.</summary>
    public static float Reach(ItemDefinition? weapon, float baseReach, string? itemKey = null)
        => WeaponValues(weapon, itemKey) is { } tool ? Math.Min(MaxReach, Math.Max(MinReach, tool.Range)) : baseReach;

    /// <summary>The values of the weapon the robot carries, or null when the item is no weapon. With the item's key these
    /// are the values of that very item (#3730): what the bio lab changed on it rides in the key
    /// (<see cref="ToolMods.Effective"/>), so a sharpened laser hits harder in the robot's hand too. Without a key — or
    /// with a plain one, as every robot of an older save carries — they are the definition's own.</summary>
    public static ToolProperties? WeaponValues(ItemDefinition? weapon, string? itemKey = null)
        => weapon?.Tool is { Kind: ToolKind.Weapon } ? ToolMods.Effective(weapon, itemKey) : null;

    /// <summary>The tracer colour of the robot's shot with <paramref name="weapon"/> (0xRRGGBB, the weapon's FX colour); 0 =
    /// the robot's own light cyan.</summary>
    public static int TracerRgb(ItemDefinition? weapon)
    {
        string? hex = weapon?.Tool is { Kind: ToolKind.Weapon } tool ? tool.Fx?.Color : null;
        if (!HeldModelPart.TryParseColor(hex, out float r, out float g, out float b))
        {
            return 0;
        }

        return ((int)Math.Round(r * 255f) << 16) | ((int)Math.Round(g * 255f) << 8) | (int)Math.Round(b * 255f);
    }
}
