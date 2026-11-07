// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One saved HUD frame design of the player's own library (#3318): a name and the design string
    /// (<c>HudFrameDesign.Encode</c>). A flat class so JsonUtility can persist it; separate from the outfits.</summary>
    [Serializable]
    public sealed class HudFrameDesignEntry
    {
        public string Name = "";
        public string Design = "";
    }

    /// <summary>
    /// The HUD frame around the view (#3317): the design the player wears and how it is shown. Purely the player's own —
    /// which templates a world has unlocked comes from the server (<c>NetworkClient.CosmeticUnlockKeys</c>), but what the
    /// player then paints and wears lives here and never travels. Old settings files have none of these fields, so
    /// JsonUtility keeps the defaults below: no frame, shown when there is one, medium size, fully opaque.
    /// </summary>
    public sealed partial class ClientSettings
    {
        /// <summary>Upper bound of the own frame library — the editor's column shows them without scrolling.</summary>
        public const int MaxHudFrameDesigns = 8;

        /// <summary>The faintest frame the opacity setting allows.</summary>
        public const float MinHudFrameOpacity = 0.6f;

        /// <summary>The design string of the frame the player wears (<c>hf1|…</c>); empty = no frame.</summary>
        public string HudFrameDesign = "";

        /// <summary>Show the frame (when there is one). Off keeps the design for later.</summary>
        public bool HudFrameOn = true;

        /// <summary>0 small, 1 medium, 2 large (<c>HudFrameLayout.TilePixels</c>).</summary>
        public int HudFrameSize = 1;

        /// <summary>The frame's opacity, <see cref="MinHudFrameOpacity"/> to 1.</summary>
        public float HudFrameOpacity = 1f;

        /// <summary>The player's own saved frames, in the order they were saved (at most <see cref="MaxHudFrameDesigns"/>).</summary>
        public List<HudFrameDesignEntry> HudFrameDesigns = new List<HudFrameDesignEntry>();

        /// <summary>The template keys whose unlock was already announced (comma-separated): a new <c>hudframe:</c> unlock is
        /// put on (when the player wears no frame) and announced once per installation, never again on the next join.</summary>
        public string HudFrameAnnounced = "";

        /// <summary><see cref="HudFrameSize"/> clamped to the three sizes — a hand-edited file stays in range.</summary>
        public int HudFrameSizeLevel => Mathf.Clamp(HudFrameSize, 0, 2);

        /// <summary><see cref="HudFrameOpacity"/> clamped to its range.</summary>
        public float HudFrameAlpha => Mathf.Clamp(HudFrameOpacity, MinHudFrameOpacity, 1f);

        /// <summary>Notes that the unlock of template <paramref name="key"/> was announced. False when it already was.</summary>
        public bool MarkHudFrameAnnounced(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf(',') >= 0)
            {
                return false;
            }

            foreach (string known in (HudFrameAnnounced ?? string.Empty).Split(','))
            {
                if (string.Equals(known, key, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            HudFrameAnnounced = string.IsNullOrEmpty(HudFrameAnnounced) ? key : HudFrameAnnounced + "," + key;
            return true;
        }
    }
}
