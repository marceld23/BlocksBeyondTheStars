// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions
{
    /// <summary>How heavy a carried block is — three classes from the block's hardness (blocks have no weight of their
    /// own): what it does on a hit and how far it flies.</summary>
    public enum BlockWeight
    {
        /// <summary>Soft material: earth, sand, leaves, wool.</summary>
        Light = 0,

        /// <summary>Wood, stone, ordinary ore.</summary>
        Medium = 1,

        /// <summary>Metal, obsidian, the hardest rock.</summary>
        Heavy = 2,
    }

    /// <summary>
    /// The rules of carrying a block over the head and throwing it (#3766, an idea of a young player): one block of the
    /// selected hotbar stack is lifted with both hands, thrown with the attack control, hurts a machine, a robber or a wild
    /// animal it hits — by its material — and then lies on the ground as an item to pick up again. Never placed, never
    /// lost, never a danger for players or tame animals. One class for the server (which flies the block and books the
    /// hit) and the client (which draws the same arc).
    /// </summary>
    public static class BlockThrow
    {
        /// <summary>Hardness below this is <see cref="BlockWeight.Light"/> …</summary>
        public const float MediumFromHardness = 1f;

        /// <summary>… from this on it is <see cref="BlockWeight.Heavy"/>.</summary>
        public const float HeavyFromHardness = 4f;

        /// <summary>Where the block leaves the hands: this far above the thrower's feet.</summary>
        public const float ReleaseHeight = 2.2f;

        /// <summary>The pull on a flying block, blocks per second squared — the same on every world (a throw is a game
        /// move, not a physics lesson: the arc a player learned stays the arc).</summary>
        public const float Gravity = 18f;

        /// <summary>A flight that hits nothing ends after this many seconds; the block drops where it is.</summary>
        public const float MaxFlightSeconds = 2.5f;

        /// <summary>The step the server flies the block in, seconds.</summary>
        public const float StepSeconds = 1f / 30f;

        /// <summary>Seconds between two throws of one player.</summary>
        public const double ThrowGapSeconds = 0.6;

        /// <summary>How close the flying block has to pass a body's axis to hit it, per unit of the body's size.</summary>
        public const float HitRadius = 0.8f;

        /// <summary>How tall a body of size 1 is for the hit test, blocks.</summary>
        public const float BodyHeight = 2f;

        /// <summary>The steepest throw: this far up or down from level, degrees.</summary>
        public const float MaxPitchDegrees = 80f;

        public static BlockWeight WeightOf(float hardness)
            => hardness >= HeavyFromHardness ? BlockWeight.Heavy
                : hardness >= MediumFromHardness ? BlockWeight.Medium
                : BlockWeight.Light;

        /// <summary>The damage of one hit: a light block stings (4), a medium one is a club blow (7), a heavy one a touch
        /// more (10) — moderate by decision; throwing is a trick, not the best weapon.</summary>
        public static float Damage(BlockWeight weight)
            => weight == BlockWeight.Heavy ? 10f : weight == BlockWeight.Medium ? 7f : 4f;

        /// <summary>The speed the block leaves the hands with, blocks per second: a heavy one does not fly as far.</summary>
        public static float Speed(BlockWeight weight)
            => weight == BlockWeight.Heavy ? 11f : weight == BlockWeight.Medium ? 13f : 15f;

        /// <summary>The claimed throw direction made safe: normalised, its pitch inside <see cref="MaxPitchDegrees"/>; a
        /// zero or broken direction becomes the level direction of <paramref name="yawDegrees"/> (yaw 0 = +Z, 90 = +X).</summary>
        public static void Direction(float x, float y, float z, float yawDegrees, out float dx, out float dy, out float dz)
        {
            double len = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            if (!(len > 1e-4) || double.IsInfinity(len))
            {
                double yaw = yawDegrees * Math.PI / 180.0;
                dx = (float)Math.Sin(yaw);
                dy = 0f;
                dz = (float)Math.Cos(yaw);
                return;
            }

            double maxY = Math.Sin(MaxPitchDegrees * Math.PI / 180.0);
            double ny = Math.Max(-maxY, Math.Min(maxY, y / len));
            double flat = Math.Sqrt((double)x * x + (double)z * z);
            double keep = Math.Sqrt(1.0 - ny * ny);
            if (flat < 1e-6)
            {
                double yaw = yawDegrees * Math.PI / 180.0;
                dx = (float)(Math.Sin(yaw) * keep);
                dz = (float)(Math.Cos(yaw) * keep);
            }
            else
            {
                dx = (float)(x / flat * keep);
                dz = (float)(z / flat * keep);
            }

            dy = (float)ny;
        }

        /// <summary>Where a block thrown from a point with a velocity is after <paramref name="t"/> seconds — the one arc the
        /// server flies and the client draws.</summary>
        public static void PositionAt(float x, float y, float z, float vx, float vy, float vz, float t, out float px, out float py, out float pz)
        {
            px = x + vx * t;
            py = y + vy * t - 0.5f * Gravity * t * t;
            pz = z + vz * t;
        }

        /// <summary>The height of a block that flew from <paramref name="fromY"/> to <paramref name="toY"/> in
        /// <paramref name="seconds"/>, <paramref name="t"/> seconds after it left the hands — the arc of
        /// <see cref="PositionAt"/> rebuilt from its two ends (the sideways motion is even, so a client that knows the
        /// ends and the time draws exactly what the server flew).</summary>
        public static float ArcY(float fromY, float toY, float seconds, float t)
        {
            if (seconds <= 1e-4f)
            {
                return toY;
            }

            float vy = (toY - fromY + (0.5f * Gravity * seconds * seconds)) / seconds;
            return fromY + (vy * t) - (0.5f * Gravity * t * t);
        }

        /// <summary>True if a point lies inside a standing body's hit cylinder: within <see cref="HitRadius"/> × size of its
        /// axis (the horizontal distance squared is the caller's — a planet wraps) and between its feet and its head.</summary>
        public static bool HitsBody(float horizontalDistSq, float heightAboveFeet, float sizeScale)
        {
            float size = sizeScale > 1f ? sizeScale : 1f;
            float r = HitRadius * size;
            return horizontalDistSq <= r * r && heightAboveFeet >= -0.4f && heightAboveFeet <= BodyHeight * size + 0.4f;
        }
    }
}
