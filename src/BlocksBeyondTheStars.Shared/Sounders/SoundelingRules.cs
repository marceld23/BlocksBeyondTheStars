// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The Soundelings (#3359, #3366 — canon from the school club's creature notes): small pastel fluff balls with a soft glow on
/// tiny feet. They love light and fear the dark: curious by day, curled into a motionless fluff ball at night, and by day in a
/// dark place they hide — they walk to a light nearby, or curl up where they are. By day a player can hand one a light (a torch,
/// a lantern, any lamp block, with the Feed key): it coos, thinks for a moment, carries the light to a dark spot nearby and puts
/// it there — on a free cell with solid ground under it, never inside somebody else's base. Useful in caves and the ruins of
/// Ornasa.
/// <para>The numbers the server's rules, the client's Feed probe and the tests share. Pure and Unity-free.</para>
/// </summary>
public static class SoundelingRules
{
    /// <summary>A player hands a light to a Soundeling within this many blocks (the Feed key's reach for this).</summary>
    public const float GiveRange = 6f;

    /// <summary>How far from where it stands a Soundeling looks for a dark spot to light.</summary>
    public const int DarkSearchRadius = 12;

    /// <summary>How far up and down from its feet it looks for that spot.</summary>
    public const int DarkSearchDepth = 6;

    /// <summary>It carries the light at least this far — a light dropped at its own feet would not be "carried".</summary>
    public const float MinCarryDistance = 2.5f;

    /// <summary>A cell counts as lit when a light source stands within this many blocks of it (or its light's own reach, when
    /// shorter).</summary>
    public const int LitRadius = 6;

    /// <summary>A cell is under open sky when nothing solid stands in this many cells over it.</summary>
    public const int SkyScan = 16;

    /// <summary>The little moment it thinks before it sets off with the light, in seconds (it coos meanwhile).</summary>
    public const double PonderSeconds = 1.5;

    /// <summary>How long it tries to reach the spot before it puts the light down where it got to, in seconds.</summary>
    public const double CarryTimeoutSeconds = 25.0;

    /// <summary>It puts the light down once it is this close to the spot (blocks, horizontal).</summary>
    public const float PlaceReach = 1.6f;

    /// <summary>How often a Soundeling checks whether it stands in the dark, in seconds (the check reads blocks).</summary>
    public const double DarkCheckSeconds = 2.0;

    /// <summary>By day in the dark it walks to a light within this many blocks; with none in reach it curls up and hides.</summary>
    public const float LightSeekRange = 10f;

    /// <summary>It stops beside the light it walked to at this distance.</summary>
    public const float LightRestDistance = 2f;

    /// <summary>The coo's sound family: <c>soundeling_coo_1..3</c>.</summary>
    public const string CooPrefix = "soundeling_coo_";

    /// <summary>How many coos the family holds.</summary>
    public const int CooVariants = 3;

    /// <summary>The block category every lamp block wears in <c>data/blocks.json</c>.</summary>
    public const string LightCategory = "light";

    /// <summary>Whether <paramref name="sp"/> is a Soundeling.</summary>
    public static bool IsSoundeling(CreatureSpecies? sp) => SoundersFauna.IsSoundeling(sp);

    /// <summary>Whether <paramref name="item"/> is a light a Soundeling takes: an item that places a lamp block — a block of the
    /// "light" category that really lights (<see cref="BlockLight.NaturalColorOf"/>), the torch, the lantern, every lamp.
    /// <paramref name="block"/> is the block the item places (the caller looks it up).</summary>
    public static bool IsLightItem(ItemDefinition? item, BlockDefinition? block)
        => item is not null && !string.IsNullOrEmpty(item.PlacesBlock) && block is not null
           && string.Equals(block.Key, item.PlacesBlock, StringComparison.Ordinal)
           && string.Equals(block.Category, LightCategory, StringComparison.Ordinal)
           && BlockLight.NaturalColorOf(block) != 0;

    /// <summary>Whether a light whose source block reaches <paramref name="sourceRadius"/> blocks lights a cell
    /// <paramref name="distance"/> blocks away (Chebyshev).</summary>
    public static bool Lights(int distance, int sourceRadius) => distance <= Math.Min(LitRadius, Math.Max(1, sourceRadius));

    /// <summary>The darkness rule: a cell is dark when it lies under a roof (no open sky) and no light reaches it.</summary>
    public static bool IsDark(bool openSky, bool lit) => !openSky && !lit;

    /// <summary>Whether a Soundeling takes a light right now: by day only, awake (not curled up), and not carrying one already.</summary>
    public static bool TakesLight(bool night, bool curled, bool carrying) => !night && !curled && !carrying;

    /// <summary>The coo for <paramref name="roll"/>.</summary>
    public static string CooSound(int roll) => SoundersFauna.Variant(CooPrefix, CooVariants, roll);
}
