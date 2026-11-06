// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The cockpit camera of the transition cinematics, in the ship's own (structure-local) frame: where it sits
    /// and where it looks.</summary>
    public readonly struct CockpitPose
    {
        public CockpitPose(float x, float y, float z, float dirX, float dirY, float dirZ, bool atHelm)
        {
            X = x;
            Y = y;
            Z = z;
            DirX = dirX;
            DirY = dirY;
            DirZ = dirZ;
            AtHelm = atHelm;
        }

        /// <summary>The camera position, ship-local (cell (x, y, z) spans x..x+1).</summary>
        public float X { get; }

        /// <inheritdoc cref="X"/>
        public float Y { get; }

        /// <inheritdoc cref="X"/>
        public float Z { get; }

        /// <summary>The look direction, ship-local, normalised.</summary>
        public float DirX { get; }

        /// <inheritdoc cref="DirX"/>
        public float DirY { get; }

        /// <inheritdoc cref="DirX"/>
        public float DirZ { get; }

        /// <summary>True behind the helm; false for the fallback at the player's own eyes.</summary>
        public bool AtHelm { get; }
    }

    /// <summary>
    /// Where the transition cinematics' cockpit camera sits (#3258): for whatever stretch of a landing or a lift-off no
    /// outside spot can see (a pad in a hall, a courtyard), the shot plays from inside the own ship instead — one cell
    /// behind the helm at eye height, looking out along the nose a little downward, riding with the hull. A ship without a
    /// helm cell looks from the player's own eyes toward the nose. Pure; covered by <c>TransitionCockpitTests</c>.
    /// </summary>
    public static class TransitionCockpit
    {
        /// <summary>The eye's height over the floor the camera stands on.</summary>
        public const float EyeHeight = 1.6f;

        /// <summary>How far the cockpit camera looks down from the horizon (the console and the ground ahead in frame).</summary>
        public const float LookDownDegrees = 8f;

        private const int FloorSearch = 3;

        /// <summary>The nose: the flattened, normalised way from the engines' centroid to
        /// (<paramref name="towardX"/>, <paramref name="towardZ"/>) (the helm, or the hull's middle). No engines, or engines
        /// all round the target → +Z, the way every stock ship faces.</summary>
        public static (float X, float Z) Nose(IReadOnlyList<(int X, int Y, int Z)>? engines, float towardX, float towardZ)
        {
            if (engines == null || engines.Count == 0)
            {
                return (0f, 1f);
            }

            float sx = 0f, sz = 0f;
            foreach (var e in engines)
            {
                sx += e.X + 0.5f;
                sz += e.Z + 0.5f;
            }

            float dx = towardX - sx / engines.Count, dz = towardZ - sz / engines.Count;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            return len < 0.5f ? (0f, 1f) : (dx / len, dz / len);
        }

        /// <summary>The camera behind the helm cell (<paramref name="helmX"/>, <paramref name="helmY"/>, <paramref name="helmZ"/>):
        /// one cell back against <paramref name="nose"/> along its main axis, at <see cref="EyeHeight"/> over the floor there.
        /// When that cell (or the one above it) is built up, the camera stands over the helm itself.</summary>
        public static CockpitPose AtHelm(int helmX, int helmY, int helmZ, (float X, float Z) nose, Func<int, int, int, bool> occupied)
        {
            if (occupied == null)
            {
                throw new ArgumentNullException(nameof(occupied));
            }

            int bx = helmX, bz = helmZ;
            if (Math.Abs(nose.X) > Math.Abs(nose.Z))
            {
                bx -= Math.Sign(nose.X);
            }
            else
            {
                bz -= nose.Z < 0f ? -1 : 1;
            }

            float floorTop = helmY;
            if (occupied(bx, helmY, bz) || occupied(bx, helmY + 1, bz))
            {
                bx = helmX;
                bz = helmZ;
            }
            else
            {
                for (int y = helmY - 1; y >= helmY - FloorSearch; y--)
                {
                    if (occupied(bx, y, bz))
                    {
                        floorTop = y + 1;
                        break;
                    }
                }
            }

            var dir = Look(nose);
            return new CockpitPose(bx + 0.5f, floorTop + EyeHeight, bz + 0.5f, dir.X, dir.Y, dir.Z, true);
        }

        /// <summary>The fallback for a ship without a helm cell: the player's own eyes, turned toward the nose.</summary>
        public static CockpitPose AtEye(float x, float y, float z, (float X, float Z) nose)
        {
            var dir = Look(nose);
            return new CockpitPose(x, y, z, dir.X, dir.Y, dir.Z, false);
        }

        /// <summary>True when the straight way between two ship-local points crosses no cell of the ship — the camera may
        /// glide through the cabin there instead of blinking.</summary>
        public static bool CabinPathClear(Func<int, int, int, bool> occupied,
            float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            if (occupied == null)
            {
                throw new ArgumentNullException(nameof(occupied));
            }

            return CinematicStageScan.PathClear((x, y, z) => !occupied(x, y, z), fromX, fromY, fromZ, toX, toY, toZ);
        }

        private static (float X, float Y, float Z) Look((float X, float Z) nose)
        {
            float len = (float)Math.Sqrt(nose.X * nose.X + nose.Z * nose.Z);
            float nx = len > 1e-4f ? nose.X / len : 0f, nz = len > 1e-4f ? nose.Z / len : 1f;
            double down = LookDownDegrees * Math.PI / 180.0;
            float flat = (float)Math.Cos(down);
            return (nx * flat, -(float)Math.Sin(down), nz * flat);
        }
    }
}
