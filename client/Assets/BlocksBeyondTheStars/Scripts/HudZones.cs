// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The HUD's zone plan (#3668), in the HUD reference space (<see cref="UiKit.HudRefW"/> × <see cref="UiKit.HudRefH"/>,
    /// top-left origin). Every HUD element belongs to one zone; the centre stays free for the crosshair, the interaction
    /// prompt and world labels.
    /// <list type="bullet">
    /// <item><b>A</b> status, top left: location and vitals (on board also hull, shield and cargo — the same bars).</item>
    /// <item><b>B</b> one toast lane, top centre.</item>
    /// <item><b>C</b> the map, top right: the compass and the clock on foot, the radar in space.</item>
    /// <item><b>D</b> objectives, right, under C.</item>
    /// <item><b>E</b> the context card, right, under D: a scan result, the planet card, a wreck to repair.</item>
    /// <item><b>F</b> comms, bottom left: VEGA's speech panel at the bottom, the chat above it.</item>
    /// <item><b>H</b> the hotbar (and in space the flight strip and the systems bar), bottom centre.</item>
    /// <item><b>G</b> at most four context keys, bottom right.</item>
    /// </list>
    /// The right column is a STACK: its producers (the clock and playtime on foot, the radar in space) write
    /// <see cref="RightStackBottom"/> every frame, VEGA's objective chip stacks under it and writes
    /// <see cref="ObjectivesBottom"/>, and a context card starts at <see cref="ContextTop"/>. Elements know their zone, not
    /// fixed pixels — so a card can no longer slide under the playtime tile (#3670: the planet card did) or a caption under
    /// the clock (#3217). Each value is at most one frame old, which a stack that changes with what is shown can afford.
    /// <para>On a touch device the touch layer's buttons take parts of the screen (<see cref="Touch"/>): the map part starts
    /// under the row along the top (<see cref="MapShift"/>), the context cards stop above the action buttons
    /// (<see cref="StackLimit"/>; a second card column left of the first takes a card the first has no room for), and VEGA's
    /// speech panel takes a free spot right of the move stick (VegaPanel.Layout.cs).</para>
    /// </summary>
    public static class HudZones
    {
        /// <summary>The gap between two stacked elements.</summary>
        public const float Gap = 8f;

        /// <summary>The right edge of everything in the right column.</summary>
        public const float RightEdge = UiKit.HudRefW - 10f;

        /// <summary>Width of the objectives and the context cards in the right column.</summary>
        public const float RightW = 330f;

        /// <summary>Left edge of the objectives and context cards.</summary>
        public const float RightX = RightEdge - RightW;

        /// <summary>The comms zone's column: VEGA's speech panel and the chat (ChatWindowLayout.CommsX/CommsW mirror it). It
        /// ends left of the melody bar (x 458…), which shares its height above the hotbar.</summary>
        public const float CommsX = 16f, CommsW = 440f;

        /// <summary>The radar's face in space (top right, where the compass is on foot) and the two text lines under it.</summary>
        public const float RadarSize = 160f, RadarTop = 10f;

        /// <summary>The touch layer's controls this frame (HUD units; <see cref="TouchReservation.None"/> without touch). The
        /// touch layer publishes them (<see cref="SetTouch"/>) before the HUD lays out.</summary>
        public static TouchReservation Touch { get; private set; } = TouchReservation.None;

        /// <summary>How far the map part of the right column (compass and clock on foot, radar in space — and so everything
        /// stacked under it) moves down on a touch device: under the touch row along the top where it lies over the column
        /// (<see cref="TouchHudLayout.MapShift"/>). 0 on desktop and pad.</summary>
        public static float MapShift { get; private set; }

        /// <summary>Where the map part starts this frame.</summary>
        public static float MapTop => RadarTop + MapShift;

        /// <summary>The lowest y the radar and its station and waypoint lines reach.</summary>
        public static float SpaceRadarBottom => MapTop + RadarSize + 52f;

        /// <summary>Publishes the touch layer's controls (TouchControlsUi, every time the context, the canvas or the UI scale
        /// changes; <see cref="TouchReservation.None"/> when the layer goes).</summary>
        public static void SetTouch(TouchReservation touch)
        {
            Touch = touch ?? TouchReservation.None;
            MapShift = TouchHudLayout.MapShift(Touch);
        }

        /// <summary>How far down a stack of cards over the columns <paramref name="x0"/> … <paramref name="x1"/> may run: above
        /// the touch layer's action buttons with a thumb's margin; +∞ on desktop and pad.</summary>
        public static float StackLimit(float x0, float x1) => Touch.StackLimit(x0, x1, MapTop);

        /// <summary>The flight view's strip above the systems bar (SpaceView.Hud.cs: the instruments, 540 wide around the screen
        /// centre, from 182 above the bottom edge, and the systems bar under them) on a canvas of <paramref name="canvasW"/> ×
        /// <paramref name="canvasH"/> HUD units — VEGA's touch spot keeps above it.</summary>
        public static HudRect FlightStrip(float canvasW, float canvasH) => new HudRect((canvasW / 2f) - 270f, canvasH - 182f, 540f, 182f);

        /// <summary>Where the right column's map part (clock and playtime on foot, radar in space) ends this frame.</summary>
        public static float RightStackBottom = HudRightColumn.ClockY + HudRightColumn.ClockH;

        /// <summary>Where VEGA's objective chip and its extra rows end this frame (0 while none shows).</summary>
        public static float ObjectivesBottom;

        /// <summary>Where VEGA's objective chip goes: under the map part of the right column.</summary>
        public static float ObjectivesTop => RightStackBottom + Gap;

        /// <summary>Where a context card (scan result, planet card, wreck to repair) starts: under the objectives, or under
        /// the map part while no objective shows.</summary>
        public static float ContextTop => Mathf.Max(RightStackBottom, ObjectivesBottom) + Gap;

        private static float _hudCardsBottom;
        private static int _hudCardsFrame = -10;

        /// <summary>The HUD's own context cards (scan, wreck, ship repair) stack from <see cref="ContextTop"/> down; the HUD
        /// writes where the stack ended this frame (<paramref name="bottom"/> already includes the gap).</summary>
        public static void SetHudCardsBottom(float bottom)
        {
            _hudCardsBottom = bottom;
            _hudCardsFrame = Time.frameCount;
        }

        private static int _planetCardFrame = -10;

        /// <summary>The planet card says it is on screen this frame (PlanetOverviewCard).</summary>
        public static void MarkPlanetCard() => _planetCardFrame = Time.frameCount;

        /// <summary>Whether the planet card showed lately: on a touch device it takes the first card column while it shows, and
        /// the HUD's own cards (the ship-repair card in flight) step into the second — the two never lie on each other.</summary>
        public static bool PlanetCardUp => Time.frameCount - _planetCardFrame <= 2;

        /// <summary>Where a card from outside the HUD (the planet card) starts: under the HUD's context cards — the ship repair
        /// card shows in flight too — or at <see cref="ContextTop"/> when the HUD has not stacked any lately.</summary>
        public static float FreeContextTop => Time.frameCount - _hudCardsFrame <= 2 ? Mathf.Max(ContextTop, _hudCardsBottom) : ContextTop;
    }
}
