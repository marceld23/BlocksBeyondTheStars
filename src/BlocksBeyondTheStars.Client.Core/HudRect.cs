// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A rectangle in HUD reference units — 1536 × 864 at UI scale 1, top-left origin, y grows downwards (the uGUI
    /// <c>UiKit.Place</c> frame). The touch layout (<see cref="TouchLayout"/>, <see cref="TouchReservation"/>) measures the
    /// on-screen touch controls in it, so the HUD can keep its zones clear of them.
    /// </summary>
    public readonly struct HudRect : IEquatable<HudRect>
    {
        public HudRect(float x, float y, float w, float h)
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
        public bool Overlaps(HudRect other)
            => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

        /// <summary>True when the rectangle shares any of the columns <paramref name="x0"/> … <paramref name="x1"/>.</summary>
        public bool OverlapsX(float x0, float x1) => X < x1 && x0 < Right;

        /// <summary>The same rectangle in another unit (<paramref name="k"/> new units per old unit).</summary>
        public HudRect Scaled(float k) => new HudRect(X * k, Y * k, W * k, H * k);

        /// <summary>The same size with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public HudRect At(float x, float y) => new HudRect(x, y, W, H);

        /// <summary>True when the rectangle lies inside a canvas of <paramref name="w"/> × <paramref name="h"/> with
        /// <paramref name="margin"/> to every edge.</summary>
        public bool InsideCanvas(float w, float h, float margin)
            => X >= margin && Y >= margin && Right <= w - margin && Bottom <= h - margin;

        public static bool operator ==(HudRect a, HudRect b) => a.Equals(b);

        public static bool operator !=(HudRect a, HudRect b) => !a.Equals(b);

        /// <summary>Exact equality — the callers compare a rectangle with the one they placed last frame.</summary>
        public bool Equals(HudRect other)
            => X.Equals(other.X) && Y.Equals(other.Y) && W.Equals(other.W) && H.Equals(other.H);

        public override bool Equals(object? obj) => obj is HudRect other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, W, H);

        public override string ToString() => FormattableString.Invariant($"({X:0.#}, {Y:0.#}, {W:0.#} × {H:0.#})");
    }
}
