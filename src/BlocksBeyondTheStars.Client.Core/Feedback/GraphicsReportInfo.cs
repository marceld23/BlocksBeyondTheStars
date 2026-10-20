// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// The graphics state a report was filed in (#2844). An upside-down HUD in the browser (#2512, #2843) could not
    /// be pinned down for weeks because the report said which GPU drew the frame but not how: the preset, whether the
    /// visor HUD pipeline was up, which way its texture lay, the render scale, the size the screen claimed. These keys
    /// answer that without asking the player. Plain values, filled by the client on the main thread.
    /// </summary>
    public sealed class GraphicsReportInfo
    {
        /// <summary>The graphics API (<c>SystemInfo.graphicsDeviceType</c>: Direct3D11, WebGPU, OpenGLES3, …).</summary>
        public string Api { get; set; } = string.Empty;

        /// <summary>Whether the API's texture origin is top-left (<c>SystemInfo.graphicsUVStartsAtTop</c>).</summary>
        public bool UvStartsAtTop { get; set; }

        /// <summary>The quality preset's name (Potato … Ultra).</summary>
        public string Preset { get; set; } = string.Empty;

        /// <summary>Whether the preset is still the automatic pick (the calibrator may have changed it).</summary>
        public bool PresetAuto { get; set; }

        /// <summary>The player's "Visor effect" setting.</summary>
        public bool VisorEffects { get; set; }

        /// <summary>Whether the visor HUD pipeline (HUD camera + composite) is running right now.</summary>
        public bool VisorPipeline { get; set; }

        /// <summary>Which way the visor's HUD texture lay at the last check: <c>normal</c>, <c>flipped</c> (the composite
        /// turns it the right way up), <c>unknown</c> (no check yet, or the API cannot read back asynchronously) or
        /// <c>off</c> (no visor pipeline).</summary>
        public string HudOrientation { get; set; } = "off";

        /// <summary>The render pipeline's render scale.</summary>
        public float RenderScale { get; set; } = 1f;

        /// <summary>MSAA samples of the render pipeline.</summary>
        public int Msaa { get; set; } = 1;

        /// <summary>The view camera's post anti-aliasing (None / FXAA / SMAA / TAA) — like HDR and the render scale it
        /// changes how many full-screen copies the frame goes through before the visor composite.</summary>
        public string Antialiasing { get; set; } = string.Empty;

        /// <summary>Whether the render pipeline renders in HDR.</summary>
        public bool Hdr { get; set; }

        /// <summary><c>Screen.width</c> / <c>Screen.height</c> — the size the engine believes the screen has.</summary>
        public int ScreenWidth { get; set; }

        public int ScreenHeight { get; set; }

        /// <summary>The part of the last screen capture the frame actually covered, as <c>W×H of W×H</c>; empty when
        /// nothing was cut. A mismatch here is the browser's back buffer disagreeing with <c>Screen</c>.</summary>
        public string LastCaptureCover { get; set; } = string.Empty;

        /// <summary>Adds the <c>graphics</c> node to a report's diagnostic snapshot.</summary>
        public void WriteTo(IDictionary<string, object> reportJson)
        {
            if (reportJson == null)
            {
                return;
            }

            var node = new Dictionary<string, object>
            {
                ["api"] = Api,
                ["uvStartsAtTop"] = UvStartsAtTop,
                ["preset"] = Preset,
                ["presetAuto"] = PresetAuto,
                ["visorEffects"] = VisorEffects,
                ["visorPipeline"] = VisorPipeline,
                ["hudOrientation"] = HudOrientation,
                ["renderScale"] = System.Math.Round(RenderScale, 3),
                ["msaa"] = Msaa,
                ["antialiasing"] = Antialiasing,
                ["hdr"] = Hdr,
                ["screen"] = ScreenWidth + "x" + ScreenHeight,
            };
            if (!string.IsNullOrEmpty(LastCaptureCover))
            {
                node["captureCover"] = LastCaptureCover;
            }

            reportJson["graphics"] = node;
        }
    }
}
