// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A rectangle in HUD reference units: top-left origin, y grows downwards (the uGUI <c>UiKit.Place</c> frame).</summary>
    public readonly struct ChatRect
    {
        public ChatRect(float x, float y, float w, float h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public float X { get; }

        public float Y { get; }

        public float W { get; }

        public float H { get; }

        public float Right => X + W;

        public float Bottom => Y + H;

        /// <summary>True when the two rectangles share any area (touching edges do not count).</summary>
        public bool Overlaps(ChatRect other)
            => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    }

    /// <summary>
    /// Where the big chat window sits (#3264). The opened chat is a centred, half-transparent window in the HUD's
    /// reference frame (1536 × 864, top-left anchored like every HUD element): x 384…1152, y 64…584 by default — twice
    /// the width of the old left lane and ~1.7× its height. It is placed around its neighbours, never over them:
    /// <list type="bullet">
    /// <item>VEGA's speech panel (x 24…664, y 396…586) draws UNDER the chat canvas and is the story-critical one, so while
    /// a line is up the window slides right to start at x ≥ <see cref="VegaClearX"/> (and shrinks to stay clear of the
    /// HUD's right column); where that leaves less than <see cref="MinSlideW"/> (a big UI scale) it ends above the panel
    /// instead.</item>
    /// <item>The bottom never passes <see cref="MaxBottom"/>, the top of VEGA's objective chip (y 594) minus a gap.</item>
    /// <item>The gamepad's on-screen keyboard (its own 1920 × 1080 canvas) takes the middle of the screen: the window
    /// docks to the top, above the keyboard's panel.</item>
    /// <item>A touch device's system keyboard covers the lower half: the window stays in the upper half.</item>
    /// </list>
    /// Everything is clamped to the live canvas (its size in HUD units follows the aspect ratio and the UI-scale
    /// setting). Unity-free so the headless suite pins every rule.
    /// </summary>
    public static class ChatWindowLayout
    {
        /// <summary>The HUD's reference frame (UiKit.HudRefW × HudRefH); its elements are anchored top-left in it.</summary>
        public const float HudRefW = 1536f, HudRefH = 864f;

        /// <summary>The window's size and top with nothing in the way.</summary>
        public const float DefaultW = 768f, DefaultH = 520f, DefaultTop = 64f;

        /// <summary>The lowest the window may reach: VEGA's objective chip starts at y 594.</summary>
        public const float MaxBottom = 588f;

        /// <summary>The distance kept from the canvas edges.</summary>
        public const float EdgeMargin = 16f;

        /// <summary>The window's top while the on-screen keyboard is up (it docks to the very top).</summary>
        public const float DockTop = 12f;

        /// <summary>The gap kept to a neighbour the window yields to.</summary>
        public const float Gap = 8f;

        /// <summary>VEGA's speech panel (VegaPanel: x 24, width 640, y SpeechY, height SpeechH) — an EditMode test pins
        /// these against the panel's own constants.</summary>
        public const float VegaSpeechX = 24f, VegaSpeechW = 640f, VegaSpeechY = 396f, VegaSpeechH = 190f;

        /// <summary>Where the window starts while VEGA speaks: the panel's right edge (664) plus a gap.</summary>
        public const float VegaClearX = 680f;

        /// <summary>The HUD's right column (compass, clock, session) starts at x 1326 (HudUi: W − 210).</summary>
        public const float RightColumnX = 1326f;

        /// <summary>A slid window narrower than this is no better than the lane: the window rises above VEGA instead.</summary>
        public const float MinSlideW = 480f;

        /// <summary>The on-screen keyboard's own reference canvas (OnScreenKeyboardUi: CreateCanvas, 1920 × 1080).</summary>
        public const float OskRefW = 1920f, OskRefH = 1080f;

        /// <summary>VEGA's speech panel as a rectangle.</summary>
        public static ChatRect VegaSpeech => new ChatRect(VegaSpeechX, VegaSpeechY, VegaSpeechW, VegaSpeechH);

        /// <summary>
        /// The on-screen keyboard's top edge in HUD units. Both canvases scale with <c>ScreenMatchMode.Expand</c> and the
        /// keyboard's panel is anchored top-left at <paramref name="oskPanelTop"/> of its 1920 × 1080 reference, so one
        /// keyboard unit is <c>min(canvasW / 1920, canvasH / 1080)</c> HUD units — whatever the aspect and UI scale.
        /// </summary>
        public static float OskTopInHud(float oskPanelTop, float canvasW, float canvasH)
            => oskPanelTop * Math.Min(canvasW / OskRefW, canvasH / OskRefH);

        /// <summary>
        /// The window's rectangle this frame. <paramref name="canvasW"/> × <paramref name="canvasH"/> is the live chat
        /// canvas in HUD units; <paramref name="oskPanelTop"/> is the keyboard panel's top in its own reference (read only
        /// while <paramref name="osk"/>). See the class summary for the rules.
        /// </summary>
        public static ChatRect Resolve(bool vegaSpeech, bool osk, bool touchKeyboard, float canvasW, float canvasH, float oskPanelTop)
        {
            float w = Math.Max(0f, Math.Min(DefaultW, canvasW - (2f * EdgeMargin)));
            float frameW = Math.Min(canvasW, HudRefW); // the HUD's top-left frame — its columns do not follow a wider screen
            float x = Math.Max(EdgeMargin, (frameW - w) / 2f);
            float top = osk ? DockTop : DefaultTop;
            float bottom = Math.Min(Math.Min(DefaultTop + DefaultH, MaxBottom), canvasH - EdgeMargin);
            if (osk)
            {
                bottom = Math.Min(bottom, OskTopInHud(oskPanelTop, canvasW, canvasH) - Gap);
            }

            if (touchKeyboard)
            {
                bottom = Math.Min(bottom, (canvasH / 2f) - Gap);
            }

            if (vegaSpeech && new ChatRect(x, top, w, Math.Max(0f, bottom - top)).Overlaps(VegaSpeech))
            {
                float rightLimit = Math.Min(canvasW - EdgeMargin, RightColumnX - EdgeMargin);
                float slideX = Math.Max(x, VegaClearX);
                float slideW = Math.Min(w, rightLimit - slideX);
                if (slideW >= MinSlideW)
                {
                    x = slideX;
                    w = slideW;
                }
                else
                {
                    bottom = Math.Min(bottom, VegaSpeechY - Gap);
                }
            }

            return new ChatRect(x, top, w, Math.Max(0f, bottom - top));
        }
    }
}
