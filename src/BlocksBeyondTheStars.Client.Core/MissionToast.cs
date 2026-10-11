// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The toast for a successful <see cref="MissionResult"/> (#2462, B24). It used to read
    /// <c>Mission '&lt;raw id&gt;' complete!</c> — in English, and for an accepted, a posted and a dialogue-granted
    /// mission alike. Now it is localized, names the mission by its title and says what happened: accepted, complete
    /// (only on a real turn-in) or posted. An older server sends no outcome; the toast then says neutrally that the
    /// mission was updated. Unity-free so the wording rules are covered by plain .NET tests.
    /// </summary>
    public static class MissionToast
    {
        public const string AcceptedKey = "ui.missions.toast.accepted";
        public const string CompletedKey = "ui.missions.toast.completed";
        public const string PostedKey = "ui.missions.toast.posted";
        public const string UpdatedKey = "ui.missions.toast.updated";
        public const string UnnamedKey = "ui.missions.toast.unnamed";

        /// <summary>The toast text. <paramref name="known"/> is the mission from the client's mission list (same id), the
        /// name source for an older server whose result carries no title; may be null.</summary>
        public static string Text(MissionResult result, NetMission? known, Func<string, string> localize, Func<string, bool> has)
        {
            string name = Name(result.Title, result.FreeText, localize, has);
            if (name.Length == 0 && known is not null)
            {
                name = Name(known.Title, known.FreeText, localize, has);
            }

            if (name.Length == 0)
            {
                name = localize(UnnamedKey);
            }

            string key = result.Outcome switch
            {
                MissionOutcomes.Accepted => AcceptedKey,
                MissionOutcomes.Completed => CompletedKey,
                MissionOutcomes.Posted => PostedKey,
                _ => UpdatedKey,
            };

            // Replace, not string.Format: a translation with a stray brace must not throw inside a network callback.
            return localize(key).Replace("{0}", name);
        }

        /// <summary>A display name from a title: free text verbatim, a locale key localized — or empty when the key is
        /// unknown to this client, so the raw key never reaches the screen.</summary>
        private static string Name(string? title, bool freeText, Func<string, string> localize, Func<string, bool> has)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            if (freeText)
            {
                return title!.Trim();
            }

            return has(title!) ? localize(title!) : string.Empty;
        }
    }
}
