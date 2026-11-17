// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The villagers in the feel pass: whoever speaks moves the mouth. Each clip of an NPC's babble opens a
    /// Sounder's mouth as before (#3355) and now moves a villager's stock mouth for as long as the clip plays, so a
    /// speech bubble has a speaker one can see.
    /// </summary>
    public sealed partial class NpcView
    {
        private void PulseMouth(Npc n, float clipSeconds)
        {
            n.SounderRig?.Pulse(0.8f); // #3355: a Sounder's mouth opens as it hums
            if (n.Avatar != null)
            {
                n.Avatar.Talk(Mathf.Max(0.25f, clipSeconds));
            }
        }
    }
}
