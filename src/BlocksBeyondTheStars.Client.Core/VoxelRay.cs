// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The first blocking cell a <see cref="VoxelRay"/> entered.</summary>
    public readonly struct VoxelHit
    {
        public VoxelHit(int x, int y, int z, int face, float distance)
        {
            X = x;
            Y = y;
            Z = z;
            Face = face;
            Distance = distance;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        /// <summary>The outward direction of the face the ray came in through (0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z — the
        /// order of <see cref="DecalFaces.Normal"/>).</summary>
        public int Face { get; }

        /// <summary>Metres from the ray's start to the point where it entered the cell.</summary>
        public float Distance { get; }
    }

    /// <summary>
    /// An exact voxel walk (Amanatides–Woo DDA) from one point to another: the first cell <c>blocking</c> reports and
    /// the face the ray entered it through. #3058: another player's shot arrives only as from/to/hit (<c>ActionFx</c>), so
    /// the scorch mark it leaves finds its cell and face here instead of through a protocol change. The start cell is
    /// never reported — a shot leaves the shooter's own cell.
    /// </summary>
    public static class VoxelRay
    {
        /// <summary>Walks from (<paramref name="fromX"/>, …) to (<paramref name="toX"/>, …); true with the first blocking
        /// cell along the segment (the start cell excluded), false when the segment stays clear.</summary>
        public static bool Cast(Func<int, int, int, bool> blocking, float fromX, float fromY, float fromZ,
            float toX, float toY, float toZ, out VoxelHit hit)
        {
            hit = default;
            float dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-6f)
            {
                return false;
            }

            dx /= length;
            dy /= length;
            dz /= length;
            var ax = new Axis(fromX, dx);
            var ay = new Axis(fromY, dy);
            var az = new Axis(fromZ, dz);
            int steps = Math.Abs(Floor(toX) - ax.Cell) + Math.Abs(Floor(toY) - ay.Cell) + Math.Abs(Floor(toZ) - az.Cell) + 1;
            for (int i = 0; i < steps; i++)
            {
                int face;
                float t;
                if (ax.Next <= ay.Next && ax.Next <= az.Next)
                {
                    t = ax.Advance();
                    face = ax.Step > 0 ? 1 : 0; // stepping +X enters the new cell through its −X face
                }
                else if (ay.Next <= az.Next)
                {
                    t = ay.Advance();
                    face = ay.Step > 0 ? 3 : 2;
                }
                else
                {
                    t = az.Advance();
                    face = az.Step > 0 ? 5 : 4;
                }

                if (t > length)
                {
                    return false;
                }

                if (blocking(ax.Cell, ay.Cell, az.Cell))
                {
                    hit = new VoxelHit(ax.Cell, ay.Cell, az.Cell, face, t);
                    return true;
                }
            }

            return false;
        }

        private static int Floor(float v) => (int)Math.Floor(v);

        /// <summary>One axis of the walk: the current cell, the step direction, and the ray distance of the next cell
        /// boundary along this axis.</summary>
        private struct Axis
        {
            private readonly float _delta;

            public Axis(float origin, float dir)
            {
                Cell = Floor(origin);
                Step = dir > 0f ? 1 : dir < 0f ? -1 : 0;
                _delta = Step != 0 ? Math.Abs(1f / dir) : float.PositiveInfinity;
                Next = Step > 0 ? (Cell + 1 - origin) * _delta : Step < 0 ? (origin - Cell) * _delta : float.PositiveInfinity;
            }

            public int Cell { get; private set; }

            public int Step { get; }

            public float Next { get; private set; }

            /// <summary>Moves into the next cell along this axis; returns the ray distance of the boundary crossed.</summary>
            public float Advance()
            {
                float t = Next;
                Cell += Step;
                Next += _delta;
                return t;
            }
        }
    }
}
