// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The screen the touch controls take this frame, in HUD units (<see cref="HudRect"/>): every control of the current
    /// context — the shared ones and its cluster, whether or not a contextual button (ROTATE, FEED, ACT …) shows right now, so
    /// the HUD does not hop each time one comes and goes. VEGA's NEXT is left out: it follows her panel. The HUD asks it where
    /// its zones may go: the right column starts under the touch row at the top (<see cref="TopRowBottom"/>) and ends above
    /// the action buttons (<see cref="StackLimit"/>). <see cref="None"/> (no touch layer) answers "nothing reserved", so the
    /// desktop and pad layout is the plain one.
    /// </summary>
    public sealed class TouchReservation
    {
        /// <summary>A control whose top edge is above this belongs to the row along the top of the screen.</summary>
        public const float TopBand = 200f;

        /// <summary>The room left between a HUD card and a thumb button under it.</summary>
        public const float ThumbMargin = 16f;

        /// <summary>No touch layer: nothing is reserved.</summary>
        public static readonly TouchReservation None = new TouchReservation(Array.Empty<HudRect>(), 0f, 0f, 0f);

        public TouchReservation(IReadOnlyList<HudRect> controls, float canvasW, float canvasH, float hudPerTouch)
        {
            Controls = controls;
            CanvasW = canvasW;
            CanvasH = canvasH;
            HudPerTouch = hudPerTouch;
        }

        /// <summary>The reserved controls in HUD units.</summary>
        public IReadOnlyList<HudRect> Controls { get; }

        /// <summary>The HUD canvas's live size in HUD units (it grows with the aspect and shrinks with the UI scale).</summary>
        public float CanvasW { get; }

        public float CanvasH { get; }

        /// <summary>HUD units per touch unit (<see cref="TouchLayout.HudPerTouch"/>) — for placing a touch control at a HUD spot.</summary>
        public float HudPerTouch { get; }

        /// <summary>Whether a touch layer reserves anything.</summary>
        public bool Any => Controls.Count > 0;

        /// <summary>
        /// The reservation of one control context on a touch canvas of <paramref name="touchW"/> × <paramref name="touchH"/>
        /// touch units (the live canvas) at UI scale <paramref name="uiScale"/>: the shared controls plus the cluster's.
        /// </summary>
        public static TouchReservation For(TouchCluster cluster, float touchW, float touchH, float uiScale)
        {
            float k = TouchLayout.HudPerTouch(uiScale);
            var rects = new List<HudRect>();
            foreach (var spec in TouchLayout.All)
            {
                if (spec.Control != TouchControl.VegaNext && (spec.Cluster == TouchCluster.Shared || spec.Cluster == cluster))
                {
                    rects.Add(TouchLayout.RectOnCanvas(spec, touchW, touchH).Scaled(k));
                }
            }

            return new TouchReservation(rects, touchW * k, touchH * k, k);
        }

        /// <summary>For a screen of <paramref name="screenW"/> × <paramref name="screenH"/> pixels — the headless tests' way in.</summary>
        public static TouchReservation ForScreen(TouchCluster cluster, float screenW, float screenH, float uiScale)
        {
            var (w, h) = TouchLayout.ExpandCanvas(screenW, screenH, TouchLayout.RefW, TouchLayout.RefH);
            return For(cluster, w, h, uiScale);
        }

        /// <summary>The lowest bottom edge of a top-row control over the columns <paramref name="x0"/> … <paramref name="x1"/>
        /// (0 when none lies there — a wide screen moves the right-anchored buttons off the HUD's 1536 frame).</summary>
        public float TopRowBottom(float x0, float x1)
        {
            float bottom = 0f;
            for (int i = 0; i < Controls.Count; i++)
            {
                var r = Controls[i];
                if (r.Y < TopBand && r.OverlapsX(x0, x1))
                {
                    bottom = Math.Max(bottom, r.Bottom);
                }
            }

            return bottom;
        }

        /// <summary>The highest top edge of a control at or below <paramref name="fromY"/> over the columns
        /// <paramref name="x0"/> … <paramref name="x1"/>; +∞ when none lies there.</summary>
        public float FloorBelow(float x0, float x1, float fromY)
        {
            float floor = float.PositiveInfinity;
            for (int i = 0; i < Controls.Count; i++)
            {
                var r = Controls[i];
                if (r.Y >= fromY && r.OverlapsX(x0, x1))
                {
                    floor = Math.Min(floor, r.Y);
                }
            }

            return floor;
        }

        /// <summary>How far down a stack of HUD cards over the columns <paramref name="x0"/> … <paramref name="x1"/> may run when
        /// it starts at or below <paramref name="fromY"/>: a thumb margin above the first control under it. +∞ without touch.</summary>
        public float StackLimit(float x0, float x1, float fromY) => FloorBelow(x0, x1, fromY) - ThumbMargin;

        /// <summary>Whether <paramref name="rect"/> lies on any reserved control.</summary>
        public bool Hits(HudRect rect)
        {
            for (int i = 0; i < Controls.Count; i++)
            {
                var r = Controls[i];
                if (r.Overlaps(rect))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary><paramref name="rect"/> moved straight up until no control lies on it (a gap above each one it met).</summary>
        public HudRect LiftClear(HudRect rect, float gap)
        {
            for (int guard = 0; guard <= Controls.Count; guard++)
            {
                bool moved = false;
                for (int i = 0; i < Controls.Count; i++)
                {
                    var r = Controls[i];
                    if (r.Overlaps(rect))
                    {
                        rect = rect.At(rect.X, r.Y - gap - rect.H);
                        moved = true;
                        break;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            return rect;
        }
    }
}
