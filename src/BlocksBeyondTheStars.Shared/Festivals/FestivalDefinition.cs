// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>
/// The festival file (<c>data/festivals.json</c>, #3000): the real-world festivals the game marks with a mood, a few
/// extra things in the world and a short, kid-friendly explanation. The calendar only sets the default — see
/// <see cref="FestivalCalendar"/>; a world admin may switch any festival on by hand at any time (epic decision 1: no FOMO).
/// </summary>
public sealed class FestivalCatalog
{
    /// <summary>The first year every dated (movable) festival must carry a range for (#3002).</summary>
    public int DatesFrom { get; set; }

    /// <summary>The last year every dated festival carries ranges for; the content test reminds us to extend the file.</summary>
    public int DatesThrough { get; set; }

    public List<FestivalDefinition> Festivals { get; set; } = new();
}

/// <summary>One festival: its key, theme, glyph, calendar window and the client-side mood it brings (#3001).</summary>
public sealed class FestivalDefinition
{
    /// <summary>Stable id (<c>halloween</c>, <c>diwali</c> …) — the locale keys and the save's milestones hang on it.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>One of <see cref="FestivalThemes.All"/>: what the festival shares with its siblings under the hood
    /// (the lights, the blossoms, the harvest) — the challenge packs and spawns are per theme, the texts per festival.</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>A plain text symbol for the HUD chip and the menu card (the weather chip's kind: ☀ ❄ ★ …, no emoji —
    /// the game font has no colour glyphs).</summary>
    public string Glyph { get; set; } = string.Empty;

    public FestivalWindow Window { get; set; } = new();

    /// <summary>The cosmetic mood on the client while the festival is on (#3004); null = none.</summary>
    public FestivalEffects? Effects { get; set; }

    /// <summary>The runtime deposit layer while the festival is on (#3006); null = the festival places nothing.</summary>
    public FestivalSpawn? Spawn { get; set; }

    /// <summary>Locale key of the festival's name.</summary>
    public string NameKey => "festival." + Key + ".name";

    /// <summary>Locale key of the two-sentence explanation (what is celebrated, and where).</summary>
    public string ExplainKey => "festival." + Key + ".explain";

    /// <summary>Locale key of VEGA's greeting line, spoken once per festival-year.</summary>
    public string VegaKey => "festival." + Key + ".vega";
}

/// <summary>
/// When a festival is on. Three kinds: <c>fixed</c> (a month/day anchor every year), <c>weekday</c> (the first given weekday
/// on or after a month/day — Thanksgiving, Erntedank, Advent) and <c>dates</c> (explicit ranges per year for the lunar and
/// other movable calendars). <see cref="DaysBefore"/>/<see cref="DaysAfter"/> widen the anchor or range on both sides;
/// <see cref="UntilMonth"/>/<see cref="UntilDay"/> give a fixed end instead (Advent runs to 23 December).
/// </summary>
public sealed class FestivalWindow
{
    public const string KindFixed = "fixed";
    public const string KindWeekday = "weekday";
    public const string KindDates = "dates";

    public string Kind { get; set; } = KindFixed;

    public int Month { get; set; }

    public int Day { get; set; }

    /// <summary><c>weekday</c> only: the day of the week looked for (<c>Sunday</c>, <c>Thursday</c> …).</summary>
    public string Weekday { get; set; } = string.Empty;

    public int DaysBefore { get; set; }

    public int DaysAfter { get; set; }

    /// <summary>Optional fixed end (inclusive) for <c>fixed</c>/<c>weekday</c> windows; 0 = use <see cref="DaysAfter"/>.</summary>
    public int UntilMonth { get; set; }

    public int UntilDay { get; set; }

    /// <summary><c>dates</c> only: <c>["yyyy-MM-dd", "yyyy-MM-dd"]</c> pairs, both ends inclusive (UTC dates).</summary>
    public string[][] Ranges { get; set; } = Array.Empty<string[]>();
}

/// <summary>The client-side mood of a festival (#3004) — parameters on systems the client already has, no assets.</summary>
public sealed class FestivalEffects
{
    /// <summary>An ambient mote style by name (<c>petals</c>, <c>lamplight</c>, <c>embers</c>, <c>frost</c>, <c>seeds</c> …);
    /// empty = the world's own motes.</summary>
    public string Motes { get; set; } = string.Empty;

    /// <summary>Colour-grade warmth shift, −1 (cool) … 0 (neutral) … 1 (warm); the client folds a fraction of it in.</summary>
    public float Warmth { get; set; }

    /// <summary>Colour-grade saturation multiplier; 1 = neutral.</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>Cosmetic fog tint (<c>#rrggbb</c>); empty = the sky colour as always.</summary>
    public string FogTint { get; set; } = string.Empty;

    /// <summary>How much nearer the fog sits, 0 … 1 (0.35 = a cosy haze, never a pea-souper).</summary>
    public float FogBoost { get; set; }

    /// <summary>Snow caps on the sky-lit faces even where it is warm — a wintry look without one snow block.</summary>
    public bool SnowCap { get; set; }

    /// <summary>More shooting stars by night.</summary>
    public bool ShootingStars { get; set; }

    /// <summary>The Festive synth mood's flavour (<c>cosy</c>, <c>spooky</c>, <c>bright</c>, <c>calm</c>); empty = the usual music.</summary>
    public string Music { get; set; } = string.Empty;
}

/// <summary>What a festival puts into the world at runtime (#3006), on the weather-snow deposit pattern.</summary>
public sealed class FestivalSpawn
{
    /// <summary>The block that appears (a plant, a lantern …).</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>Blocks it may stand on.</summary>
    public string[] Hosts { get; set; } = Array.Empty<string>();

    /// <summary>Cells per pass (one pass every few seconds).</summary>
    public int PerPass { get; set; } = 6;

    /// <summary>Hard ceiling of festival cells per world.</summary>
    public int Cap { get; set; } = 1500;

    /// <summary>How far from a player a cell may appear.</summary>
    public int Radius { get; set; } = 24;
}

/// <summary>The festival themes (#3002): the content packages the festivals share.</summary>
public static class FestivalThemes
{
    public const string Harvest = "harvest";
    public const string Lights = "lights";
    public const string NewYear = "newyear";
    public const string Blossoms = "blossoms";
    public const string Colours = "colours";
    public const string Stars = "stars";
    public const string Midsummer = "midsummer";
    public const string Children = "children";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Harvest, Lights, NewYear, Blossoms, Colours, Stars, Midsummer, Children,
    };

    /// <summary>The <c>world:</c> story predicate that is true while a festival of this theme is on (#3007).</summary>
    public static string Predicate(string theme) => "festival_" + theme;
}
