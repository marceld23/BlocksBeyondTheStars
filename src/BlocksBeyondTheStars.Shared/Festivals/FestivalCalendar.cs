// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Festivals;

/// <summary>
/// The pure calendar behind the festivals (#3001): which festivals a UTC date falls into and which come next. Server and
/// client run the same code on the same bundled file, so the menu card and the world agree (epic decision 4). Everything
/// is date-only in UTC — windows are whole days, so the hour a festival starts never matters to anyone's time zone.
/// </summary>
public static class FestivalCalendar
{
    /// <summary>One occurrence of a festival: its first day and the day after its last day (both UTC dates).</summary>
    public readonly struct FestivalSpan
    {
        public FestivalSpan(FestivalDefinition festival, DateTime start, DateTime endExclusive)
        {
            Festival = festival;
            Start = start;
            EndExclusive = endExclusive;
        }

        public FestivalDefinition Festival { get; }

        public DateTime Start { get; }

        public DateTime EndExclusive { get; }

        public bool Contains(DateTime date) => date >= Start && date < EndExclusive;
    }

    /// <summary>The festivals on at <paramref name="utcNow"/>, the primary one first: the one that started last; ties keep
    /// the file's order. Several festivals may overlap (Christmas and Kwanzaa, Chuseok and the Mid-Autumn Festival).</summary>
    public static IReadOnlyList<FestivalDefinition> ActiveOn(IEnumerable<FestivalDefinition> festivals, DateTime utcNow)
    {
        var date = utcNow.Date;
        var hits = new List<(FestivalDefinition Festival, DateTime Start, int Order)>();
        int order = 0;
        foreach (var festival in festivals)
        {
            foreach (var span in SpansAround(festival, date.Year))
            {
                if (span.Contains(date))
                {
                    hits.Add((festival, span.Start, order));
                    break;
                }
            }

            order++;
        }

        return hits.OrderByDescending(h => h.Start).ThenBy(h => h.Order).Select(h => h.Festival).ToList();
    }

    /// <summary>The festivals that start within <paramref name="horizonDays"/> days after <paramref name="utcNow"/> and are
    /// not on right now, soonest first.</summary>
    public static IReadOnlyList<FestivalSpan> Upcoming(IEnumerable<FestivalDefinition> festivals, DateTime utcNow, int horizonDays)
    {
        var date = utcNow.Date;
        var limit = date.AddDays(Math.Max(0, horizonDays));
        var list = new List<FestivalSpan>();
        foreach (var festival in festivals)
        {
            FestivalSpan? next = null;
            bool active = false;
            foreach (var span in SpansAround(festival, date.Year))
            {
                if (span.Contains(date))
                {
                    active = true;
                    break;
                }

                if (span.Start > date && span.Start <= limit && (next is null || span.Start < next.Value.Start))
                {
                    next = span;
                }
            }

            if (!active && next is not null)
            {
                list.Add(next.Value);
            }
        }

        return list.OrderBy(s => s.Start).ToList();
    }

    /// <summary>Whether <paramref name="utcNow"/> is one of the festival's mood days (#3022): inside its window and either on
    /// <see cref="FestivalEffects.CoreWeekday"/> or within the first <see cref="FestivalEffects.CoreDays"/> days of the
    /// window. False outside the window and for a festival without effects.</summary>
    public static bool IsCoreDay(FestivalDefinition festival, DateTime utcNow)
    {
        var effects = festival.Effects;
        if (effects is null)
        {
            return false;
        }

        var date = utcNow.Date;
        foreach (var span in SpansAround(festival, date.Year))
        {
            if (!span.Contains(date))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(effects.CoreWeekday) && TryParseWeekday(effects.CoreWeekday, out var weekday))
            {
                return date.DayOfWeek == weekday;
            }

            return (date - span.Start).TotalDays < effects.CoreDays;
        }

        return false;
    }

    /// <summary>Every occurrence of the festival that touches the years around <paramref name="year"/> (a window over New
    /// Year belongs to both years). Malformed windows yield nothing — the content validation names them.</summary>
    public static IEnumerable<FestivalSpan> SpansAround(FestivalDefinition festival, int year)
    {
        var w = festival.Window;
        switch ((w.Kind ?? string.Empty).ToLowerInvariant())
        {
            case FestivalWindow.KindDates:
                foreach (var range in w.Ranges ?? Array.Empty<string[]>())
                {
                    if (range is { Length: 2 } && TryParseDate(range[0], out var from) && TryParseDate(range[1], out var to) && to >= from)
                    {
                        yield return new FestivalSpan(festival, from.AddDays(-w.DaysBefore), to.AddDays(w.DaysAfter + 1));
                    }
                }

                break;

            case FestivalWindow.KindWeekday:
                for (int y = year - 1; y <= year + 1; y++)
                {
                    if (TryAnchor(w, y, out var anchor) && TryParseWeekday(w.Weekday, out var weekday))
                    {
                        while (anchor.DayOfWeek != weekday)
                        {
                            anchor = anchor.AddDays(1);
                        }

                        yield return Span(festival, w, anchor);
                    }
                }

                break;

            default:
                for (int y = year - 1; y <= year + 1; y++)
                {
                    if (TryAnchor(w, y, out var anchor))
                    {
                        yield return Span(festival, w, anchor);
                    }
                }

                break;
        }
    }

    /// <summary>The years a dated festival covers (a range counts for the year it starts in).</summary>
    public static IEnumerable<int> DatedYears(FestivalWindow window)
    {
        foreach (var range in window.Ranges ?? Array.Empty<string[]>())
        {
            if (range is { Length: 2 } && TryParseDate(range[0], out var from))
            {
                yield return from.Year;
            }
        }
    }

    /// <summary>Parses a <c>yyyy-MM-dd</c> UTC date.</summary>
    public static bool TryParseDate(string? text, out DateTime date)
        => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

    /// <summary>Parses an English weekday name (<c>Sunday</c> …), case-insensitive.</summary>
    public static bool TryParseWeekday(string? text, out DayOfWeek weekday)
        => Enum.TryParse(text, ignoreCase: true, out weekday) && Enum.IsDefined(typeof(DayOfWeek), weekday)
           && !string.IsNullOrEmpty(text) && !char.IsDigit(text![0]);

    private static bool TryAnchor(FestivalWindow w, int year, out DateTime anchor)
    {
        anchor = default;
        if (w.Month is < 1 or > 12 || w.Day < 1 || w.Day > DateTime.DaysInMonth(year, w.Month))
        {
            return false;
        }

        anchor = new DateTime(year, w.Month, w.Day, 0, 0, 0, DateTimeKind.Utc);
        return true;
    }

    private static FestivalSpan Span(FestivalDefinition festival, FestivalWindow w, DateTime anchor)
    {
        var start = anchor.AddDays(-Math.Max(0, w.DaysBefore));
        DateTime endExclusive;
        if (w.UntilMonth is >= 1 and <= 12 && w.UntilDay >= 1)
        {
            int untilYear = w.UntilMonth < anchor.Month || (w.UntilMonth == anchor.Month && w.UntilDay < anchor.Day) ? anchor.Year + 1 : anchor.Year;
            int day = Math.Min(w.UntilDay, DateTime.DaysInMonth(untilYear, w.UntilMonth));
            endExclusive = new DateTime(untilYear, w.UntilMonth, day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);
        }
        else
        {
            endExclusive = anchor.AddDays(Math.Max(0, w.DaysAfter) + 1);
        }

        return new FestivalSpan(festival, start, endExclusive);
    }
}
