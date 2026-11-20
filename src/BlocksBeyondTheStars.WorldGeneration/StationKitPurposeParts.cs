// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3871, the lively places: hull parts that say what a station is for, placed by <see cref="StationKitExterior"/> after
/// the solar wings, domes and antennas — and only on a station composed with a purpose, so every station composed before
/// stays block for block what it was.
/// <list type="bullet">
/// <item><b>Trade</b> — cargo pod racks: two rows of freight crates on a free side wall under a beacon light.</item>
/// <item><b>Mining</b> — ore hoppers: a funnel of hull plates on a roof, filled with ore, on a dark spout.</item>
/// <item><b>Research</b> — radio dishes: a bowl on a post on a roof, a light at its feed.</item>
/// </list>
/// How many follows from the number of modules (<see cref="CountFor"/>), so a pinned composition replays the same parts.
/// The exterior pass's own rules hold: a cell is used only when it is free (outside every module, clear of the hangar
/// mouths' approach and of the windows), and a wall part mounts only on solid wall.
/// </summary>
public static class StationKitPurposeParts
{
    /// <summary>How many purpose parts a station of <paramref name="modules"/> modules carries: one per five, one to four.</summary>
    public static int CountFor(int modules) => Math.Clamp(modules / 5, 1, 4);

    /// <summary>The block keys each purpose's part is made of (for tests and the docs).</summary>
    public const string CrateBlock = "transport_crate";
    public const string OreBlock = "iron_ore";

    /// <summary>Places the parts of <paramref name="purpose"/>; returns how many found room.</summary>
    public static int Place(string purpose, IReadOnlyList<(Vector3i Min, Vector3i Max)> modules, Func<int, int, int, bool> free,
        Func<int, int, int, bool> opaque, Func<int, int, int, ushort> get, Action<int, int, int, ushort, int, int, int> set,
        GameContent content, Random rng)
    {
        ushort hull = content.GetBlock("iron_wall")?.NumericId.Value ?? 0;
        ushort dark = content.GetBlock("carbon")?.NumericId.Value ?? hull;
        ushort light = content.GetBlock("light_white")?.NumericId.Value ?? hull;
        ushort crate = content.GetBlock(CrateBlock)?.NumericId.Value ?? hull;
        ushort ore = content.GetBlock(OreBlock)?.NumericId.Value ?? dark;
        if (hull == 0 || !StationPurposes.IsKnown(purpose))
        {
            return 0;
        }

        int wanted = CountFor(modules.Count), placed = 0;
        var order = new List<int>();
        for (int i = 0; i < modules.Count; i++)
        {
            order.Add(i);
        }

        Shuffle(order, rng);
        foreach (int i in order)
        {
            if (placed >= wanted)
            {
                break;
            }

            bool done = purpose switch
            {
                StationPurposes.Trade => TryRack(modules[i], rng, free, opaque, set, crate, light),
                StationPurposes.Mining => TryHopper(modules[i], free, get, set, hull, ore, dark),
                _ => TryDish(modules[i], free, get, set, hull, dark, light),
            };
            placed += done ? 1 : 0;
        }

        return placed;
    }

    private static readonly (int X, int Z)[] Faces = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>A cargo rack against a module's side wall: three crates wide and two high, one block out, a beacon light
    /// over the middle. The wall behind must be solid on both rows.</summary>
    private static bool TryRack((Vector3i Min, Vector3i Max) box, Random rng, Func<int, int, int, bool> free,
        Func<int, int, int, bool> opaque, Action<int, int, int, ushort, int, int, int> set, ushort crate, ushort light)
    {
        var (min, max) = box;
        var faces = new List<(int X, int Z)>(Faces);
        Shuffle(faces, rng);
        foreach (var face in faces)
        {
            int a0 = face.X != 0 ? min.Z : min.X, a1 = face.X != 0 ? max.Z : max.X;
            int mid = (a0 + a1) / 2, plane = face.X > 0 ? max.X : face.X < 0 ? min.X : face.Z > 0 ? max.Z : min.Z;
            if (a1 - a0 < 4)
            {
                continue;
            }

            int y0 = min.Y + 1;
            bool fits = true;
            for (int a = mid - 1; a <= mid + 1 && fits; a++)
            {
                int wx = face.X != 0 ? plane : a, wz = face.X != 0 ? a : plane;
                fits = opaque(wx, y0, wz) && opaque(wx, y0 + 1, wz)
                       && free(wx + face.X, y0, wz + face.Z) && free(wx + face.X, y0 + 1, wz + face.Z);
            }

            int bx = (face.X != 0 ? plane : mid) + face.X, bz = (face.X != 0 ? mid : plane) + face.Z;
            if (!fits || !free(bx, y0 + 2, bz))
            {
                continue;
            }

            for (int a = mid - 1; a <= mid + 1; a++)
            {
                int wx = (face.X != 0 ? plane : a) + face.X, wz = (face.X != 0 ? a : plane) + face.Z;
                set(wx, y0, wz, crate, 0, 0, 0);
                set(wx, y0 + 1, wz, crate, 0, 0, 0);
            }

            set(bx, y0 + 2, bz, light, 0, 0, 0);
            return true;
        }

        return false;
    }

    /// <summary>An ore hopper in the middle of a module's roof: a dark spout, then two rings of hull plates three wide
    /// with ore heaped inside.</summary>
    private static bool TryHopper((Vector3i Min, Vector3i Max) box, Func<int, int, int, bool> free, Func<int, int, int, ushort> get,
        Action<int, int, int, ushort, int, int, int> set, ushort hull, ushort ore, ushort dark)
    {
        var (min, max) = box;
        int cx = (min.X + max.X) / 2, cz = (min.Z + max.Z) / 2, y = max.Y + 1;
        if (max.X - min.X < 4 || max.Z - min.Z < 4 || !RoofHolds(get, cx, cz, max.Y, 1)
            || !Clear(free, cx, cz, y, 0) || !Clear(free, cx, cz, y + 1, 1) || !Clear(free, cx, cz, y + 2, 1))
        {
            return false;
        }

        set(cx, y, cz, dark, 0, 0, 0);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                bool inner = dx == 0 && dz == 0;
                set(cx + dx, y + 1, cz + dz, inner ? ore : hull, 0, 0, 0);
                set(cx + dx, y + 2, cz + dz, inner ? ore : hull, 0, 0, 0);
            }
        }

        return true;
    }

    /// <summary>A radio dish in the middle of a module's roof: a dark post, a plate three wide, a rim five wide (its
    /// corners left out, so it reads round) and a light at the feed in the middle.</summary>
    private static bool TryDish((Vector3i Min, Vector3i Max) box, Func<int, int, int, bool> free, Func<int, int, int, ushort> get,
        Action<int, int, int, ushort, int, int, int> set, ushort hull, ushort dark, ushort light)
    {
        var (min, max) = box;
        int cx = (min.X + max.X) / 2, cz = (min.Z + max.Z) / 2, y = max.Y + 1;
        if (max.X - min.X < 6 || max.Z - min.Z < 6 || !RoofHolds(get, cx, cz, max.Y, 0)
            || !Clear(free, cx, cz, y, 0) || !Clear(free, cx, cz, y + 1, 1) || !Clear(free, cx, cz, y + 2, 2))
        {
            return false;
        }

        set(cx, y, cz, dark, 0, 0, 0);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                set(cx + dx, y + 1, cz + dz, hull, 0, 0, 0);
            }
        }

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                bool rim = Math.Max(Math.Abs(dx), Math.Abs(dz)) == 2 && !(Math.Abs(dx) == 2 && Math.Abs(dz) == 2);
                if (rim)
                {
                    set(cx + dx, y + 2, cz + dz, hull, 0, 0, 0);
                }
            }
        }

        set(cx, y + 2, cz, light, 0, 0, 0);
        return true;
    }

    /// <summary>Whether the roof under a square of half-width <paramref name="r"/> is closed (no shaft under the part).</summary>
    private static bool RoofHolds(Func<int, int, int, ushort> get, int cx, int cz, int roofY, int r)
    {
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (get(cx + dx, roofY, cz + dz) == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a square of half-width <paramref name="r"/> at height <paramref name="y"/> is free.</summary>
    private static bool Clear(Func<int, int, int, bool> free, int cx, int cz, int y, int r)
    {
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (!free(cx + dx, y, cz + dz))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
