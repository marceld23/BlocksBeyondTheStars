// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The beak on a head (#3974, the birds of Earth; #3975, the dolphin's beaked snout). Every species before it has none.
/// Appended values only: the name travels on the wire and in saves.
/// </summary>
public enum CreatureBeakStyle
{
    None,
    Cone,    // a short, pointed beak — a chicken's
    Hooked,  // a deep beak whose upper half curves down over the lower one — a parrot's, an eagle's
    Rostrum, // a long, rounded snout in the head's own colour — a dolphin's
}

/// <summary>
/// The beak's measures (#3974): one rule for the depth the client's <c>CreatureBuilder.Beak.cs</c> draws in front of the face
/// and the server's head reach (<see cref="CreatureFootprint.HeadReach"/>), as the hound's muzzle has
/// (<see cref="CreatureFootprint.HoundMuzzleDepth"/>). Pure and Unity-free.
/// </summary>
public static class CreatureBeaks
{
    /// <summary>The longest beak, as a share of the head's depth unit (a dolphin's rostrum).</summary>
    public const float MaxLength = 1.2f;

    /// <summary>The red of a chicken's comb and wattle (0xRRGGBB) — combs are red in every breed, so it is no coat colour.</summary>
    public const int CombRgb = 0xD8282A;

    /// <summary>The length a style takes when the record names none.</summary>
    public static float DefaultLength(CreatureBeakStyle style) => style switch
    {
        CreatureBeakStyle.Cone => 0.35f,
        CreatureBeakStyle.Hooked => 0.45f,
        CreatureBeakStyle.Rostrum => 0.7f,
        _ => 0f,
    };

    /// <summary>The length a species' beak is drawn with: its own (clamped to <see cref="MaxLength"/>), the style's default when
    /// it names none, 0 without a beak.</summary>
    public static float LengthOf(CreatureBeakStyle style, float length)
        => style == CreatureBeakStyle.None ? 0f : length > 0f ? Math.Min(length, MaxLength) : DefaultLength(style);

    /// <summary>How far a beak reaches in front of the face for a body unit <paramref name="unit"/> — the same scale as the
    /// hound's muzzle (a share × 0.8u). The client builds it with exactly this depth.</summary>
    public static float Depth(CreatureBeakStyle style, float length, float unit) => LengthOf(style, length) * 0.8f * unit;

    /// <summary>The wire's style name back to the style (case-sensitive, as the enum's name); an unknown name (a newer server's)
    /// or a number reads as none.</summary>
    public static CreatureBeakStyle Parse(string? name)
        => name != null && Enum.IsDefined(typeof(CreatureBeakStyle), name)
            ? (CreatureBeakStyle)Enum.Parse(typeof(CreatureBeakStyle), name)
            : CreatureBeakStyle.None;
}
