// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The capture switch for the High details (#3061): <c>-highDetails Off|All</c>.</summary>
    public sealed partial class ClipDirector
    {
        private string _highDetails;
        private bool[] _highBefore;

        /// <summary>Captures with every "Details (High)" switch off or on — together with <c>-preset High</c> the
        /// before/after pair of the package on one build. The player's switches are restored in Quit().</summary>
        private void ApplyHighDetailsCapture(ClientSettings s)
        {
            if (s == null || string.IsNullOrEmpty(_highDetails))
            {
                return;
            }

            bool on = string.Equals(_highDetails, "All", StringComparison.OrdinalIgnoreCase);
            if (!on && !string.Equals(_highDetails, "Off", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[Clip] -highDetails expects Off or All, got '{_highDetails}' — ignored");
                return;
            }

            _highBefore = new[] { s.HighRelief, s.HighGrass, s.HighFlowers, s.HighRoomReflections, s.HighDecals, s.HighVolumetricLight, s.HighLampShadows };
            s.HighRelief = s.HighGrass = s.HighFlowers = s.HighRoomReflections = s.HighDecals = s.HighVolumetricLight = s.HighLampShadows = on;
            s.Apply();
            Debug.Log($"[Clip] highDetails={(on ? "All" : "Off")} (restored on exit)");
        }

        private void RestoreHighDetailsCapture(ClientSettings s)
        {
            if (s == null || _highBefore == null)
            {
                return;
            }

            s.HighRelief = _highBefore[0];
            s.HighGrass = _highBefore[1];
            s.HighFlowers = _highBefore[2];
            s.HighRoomReflections = _highBefore[3];
            s.HighDecals = _highBefore[4];
            s.HighVolumetricLight = _highBefore[5];
            s.HighLampShadows = _highBefore[6]; // #3233
            _highBefore = null;
        }
    }
}
