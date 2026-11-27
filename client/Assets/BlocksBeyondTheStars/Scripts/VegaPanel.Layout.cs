// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where VEGA's speech panel and folded tab sit this frame (#3668, the comms zone). With a mouse, keyboard or pad they
    /// stay at the bottom of the left column (<see cref="SpeechX"/>, <see cref="SpeechY"/>). On a touch device that corner
    /// belongs to the touch layer — the move stick, the RUN switch on foot, UP and DOWN in flight, the ACT and ◄ buttons —
    /// and to the gear strip, whose canvas lies above the touch layer (a tap on the continue pill under it toggled the lamp).
    /// Where those are depends on the screen's aspect and the UI scale, so the panel takes the free spot nearest its
    /// desktop one, measured every frame against the live rectangles (<see cref="TouchCommsLayout.SpeechSpot"/>): at 16:9
    /// and UI scale 1 that is x 464 (right of the chat lane above the stick), bottom 662 (above the gear strip) on foot. The
    /// folded tab stays on the panel's last row, and the touch layer's NEXT button sits beside the panel
    /// (<see cref="TouchNextSpot"/>).
    /// </summary>
    public sealed partial class VegaPanel
    {
        /// <summary>NEXT's side in touch units (the touch layer's table).</summary>
        private static readonly float NextTouchSize = TouchLayout.Of(TouchControl.VegaNext).Size;

        private Vector2 _speechAt = new Vector2(SpeechX, SpeechY);
        private RectTransform _speechRect;
        private readonly List<HudRect> _touchObstacles = new List<HudRect>(40);

        /// <summary>The speech panel's current top-left corner in HUD units.</summary>
        public Vector2 SpeechAt => _speechAt;

        /// <summary>Where the touch layer's NEXT button goes this frame, in HUD units: beside the speech panel, or beside the
        /// folded tab. Zero-sized without a touch layer.</summary>
        public HudRect TouchNextSpot { get; private set; }

        /// <summary>The folded tab's row: the speech panel's last row (<see cref="ChipY"/> at the desktop spot).</summary>
        private float TabY => _speechAt.y + SpeechH - TabH;

        private void PlaceSpeech()
        {
            if (_speech == null)
            {
                return;
            }

            var touch = HudZones.Touch;
            var at = touch.Any ? TouchSpeechSpot(touch) : new Vector2(SpeechX, SpeechY);
            if (at != _speechAt)
            {
                _speechAt = at;
                _speechRect ??= (RectTransform)_speech.transform;
                _speechRect.anchoredPosition = new Vector2(at.x, -at.y);
                if (_tabRect != null)
                {
                    _tabRect.anchoredPosition = new Vector2(at.x, -TabY);
                }
            }

            TouchNextSpot = touch.Any
                ? TouchCommsLayout.NextSpot(NextAnchor(), NextTouchSize * touch.HudPerTouch, _touchObstacles, touch.CanvasW, touch.CanvasH)
                : default;
        }

        /// <summary>
        /// The panel's touch spot: clear of every touch control of the context, of the HUD's bottom band (gear strip, melody bar,
        /// quick-bar), in flight of the instruments strip, and — where the canvas has room for it, which only a large UI scale on
        /// a 16:9 screen has not — of the chat lane above the desktop spot (x 16…456 from y 272; the chat draws above VEGA);
        /// never above the comms zone's top. The obstacle list is kept for NEXT's spot.
        /// </summary>
        private Vector2 TouchSpeechSpot(TouchReservation touch)
        {
            _touchObstacles.Clear();
            for (int i = 0; i < touch.Controls.Count; i++)
            {
                _touchObstacles.Add(touch.Controls[i]);
            }

            Game.Views.Hud?.CollectBottomBand(_touchObstacles);
            if (Game.SpaceViewActive)
            {
                _touchObstacles.Add(HudZones.FlightStrip(touch.CanvasW, touch.CanvasH));
            }

            // Last, as the one soft obstacle: dropped when nothing else fits (a small canvas at a large UI scale).
            _touchObstacles.Add(new HudRect(ChatWindowLayout.CommsX, ChatWindowLayout.CommsTop, ChatWindowLayout.CommsW,
                SpeechY - ChatWindowLayout.CommsTop));
            var spot = TouchCommsLayout.SpeechSpot(SpeechW, SpeechH, SpeechX, SpeechY, ChatWindowLayout.CommsTop, _touchObstacles, 1,
                touch.CanvasW, touch.CanvasH);
            return new Vector2(spot.X, spot.Y);
        }

        /// <summary>What NEXT sits beside: the folded tab while VEGA is folded, else the speech panel.</summary>
        private HudRect NextAnchor()
            => _tab != null && _tab.activeSelf && _tabRect != null
                ? new HudRect(_speechAt.x, TabY, _tabRect.sizeDelta.x, TabH)
                : new HudRect(_speechAt.x, _speechAt.y, SpeechW, SpeechH);

        /// <summary>
        /// #3671: the console (Tab menu) is opaque now, so VEGA's canvas — normally just above the HUD — would vanish behind
        /// it. A line she says while the console is open (a lesson about that very screen) rises above it instead, opaque and
        /// readable; the objective chip and the folded tab step aside (<see cref="YieldChipRowToMelodyBar"/>). The bio lab
        /// likewise: three of her lines are about the lab's own tabs while it is open (its client lesson
        /// <c>vega.hint.bio_lab_tabs</c>, the first-analysis and no-change hints), and its opaque 1500 × 900 window at order
        /// 59 would hide them. Trade, a dialogue (laid out clear of her panel) and the taming panel keep VEGA under them.
        /// <para>#4071: the console's Codex and DataQubes sub-screens draw on their own canvases at order 55, above the console;
        /// with VEGA just above the console (52) her line shone through their near-opaque backdrop as an unreadable ghost. So
        /// "above the console" means above those two as well — and still under what opens over the console: the appearance
        /// editor (59), the texture editor and trade (60) and the console's own dialogs (61).</para>
        /// <para>The sorting order alone does not do it under the visor effect: there VEGA's canvas, like every diegetic
        /// canvas, renders through the visor's HUD camera into its render texture, which the visor composite puts under every
        /// screen-space-overlay canvas — the console's included, whatever the order. So while the console is open her canvas
        /// is a plain overlay; afterwards it goes back the way UiKit routes a diegetic canvas (through
        /// <see cref="UiKit.HudCamera"/> while the visor pipeline is up, else an overlay). The pipeline's own runtime
        /// re-targeting (UiKit.RetargetDiegeticCanvases) applies the same rule, and this check runs every frame, so the two
        /// never disagree for longer than a frame.</para>
        /// </summary>
        private void RiseAboveMenu()
        {
            bool lab = BioLabOpen;
            bool above = Game.ConsoleOpen || lab;
            int order = lab ? AboveLabSortingOrder : above ? AboveMenuSortingOrder : HudSortingOrder;
            if (_canvas.sortingOrder != order)
            {
                _canvas.sortingOrder = order;
            }

            var cam = above || UiKit.HudLayer < 0 ? null : UiKit.HudCamera;
            var mode = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            if (_canvas.renderMode != mode || _canvas.worldCamera != cam)
            {
                _canvas.renderMode = mode;
                _canvas.worldCamera = cam;
                if (cam != null)
                {
                    _canvas.planeDistance = 1f; // as UiKit's diegetic routing sets it
                }
            }
        }

        /// <summary>VEGA's canvas order: just above the HUD (10); above the console (50) and its Codex / DataQubes screens (55,
        /// #4071) or the bio lab (59) while one is open.</summary>
        private const int HudSortingOrder = 11, AboveMenuSortingOrder = 56, AboveLabSortingOrder = 61;

        /// <summary>True while the bio lab's window is open (a lookup — never builds the panel).</summary>
        private bool BioLabOpen => Game?.Context.Panel<BioLabUi>()?.IsOpen ?? false;
    }
}
