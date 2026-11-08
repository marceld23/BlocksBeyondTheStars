// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Shared.Geometry;

/// <summary>
/// #3420: a point against the blocks of a voxel body (a rock, a wreckage fragment, the wreck). The cells are given in the
/// body's own grid, where cell <c>k</c> fills the unit box from <c>k</c> to <c>k + 1</c>; every stored cell counts as
/// solid. The flight view uses it to keep the flown ship out of rocks, the server to tell a ram from a near miss — one
/// piece of geometry, so both sides agree where a rock's surface is.
/// </summary>
public static class VoxelSurface
{
    /// <summary>The largest search radius honoured; the callers ask for a ship's radius, a few blocks.</summary>
    public const float MaxReach = 16f;

    /// <summary>The point on the body's blocks nearest to <paramref name="point"/>, searching only the blocks within
    /// <paramref name="reach"/> of it (clamped to <see cref="MaxReach"/>). False when no block lies that close.
    /// <paramref name="distance"/> is 0 when the point lies inside a block — <paramref name="nearest"/> is then the
    /// point itself, so there is no direction out of the body.</summary>
    public static bool TryNearest(IReadOnlyDictionary<Vector3i, BlockId> cells, Vector3f point, float reach,
        out Vector3f nearest, out float distance)
    {
        nearest = point;
        distance = float.MaxValue;
        if (cells is null || cells.Count == 0 || !(reach >= 0f)
            || !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
        {
            return false;
        }

        reach = Math.Min(reach, MaxReach);
        int x0 = Floor(point.X - reach), x1 = Floor(point.X + reach);
        int y0 = Floor(point.Y - reach), y1 = Floor(point.Y + reach);
        int z0 = Floor(point.Z - reach), z1 = Floor(point.Z + reach);
        float bestSq = reach * reach;
        bool found = false;
        for (int x = x0; x <= x1; x++)
        {
            float qx = Clamp(point.X, x, x + 1);
            for (int y = y0; y <= y1; y++)
            {
                float qy = Clamp(point.Y, y, y + 1);
                for (int z = z0; z <= z1; z++)
                {
                    float qz = Clamp(point.Z, z, z + 1);
                    float dx = point.X - qx, dy = point.Y - qy, dz = point.Z - qz;
                    float dsq = dx * dx + dy * dy + dz * dz;
                    if ((found ? dsq >= bestSq : dsq > bestSq) || !cells.ContainsKey(new Vector3i(x, y, z)))
                    {
                        continue;
                    }

                    found = true;
                    bestSq = dsq;
                    nearest = new Vector3f(qx, qy, qz);
                }
            }
        }

        if (found)
        {
            distance = (float)Math.Sqrt(bestSq);
        }

        return found;
    }

    private static int Floor(float v) => (int)Math.Floor(v);

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
