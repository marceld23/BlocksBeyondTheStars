// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How the interface's signal colours (good, caution) are chosen — for players who cannot tell red from
    /// green, or blue from yellow.</summary>
    public enum ColourVision
    {
        Standard,
        RedGreen,
        BlueYellow,
    }

    /// <summary>
    /// The settings of the feel pass: two more volume sliders that sit UNDER the effects volume (a world's ambience and
    /// the interface's own sounds can each be turned down on their own), the comfort choices (keys that switch instead
    /// of being held, the story text's size and backdrop, the signal colours) and the display choices (the fullscreen
    /// resolution, the monitor, the pause when the window loses focus).
    /// Serialized with the rest of the class (JsonUtility); an older settings file simply leaves the defaults.
    /// </summary>
    public sealed partial class ClientSettings
    {
        /// <summary>The story text sizes offered: the normal size, a quarter and a half larger.</summary>
        public static readonly float[] CaptionScales = { 1f, 1.25f, 1.5f };

        /// <summary>The ambience beds' share of the effects volume (biome, weather, night, cave, the ship's hum), 0…1.</summary>
        public float AmbienceVolume = 1f;

        /// <summary>The interface sounds' share of the effects volume (clicks, windows, hints), 0…1.</summary>
        public float UiVolume = 1f;

        /// <summary>The keyboard's run key switches the run on and off instead of being held.</summary>
        public bool SprintToggle;

        /// <summary>The keyboard's crouch key switches crouching on and off instead of being held.</summary>
        public bool CrouchToggle;

        /// <summary>The story text's size on top of the UI scale (<see cref="CaptionScales"/>).</summary>
        public float CaptionScale = 1f;

        /// <summary>A dark plate behind the story text, for bright scenes and weak eyes.</summary>
        public bool CaptionBackdrop;

        /// <summary>The signal colours' palette.</summary>
        public ColourVision ColourVision = ColourVision.Standard;

        /// <summary>The game opens its pause menu when the window loses the focus (desktop).</summary>
        public bool PauseOnFocusLoss = true;

        /// <summary>The fullscreen resolution; 0 × 0 = the monitor's own. A monitor that cannot show it gets its own.</summary>
        public int FullscreenWidth;

        /// <summary>See <see cref="FullscreenWidth"/>.</summary>
        public int FullscreenHeight;

        /// <summary>The monitor the game shows on (index into the system's list; 0 = the main one).</summary>
        public int Monitor;

        /// <summary>The lowest 3D resolution the player may choose, as a share of the screen's.</summary>
        public const float RenderScaleMin = 0.5f;

        /// <summary>The 3D view's resolution as a share of the screen's (0.5…1), on top of the preset's own scale. The
        /// interface is never scaled. Desktop only — the browser keeps its preset values.</summary>
        public float RenderScale = 1f;

        /// <summary>Sets the pipeline's render scale: the preset's (<paramref name="presetScale"/>) times the player's
        /// own, and — when the PLAYER turned the 3D resolution down on desktop — the sharpening upscaler (FSR 1)
        /// instead of a plain stretch. A preset's own scale alone never asks for it: the cheapest preset renders
        /// smaller to save time on a weak machine, and the upscaler is one more full-screen pass.</summary>
        private void ApplyRenderScale(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp, float presetScale)
        {
            bool browser = Application.platform == RuntimePlatform.WebGLPlayer;
            float own = browser ? 1f : Mathf.Clamp(RenderScale, RenderScaleMin, 1f);
            urp.renderScale = Mathf.Clamp(presetScale * own, 0.35f, 1f);
            urp.upscalingFilter = own < 0.999f
                ? UnityEngine.Rendering.Universal.UpscalingFilterSelection.FSR
                : UnityEngine.Rendering.Universal.UpscalingFilterSelection.Auto;
        }

        /// <summary>The recipe pinned to the HUD (its key), or empty: the card that shows what is still missing.</summary>
        public string PinnedRecipe = string.Empty;

        /// <summary>The pad's button names follow the connected pad's own name (<c>PadGlyphTable.Detect</c>);
        /// <see cref="PadGlyphs"/> is then only the fallback for a pad whose name says nothing.</summary>
        public bool PadGlyphsAuto = true;

        /// <summary>The feel pass's share of <see cref="Apply"/>: the UI's motion and comfort switches, the input
        /// toggles, the story text and the palette.</summary>
        private void ApplyFeel()
        {
            UiKit.ReducedMotion = ReducedEffects; // UI transitions snap instantly for reduced-effects users
            InputMap.SprintToggle = SprintToggle;
            InputMap.CrouchToggle = CrouchToggle;
            UiKit.CaptionScale = Mathf.Clamp(CaptionScale, 1f, 1.5f);
            UiKit.CaptionBackdrop = CaptionBackdrop;
            UiPalette.Mode = ColourVision;
            if (!_monitorPlaced)
            {
                _monitorPlaced = true; // once per run: the window opens where the system puts it, then moves to the saved monitor
                if (Monitor > 0)
                {
                    ApplyMonitor();
                }
            }
        }

        private static bool _monitorPlaced;

        /// <summary>The fullscreen width to ask for: the chosen one while the monitor supports it, else the monitor's own.</summary>
        private int FullWidth(Resolution native) => SupportedFullscreen(native) ? FullscreenWidth : MonitorSize(native).x;

        /// <summary>The fullscreen height to ask for — see <see cref="FullWidth"/>.</summary>
        private int FullHeight(Resolution native) => SupportedFullscreen(native) ? FullscreenHeight : MonitorSize(native).y;

        private bool SupportedFullscreen(Resolution native)
        {
            var monitor = MonitorSize(native);
            return FullscreenWidth >= 640 && FullscreenHeight >= 480
                && FullscreenWidth <= monitor.x && FullscreenHeight <= monitor.y;
        }

        /// <summary>The monitor's OWN resolution. <c>Screen.currentResolution</c> is the mode the game itself switched to
        /// while it runs in exclusive fullscreen, so "back to the monitor's own" must ask the display instead.</summary>
        private static Vector2Int MonitorSize(Resolution current)
        {
            var display = Screen.mainWindowDisplayInfo;
            return display.width > 0 && display.height > 0
                ? new Vector2Int(display.width, display.height)
                : new Vector2Int(current.width, current.height);
        }

        /// <summary>The fullscreen sizes this monitor offers, largest first, without the refresh-rate duplicates and
        /// without anything smaller than 1280 × 720 (the interface is laid out for 1920 × 1080 and scales down, but not
        /// endlessly).</summary>
        public static List<Vector2Int> FullscreenChoices()
        {
            var seen = new HashSet<Vector2Int>();
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (r.width >= 1280 && r.height >= 720 && seen.Add(size))
                {
                    list.Add(size);
                }
            }

            list.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        /// <summary>Moves the window to the chosen <see cref="Monitor"/> (desktop players with more than one). Does
        /// nothing in the browser, in the Editor or when the index does not exist any more.</summary>
        public void ApplyMonitor()
        {
#if !UNITY_WEBGL && !UNITY_EDITOR
            var displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            if (displays.Count < 2 || Monitor < 0 || Monitor >= displays.Count)
            {
                return;
            }

            var target = displays[Monitor];
            if (!target.Equals(Screen.mainWindowDisplayInfo))
            {
                Screen.MoveMainWindowTo(target, Vector2Int.zero);
            }
#endif
        }
    }
}
