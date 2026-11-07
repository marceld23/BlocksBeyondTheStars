// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The geometry of a melody site (#3346): eight pillars around a centre, pillar <c>i</c> = tone <c>i</c>, ordered clockwise
/// from north (north is +Z — the compass's bearing 0°; east is +X). The Singing Shrine's eight rune pillars are one; a
/// player-built <b>ring</b> is the other: eight <c>rune_stone</c> columns around a centre block, one in each 45° sector,
/// all about the same distance (2–5 blocks) from it. Pure, so the server's detector and the tests share it.
/// </summary>
public static class MelodySiteGeometry
{
    /// <summary>Pillars per site — one per tone.</summary>
    public const int Pillars = SounderAlphabet.ToneCount;

    /// <summary>A ring stone stands at least this far (horizontally) from the centre block.</summary>
    public const double RingMinDistance = 1.9;

    /// <summary>…and at most this far (a radius-5 ring's diagonal stone stands 5.66 away).</summary>
    public const double RingMaxDistance = 5.8;

    /// <summary>The ring's stones may differ this much in distance (a square-ish ring of radius 2 has 2 and 2.83).</summary>
    public const double RingRoundness = 1.5;

    /// <summary>How far (blocks) the ring search looks for a centre around a struck stone.</summary>
    public const int RingSearchRadius = 6;

    /// <summary>The bearing of (<paramref name="dx"/>, <paramref name="dz"/>) clockwise from north (+Z), in degrees [0, 360).</summary>
    public static double Bearing(double dx, double dz)
    {
        double deg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360.0 : deg;
    }

    /// <summary>The 45° sector (0 = north, 1 = north-east … 7 = north-west) of a horizontal offset.</summary>
    public static int Sector(double dx, double dz) => (int)Math.Floor((Bearing(dx, dz) + 22.5) / 45.0) % Pillars;

    /// <summary><paramref name="pillars"/> ordered clockwise from north around <paramref name="centre"/> (horizontal offsets
    /// only; the caller passes offsets already unwrapped across the world seam).</summary>
    public static Vector3i[] OrderClockwiseFromNorth(Vector3i centre, IEnumerable<Vector3i> pillars)
    {
        var list = new List<Vector3i>(pillars);
        list.Sort((a, b) => Bearing(a.X - centre.X, a.Z - centre.Z).CompareTo(Bearing(b.X - centre.X, b.Z - centre.Z)));
        return list.ToArray();
    }

    /// <summary>
    /// The Singing Shrine's pillars for a shrine stamped at (<paramref name="cx"/>, <paramref name="groundY"/>,
    /// <paramref name="cz"/>) — the same cells <c>GameServer.WriteShrine</c> writes (eight three-stone rune columns five
    /// blocks out, standing on the plinth at <c>groundY</c>) — as base cells (the lowest stone), clockwise from north.
    /// Not wrapped: the caller canonicalises.
    /// </summary>
    public static Vector3i[] ShrinePillars(int cx, int groundY, int cz)
    {
        var pillars = new Vector3i[Pillars];
        for (int i = 0; i < Pillars; i++)
        {
            double a = i * Math.PI / 4.0;
            pillars[i] = new Vector3i(cx + (int)Math.Round(Math.Cos(a) * 5.0), groundY + 1, cz + (int)Math.Round(Math.Sin(a) * 5.0));
        }

        return OrderClockwiseFromNorth(new Vector3i(cx, groundY, cz), pillars);
    }

    /// <summary>
    /// Looks for a ring of eight stone columns around <paramref name="centre"/>: within <see cref="RingMinDistance"/> …
    /// <see cref="RingMaxDistance"/> horizontally, exactly one column in each 45° sector, distances no more than
    /// <see cref="RingRoundness"/> apart. A column counts when any cell from <c>centre.Y - 1</c> to <c>centre.Y + 3</c> holds a
    /// stone (<paramref name="isStone"/> takes offsets from the centre); its pillar is its lowest stone there. Returns the
    /// eight pillar cells (absolute, centre + offset — the caller canonicalises) clockwise from north, or null.
    /// </summary>
    public static Vector3i[]? FindRing(Vector3i centre, Func<int, int, int, bool> isStone)
    {
        var found = new Vector3i?[Pillars];
        var distance = new double[Pillars];
        int reach = (int)Math.Ceiling(RingMaxDistance);
        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                double d = Math.Sqrt((dx * dx) + (dz * dz));
                if (d < RingMinDistance || d > RingMaxDistance || LowestStone(dx, dz, isStone) is not { } dy)
                {
                    continue;
                }

                int sector = Sector(dx, dz);
                if (found[sector] is not null)
                {
                    return null; // two stones in one sector: a wall or a heap, not a ring of eight
                }

                found[sector] = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                distance[sector] = d;
            }
        }

        return Complete(found, distance);
    }

    private static int? LowestStone(int dx, int dz, Func<int, int, int, bool> isStone)
    {
        for (int dy = -1; dy <= 3; dy++)
        {
            if (isStone(dx, dy, dz))
            {
                return dy;
            }
        }

        return null;
    }

    private static Vector3i[]? Complete(Vector3i?[] found, double[] distance)
    {
        double min = double.MaxValue, max = 0;
        var ring = new Vector3i[Pillars];
        for (int i = 0; i < Pillars; i++)
        {
            if (found[i] is not { } cell)
            {
                return null;
            }

            ring[i] = cell;
            min = Math.Min(min, distance[i]);
            max = Math.Max(max, distance[i]);
        }

        return max - min <= RingRoundness ? ring : null;
    }

    /// <summary>Which pillar (0–7) a struck cell belongs to: the same column (x, z) and from the pillar's base up to three
    /// cells above it (the shrine's pillars are three stones tall). -1 when none.</summary>
    public static int PillarIndex(IReadOnlyList<Vector3i> pillars, Vector3i cell)
    {
        for (int i = 0; i < pillars.Count; i++)
        {
            var p = pillars[i];
            if (p.X == cell.X && p.Z == cell.Z && cell.Y >= p.Y && cell.Y <= p.Y + 3)
            {
                return i;
            }
        }

        return -1;
    }
}
