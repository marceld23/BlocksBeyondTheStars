// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Festivals;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Character tab's festival calendar button (#3316): while the server says a calendar is on for this player (the
    /// Advent calendar for those who celebrate Christmas), a row under the appearance card opens the calendar window
    /// (<see cref="UiFestivalCalendar"/>) and says how many doors wait — warm, never urgent. Nothing without an active
    /// calendar.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private static readonly Color CalendarGold = new Color(1f, 0.82f, 0.45f);

        /// <summary>The refresh hash's share of the calendar: a new state rebuilds the Character tab's button.</summary>
        private int CalendarHash() => Game != null ? Game.CalendarVersion * 1931 : 0;

        /// <summary>Adds the calendar row at <paramref name="y"/> when a calendar is active; returns the next y.</summary>
        private float AddCalendarRow(float y)
        {
            var state = Game?.ActiveCalendar;
            if (state == null)
            {
                return y;
            }

            string key = state.Calendar;
            var def = Game.Content?.FindFestivalCalendar(key);
            var festival = Game.Content?.FindFestival(state.Festival);
            string name = L(def != null ? def.NameKey : "calendar." + key + ".name");
            if (festival != null && !string.IsNullOrEmpty(festival.Glyph))
            {
                name = festival.Glyph + " " + name;
            }

            int waiting = FestivalCalendarView.Waiting(state);
            var row = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => UiFestivalCalendar.Open(Game, key, _canvas));
            row.GetComponent<Image>().color = waiting > 0 ? new Color(0.62f, 0.5f, 0.28f) : new Color(0.16f, 0.28f, 0.40f);
            var title = UiKit.AddText(row.transform, 16, 0, 470, 78, name, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(title, 14, 24);
            var value = UiKit.AddText(row.transform, 500, 0, 270, 78, waiting > 0 ? WaitingChip(waiting) : L("ui.calendar.look"), 18,
                waiting > 0 ? CalendarGold : UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(value, 11, 18);
            return y + 96f;
        }

        /// <summary>"2 doors waiting" / "1 door waiting".</summary>
        private string WaitingChip(int waiting)
            => waiting == 1
                ? L("ui.calendar.chip_one")
                : L("ui.calendar.chip").Replace("{n}", waiting.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
