// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2570: the words of the planet map and the HUD compass for VEGA's story help marker (<see cref="NetMarker.Story"/>) —
    /// the target of an open story step, shown until the step is done or skipped — and for an ordinary ping, whose map label
    /// used to be the hardcoded English <c>"Ping · " + owner</c>. The marker carries the step's objective key; its label is
    /// that objective ("Pick 3 berries"), or the localized "Goal" when there is none. Formatting never throws: a translation
    /// without its <c>{0}</c> gets the value appended. Unity-free, unit-tested headless.
    /// </summary>
    public static class StoryMarkerText
    {
        /// <summary>The story marker's fallback label ("Goal").</summary>
        public const string GoalKey = "ui.map.story_target";

        /// <summary>The compass caption under the dial ("Goal {0} m").</summary>
        public const string CompassKey = "ui.hud.compass_story_target";

        /// <summary>An ordinary ping's map label ("Ping · {0}", the owner's name).</summary>
        public const string PingKey = "ui.map.ping";

        /// <summary>Whether a marker is VEGA's story help marker (an older server never sets the flag).</summary>
        public static bool IsStory(NetMarker? marker) => marker is { Story: true };

        /// <summary>The story marker's label: the step's objective text — <paramref name="storyText"/> resolves a story key of
        /// a pack (key, pack id), the way the objective chip does — with its <c>{0}</c> filled from
        /// <see cref="NetMarker.LabelArg"/> (#2742: a chapter lead names its body), else the localized "Goal".</summary>
        public static string Label(NetMarker marker, Func<string, string, string> storyText, Func<string, string> localize)
        {
            string text = string.IsNullOrEmpty(marker.LabelKey)
                ? string.Empty
                : storyText(marker.LabelKey, marker.PackId ?? string.Empty) ?? string.Empty;
            if (text.Trim().Length == 0)
            {
                return localize(GoalKey);
            }

            string arg = marker.LabelArg ?? string.Empty;
            return arg.Length > 0 && text.Contains("{0}") ? text.Replace("{0}", arg) : text;
        }

        /// <summary>The compass caption: "Goal 120 m".</summary>
        public static string CompassCaption(int metres, Func<string, string> localize)
            => Fill(localize(CompassKey), metres.ToString(CultureInfo.InvariantCulture));

        /// <summary>An ordinary ping's map label: "Ping · Alice".</summary>
        public static string PingLabel(string? owner, Func<string, string> localize)
            => Fill(localize(PingKey), owner ?? string.Empty);

        /// <summary>The template with its <c>{0}</c> replaced (no <c>string.Format</c>: a stray brace in a translation or a
        /// player name must not throw); a template without the slot gets the value appended.</summary>
        private static string Fill(string? template, string value)
        {
            string t = template ?? string.Empty;
            return t.Contains("{0}") ? t.Replace("{0}", value) : (t + " " + value).Trim();
        }
    }
}
