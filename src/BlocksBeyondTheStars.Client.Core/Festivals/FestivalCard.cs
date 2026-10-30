// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client.Festivals
{
    /// <summary>
    /// The main menu's festival card (#3003), Unity-free: what the card says today, from the client's bundled calendar and
    /// its UTC clock — the same file and the same code the server runs, so the card and the world agree (epic #3000,
    /// decision 4). The card shows the festivals that are on (up to <see cref="MaxActive"/>) or, when none is, the next one
    /// that starts within <see cref="CardHorizonDays"/>; "More festivals" opens "My festivals", every festival by its next
    /// start with a switch (#3021). Since #3021 the card names only the festivals the player celebrates.
    /// </summary>
    public static class FestivalCard
    {
        /// <summary>The card appears this many days ahead of the next festival.</summary>
        public const int CardHorizonDays = 14;

        /// <summary>The "More festivals" list looks this far ahead.</summary>
        public const int UpcomingHorizonDays = 60;

        /// <summary>How many festivals the card names when several are on at once.</summary>
        public const int MaxActive = 3;

        /// <summary>One card line: a festival and how many days until it starts (0 = on today).</summary>
        public readonly struct Line
        {
            public Line(FestivalDefinition festival, int daysUntil)
            {
                Festival = festival;
                DaysUntil = daysUntil;
            }

            public FestivalDefinition Festival { get; }

            public int DaysUntil { get; }
        }

        /// <summary>The festivals on today that the player celebrates (#3021; null = every festival), the leading one first
        /// (#3022: priority, then the one that started last), at most <see cref="MaxActive"/>.</summary>
        public static IReadOnlyList<Line> Active(GameContent? content, DateTime utcNow, ICollection<string>? celebrated = null)
        {
            if (content is null)
            {
                return Array.Empty<Line>();
            }

            var keys = FestivalCalendar.ActiveOn(content.Festivals, utcNow).Select(f => f.Key)
                .Where(k => celebrated is null || celebrated.Contains(k));
            return FestivalSelection.ByPriority(keys, content.FindFestival).Take(MaxActive)
                .Select(k => new Line(content.FindFestival(k)!, 0)).ToList();
        }

        /// <summary>The festivals the player celebrates that start within <paramref name="horizonDays"/>, soonest first.</summary>
        public static IReadOnlyList<Line> Upcoming(GameContent? content, DateTime utcNow, int horizonDays, ICollection<string>? celebrated = null)
            => content is null
                ? Array.Empty<Line>()
                : FestivalCalendar.Upcoming(content.Festivals, utcNow, horizonDays)
                    .Where(s => celebrated is null || celebrated.Contains(s.Festival.Key))
                    .Select(s => new Line(s.Festival, (int)(s.Start - utcNow.Date).TotalDays)).ToList();

        /// <summary>The next celebrated festival within <see cref="CardHorizonDays"/>, or null.</summary>
        public static Line? Next(GameContent? content, DateTime utcNow, ICollection<string>? celebrated = null)
        {
            var upcoming = Upcoming(content, utcNow, CardHorizonDays, celebrated);
            return upcoming.Count > 0 ? upcoming[0] : (Line?)null;
        }

        /// <summary>Whether the card has anything to say: a celebrated festival is on or near.</summary>
        public static bool ShouldShow(GameContent? content, DateTime utcNow, ICollection<string>? celebrated)
            => Active(content, utcNow, celebrated).Count > 0 || Next(content, utcNow, celebrated) is not null;

        /// <summary>Every festival of the calendar by its next start (the "My festivals" list, #3021): the ones on today first
        /// (0 days), then the coming ones, soonest first. A dated festival past the file's last year is left out.</summary>
        public static IReadOnlyList<Line> Calendar(GameContent? content, DateTime utcNow)
        {
            if (content is null)
            {
                return Array.Empty<Line>();
            }

            var list = FestivalCalendar.ActiveOn(content.Festivals, utcNow).Select(f => new Line(f, 0)).ToList();
            list.AddRange(Upcoming(content, utcNow, 400));
            return list;
        }

        /// <summary>What the "when" column of a list row says: on now, tomorrow, in n days (locale keys <c>ui.festival.when_*</c>).</summary>
        public static string When(Line line, Func<string, string> localize)
            => line.DaysUntil <= 0 ? localize("ui.festival.when_today")
                : line.DaysUntil == 1 ? localize("ui.festival.when_tomorrow")
                : localize("ui.festival.when_days").Replace("{days}", line.DaysUntil.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>The headline of a line: "Today: X", "Tomorrow: X" or "In n days: X" (locale keys <c>ui.festival.*</c>).</summary>
        public static string Headline(Line line, Func<string, string> localize)
        {
            string name = line.Festival.Glyph + " " + localize(line.Festival.NameKey);
            if (line.DaysUntil <= 0)
            {
                return localize("ui.festival.today").Replace("{name}", name);
            }

            if (line.DaysUntil == 1)
            {
                return localize("ui.festival.next_tomorrow").Replace("{name}", name);
            }

            return localize("ui.festival.next").Replace("{days}", line.DaysUntil.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{name}", name);
        }

        /// <summary>The effects of the leading festival among <paramref name="activeKeys"/> (the server's list, already this
        /// player's festivals in order) that shows its mood today (#3022): on one of its core days, or any day when the festival
        /// was switched on by hand (<paramref name="forced"/>). Null when none does.</summary>
        public static FestivalEffects? PrimaryEffects(GameContent? content, IReadOnlyList<string>? activeKeys, DateTime utcNow, bool forced)
        {
            if (content is null || activeKeys is null)
            {
                return null;
            }

            foreach (string key in activeKeys)
            {
                var f = content.FindFestival(key);
                if (f?.Effects is not null && (forced || FestivalCalendar.IsCoreDay(f, utcNow)))
                {
                    return f.Effects;
                }
            }

            return null;
        }

        /// <summary>The HUD chip text for the primary festival: the glyph and the name, or empty.</summary>
        public static string Chip(GameContent? content, IReadOnlyList<string>? activeKeys, Func<string, string> localize)
        {
            if (content is null || activeKeys is null || activeKeys.Count == 0)
            {
                return string.Empty;
            }

            var f = content.FindFestival(activeKeys[0]);
            return f is null ? string.Empty : (f.Glyph + " " + localize(f.NameKey)).Trim();
        }

        /// <summary>The festivals that appear in <paramref name="now"/> but not in <paramref name="before"/> — what just began.</summary>
        public static IReadOnlyList<string> Began(IReadOnlyList<string>? before, IReadOnlyList<string>? now)
        {
            if (now is null || now.Count == 0)
            {
                return Array.Empty<string>();
            }

            return before is null ? now : now.Where(k => !before.Contains(k, StringComparer.Ordinal)).ToList();
        }
    }
}
