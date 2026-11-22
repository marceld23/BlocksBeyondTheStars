// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The editors' shared "as in the world" preview. Every editor preview renders with its own camera
    /// into a render texture; without this, a preview inside a world took the world's look globals as they stood (the
    /// look's sun and rim light on the models), and in the main menu the plain studio light — the same work looked
    /// different depending on where the editor was opened. <see cref="Attach"/> gives a preview camera a
    /// <see cref="LookPreviewLight"/>, which sets ONE fixed look light right before that camera draws and puts the
    /// world's values back right after it: a warm sun from a fixed direction, the sky's light from above, the ground's
    /// from below, the rim light, and the look's calm, soft edges and grain for the blocks.
    /// <para>
    /// The player switches each preview between "as in the world" and "pixels only" (the plain studio preview) with
    /// <see cref="AddToggle"/>; the choice is one for all editors and lasts the session.
    /// </para>
    /// </summary>
    internal static class LookPreview
    {
        /// <summary>The fixed sun of every preview (world direction TO the sun): above and in front of the cameras, which
        /// all look from the −Z / +X side, a little from the right — the lit faces are the ones the player sees.</summary>
        public static readonly Vector3 SunDirection = new Vector3(0.42f, 0.72f, -0.55f).normalized;

        /// <summary>A clear late-morning sun (sRGB): warm, not yet golden.</summary>
        public static readonly Color SunTint = new Color(1f, 0.94f, 0.84f, 1f);

        /// <summary>The sky it stands under (sRGB): its hue goes into the light from above and the reflections.</summary>
        public static readonly Color SkyTint = new Color(0.52f, 0.68f, 0.93f, 1f);

        private static bool _asInWorld = true;

        /// <summary>True while previews show the look's light (and, where it applies, the block shader).</summary>
        public static bool AsInWorld => _asInWorld;

        /// <summary>Raised when the player switches between "as in the world" and "pixels only".</summary>
        public static event Action Changed;

        public static void Toggle()
        {
            _asInWorld = !_asInWorld;
            Changed?.Invoke();
        }

        /// <summary>Gives <paramref name="camera"/> the preview light; null (and nothing done) without a camera.</summary>
        public static LookPreviewLight Attach(Camera camera)
        {
            if (camera == null)
            {
                return null;
            }

            var light = camera.GetComponent<LookPreviewLight>();
            if (light == null)
            {
                light = camera.gameObject.AddComponent<LookPreviewLight>();
            }

            return light;
        }

        /// <summary>
        /// The switch over the bottom edge of a preview picture whose rect on <paramref name="parent"/> is
        /// (<paramref name="x"/>, <paramref name="y"/>, <paramref name="w"/>, <paramref name="h"/>): it names the current
        /// mode ("As in the world" / "Pixels only") and flips it. Null without a parent. Call it after the picture is
        /// placed, so the button draws (and takes clicks) above it.
        /// </summary>
        public static Button AddToggle(Transform parent, float x, float y, float w, float h, Func<string, string> localize)
        {
            if (parent == null || localize == null)
            {
                return null;
            }

            float bw = Mathf.Min(w - 12f, 220f);
            Button button = null;
            button = UiKit.AddTierButton(parent, x + ((w - bw) * 0.5f), y + h - 42f, bw, 36f, Label(localize), () =>
            {
                Toggle();
                Relabel(button, localize);
            }, AsInWorld ? ButtonTier.Secondary : ButtonTier.Quiet);
            UiKit.FitButtonLabel(button.GetComponentInChildren<Text>(true));
            Action follow = null;
            follow = () =>
            {
                if (button == null)
                {
                    Changed -= follow; // the editor closed: its button is gone
                    return;
                }

                Relabel(button, localize);
            };
            Changed += follow;
            return button;
        }

        private static string Label(Func<string, string> localize)
            => localize(AsInWorld ? "ui.tex.look_world" : "ui.tex.look_pixels");

        private static void Relabel(Button button, Func<string, string> localize)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = Label(localize);
                UiKit.FitButtonLabel(text);
            }

            UiKit.StyleTier(button, AsInWorld ? ButtonTier.Secondary : ButtonTier.Quiet);
        }
    }
}
