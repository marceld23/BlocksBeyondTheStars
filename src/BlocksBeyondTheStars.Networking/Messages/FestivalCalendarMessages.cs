// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>#3315: the client opens door <see cref="Door"/> (1-based) of the festival day calendar <see cref="Calendar"/>.
/// <see cref="UtcOffsetMinutes"/> is the player's local UTC offset (the day boundary is the player's local midnight; the
/// server clamps it to ±14 h). The server checks the door is there and not opened yet and that its contents fit, then
/// answers with <see cref="FestivalCalendarState"/> (its <see cref="FestivalCalendarState.Reveal"/> names the door).
/// Allowed while the singleplayer pause holds the world — the calendar opens from the in-game menu.</summary>
public sealed class OpenCalendarDoorIntent
{
    public string Calendar { get; set; } = string.Empty;
    public int Door { get; set; }
    public int UtcOffsetMinutes { get; set; }
}

/// <summary>#3315: server → client, one festival day calendar of this player in this world — at join, when a new door
/// comes (local midnight), after a door was opened and when the calendar ends. <see cref="Active"/> false = the calendar is
/// over or not for this player (the client hides it).</summary>
public sealed class FestivalCalendarState
{
    public string Calendar { get; set; } = string.Empty;

    /// <summary>The festival the calendar belongs to (<c>christmas</c>).</summary>
    public string Festival { get; set; } = string.Empty;

    public bool Active { get; set; }

    /// <summary>The season (the year of door 1) — the milestones and the seeded contents hang on it.</summary>
    public int Season { get; set; }

    /// <summary>Number of doors (24).</summary>
    public int Doors { get; set; }

    /// <summary>Doors 1…<see cref="Available"/> may be opened (all of them when the festival is switched on by hand or
    /// door 24's day has passed).</summary>
    public int Available { get; set; }

    /// <summary>Bit (N − 1) set = door N is open.</summary>
    public int OpenedMask { get; set; }

    /// <summary>The last local day (<c>yyyy-MM-dd</c>) missed doors can still be opened.</summary>
    public string CatchUpUntil { get; set; } = string.Empty;

    /// <summary>The contents of every opened door (for the open doors' icons).</summary>
    public CalendarDoorContent[] Contents { get; set; } = System.Array.Empty<CalendarDoorContent>();

    /// <summary>The door just opened by this player's intent (play the reveal), 0 = none.</summary>
    public int Reveal { get; set; }

    /// <summary>A locale key to show once (<c>ui.calendar.bag_full</c> …), empty = none.</summary>
    public string Notice { get; set; } = string.Empty;
}

/// <summary>#3315: what one opened door held.</summary>
public sealed class CalendarDoorContent
{
    public int Door { get; set; }
    public string[] Items { get; set; } = System.Array.Empty<string>();
    public int[] Counts { get; set; } = System.Array.Empty<int>();

    /// <summary>The cosmetic it unlocked (<c>hudframe:christmas_lights</c>, <c>skin:christmas</c>), empty = none.</summary>
    public string Cosmetic { get; set; } = string.Empty;
}

/// <summary>#3315: server → client, the cosmetics this player has unlocked in this world (<c>hudframe:&lt;key&gt;</c>,
/// <c>skin:&lt;key&gt;</c>) — at join and whenever one is added. The editors offer the matching templates.</summary>
public sealed class CosmeticUnlocks
{
    public string[] Keys { get; set; } = System.Array.Empty<string>();
}
