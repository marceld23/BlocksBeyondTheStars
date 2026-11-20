// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places, where the small creatures gather: in an inhabited place a share of the micro-fauna appears at a
    /// garden by day and at a street lantern by night instead of anywhere in the ring around the player — butterflies over
    /// the beds, moths in the lamplight. Nothing is added: the population is the one the world has, only its places
    /// change. Unity-free, so the headless tests pin the rule; the Unity side (<c>MicroFaunaView</c>) rolls the dice.
    /// </summary>
    public static class PlaceGathering
    {
        /// <summary>The share of a place's spawns that goes to a gathering point (the rest keeps the ring).</summary>
        public const float Share = 0.5f;

        /// <summary>A point counts from this distance on (nothing pops into view at the player's feet) …</summary>
        public const float Near = 8f;

        /// <summary>… up to this one (inside the view's cull radius, so what appears there stays).</summary>
        public const float Far = 28f;

        /// <summary>How far around the point a creature appears.</summary>
        public const float Spread = 3f;

        /// <summary>The kind of point the creatures gather at: a lantern by night, a garden by day.</summary>
        public static byte KindFor(bool night) => night ? PlacePointKinds.Lamp : PlacePointKinds.Garden;

        /// <summary>
        /// Picks the gathering point for one spawn among the place's points of the hour's kind that lie between
        /// <see cref="Near"/> and <see cref="Far"/> of the player (the short way round the seams), by
        /// <paramref name="roll"/> in [0, 1). Gives the point's offset from the player; false when the place has none in
        /// range — the caller then keeps the ring.
        /// </summary>
        public static bool TryPick(NetPlace? place, bool night, float playerX, float playerZ, int circumference, float roll,
            out float dx, out float dz)
        {
            dx = 0f;
            dz = 0f;
            var points = place?.Points;
            if (points == null || points.Length == 0)
            {
                return false;
            }

            byte kind = KindFor(night);
            int inRange = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (InRange(points[i], kind, playerX, playerZ, circumference, out _, out _))
                {
                    inRange++;
                }
            }

            if (inRange == 0)
            {
                return false;
            }

            int pick = Math.Min(inRange - 1, Math.Max(0, (int)(roll * inRange)));
            for (int i = 0; i < points.Length; i++)
            {
                if (InRange(points[i], kind, playerX, playerZ, circumference, out float px, out float pz) && pick-- == 0)
                {
                    dx = px;
                    dz = pz;
                    return true;
                }
            }

            return false;
        }

        private static bool InRange(NetPlacePoint? point, byte kind, float playerX, float playerZ, int circumference, out float dx, out float dz)
        {
            dx = 0f;
            dz = 0f;
            if (point == null || point.Kind != kind)
            {
                return false;
            }

            dx = (float)WorldConstants.WrapDeltaX(point.X + 0.5 - playerX, circumference);
            dz = (float)WorldConstants.WrapDeltaZ(point.Z + 0.5 - playerZ, circumference);
            double sq = ((double)dx * dx) + ((double)dz * dz);
            return sq >= Near * Near && sq <= Far * Far;
        }
    }
}
