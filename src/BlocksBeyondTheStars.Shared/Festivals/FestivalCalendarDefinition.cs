// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>
/// The festival day calendars (<c>data/festival_calendars.json</c>, #3315): a festival may carry a calendar of daily doors —
/// the Advent calendar is the first. A calendar <b>waits</b>: door N is there from the N-th day of the calendar (in the
/// player's local time) until the catch-up end, missed doors stay openable, there is no streak (the parents' promise of
/// "no daily-login mechanics", ADR 0020's "nothing can be missed"). Materials are random per player but seeded, so
/// reconnecting never re-rolls; specials are fixed doors.
/// </summary>
public sealed class FestivalCalendarCatalog
{
    public List<FestivalCalendarDefinition> Calendars { get; set; } = new();
}

/// <summary>One calendar of daily doors, attached to a festival.</summary>
public sealed class FestivalCalendarDefinition
{
    /// <summary>Stable id (<c>advent</c>) — the milestones (<c>calendar:&lt;key&gt;:&lt;season&gt;:&lt;door&gt;</c>) and
    /// the locale keys (<c>calendar.&lt;key&gt;.name</c>) hang on it.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The festival whose celebrants get the calendar (<c>christmas</c>): a player sees it when their festival
    /// selection ("My festivals") includes this festival, or when the festival is switched on by hand.</summary>
    public string Festival { get; set; } = string.Empty;

    /// <summary>Month and day of door 1 (1 December).</summary>
    public int StartMonth { get; set; }

    public int StartDay { get; set; }

    /// <summary>How many doors (24) — door N is there from start + N − 1 days.</summary>
    public int Doors { get; set; }

    /// <summary>Month and day (inclusive, in the following year when it lies before the start) until which missed doors
    /// can still be opened (6 January).</summary>
    public int CatchUpMonth { get; set; }

    public int CatchUpDay { get; set; }

    /// <summary>How many different pool entries an ordinary door holds.</summary>
    public int PicksPerDoor { get; set; } = 2;

    /// <summary>The materials ordinary doors draw from (weighted, seeded per world, player, season and door).</summary>
    public List<CalendarPoolEntry> Pool { get; set; } = new();

    /// <summary>The fixed doors: a cosmetic unlock and/or fixed items instead of a pool draw.</summary>
    public List<CalendarSpecial> Specials { get; set; } = new();

    /// <summary>The order the doors sit in the calendar's grid, row by row (a real calendar's doors are shuffled); empty =
    /// 1…N in order. Must be a permutation of 1…<see cref="Doors"/>.</summary>
    public int[] Layout { get; set; } = Array.Empty<int>();

    /// <summary>Locale key of the calendar's name.</summary>
    public string NameKey => "calendar." + Key + ".name";

    /// <summary>Locale key of VEGA's once-per-season greeting.</summary>
    public string VegaKey => "calendar." + Key + ".vega";
}

/// <summary>A material an ordinary door may hold: between <see cref="Min"/> and <see cref="Max"/> of <see cref="Item"/>,
/// drawn with relative <see cref="Weight"/>.</summary>
public sealed class CalendarPoolEntry
{
    public string Item { get; set; } = string.Empty;

    public int Min { get; set; } = 1;

    public int Max { get; set; } = 1;

    public int Weight { get; set; } = 1;
}

/// <summary>A fixed door: <see cref="Cosmetic"/> (a <see cref="CosmeticKeys"/> key such as <c>hudframe:christmas_lights</c>
/// or <c>skin:christmas</c>) and/or <see cref="Items"/> (the reindeer's star bell). A special door draws nothing from the
/// pool.</summary>
public sealed class CalendarSpecial
{
    public int Door { get; set; }

    public string Cosmetic { get; set; } = string.Empty;

    public List<ItemAmount> Items { get; set; } = new();
}
