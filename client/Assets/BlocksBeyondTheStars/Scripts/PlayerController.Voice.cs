// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The figure's breath of effort on a jump (#3767 — the voice of the character editor needs something to say): a
    /// short "hup" in the player's own voice, rarely. The body layer is light by decision — never constant — so it comes
    /// with a jump only when the body works (the stamina is worn) or once in a long while, never with every hop.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>Seconds between two breaths of effort while the stamina is worn …</summary>
        private const float EffortBreathGapWorn = 2.5f;

        /// <summary>… and while the figure is fresh.</summary>
        private const float EffortBreathGapFresh = 9f;

        private float _nextEffortBreath;

        private void EffortBreath()
        {
            float now = Time.time;
            if (now < _nextEffortBreath)
            {
                return;
            }

            bool worn = _stamina.Strain > 0.3f;
            _nextEffortBreath = now + (worn ? EffortBreathGapWorn : EffortBreathGapFresh);
            ClientAudio.Instance?.Voice("breath_effort", worn ? 0.32f : 0.22f);
        }
    }
}
