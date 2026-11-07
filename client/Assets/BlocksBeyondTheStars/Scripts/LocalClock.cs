// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Festivals;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's local UTC offset (#3315): a festival day calendar's doors open at the player's own midnight, so the
    /// offset rides on the join and on every door request (<see cref="NetworkClient.UtcOffsetMinutes"/>). The desktop
    /// reads <see cref="TimeZoneInfo.Local"/>; the browser build asks JavaScript (<c>Plugins/BbsLocalTime.jslib</c>),
    /// because .NET's local zone is UTC inside the browser. Clamped to ±14 h by <see cref="FestivalCalendarView"/>.
    /// </summary>
    public static class LocalClock
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int BbsTimezoneOffsetMinutes();
#endif

        /// <summary>The local offset from UTC in minutes right now (60 in Berlin in winter, −300 in New York).</summary>
        public static int UtcOffsetMinutes()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return FestivalCalendarView.FromBrowserTimezoneOffset(BbsTimezoneOffsetMinutes());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGL] Local time zone unavailable: {ex.Message}");
                return 0;
            }
#else
            try
            {
                return FestivalCalendarView.UtcOffsetMinutes(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Local time zone unavailable: {ex.Message}");
                return 0;
            }
#endif
        }
    }
}
