// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>What a festival day calendar is for one player on one local day (#3315): whether it shows at all, the season
/// (the year of door 1) and how many doors may be opened (doors 1…<see cref="Available"/>).</summary>
public readonly struct CalendarDay : IEquatable<CalendarDay>
{
    public CalendarDay(bool active, int season, int available)
    {
        Active = active;
        Season = season;
        Available = available;
    }

    public bool Active { get; }

    public int Season { get; }

    public int Available { get; }

    public bool Equals(CalendarDay other) => Active == other.Active && Season == other.Season && Available == other.Available;

    public override bool Equals(object? obj) => obj is CalendarDay other && Equals(other);

    public override int GetHashCode() => (Active ? 1 : 0) ^ (Season * 397) ^ (Available * 7919);

    public static bool operator ==(CalendarDay left, CalendarDay right) => left.Equals(right);

    public static bool operator !=(CalendarDay left, CalendarDay right) => !left.Equals(right);
}

/// <summary>What one door holds (#3315): the fixed cosmetic of a special door (empty for none) and the items.</summary>
public sealed class CalendarDoorRoll
{
    public CalendarDoorRoll(string cosmetic, List<ItemAmount> items)
    {
        Cosmetic = cosmetic;
        Items = items;
    }

    /// <summary>A <see cref="CosmeticKeys"/> key (<c>hudframe:christmas_lights</c>), empty = none.</summary>
    public string Cosmetic { get; }

    public List<ItemAmount> Items { get; }
}

/// <summary>
/// The pure rules of the festival day calendars (#3315), shared by the server (the authority) and the tests. Everything is
/// date-only in the player's <b>local</b> time: the server adds the player's clamped UTC offset to its wall clock, so a door
/// opens at the player's own midnight. A calendar <b>waits</b>: door N is there from the N-th day of the season until the
/// catch-up end (inclusive), and nothing ever expires earlier — no streaks.
/// <para>Ordinary doors draw <see cref="FestivalCalendarDefinition.PicksPerDoor"/> different pool entries, weighted, each
/// with a count uniform in [Min, Max] — random, so players have something to trade, but <b>seeded</b> by world seed, player,
/// calendar, season and door through <see cref="StableHashing"/> (never <c>string.GetHashCode</c> or a process-random
/// <see cref="Random"/>): a reconnect, a restart or another machine always rolls the same door. A special door gives exactly
/// its special and draws nothing.</para>
/// <para>Milestones (per world, in the player's state): <c>calendar:&lt;key&gt;:&lt;season&gt;:&lt;door&gt;</c> for an
/// opened door and <c>calendar:&lt;key&gt;:&lt;season&gt;:greeted</c> for VEGA's greeting; older seasons are pruned.</para>
/// </summary>
public static class FestivalCalendarRules
{
    /// <summary>The widest real UTC offset (Kiribati's +14 h, and as far back) — client input is clamped to it.</summary>
    public const int MaxUtcOffsetMinutes = 14 * 60;

    /// <summary>The prefix of every calendar milestone.</summary>
    public const string MilestonePrefix = "calendar:";

    /// <summary>The last part of the milestone of VEGA's once-per-season greeting.</summary>
    public const string GreetedSuffix = "greeted";

    /// <summary>The player's UTC offset clamped to ±14 h.</summary>
    public static int ClampUtcOffset(int minutes) => Math.Max(-MaxUtcOffsetMinutes, Math.Min(MaxUtcOffsetMinutes, minutes));

    /// <summary>The player's local calendar date at <paramref name="utcNow"/>.</summary>
    public static DateTime LocalDate(DateTime utcNow, int utcOffsetMinutes)
        => DateTime.SpecifyKind(utcNow, DateTimeKind.Unspecified).AddMinutes(ClampUtcOffset(utcOffsetMinutes)).Date;

    /// <summary>The day of door 1 in <paramref name="season"/>; false for a malformed month/day (the content validation
    /// names it).</summary>
    public static bool TryStartOf(FestivalCalendarDefinition calendar, int season, out DateTime start)
        => TryDate(season, calendar.StartMonth, calendar.StartDay, out start);

    /// <summary>The last day (inclusive) missed doors of <paramref name="season"/> can be opened — in the following year when
    /// the catch-up month/day lies before the start (6 January after 1 December).</summary>
    public static bool TryCatchUpEndOf(FestivalCalendarDefinition calendar, int season, out DateTime end)
    {
        bool nextYear = calendar.CatchUpMonth < calendar.StartMonth
                        || (calendar.CatchUpMonth == calendar.StartMonth && calendar.CatchUpDay < calendar.StartDay);
        return TryDate(nextYear ? season + 1 : season, calendar.CatchUpMonth, calendar.CatchUpDay, out end);
    }

    /// <summary>The season a local date belongs to: its year once door 1's day of that year has come, else the year before
    /// (3 January 2032 still belongs to the 2031 Advent calendar).</summary>
    public static int SeasonOf(FestivalCalendarDefinition calendar, DateTime localDate)
    {
        var date = localDate.Date;
        return TryStartOf(calendar, date.Year, out var start) && date < start ? date.Year - 1 : date.Year;
    }

    /// <summary>The calendar on <paramref name="localDate"/>. <paramref name="forced"/> (its festival switched on by hand)
    /// shows it with every door, whatever the date; otherwise it shows from door 1's day through the catch-up end with one
    /// more door each day.</summary>
    public static CalendarDay Evaluate(FestivalCalendarDefinition calendar, DateTime localDate, bool forced)
    {
        var date = localDate.Date;
        int season = SeasonOf(calendar, date);
        int doors = Math.Max(0, calendar.Doors);
        if (forced)
        {
            return new CalendarDay(doors > 0, season, doors);
        }

        if (!TryStartOf(calendar, season, out var start) || !TryCatchUpEndOf(calendar, season, out var end)
            || date < start || date > end)
        {
            return new CalendarDay(false, season, 0);
        }

        int available = Math.Min(doors, (int)(date - start).TotalDays + 1);
        return new CalendarDay(available > 0, season, Math.Max(0, available));
    }

    /// <summary>The catch-up end as <c>yyyy-MM-dd</c> (empty for a malformed calendar).</summary>
    public static string CatchUpUntil(FestivalCalendarDefinition calendar, int season)
        => TryCatchUpEndOf(calendar, season, out var end) ? end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>The special of <paramref name="door"/>, or null for an ordinary door.</summary>
    public static CalendarSpecial? SpecialOf(FestivalCalendarDefinition calendar, int door)
    {
        foreach (var special in calendar.Specials ?? new List<CalendarSpecial>())
        {
            if (special is not null && special.Door == door)
            {
                return special;
            }
        }

        return null;
    }

    /// <summary>What <paramref name="door"/> of <paramref name="season"/> holds for <paramref name="playerId"/> in the world
    /// <paramref name="worldSeed"/> — the special, or the seeded pool draw. Pure: the same inputs give the same door.</summary>
    public static CalendarDoorRoll RollDoor(FestivalCalendarDefinition calendar, long worldSeed, string playerId, int season, int door)
    {
        var items = new List<ItemAmount>();
        if (SpecialOf(calendar, door) is { } special)
        {
            foreach (var item in special.Items ?? new List<ItemAmount>())
            {
                if (item is not null && !string.IsNullOrEmpty(item.Item) && item.Count > 0)
                {
                    items.Add(new ItemAmount(item.Item, item.Count));
                }
            }

            return new CalendarDoorRoll(special.Cosmetic ?? string.Empty, items);
        }

        var remaining = new List<CalendarPoolEntry>();
        foreach (var entry in calendar.Pool ?? new List<CalendarPoolEntry>())
        {
            if (entry is not null && !string.IsNullOrEmpty(entry.Item) && entry.Weight > 0)
            {
                remaining.Add(entry);
            }
        }

        ulong state = StableHashing.Mixed(string.Join("|", "calendar", calendar.Key, Text(season), Text(door), playerId ?? string.Empty), worldSeed);
        int picks = Math.Min(Math.Max(0, calendar.PicksPerDoor), remaining.Count);
        for (int i = 0; i < picks; i++)
        {
            var entry = DrawWeighted(remaining, Next(ref state));
            remaining.Remove(entry);
            int min = Math.Max(1, entry.Min);
            int max = Math.Max(min, entry.Max);
            items.Add(new ItemAmount(entry.Item, min + StableHashing.Index(Next(ref state), max - min + 1)));
        }

        return new CalendarDoorRoll(string.Empty, items);
    }

    // ---------------- Milestones ----------------

    /// <summary>The prefix of every milestone of calendar <paramref name="key"/>.</summary>
    public static string PrefixOf(string key) => MilestonePrefix + key + ":";

    /// <summary>The milestone of an opened door.</summary>
    public static string DoorMilestone(string key, int season, int door) => PrefixOf(key) + Text(season) + ":" + Text(door);

    /// <summary>The milestone of VEGA's greeting in a season.</summary>
    public static string GreetedMilestone(string key, int season) => PrefixOf(key) + Text(season) + ":" + GreetedSuffix;

    /// <summary>Bit (N − 1) set = door N of <paramref name="season"/> is open.</summary>
    public static int OpenedMask(FestivalCalendarDefinition calendar, int season, ICollection<string> milestones)
    {
        int mask = 0;
        int doors = Math.Min(31, Math.Max(0, calendar.Doors));
        for (int door = 1; door <= doors; door++)
        {
            if (milestones.Contains(DoorMilestone(calendar.Key, season, door)))
            {
                mask |= 1 << (door - 1);
            }
        }

        return mask;
    }

    /// <summary>Whether <paramref name="milestone"/> belongs to calendar <paramref name="key"/> and a season before
    /// <paramref name="season"/> — those are pruned so a long-lived world never piles them up.</summary>
    public static bool IsOlderSeason(string milestone, string key, int season)
    {
        string prefix = PrefixOf(key);
        if (milestone is null || !milestone.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int colon = milestone.IndexOf(':', prefix.Length);
        string seasonText = colon < 0 ? milestone.Substring(prefix.Length) : milestone.Substring(prefix.Length, colon - prefix.Length);
        return int.TryParse(seasonText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s < season;
    }

    private static CalendarPoolEntry DrawWeighted(List<CalendarPoolEntry> entries, ulong mixed)
    {
        long total = 0;
        foreach (var e in entries)
        {
            total += e.Weight;
        }

        long roll = (long)(((mixed >> 32) * (ulong)Math.Min(total, int.MaxValue)) >> 32);
        foreach (var e in entries)
        {
            roll -= e.Weight;
            if (roll < 0)
            {
                return e;
            }
        }

        return entries[entries.Count - 1];
    }

    /// <summary>The next value of a SplitMix64 sequence over <paramref name="state"/>.</summary>
    private static ulong Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            return StableHashing.Mix64(state);
        }
    }

    private static bool TryDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year < 1 || year > 9998 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        return true;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
