// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The hound body plan — the <b>Schuppenhund</b> (#3325–#3328, generation 33), a school club idea by Jafar: "Neues Tier —
/// Schuppenhund (gefährlich) — 10 hp pro Biss — bellt". A scaly four-legged dog with ears, a toothed muzzle and three claws on
/// every foot; very dangerous, it lives alone and only on the dry worlds (a type's <see cref="PlanetType.ScaleHoundChance"/>).
/// It is a class, not one animal: every dry world rolls its own (colours, size, scale hide, ears, muzzle, eyes, rhythm), and
/// <see cref="CreatureBodyPlan.Hound"/> carries the rest. Every number the generator, the server and the client share lives here.
/// <para>Kid-friendly by construction: its top speed (even charging) stays below the player's walk, so running and hiding
/// remain an answer; the family/peaceful presets (planet enemies off) switch its bite off like every other.</para>
/// </summary>
public static class HoundRules
{
    /// <summary>The species' name on every world and in every language (Marcel 2026-11-07: "Schuppenhund in allen Sprachen") —
    /// a proper name like "Mini-Michi-Paul", never coined, never translated.</summary>
    public const string SpeciesName = "Schuppenhund";

    /// <summary>The roster id of a world's hound — one per world at most.</summary>
    public const string SpeciesId = "hd_schuppenhund";

    /// <summary>Waist- to chest-high on the player (the standard roll's size is a scale, ~0.5 blocks per unit).</summary>
    public const float MinSize = 1.0f;
    public const float MaxSize = 1.6f;

    /// <summary>The species speed. Its Prowler burst is ×1.4 and the charge ×<see cref="ChargeFactor"/> on top — at most
    /// 3.6 × 1.4 × 1.15 ≈ 5.8 blocks/s, still below the player's walk of 6.</summary>
    public const float MinSpeed = 3.0f;
    public const float MaxSpeed = 3.6f;

    /// <summary>Tough: a rolled hostile has ~30, the Sreekmakra 90.</summary>
    public const float MinHealth = 60f;
    public const float MaxHealth = 80f;

    /// <summary>"10 hp pro Biss" — Jafar's number.</summary>
    public const float BiteDamage = 10f;

    /// <summary>Seconds between two bites, rolled per world: 10 per 1.3–1.7 s ≈ 6–7.7 per second, the top of the rolled
    /// hostile band (2–7) and below the Sreekmakra (10.5).</summary>
    public const float MinBiteInterval = 1.3f;
    public const float MaxBiteInterval = 1.7f;

    /// <summary>A hunter notices prey from further away than the classic 8 blocks.</summary>
    public const float AggroRange = 12f;

    /// <summary>Within this many blocks of its prey a hunting hound charges.</summary>
    public const float ChargeRange = 7f;

    /// <summary>The charge's speed factor on cruise and burst (see <see cref="MaxSpeed"/> for the ceiling).</summary>
    public const float ChargeFactor = 1.15f;

    /// <summary>It lives alone: at most this many wild hounds around a player at a time.</summary>
    public const int MaxWildAtOnce = 1;

    /// <summary>VEGA warns the first time a player comes within this many blocks of one (once per session).</summary>
    public const float SightingRange = 24f;

    /// <summary>How far the muzzle reaches in front of the head, as a share of the head's depth.</summary>
    public const float MinSnout = 0.45f;
    public const float MaxSnout = 0.8f;

    /// <summary>What it drops: meat (Marcel 2026-11-07).</summary>
    public const string DropItem = "creature_meat";
    public const int DropCount = 3;

    /// <summary>The scale hides a hound wears, everywhere — the head too, as drawn. A client without one skips it.</summary>
    public static readonly System.Collections.Generic.IReadOnlyList<string> Hides = new[] { "coarsescales", "scales", "finescales", "lizard", "snake", "pebbled", "plated" };

    /// <summary>The ear styles a hound rolls (never <see cref="CreatureEarStyle.None"/>: "Ohren", 2026-11-07).</summary>
    public static readonly System.Collections.Generic.IReadOnlyList<CreatureEarStyle> EarStyles = new[] { CreatureEarStyle.Pointed, CreatureEarStyle.Pointed, CreatureEarStyle.Tall, CreatureEarStyle.Swept };

    public static bool IsHound(CreatureSpecies sp) => sp.BodyPlan == CreatureBodyPlan.Hound;
}
