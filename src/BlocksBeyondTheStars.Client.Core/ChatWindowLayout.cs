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
    /// Where the opened chat window sits (#3264, #3669). The HUD's comms zone (#3668) is the bottom-left column under the
    /// status panel: VEGA's speech panel sits at its bottom and the chat lives above it — the small lane while the chat is
    /// closed, this window while the player types. It used to be a centred 768 × 520 pane that jumped to the right whenever
    /// VEGA spoke, so the same window appeared in two places and an empty chat was a large glass pane over the game view.
    /// In the HUD's reference frame (1536 × 864, top-left anchored like every HUD element):
    /// <list type="bullet">
    /// <item>Normally the window docks in the comms zone: x <see cref="CommsX"/>, <see cref="CommsW"/> wide, from
    /// <see cref="CommsTop"/> (under the status panel) down to <see cref="MaxBottom"/> (above VEGA's folded tab) — or, while
    /// VEGA speaks, down to her speech panel's top minus a gap. Nothing moves sideways any more.</item>
    /// <item>The gamepad's on-screen keyboard (its own 1920 × 1080 canvas) fills the lower screen: the window docks to the
    /// top, centred, above the keyboard's panel.</item>
    /// <item>A touch device's system keyboard covers the lower half: the window stays centred in the upper half.</item>
    /// </list>
    /// Both keyboard layouts still slide clear of VEGA's speech panel should they ever reach it. Everything is clamped to
    /// the live canvas (its size in HUD units follows the aspect ratio and the UI-scale setting). Unity-free so the headless
    /// suite pins every rule.
    /// </summary>
    public static class ChatWindowLayout
    {
        /// <summary>The HUD's reference frame (UiKit.HudRefW × HudRefH); its elements are anchored top-left in it.</summary>
        public const float HudRefW = 1536f, HudRefH = 864f;

        /// <summary>The centred window of the keyboard layouts: its size and top with nothing in the way.</summary>
        public const float DefaultW = 768f, DefaultH = 520f, DefaultTop = 64f;

        /// <summary>The comms zone's column (#3668): the status panel's left edge and VEGA's speech panel's width.</summary>
        public const float CommsX = 16f, CommsW = 440f;

        /// <summary>The comms zone's top: under the status panel (vitals end at y 260) with a gap. A taller status panel (an
        /// exposure meter, running effects) passes its own top to <see cref="Resolve"/>.</summary>
        public const float CommsTop = 272f;

        /// <summary>The least height the docked window keeps when a tall status panel pushes its top down.</summary>
        public const float MinDockH = 160f;

        /// <summary>The lowest the window may reach: VEGA's folded tab row starts at y 696 (VegaPanel.ChipY).</summary>
        public const float MaxBottom = 688f;

        /// <summary>The distance kept from the canvas edges.</summary>
        public const float EdgeMargin = 16f;

        /// <summary>The window's top while the on-screen keyboard is up (it docks to the very top).</summary>
        public const float DockTop = 12f;

        /// <summary>The gap kept to a neighbour the window yields to.</summary>
        public const float Gap = 8f;

        /// <summary>VEGA's speech panel (VegaPanel: SpeechX, SpeechW, SpeechY, SpeechH) — an EditMode test pins these against
        /// the panel's own constants.</summary>
        public const float VegaSpeechX = 16f, VegaSpeechW = 440f, VegaSpeechY = 546f, VegaSpeechH = 190f;

        /// <summary>Where a keyboard layout's window starts when it has to slide clear of VEGA: her right edge plus a gap.</summary>
        public const float VegaClearX = 472f;

        /// <summary>The HUD's right column (objectives, context cards: HudZones.RightX) starts at x 1196.</summary>
        public const float RightColumnX = 1196f;

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
        /// while <paramref name="osk"/>); <paramref name="commsTop"/> is where the docked window may start — under the status
        /// panel, which grows with an exposure meter or running effects. See the class summary for the rules.
        /// </summary>
        public static ChatRect Resolve(bool vegaSpeech, bool osk, bool touchKeyboard, float canvasW, float canvasH, float oskPanelTop,
            float commsTop = CommsTop)
        {
            if (osk || touchKeyboard)
            {
                return ResolveAboveKeyboard(vegaSpeech, osk, touchKeyboard, canvasW, canvasH, oskPanelTop);
            }

            float w = Math.Max(0f, Math.Min(CommsW, canvasW - (2f * EdgeMargin)));
            float bottom = Math.Min(vegaSpeech ? VegaSpeechY - Gap : MaxBottom, canvasH - EdgeMargin);
            float startTop = Math.Min(Math.Max(CommsTop, commsTop), bottom - MinDockH); // a very tall status panel: overlap a little
            float top = Math.Max(Math.Min(startTop, bottom), bottom - DefaultH);
            return new ChatRect(CommsX, top, w, Math.Max(0f, bottom - top));
        }

        /// <summary>The centred window of the two keyboard layouts (#3264): docked to the top above the on-screen keyboard,
        /// or kept in the upper half above a touch keyboard; it slides right of VEGA's speech panel should it reach it.</summary>
        private static ChatRect ResolveAboveKeyboard(bool vegaSpeech, bool osk, bool touchKeyboard, float canvasW, float canvasH, float oskPanelTop)
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
