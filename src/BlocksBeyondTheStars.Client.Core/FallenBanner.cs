// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the fallen player's banner says (#3765), Unity-free so the suite can hold it. The server tells the client
    /// the seconds until a restart at the ship is allowed (<c>RespawnOptions.RestartInSeconds</c>: -1 while a team-mate
    /// can still help, a count while nobody is left, 0 now); the client counts a positive number down by itself and
    /// picks the line. The server checks the restart again — this only decides what is shown and when the control is
    /// offered.
    /// </summary>
    public static class FallenBanner
    {
        /// <summary>The seconds left now, from the number told and the time since: a wait for the team stays a wait
        /// (negative), a count runs down to zero.</summary>
        public static float SecondsLeft(float toldSeconds, float sinceTold)
        {
            if (toldSeconds < 0f)
            {
                return toldSeconds;
            }

            float left = toldSeconds - (sinceTold > 0f ? sinceTold : 0f);
            return left > 0f ? left : 0f;
        }

        /// <summary>True once the control that starts again at the ship is offered.</summary>
        public static bool MayRestart(float secondsLeft) => secondsLeft == 0f;

        /// <summary>The banner's line: wait for a team-mate, the countdown (its <c>{0}</c> is the seconds), or the control.</summary>
        public static string LineKey(float secondsLeft)
            => secondsLeft < 0f ? "ui.fallen.wait" : secondsLeft > 0f ? "ui.fallen.countdown" : "ui.fallen.restart";

        /// <summary>The banner's line as shown: the localized <paramref name="template"/> of <see cref="LineKey"/> with its
        /// control tokens (<c>{key:Interact}</c>) expanded by <paramref name="expandKeys"/> and its <c>{0}</c> replaced by
        /// the whole seconds left. The tokens go first and the number is put in by plain replacement: a control token is
        /// no format item, and <c>string.Format</c> on it throws — which left the restart control dead.</summary>
        public static string Line(string? template, float secondsLeft, System.Func<string, string>? expandKeys)
        {
            string text = template ?? string.Empty;
            if (expandKeys != null)
            {
                text = expandKeys(text) ?? string.Empty;
            }

            int seconds = (int)System.Math.Ceiling(System.Math.Max(0f, secondsLeft));
            return text.Replace("{0}", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
