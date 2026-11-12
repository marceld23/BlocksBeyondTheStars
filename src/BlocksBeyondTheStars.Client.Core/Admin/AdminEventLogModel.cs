// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Admin
{
    /// <summary>
    /// The admin view's Log tab (#3506), Unity-free: the backlog the server sends when the panel opens, then every live event,
    /// at most <see cref="Capacity"/>, without duplicates (by <see cref="NetAdminEvent.Id"/>). <see cref="Newest"/> lists them
    /// newest first through the category filter; <see cref="Format"/> turns an event into its line.
    /// </summary>
    public sealed class AdminEventLogModel
    {
        /// <summary>The most events kept (the server's ring buffer holds as many).</summary>
        public const int Capacity = 500;

        /// <summary>Every category the server uses, in the order the filter shows them.</summary>
        public static readonly IReadOnlyList<string> Categories = new[]
        {
            AdminEventCategories.Pvp, AdminEventCategories.Death, AdminEventCategories.Join,
            AdminEventCategories.Report, AdminEventCategories.Moderation, AdminEventCategories.Admin,
        };

        private readonly List<NetAdminEvent> _events = new List<NetAdminEvent>(); // oldest first
        private readonly HashSet<long> _ids = new HashSet<long>();
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Counts up with every change (events or filter).</summary>
        public int Version { get; private set; }

        /// <summary>How many events are held.</summary>
        public int Count => _events.Count;

        /// <summary>Replaces everything with the backlog (oldest first, as the server sends it).</summary>
        public void ApplyBacklog(IEnumerable<NetAdminEvent>? events)
        {
            _events.Clear();
            _ids.Clear();
            foreach (var ev in events ?? Array.Empty<NetAdminEvent>())
            {
                AddCore(ev);
            }

            Version++;
        }

        /// <summary>Adds one live event; false for a duplicate (already held) or null.</summary>
        public bool Add(NetAdminEvent? ev)
        {
            if (ev is null || !AddCore(ev))
            {
                return false;
            }

            Version++;
            return true;
        }

        private bool AddCore(NetAdminEvent? ev)
        {
            if (ev is null || !_ids.Add(ev.Id))
            {
                return false;
            }

            _events.Add(ev);
            while (_events.Count > Capacity)
            {
                _ids.Remove(_events[0].Id);
                _events.RemoveAt(0);
            }

            return true;
        }

        /// <summary>Forgets every event (a new session); the filter stays as the admin set it.</summary>
        public void Clear()
        {
            _events.Clear();
            _ids.Clear();
            Version++;
        }

        /// <summary>Whether events of <paramref name="category"/> are listed (all are, until the admin hides one).</summary>
        public bool IsShown(string category) => !_hidden.Contains(category ?? string.Empty);

        /// <summary>Shows or hides one category in <see cref="Newest"/>.</summary>
        public void SetShown(string category, bool shown)
        {
            bool changed = shown ? _hidden.Remove(category ?? string.Empty) : _hidden.Add(category ?? string.Empty);
            if (changed)
            {
                Version++;
            }
        }

        /// <summary>The held events of the shown categories, newest first (at most <paramref name="max"/>).</summary>
        public IReadOnlyList<NetAdminEvent> Newest(int max = Capacity)
        {
            var list = new List<NetAdminEvent>();
            for (int i = _events.Count - 1; i >= 0 && list.Count < max; i--)
            {
                if (IsShown(_events[i].Category))
                {
                    list.Add(_events[i]);
                }
            }

            return list;
        }

        /// <summary>An event's line: its text key looked up through <paramref name="localize"/> (a key → text lookup, e.g. the
        /// client's localizer), the arguments put into the <c>{0}</c>, <c>{1}</c> … placeholders — an argument that starts with
        /// <c>@</c> is a key itself (a death cause) and is looked up too.</summary>
        public static string Format(NetAdminEvent ev, Func<string, string> localize)
        {
            if (ev is null || localize is null)
            {
                return string.Empty;
            }

            return Fill(localize(ev.TextKey ?? string.Empty), ev.Args, localize);
        }

        /// <summary>Puts <paramref name="args"/> into the <c>{i}</c> placeholders of <paramref name="template"/> in one pass over
        /// the template — a placeholder written INSIDE an argument (a player's note "{1}") stays as typed. An argument starting
        /// with a single <c>@</c> is a locale key the server set (a death cause) and is looked up through
        /// <paramref name="localize"/>; one starting with <c>@@</c> is a player's text that began with <c>@</c> and reads
        /// literally with one <c>@</c> (#3502: nobody can make the log show a text of their choosing). A missing argument
        /// leaves its placeholder empty.</summary>
        public static string Fill(string template, IReadOnlyList<string>? args, Func<string, string> localize)
        {
            string t = template ?? string.Empty;
            var sb = new StringBuilder(t.Length + 32);
            int i = 0;
            while (i < t.Length)
            {
                int close = t[i] == '{' ? t.IndexOf('}', i + 1) : -1;
                if (close > i + 1 && int.TryParse(t.Substring(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                {
                    sb.Append(ArgumentText(args, n, localize));
                    i = close + 1;
                    continue;
                }

                sb.Append(t[i]);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>The text of argument <paramref name="n"/> (see <see cref="Fill"/>); empty when there is none.</summary>
        private static string ArgumentText(IReadOnlyList<string>? args, int n, Func<string, string> localize)
        {
            string value = args != null && n < args.Count ? args[n] ?? string.Empty : string.Empty;
            if (value.StartsWith("@@", StringComparison.Ordinal))
            {
                return value.Substring(1); // a player's text that began with '@' — literal
            }

            return value.Length > 1 && value[0] == '@' ? localize(value.Substring(1)) : value;
        }

        /// <summary>The events of one category, oldest first (tests and the filter's counters).</summary>
        public IEnumerable<NetAdminEvent> OfCategory(string category) => _events.Where(e => e.Category == category);
    }
}
