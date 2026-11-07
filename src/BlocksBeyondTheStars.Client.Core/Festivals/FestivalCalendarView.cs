// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client.Festivals
{
    /// <summary>How one door of a festival day calendar looks on the client (#3316).</summary>
    public enum CalendarDoorStatus
    {
        /// <summary>Its day has not come yet — closed and dim.</summary>
        Later,

        /// <summary>It may be opened now (today's door, or a missed one that waited) — closed, gently glowing.</summary>
        Available,

        /// <summary>Already opened — it shows what it held.</summary>
        Open,
    }

    /// <summary>
    /// The client's reading of a festival day calendar state (#3315, #3316): the door states, the "doors waiting" count the
    /// HUD chip and the Character tab show, the grid order of the calendar window, the player's local UTC offset (the doors
    /// open at the player's own midnight) and the dates the window names. Unity-free, so the rules are covered by plain
    /// .NET tests. The server is the truth — this only reads what it sent: an inactive state shows nothing.
    /// </summary>
    public static class FestivalCalendarView
    {
        /// <summary>The widest real UTC offset (Kiribati's +14 h, and as far back) — the server clamps to the same.</summary>
        public const int MaxUtcOffsetMinutes = 14 * 60;

        /// <summary>The most doors the open mask can carry (one bit each).</summary>
        public const int MaxDoors = 32;

        /// <summary><paramref name="minutes"/> clamped to ±14 h.</summary>
        public static int ClampUtcOffset(int minutes) => Math.Max(-MaxUtcOffsetMinutes, Math.Min(MaxUtcOffsetMinutes, minutes));

        /// <summary>A local clock's offset from UTC in whole minutes, clamped (a desktop's <c>TimeZoneInfo.Local</c>).</summary>
        public static int UtcOffsetMinutes(TimeSpan offset) => ClampUtcOffset((int)Math.Round(offset.TotalMinutes));

        /// <summary>The browser's <c>Date.getTimezoneOffset()</c> is UTC minus local (−60 in Berlin in winter); the game's
        /// offset is local minus UTC. Clamped.</summary>
        public static int FromBrowserTimezoneOffset(int browserMinutes) => ClampUtcOffset(-browserMinutes);

        /// <summary>Whether door <paramref name="door"/> (1-based) is open in <paramref name="state"/>.</summary>
        public static bool IsOpen(FestivalCalendarState? state, int door)
            => state != null && door >= 1 && door <= MaxDoors && (state.OpenedMask & (1 << (door - 1))) != 0;

        /// <summary>How door <paramref name="door"/> looks: open, openable now, or later.</summary>
        public static CalendarDoorStatus Status(FestivalCalendarState? state, int door)
        {
            if (IsOpen(state, door))
            {
                return CalendarDoorStatus.Open;
            }

            return state != null && door >= 1 && door <= DoorCount(state) && door <= state.Available
                ? CalendarDoorStatus.Available
                : CalendarDoorStatus.Later;
        }

        /// <summary>The calendar's door count, within what the open mask can carry.</summary>
        public static int DoorCount(FestivalCalendarState? state) => state == null ? 0 : Math.Max(0, Math.Min(MaxDoors, state.Doors));

        /// <summary>How many doors wait to be opened — openable and still closed. 0 for an inactive (or no) calendar.</summary>
        public static int Waiting(FestivalCalendarState? state)
        {
            if (state == null || !state.Active)
            {
                return 0;
            }

            int waiting = 0;
            int last = Math.Min(DoorCount(state), state.Available);
            for (int door = 1; door <= last; door++)
            {
                if (!IsOpen(state, door))
                {
                    waiting++;
                }
            }

            return waiting;
        }

        /// <summary>The doors waiting over every active calendar.</summary>
        public static int TotalWaiting(IEnumerable<FestivalCalendarState>? states)
        {
            int total = 0;
            foreach (var state in states ?? Array.Empty<FestivalCalendarState>())
            {
                total += Waiting(state);
            }

            return total;
        }

        /// <summary>The calendar the entry points open: the active one with doors waiting first, else any active one — by key,
        /// so the choice never depends on the order states arrived in. Null when none is active.</summary>
        public static FestivalCalendarState? FirstActive(IReadOnlyDictionary<string, FestivalCalendarState>? calendars)
        {
            if (calendars == null)
            {
                return null;
            }

            FestivalCalendarState? best = null;
            foreach (var state in calendars.Values)
            {
                if (state == null || !state.Active)
                {
                    continue;
                }

                if (best == null)
                {
                    best = state;
                    continue;
                }

                bool waits = Waiting(state) > 0;
                if (waits != Waiting(best) > 0)
                {
                    best = waits ? state : best;
                }
                else if (string.CompareOrdinal(state.Calendar, best.Calendar) < 0)
                {
                    best = state;
                }
            }

            return best;
        }

        /// <summary>The order the doors sit in the grid, row by row: the calendar's shuffled <paramref name="layout"/> when it
        /// is a permutation of 1…<paramref name="doors"/>, else 1…N in order.</summary>
        public static int[] GridOrder(int[]? layout, int doors)
        {
            doors = Math.Max(0, Math.Min(MaxDoors, doors));
            var order = new int[doors];
            if (layout != null && layout.Length == doors)
            {
                var seen = new bool[doors + 1];
                bool valid = true;
                for (int i = 0; i < doors && valid; i++)
                {
                    int door = layout[i];
                    valid = door >= 1 && door <= doors && !seen[door];
                    if (valid)
                    {
                        seen[door] = true;
                        order[i] = door;
                    }
                }

                if (valid)
                {
                    return order;
                }
            }

            for (int i = 0; i < doors; i++)
            {
                order[i] = i + 1;
            }

            return order;
        }

        /// <summary>How many columns the grid has: about one and a half times as wide as tall — 6 × 4 for 24 doors.</summary>
        public static int Columns(int doors)
        {
            if (doors <= 1)
            {
                return 1;
            }

            int columns = (int)Math.Round(Math.Sqrt(doors * 1.5));
            return Math.Max(1, Math.Min(doors, columns));
        }

        /// <summary>The grid's row count for <paramref name="doors"/>.</summary>
        public static int Rows(int doors) => doors <= 0 ? 0 : (doors + Columns(doors) - 1) / Columns(doors);

        /// <summary>What door <paramref name="door"/> held, or null when it is not open (or the server sent no contents).</summary>
        public static CalendarDoorContent? ContentOf(FestivalCalendarState? state, int door)
        {
            foreach (var content in state?.Contents ?? Array.Empty<CalendarDoorContent>())
            {
                if (content != null && content.Door == door)
                {
                    return content;
                }
            }

            return null;
        }

        /// <summary>Parses the server's <c>yyyy-MM-dd</c> day.</summary>
        public static bool TryParseDay(string? day, out DateTime date)
            => DateTime.TryParseExact(day ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        /// <summary>The day door <paramref name="door"/> of <paramref name="season"/> comes (door 1's day + N − 1), or null for
        /// a malformed calendar.</summary>
        public static DateTime? DoorDay(FestivalCalendarDefinition? calendar, int season, int door)
        {
            if (calendar == null || door < 1 || season < 1 || season > 9998)
            {
                return null;
            }

            try
            {
                return new DateTime(season, calendar.StartMonth, calendar.StartDay).AddDays(door - 1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>A day and month in the game language — "6 January", "6. Januar", "1月6日" — from the language's own
        /// culture. A platform without that culture's data (a stripped browser build) falls back to the unambiguous
        /// <c>yyyy-MM-dd</c>.</summary>
        public static string FormatDay(DateTime date, string? localeCode)
        {
            try
            {
                var culture = CultureOf(localeCode);
                if (culture != null)
                {
                    string pattern = culture.DateTimeFormat.MonthDayPattern;
                    if (!string.IsNullOrEmpty(pattern))
                    {
                        return date.ToString(pattern, culture);
                    }
                }
            }
            catch (Exception)
            {
                // Culture data missing on this platform — the ISO day below says the same without it.
            }

            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>The .NET culture of a game language, or null for an unknown code.</summary>
        private static CultureInfo? CultureOf(string? localeCode)
        {
            if (!GameLocaleExtensions.TryParse(localeCode ?? string.Empty, out var locale))
            {
                return null;
            }

            // English: the British day-month order ("6 January") reads naturally to most English players outside the US.
            string name = locale switch
            {
                GameLocale.English => "en-GB",
                GameLocale.ChineseSimplified => "zh-Hans",
                _ => locale.Code(),
            };
            return CultureInfo.GetCultureInfo(name);
        }
    }
}
