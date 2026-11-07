// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Festival day calendars (#3315, the Advent calendar) and the cosmetics their doors unlock.</summary>
    public sealed partial class NetworkClient
    {
        private readonly Dictionary<string, FestivalCalendarState> _calendars = new(StringComparer.Ordinal);
        private readonly HashSet<string> _cosmeticUnlocks = new(StringComparer.Ordinal);

        /// <summary>A festival day calendar's state arrived (join, a new door at local midnight, a door opened, the end).</summary>
        public event Action<FestivalCalendarState>? CalendarStateReceived;

        /// <summary>The unlocked cosmetics of this world changed (<see cref="CosmeticUnlockKeys"/>).</summary>
        public event Action? CosmeticUnlocksChanged;

        /// <summary>The player's local UTC offset in minutes, sent with the join and every door request — the calendar's doors
        /// open at the player's local midnight. The Unity client sets it before connecting (the browser asks JavaScript).</summary>
        public int UtcOffsetMinutes { get; set; }

        /// <summary>The latest state of every calendar the server told about (inactive ones included), by calendar key.</summary>
        public IReadOnlyDictionary<string, FestivalCalendarState> Calendars => _calendars;

        /// <summary>The cosmetics unlocked in this world (<c>hudframe:&lt;key&gt;</c>, <c>skin:&lt;key&gt;</c>).</summary>
        public IReadOnlyCollection<string> CosmeticUnlockKeys => _cosmeticUnlocks;

        /// <summary>Whether <paramref name="key"/> (<c>hudframe:christmas_lights</c>) is unlocked in this world.</summary>
        public bool HasCosmetic(string key) => _cosmeticUnlocks.Contains(key);

        /// <summary>Opens door <paramref name="door"/> (1-based) of calendar <paramref name="calendar"/>.</summary>
        public void SendOpenCalendarDoor(string calendar, int door)
            => Send(new OpenCalendarDoorIntent { Calendar = calendar ?? string.Empty, Door = door, UtcOffsetMinutes = UtcOffsetMinutes });

        private void HandleCalendarState(FestivalCalendarState m)
        {
            _calendars[m.Calendar ?? string.Empty] = m;
            CalendarStateReceived?.Invoke(m);
        }

        private void HandleCosmeticUnlocks(CosmeticUnlocks m)
        {
            _cosmeticUnlocks.Clear();
            foreach (var key in m.Keys ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(key))
                {
                    _cosmeticUnlocks.Add(key);
                }
            }

            CosmeticUnlocksChanged?.Invoke();
        }

        /// <summary>A fresh session starts without calendars or unlocks — the server sends both after the join.</summary>
        private void ResetCalendarState()
        {
            _calendars.Clear();
            _cosmeticUnlocks.Clear();
        }
    }
}
