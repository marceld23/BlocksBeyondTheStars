// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>
/// Keys of the cosmetics a festival day calendar can unlock (#3315): <c>hudframe:&lt;key&gt;</c> (a HUD frame template from
/// <c>data/hud_frames.json</c>, #3320) and <c>skin:&lt;key&gt;</c> (a character skin template from <c>data/skins.json</c>,
/// #3321). An unlock is a per-world milestone <c>cosmetic:&lt;key&gt;</c>; the server tells the player their unlocked keys
/// (<c>CosmeticUnlocks</c>). Cosmetics are templates for the editors — what a player then wears is their own.
/// </summary>
public static class CosmeticKeys
{
    public const string HudFramePrefix = "hudframe:";

    public const string SkinPrefix = "skin:";

    /// <summary>The milestone prefix of an unlocked cosmetic.</summary>
    public const string MilestonePrefix = "cosmetic:";

    /// <summary>Whether <paramref name="key"/> names a known kind of cosmetic.</summary>
    public static bool IsCosmetic(string? key)
        => !string.IsNullOrEmpty(key)
            && (key!.StartsWith(HudFramePrefix, StringComparison.Ordinal) || key.StartsWith(SkinPrefix, StringComparison.Ordinal));
}

/// <summary>The HUD frame templates (<c>data/hud_frames.json</c>, #3317/#3320).</summary>
public sealed class HudFrameCatalog
{
    public List<HudFrameDefinition> Frames { get; set; } = new();
}

/// <summary>
/// A HUD frame template: four 32 × 32 tiles of palette indices (the pixel editor's base32 alphabet, one char per pixel,
/// row 0 = top, 0 = transparent) — the top-left <see cref="Corner"/> (mirrored to the other corners), the <see cref="Top"/>
/// edge, the left <see cref="Side"/> edge (mirrored to the right) and the <see cref="Bottom"/> edge, tiled along the screen.
/// </summary>
public sealed class HudFrameDefinition
{
    /// <summary>Stable id (<c>christmas_lights</c>); the unlock key is <c>hudframe:&lt;key&gt;</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Empty = free for everybody; <c>calendar</c> = only once a calendar door unlocked it in this world.</summary>
    public string Unlock { get; set; } = string.Empty;

    public string Corner { get; set; } = string.Empty;

    public string Top { get; set; } = string.Empty;

    public string Side { get; set; } = string.Empty;

    public string Bottom { get; set; } = string.Empty;

    /// <summary>Palette indices whose pixels twinkle (the lights of a garland); empty = a still frame.</summary>
    public int[] Twinkle { get; set; } = Array.Empty<int>();

    /// <summary>The colours (<c>#rrggbb</c>) a twinkling pixel cycles through.</summary>
    public string[] TwinkleColors { get; set; } = Array.Empty<string>();

    /// <summary>Locale key of the template's name.</summary>
    public string NameKey => "hudframe." + Key + ".name";
}

/// <summary>The character skin templates (<c>data/skins.json</c>, #3321).</summary>
public sealed class SkinCatalog
{
    public List<SkinDefinition> Skins { get; set; } = new();
}

/// <summary>
/// A character skin template: the four appearance colours and the body paint of the four parts (torso, arms, legs, helmet —
/// the same payloads the character editor paints, <c>BodyPaint</c>); the player's own face stays.
/// </summary>
public sealed class SkinDefinition
{
    /// <summary>Stable id (<c>christmas</c>); the unlock key is <c>skin:&lt;key&gt;</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Empty = free for everybody; <c>calendar</c> = only once a calendar door unlocked it in this world.</summary>
    public string Unlock { get; set; } = string.Empty;

    /// <summary>Skin, torso, arms, legs colours as <c>#rrggbb</c> (the appearance colours).</summary>
    public string[] Colors { get; set; } = Array.Empty<string>();

    /// <summary>Body paint payloads by part (0 torso, 1 arms, 2 legs, 3 helmet); empty = unpainted.</summary>
    public string[] Paint { get; set; } = Array.Empty<string>();

    /// <summary>Locale key of the template's name.</summary>
    public string NameKey => "skin." + Key + ".name";
}
