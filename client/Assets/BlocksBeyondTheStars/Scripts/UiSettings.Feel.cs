// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The settings rows of the feel pass: on the Display page the fullscreen resolution, the monitor and the pause
    /// when the window loses focus; on the Accessibility page the keys that switch instead of being held, the story
    /// text's size and backdrop, and the signal colours.
    /// </summary>
    public sealed partial class UiSettings
    {
        /// <summary>Display: resolution, monitor, focus pause — desktop only (the browser's page owns the canvas).</summary>
        private void FeelDisplayRows(ref float y)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return;
            }

            // The resolution applies in the two fullscreen modes; a window has the size the player drags it to.
            if (S.Window != WindowMode.Windowed)
            {
                var choices = ClientSettings.FullscreenChoices();
                Cycle(ref y, L("ui.settings.resolution"), ResolutionLabel(), dir =>
                {
                    StepResolution(choices, dir);
                    S.Apply();
                    Rebuild();
                }, L("ui.settings.resolution_info"));
            }

            var displays = new List<DisplayInfo>();
            Screen.GetDisplayLayout(displays);
            if (displays.Count > 1)
            {
                int current = Mathf.Clamp(S.Monitor, 0, displays.Count - 1);
                Cycle(ref y, L("ui.settings.monitor"), $"{current + 1} / {displays.Count}", dir =>
                {
                    S.Monitor = Wrap(current + dir, displays.Count);
                    S.ApplyMonitor();
                    Rebuild();
                });
            }

            Toggle(ref y, L("ui.settings.focus_pause"), S.PauseOnFocusLoss, () => { S.PauseOnFocusLoss = !S.PauseOnFocusLoss; Rebuild(); },
                L("ui.settings.focus_pause_info"));
        }

        /// <summary>Graphics: the 3D view's own resolution (desktop). 50–100 % in steps of ten; the interface stays sharp.</summary>
        private void FeelGraphicsRows(ref float y)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return;
            }

            float scale = Mathf.Clamp(S.RenderScale, ClientSettings.RenderScaleMin, 1f);
            Stepper(ref y, L("ui.settings.render_scale"), (scale - ClientSettings.RenderScaleMin) / (1f - ClientSettings.RenderScaleMin),
                () => SetRenderScale(scale - 0.1f), () => SetRenderScale(scale + 0.1f),
                Mathf.RoundToInt(scale * 100f) + " %", L("ui.settings.render_scale_info"));
        }

        private void SetRenderScale(float scale)
        {
            S.RenderScale = Mathf.Clamp(Mathf.Round(scale * 10f) / 10f, ClientSettings.RenderScaleMin, 1f);
            S.Apply();
            Rebuild();
        }

        private string ResolutionLabel()
            => S.FullscreenWidth <= 0 || S.FullscreenHeight <= 0
                ? L("ui.settings.resolution.native")
                : $"{S.FullscreenWidth} × {S.FullscreenHeight}";

        /// <summary>Steps through "the monitor's own" and the sizes it offers, largest first, wrapping.</summary>
        private void StepResolution(List<Vector2Int> choices, int dir)
        {
            // Index −1 is "the monitor's own"; the rest are the choices.
            int index = choices.FindIndex(c => c.x == S.FullscreenWidth && c.y == S.FullscreenHeight);
            index = Wrap(index + 1 + dir, choices.Count + 1) - 1;
            S.FullscreenWidth = index < 0 ? 0 : choices[index].x;
            S.FullscreenHeight = index < 0 ? 0 : choices[index].y;
        }

        /// <summary>Accessibility: keys that switch instead of being held, the story text, the signal colours.</summary>
        private void FeelAccessibilityRows(ref float y)
        {
            Toggle(ref y, L("ui.settings.caption_backdrop"), S.CaptionBackdrop, () => { S.CaptionBackdrop = !S.CaptionBackdrop; S.Apply(); Rebuild(); },
                L("ui.settings.caption_backdrop_info"));
            int size = Mathf.Max(0, System.Array.IndexOf(ClientSettings.CaptionScales, S.CaptionScale));
            Cycle(ref y, L("ui.settings.caption_size"), Mathf.RoundToInt(ClientSettings.CaptionScales[size] * 100f) + " %", dir =>
            {
                S.CaptionScale = ClientSettings.CaptionScales[Wrap(size + dir, ClientSettings.CaptionScales.Length)];
                S.Apply();
                Rebuild();
            });

            Kicker(ref y, L("ui.settings.group.colours"));
            Cycle(ref y, L("ui.settings.colour_vision"), L(ColourVisionKey(S.ColourVision)), dir =>
            {
                S.ColourVision = (ColourVision)Wrap((int)S.ColourVision + dir, 3);
                S.Apply();
                Rebuild();
            }, L("ui.settings.colour_vision_info"));

            Kicker(ref y, L("ui.settings.group.keys"));
            Toggle(ref y, L("ui.settings.sprint_toggle"), S.SprintToggle, () => { S.SprintToggle = !S.SprintToggle; S.Apply(); Rebuild(); },
                L("ui.settings.sprint_toggle_info"));
            Toggle(ref y, L("ui.settings.crouch_toggle"), S.CrouchToggle, () => { S.CrouchToggle = !S.CrouchToggle; S.Apply(); Rebuild(); },
                L("ui.settings.crouch_toggle_info"));
        }

        /// <summary>Controller: which names the pad's buttons get. "Automatic" follows the connected pad's own name and
        /// shows what it found; the three fixed sets are for a pad whose name says nothing.</summary>
        private void PadGlyphRow(ref float y)
        {
            // The cycle: Automatic, then the three sets.
            int at = S.PadGlyphsAuto ? 0 : 1 + (int)S.PadGlyphs;
            string set = L("ui.settings.pad_glyphs." + InputMap.PadGlyphs.ToString().ToLowerInvariant());
            string label = S.PadGlyphsAuto ? L("ui.settings.pad_glyphs.auto").Replace("{set}", set) : set;
            Cycle(ref y, L("ui.settings.pad_glyphs"), label, dir =>
            {
                int next = Wrap(at + dir, 4);
                S.PadGlyphsAuto = next == 0;
                if (next > 0)
                {
                    S.PadGlyphs = (PadGlyphSet)(next - 1);
                }

                Rebuild();
            });
        }

        private static string ColourVisionKey(ColourVision mode) => mode switch
        {
            ColourVision.RedGreen => "ui.settings.colour_vision.red_green",
            ColourVision.BlueYellow => "ui.settings.colour_vision.blue_yellow",
            _ => "ui.settings.colour_vision.standard",
        };

        /// <summary>The feel pass's share of "reset this page".</summary>
        private void ResetFeel(ClientSettings d, bool display)
        {
            if (display)
            {
                S.FullscreenWidth = d.FullscreenWidth;
                S.FullscreenHeight = d.FullscreenHeight;
                S.Monitor = d.Monitor;
                S.PauseOnFocusLoss = d.PauseOnFocusLoss;
                return;
            }

            S.SprintToggle = d.SprintToggle;
            S.CrouchToggle = d.CrouchToggle;
            S.CaptionScale = d.CaptionScale;
            S.CaptionBackdrop = d.CaptionBackdrop;
            S.ColourVision = d.ColourVision;
        }
    }
}
