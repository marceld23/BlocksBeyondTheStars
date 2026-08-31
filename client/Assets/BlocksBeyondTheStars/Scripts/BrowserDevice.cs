// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Classifies the browser build's host device once per session (#1423). "Mobile browser" means a
    /// phone/tablet-class device, where the stripped-down browser profile applies (first-run defaults:
    /// view distance 3 + synth music, plus tighter in-process server budgets and no music prefetch).
    /// Deliberately only a START guess: the shell frame-time calibration
    /// (<see cref="AutoQualityCalibrator"/>) is the authority on the quality preset and corrects in
    /// both directions, so a strong tablet climbs back up and a weak desktop browser steps down.
    /// </summary>
    public static class BrowserDevice
    {
        private static bool? s_isMobileBrowser;

        /// <summary>True in a WebGL player on a phone/tablet-class device; always false elsewhere.</summary>
        public static bool IsMobileBrowser => s_isMobileBrowser ??= Detect();

        private static bool Detect()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                return false;
            }

            // The WebGL renderer string names the GPU family, and it is the one static signal that
            // separates device CLASSES cleanly: Mali/Adreno/PowerVR exist only in phone/tablet-class
            // hardware, while desktops report NVIDIA/AMD/Intel/Apple-M. The tempting alternatives all
            // lie — a budget tablet reports 8 cores like a desktop, and UA sniffing misses iPadOS
            // Safari (desktop "Macintosh" UA). "Apple GPU" alone is ambiguous (M-series Macs report it
            // in Safari too), so it only counts together with touch support, which Mac Safari lacks.
            string gpu = SystemInfo.graphicsDeviceName ?? string.Empty;
            bool mobileGpu = Contains(gpu, "Mali") || Contains(gpu, "Adreno") || Contains(gpu, "PowerVR");
            bool appleTouch = Contains(gpu, "Apple") && Input.touchSupported;
            return mobileGpu || appleTouch;
        }

        private static bool Contains(string haystack, string needle)
            => haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
