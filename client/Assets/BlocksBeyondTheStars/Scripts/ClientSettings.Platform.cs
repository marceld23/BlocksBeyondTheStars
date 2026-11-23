// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The graphics presets this platform offers (#3946): the browser build only Potato and Low, every other
    /// platform all four. The rule itself is <see cref="PresetLimit"/>; everything that stores, offers, steps or overrides
    /// a preset asks it through here.</summary>
    public sealed partial class ClientSettings
    {
        /// <summary>The highest preset this platform allows: Low in the WebGL player, High everywhere else.</summary>
        public static QualityPreset HighestPreset
            => (QualityPreset)PresetLimit.Highest(Application.platform == RuntimePlatform.WebGLPlayer);

        /// <summary>How many presets the settings row offers here (Potato up to <see cref="HighestPreset"/>).</summary>
        public static int PresetCount => PresetLimit.Count((int)HighestPreset);

        /// <summary><paramref name="preset"/> lowered to <see cref="HighestPreset"/> where it lies above it (and an
        /// out-of-range value from a hand-edited file into the ladder); a preset this platform offers comes back
        /// unchanged.</summary>
        public static QualityPreset AllowedPreset(QualityPreset preset)
            => (QualityPreset)PresetLimit.Clamp((int)preset, (int)HighestPreset);

        /// <summary>Lowers a stored preset this platform does not offer (a settings file carried over from a build that
        /// offered more, or edited by hand). True when it changed, so <see cref="Load"/> saves the lowered value.</summary>
        private bool LowerPresetToPlatform()
        {
            var allowed = AllowedPreset(Preset);
            if (allowed == Preset)
            {
                return false;
            }

            Debug.Log($"[Settings] Preset {Preset} is not offered on this platform — lowered to {allowed}.");
            Preset = allowed;
            return true;
        }
    }
}
