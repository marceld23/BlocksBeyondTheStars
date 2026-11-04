// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>Who may land on a player-built base landing pad (#3069) — the owner sets it at the pad.</summary>
public enum BasePadAccess
{
    /// <summary>Only the pad's owner (admins always may) — no travelling merchant either.</summary>
    Owner = 0,

    /// <summary>The owner and their allies and crew mates — and, at a Market base, the travelling merchants (#3175).</summary>
    Allies = 1,

    /// <summary>Every player — and, at a Market base, the travelling merchants (#3073).</summary>
    Everyone = 2,
}

/// <summary>
/// The numbers of the base landing pads (#3069, Marcel 2026-11-01): a player who founded a base builds up to
/// <see cref="MaxPerBase"/> pads next to it, each the centre of a <see cref="Size"/> × <see cref="Size"/> landing square —
/// the size of a communal pad, so every ship fits (the largest hull is 15 × 15). The pad belongs to the nearest own base
/// core within <see cref="ReachXZ"/> blocks; the square is kept clear for <see cref="Clearance"/> blocks above the pad and
/// protected like the base. Server and client read the same numbers (the placement preview, the chooser).
/// </summary>
public static class BasePadRules
{
    /// <summary>The block (and item) key of the pad.</summary>
    public const string BlockKey = "base_pad";

    /// <summary>Pads one base may hold.</summary>
    public const int MaxPerBase = 4;

    /// <summary>Half the landing square: the pad block plus this many blocks to every side (17 × 17).</summary>
    public const int HalfSize = 8;

    /// <summary>The landing square's edge length in blocks.</summary>
    public const int Size = 2 * HalfSize + 1;

    /// <summary>Blocks above the pad that must stay clear for a ship (the communal pads keep the same volume).</summary>
    public const int Clearance = 16;

    /// <summary>How far (horizontally, either axis) a pad may stand from its base core.</summary>
    public const int ReachXZ = 48;

    /// <summary>How far above or below its base core a pad may stand.</summary>
    public const int ReachY = 32;

    /// <summary>The longest pad name (a map label).</summary>
    public const int NameMaxLength = 24;

    /// <summary>A new pad's access.</summary>
    public const BasePadAccess DefaultAccess = BasePadAccess.Allies;

    /// <summary>Whether <paramref name="access"/> lets a player land who is (or is not) the owner or allied with them.</summary>
    public static bool Admits(BasePadAccess access, bool isOwner, bool isAlly)
        => isOwner || access == BasePadAccess.Everyone || (access == BasePadAccess.Allies && isAlly);

    /// <summary>Whether a Market base's pad with <paramref name="access"/> takes the travelling merchants (#3175): on "Me + allies"
    /// and "Everyone" — a player who sets a base to Market wants traders — never on "Only me".</summary>
    public static bool AdmitsTraders(BasePadAccess access) => access != BasePadAccess.Owner;

    /// <summary>Clamps a wire value to a known access (an unknown one reads as the safe default).</summary>
    public static BasePadAccess AccessOf(int value)
        => value is >= (int)BasePadAccess.Owner and <= (int)BasePadAccess.Everyone ? (BasePadAccess)value : DefaultAccess;
}
