// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The parked speeder / boat hull as a box, and the one question the client asks it (#1669): is the local
    /// player <i>enclosed</i> by the hull — dismounted before the server stepped them off, or walked in while the
    /// collider was off — so the collider must stay off until they walk out? The old test was a hand-typed box
    /// centred on the vehicle root (|x| &lt; 1.9, |z| &lt; 3.1, −1.2 &lt; y &lt; 2.4); the hull is meshed from a
    /// 3×2×5 cell grid at an offset from the root (−1, −0.2, −2 for the speeder), so it spans x −1…2, z −2…3 —
    /// the box hung a full block past the hull on two sides (the collider switched off before the capsule ever
    /// touched the hull: walk-through) and short of it on the other two (blocked), and standing on the hull
    /// (feet at y 1.8) counted as "inside" (fall-through). Lives here, Unity-free, so the numbers are tested.
    /// </summary>
    public static class VehicleHull
    {
        /// <summary>The hull grid, in blocks: 3 wide (x), 2 high (y), 5 long (z) — <c>SpeederCells</c> /
        /// <c>BoatCells</c> in the view fill exactly this box.</summary>
        public const float Width = 3f, Height = 2f, Length = 5f;

        /// <summary>The player capsule's radius (<c>WorldRig</c>). The enclosed box shrinks by it on every side,
        /// so the capsule's centre is inside only when the capsule itself is — a capsule touching the hull from
        /// outside sits exactly one radius past the face and must still be blocked.</summary>
        public const float PlayerRadius = 0.35f;

        /// <summary>Feet this close to the hull top (or above) read as standing ON it, not in it.</summary>
        private const float TopMargin = 0.25f;

        /// <summary>Slack under the hull bottom: a player whose feet dropped a hair under the floor while the
        /// collider was off is still "inside" and keeps walking out.</summary>
        private const float BottomSlack = 0.1f;

        /// <summary>
        /// Whether feet at the root-local point (<paramref name="lx"/>, <paramref name="ly"/>, <paramref name="lz"/>)
        /// are enclosed by a hull whose min corner sits at (<paramref name="offX"/>, <paramref name="offY"/>,
        /// <paramref name="offZ"/>) from the root: strictly inside the footprint shrunk by
        /// <paramref name="radius"/>, and lower than the hull top by <see cref="TopMargin"/>.
        /// </summary>
        public static bool Encloses(float lx, float ly, float lz, float offX, float offY, float offZ, float radius = PlayerRadius)
        {
            return lx > offX + radius && lx < offX + Width - radius
                && lz > offZ + radius && lz < offZ + Length - radius
                && ly > offY - BottomSlack && ly < offY + Height - TopMargin;
        }
    }
}
