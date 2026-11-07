// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The festival chip's calendar note (#3316): while doors of a festival day calendar wait, the chip says so
    /// ("✧ Christmas · 2 doors waiting") — a friendly note, never a countdown. Advent comes before Christmas's own festive
    /// days, so without a festival chip the note names the calendar itself ("Advent calendar · 1 door waiting"). Like the
    /// festival chip it stays away while the player switched the festival mood off; the calendar itself is in the
    /// Character tab either way.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary><paramref name="festivalChip"/> (the chip as <see cref="FestivalChip"/> built it, maybe empty) with the
        /// doors-waiting note added.</summary>
        private string WithCalendar(string festivalChip, Localizer loc)
        {
            int waiting = Game != null ? Game.CalendarDoorsWaiting : 0;
            if (waiting <= 0 || !FestivalMood.Enabled || loc == null)
            {
                return festivalChip;
            }

            string doors = waiting == 1
                ? loc.Get("ui.calendar.chip_one")
                : loc.Get("ui.calendar.chip").Replace("{n}", waiting.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(festivalChip))
            {
                return festivalChip + $"<color=#ffd27a> · {doors}</color>";
            }

            var state = Game.ActiveCalendar;
            var def = state != null ? Game.Content?.FindFestivalCalendar(state.Calendar) : null;
            string name = def != null ? loc.Get(def.NameKey) + " · " : string.Empty;
            return $"  <color=#ffd27a>{name}{doors}</color>";
        }
    }
}
