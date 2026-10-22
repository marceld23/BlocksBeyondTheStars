// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>The status toast's lifetime rules (#1860), pure so the EditMode tests can pin them: how long a
    /// message holds before it fades, how the fade runs, and when it counts as gone. Warnings hold longer; they
    /// are recognised by the resolved text's opening ("Warning:" / "Warnung:" / "Life support lost" …) because
    /// the HUD only ever sees the localized line, never its key.</summary>
    public static class HudToastPolicy
    {
        public const float HoldNormalSeconds = 8f;
        public const float HoldWarningSeconds = 15f;
        public const float FadeSeconds = 0.5f;

        /// <summary>Openings of the localized warning lines (EN + DE) — ui.base.air_lost, ui.station.air_lost,
        /// ui.base.air_left and the server's located leak lines. Ordinal, case-sensitive: these are our own strings.</summary>
        private static readonly string[] WarningPrefixes =
        {
            "Warning", "Warnung", "⚠", "Life support lost", "Lebenserhaltung verloren",
        };

        public static bool IsWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            string trimmed = message.TrimStart();
            for (int i = 0; i < WarningPrefixes.Length; i++)
            {
                if (trimmed.StartsWith(WarningPrefixes[i], System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Seconds the toast stays fully visible before the fade-out starts.</summary>
        public static float HoldSeconds(string message)
            => IsWarning(message) ? HoldWarningSeconds : HoldNormalSeconds;

        /// <summary>Alpha at <paramref name="age"/> seconds: 1 through the hold, then a linear fade to 0.</summary>
        public static float Alpha(float age, float hold)
        {
            if (age <= hold)
            {
                return 1f;
            }

            float a = 1f - ((age - hold) / FadeSeconds);
            return a < 0f ? 0f : a > 1f ? 1f : a;
        }

        /// <summary>True once the hold and the fade have both run out.</summary>
        public static bool Expired(float age, float hold) => age >= hold + FadeSeconds;
    }
}
