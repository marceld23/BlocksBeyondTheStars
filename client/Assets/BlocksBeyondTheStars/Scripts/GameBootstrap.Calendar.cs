// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The festival day calendars on the client (#3315, the Advent calendar): the server sends each calendar's state at the
    /// join, when a new door comes at the player's local midnight, after a door was opened and when the calendar ends, and
    /// the cosmetics its doors unlocked. This mirrors it for the entry points — the Character tab's calendar button
    /// (<see cref="CalendarVersion"/> rebuilds it), the HUD festival chip's "doors waiting" and the calendar window
    /// (<see cref="UiFestivalCalendar"/>), which follows the states itself while it is open. Only active states show; the
    /// server alone decides who gets a calendar and what a door holds.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Bumped on every calendar state, so an open Character tab rebuilds its calendar button.</summary>
        public int CalendarVersion { get; private set; }

        /// <summary>Bumped whenever the unlocked cosmetics of this world change (a door unlocked a HUD frame or a skin).</summary>
        public int CosmeticVersion { get; private set; }

        /// <summary>The calendar the entry points open — the active one with doors waiting first — or null.</summary>
        public FestivalCalendarState ActiveCalendar => FestivalCalendarView.FirstActive(Network?.Calendars);

        /// <summary>How many calendar doors wait to be opened (0 without an active calendar).</summary>
        public int CalendarDoorsWaiting => FestivalCalendarView.TotalWaiting(Network?.Calendars?.Values);

        /// <summary>The calendar subscriptions; called from <see cref="WireKnowledge"/>.</summary>
        private void WireCalendar()
        {
            Network.CalendarStateReceived += _ => CalendarVersion++;
            Network.CosmeticUnlocksChanged += () => CosmeticVersion++;
            Network.JoinAccepted += _ =>
            {
                // Another world: its calendars and unlocks follow the join (the network client already dropped the old ones).
                CalendarVersion++;
                CosmeticVersion++;
            };
        }
    }
}
