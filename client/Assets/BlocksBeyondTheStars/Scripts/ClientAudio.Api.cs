// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>Small hooks other audio components drive (#3247/#3248): kept apart from the router's main file.</summary>
    public sealed partial class ClientAudio
    {
        /// <summary>#3247: set by the ship soundscape while it plays the design-derived hum of the player's own ship; the
        /// router's built-in two-sine hum then stays silent so the two never stack.</summary>
        public bool ShipHumOverride { get; set; }

        /// <summary>#3247: the ambience-bed level (effects × the cutscene duck, #2495) for components that run their own beds
        /// — the ship's design hum and its module rooms duck under a scene like the router's own beds.</summary>
        public float BedLevel => SfxVol() * _duck * AmbienceVol();

        /// <summary>The ambience slider: the beds' share of the effects volume.</summary>
        private float AmbienceVol() => UnityEngine.Mathf.Clamp01(Settings?.AmbienceVolume ?? 1f);

        /// <summary>The interface slider: an interface sound's share of the effects volume. Every 2D one-shot whose clip
        /// is named <c>ui_…</c> (the slot pick, the craft's confirm and reject, the success chime, a toast) follows it,
        /// as the clicks and windows of <see cref="UiSound"/> do; 1 for any other clip. The clips are kept under their
        /// own names, so the name is the cue id (a muffled bake keeps the prefix).</summary>
        private float UiShare(UnityEngine.AudioClip clip)
            => clip.name.StartsWith("ui_", System.StringComparison.Ordinal) ? UnityEngine.Mathf.Clamp01(Settings?.UiVolume ?? 1f) : 1f;
    }
}
