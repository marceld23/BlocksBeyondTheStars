// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The graphics presets a platform offers (#3946), on the ladder of <see cref="QualityCalibration"/> (Potato 0 · Low 1
    /// · Medium 2 · High 3). A browser tab shares a weak GPU with the page and has no worker threads for the chunk
    /// mesher, so the browser build offers only Potato and Low; every other platform offers all four. The client asks
    /// this one rule wherever a preset is stored, offered, stepped or overridden, so no path can land above the limit.
    /// </summary>
    public static class PresetLimit
    {
        /// <summary>The highest preset a platform allows: Low in the browser, High everywhere else.</summary>
        public static int Highest(bool browser) => browser ? QualityCalibration.Low : QualityCalibration.High;

        /// <summary><paramref name="preset"/> held inside Potato .. <paramref name="highest"/>.</summary>
        public static int Clamp(int preset, int highest)
            => Math.Max(QualityCalibration.Potato, Math.Min(preset, Math.Max(QualityCalibration.Potato, highest)));

        /// <summary>How many presets a platform offers (Potato up to <paramref name="highest"/>): the length of the
        /// settings row's cycle.</summary>
        public static int Count(int highest) => Math.Max(QualityCalibration.Potato, highest) - QualityCalibration.Potato + 1;
    }
}
