// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Exterior detail for kit stations (#1918): solar wings on free side walls, domes and antenna masts on free roofs, so a
/// station docked together from modules reads as a space station from outside — in flight its real hull is what pilots
/// see (#1917). How many of each comes from the kit (pinned per station). The composer reserves <see cref="MarginXZ"/>
/// blocks around the modules and <see cref="MarginTop"/> above them.
/// <para>A piece goes only where it fits: never inside a module's box, never over a window, a doorway or any other
/// see-through or open wall cell, and never in front of a see-through wall (a hangar's force-field mouth), so the dock
/// approach stays clear. Candidates are drawn from the caller's rng, which it seeds from the composition — a replay places
/// the same pieces.</para>
/// </summary>
public static class StationKitExterior
{
    /// <summary>Free blocks reserved on every side of the modules (a solar wing juts out this far).</summary>
    public const int MarginXZ = 3;

    /// <summary>Free blocks reserved above the modules (a dome or an antenna mast is this tall).</summary>
    public const int MarginTop = 3;

    /// <summary>The deep blue of the solar cells (a tint on glass).</summary>
    public const int SolarTint = 0x2A4B9C;

    /// <summary>Block keys you can see through — a wall of these is a window or a hangar mouth, never a mounting face.</summary>
    public static readonly HashSet<string> SeeThroughKeys = new(StringComparer.Ordinal)
    {
        "glass", "glass_clear", "force_field", "energy_fence", "energy_gate", "water",
    };

    private static readonly (int X, int Z)[] Faces = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Adds the exterior detail to a baked kit station.</summary>
    /// <param name="get">The baked block at a structure cell (0 outside the structure).</param>
    /// <param name="set">Writes a cell: block, shape, tint, glow.</param>
    /// <param name="modules">Every module's box (inclusive), the start module first.</param>
    public static void Apply(Func<int, int, int, ushort> get, Action<int, int, int, ushort, int, int, int> set, int w, int h, int l,
        IReadOnlyList<(Vector3i Min, Vector3i Max)> modules, int solarWings, int antennas, int domes, GameContent content, Random rng)
    {
        ushort hull = content.GetBlock("iron_wall")?.NumericId.Value ?? 0;
        if (hull == 0 || modules.Count == 0)
        {
            return;
        }

        ushort dark = content.GetBlock("carbon")?.NumericId.Value ?? hull;
        ushort glass = content.GetBlock("glass")?.NumericId.Value ?? hull;
        ushort light = content.GetBlock("light_white")?.NumericId.Value ?? glass;
        var seeThrough = new HashSet<ushort>();
        foreach (var key in SeeThroughKeys)
        {
            if (content.GetBlock(key) is { } def)
            {
                seeThrough.Add(def.NumericId.Value);
            }
        }

        bool InModule(int x, int y, int z)
        {
            foreach (var (min, max) in modules)
            {
                if (x >= min.X && x <= max.X && y >= min.Y && y <= max.Y && z >= min.Z && z <= max.Z)
                {
                    return true;
                }
            }

            return false;
        }

        var corridors = MouthCorridors(get, w, h, l, modules, content.GetBlock("force_field")?.NumericId.Value ?? 0, InModule);

        bool InCorridor(int x, int y, int z)
        {
            foreach (var (min, max) in corridors)
            {
                if (x >= min.X && x <= max.X && y >= min.Y && y <= max.Y && z >= min.Z && z <= max.Z)
                {
                    return true;
                }
            }

            return false;
        }

        bool Free(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l && get(x, y, z) == 0 && !InModule(x, y, z) && !InCorridor(x, y, z);

        bool Opaque(int x, int y, int z)
        {
            ushort b = get(x, y, z);
            return b != 0 && !seeThrough.Contains(b);
        }

        // Solar wings: one per free side wall, as many as the kit asks for.
        var sites = new List<(int Module, int Face)>();
        for (int i = 0; i < modules.Count; i++)
        {
            for (int f = 0; f < Faces.Length; f++)
            {
                sites.Add((i, f));
            }
        }

        Shuffle(sites, rng);
        int wings = 0;
        foreach (var (mi, fi) in sites)
        {
            if (wings >= solarWings)
            {
                break;
            }

            if (TryWing(modules[mi], Faces[fi], Free, Opaque, set, dark, glass))
            {
                wings++;
            }
        }

        // Domes: the start module's roof first (its command cupola), then the others in a drawn order.
        var usedRoofs = new HashSet<int>();
        var order = new List<int>();
        for (int i = 1; i < modules.Count; i++)
        {
            order.Add(i);
        }

        Shuffle(order, rng);
        order.Insert(0, 0);
        int placedDomes = 0;
        foreach (int i in order)
        {
            if (placedDomes >= domes)
            {
                break;
            }

            if (TryDome(modules[i], solid: i == 0, Free, get, set, hull, glass, light))
            {
                usedRoofs.Add(i);
                placedDomes++;
            }
        }

        // Antenna masts on the roofs without a dome — a corner of each, then further corners while the kit wants more.
        var antennaOrder = new List<int>();
        for (int i = 0; i < modules.Count; i++)
        {
            if (!usedRoofs.Contains(i))
            {
                antennaOrder.Add(i);
            }
        }

        Shuffle(antennaOrder, rng);
        int placedAntennas = 0;
        bool progress = true;
        while (placedAntennas < antennas && progress)
        {
            progress = false;
            foreach (int i in antennaOrder)
            {
                if (placedAntennas >= antennas)
                {
                    break;
                }

                if (TryAntenna(modules[i], rng, Free, get, set, dark, light))
                {
                    placedAntennas++;
                    progress = true;
                }
            }
        }
    }

    /// <summary>The space in front of every force-field wall patch that opens to the outside (a hangar mouth — the ship's
    /// dock approach): its rectangle grown by one block, from the wall out to the structure's edge. Windows need no
    /// corridor: a wing only ever mounts on a row of solid wall.</summary>
    private static List<(Vector3i Min, Vector3i Max)> MouthCorridors(Func<int, int, int, ushort> get, int w, int h, int l,
        IReadOnlyList<(Vector3i Min, Vector3i Max)> modules, ushort field, Func<int, int, int, bool> inModule)
    {
        var result = new List<(Vector3i, Vector3i)>();
        foreach (var (min, max) in modules)
        {
            foreach (var (fx, fz) in Faces)
            {
                int a0 = fx != 0 ? min.Z : min.X, a1 = fx != 0 ? max.Z : max.X; // the along-wall axis
                int plane = fx > 0 ? max.X : fx < 0 ? min.X : fz > 0 ? max.Z : min.Z;
                int lo = int.MaxValue, hi = int.MinValue, ylo = int.MaxValue, yhi = int.MinValue;
                for (int a = a0; a <= a1; a++)
                    for (int y = min.Y; y <= max.Y; y++)
                    {
                        int x = fx != 0 ? plane : a, z = fx != 0 ? a : plane;
                        if (field == 0 || get(x, y, z) != field || inModule(x + fx, y, z + fz))
                        {
                            continue;
                        }

                        lo = Math.Min(lo, a); hi = Math.Max(hi, a);
                        ylo = Math.Min(ylo, y); yhi = Math.Max(yhi, y);
                    }

                if (lo > hi)
                {
                    continue;
                }

                if (fx != 0)
                {
                    int x0 = fx > 0 ? plane + 1 : 0, x1 = fx > 0 ? w - 1 : plane - 1;
                    result.Add((new Vector3i(x0, ylo - 1, lo - 1), new Vector3i(x1, yhi + 1, hi + 1)));
                }
                else
                {
                    int z0 = fz > 0 ? plane + 1 : 0, z1 = fz > 0 ? l - 1 : plane - 1;
                    result.Add((new Vector3i(lo - 1, ylo - 1, z0), new Vector3i(hi + 1, yhi + 1, z1)));
                }
            }
        }

        return result;
    }

    /// <summary>A flat solar wing jutting <see cref="MarginXZ"/> blocks from a module's side wall: a carbon strut row at the
    /// wall, then blue solar cells with a carbon frame line every third cell. It takes the highest row whose wall is solid
    /// all along (no window, no doorway) and whose space is free.</summary>
    private static bool TryWing((Vector3i Min, Vector3i Max) box, (int X, int Z) face, Func<int, int, int, bool> free,
        Func<int, int, int, bool> opaque, Action<int, int, int, ushort, int, int, int> set, ushort dark, ushort glass)
    {
        var (min, max) = box;
        int a0 = (face.X != 0 ? min.Z : min.X) + 1, a1 = (face.X != 0 ? max.Z : max.X) - 1;
        if (a1 - a0 + 1 < 3)
        {
            return false;
        }

        int plane = face.X > 0 ? max.X : face.X < 0 ? min.X : face.Z > 0 ? max.Z : min.Z;
        for (int y = max.Y - 1; y >= min.Y + 1; y--)
        {
            bool fits = true;
            for (int a = a0; a <= a1 && fits; a++)
            {
                int wx = face.X != 0 ? plane : a, wz = face.X != 0 ? a : plane;
                fits = opaque(wx, y, wz);
                for (int d = 1; d <= MarginXZ && fits; d++)
                {
                    fits = free(wx + face.X * d, y, wz + face.Z * d);
                }
            }

            if (!fits)
            {
                continue;
            }

            for (int a = a0; a <= a1; a++)
            {
                int wx = face.X != 0 ? plane : a, wz = face.X != 0 ? a : plane;
                for (int d = 1; d <= MarginXZ; d++)
                {
                    bool frame = d == 1 || (a - a0) % 3 == 2;
                    set(wx + face.X * d, y, wz + face.Z * d, frame ? dark : glass, 0, frame ? 0 : SolarTint, 0);
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>A stepped dome on a module's roof: three rings, each a block narrower, capped flat with a light in the
    /// middle. The start module gets a solid hull cupola, every other module a glass observation dome.</summary>
    private static bool TryDome((Vector3i Min, Vector3i Max) box, bool solid, Func<int, int, int, bool> free,
        Func<int, int, int, ushort> get, Action<int, int, int, ushort, int, int, int> set, ushort hull, ushort glass, ushort light)
    {
        var (min, max) = box;
        int x0 = min.X + 1, x1 = max.X - 1, z0 = min.Z + 1, z1 = max.Z - 1;
        if (x1 - x0 + 1 < 5 || z1 - z0 + 1 < 5)
        {
            return false;
        }

        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (get(x, max.Y, z) == 0)
                {
                    return false; // an open roof (a shaft): nothing to stand the dome on
                }

                for (int r = 0; r < MarginTop; r++)
                {
                    if (!free(x, max.Y + 1 + r, z))
                    {
                        return false;
                    }
                }
            }

        ushort shell = solid ? hull : glass;
        for (int r = 0; r < MarginTop; r++)
        {
            int rx0 = x0 + r, rx1 = x1 - r, rz0 = z0 + r, rz1 = z1 - r;
            int y = max.Y + 1 + r;
            bool cap = r == MarginTop - 1;
            for (int x = rx0; x <= rx1; x++)
                for (int z = rz0; z <= rz1; z++)
                {
                    bool edge = x == rx0 || x == rx1 || z == rz0 || z == rz1;
                    if (edge || cap)
                    {
                        set(x, y, z, shell, 0, 0, 0);
                    }
                }

            if (cap)
            {
                set((rx0 + rx1) / 2, y, (rz0 + rz1) / 2, light, 0, 0, 0); // the beacon at the apex
            }
        }

        return true;
    }

    /// <summary>An antenna mast on a free corner of a module's roof: two carbon segments and a light on top.</summary>
    private static bool TryAntenna((Vector3i Min, Vector3i Max) box, Random rng, Func<int, int, int, bool> free,
        Func<int, int, int, ushort> get, Action<int, int, int, ushort, int, int, int> set, ushort dark, ushort light)
    {
        var (min, max) = box;
        var corners = new List<(int X, int Z)>
        {
            (min.X + 1, min.Z + 1), (max.X - 1, min.Z + 1), (min.X + 1, max.Z - 1), (max.X - 1, max.Z - 1),
        };
        Shuffle(corners, rng);
        foreach (var (x, z) in corners)
        {
            if (get(x, max.Y, z) == 0)
            {
                continue;
            }

            bool fits = true;
            for (int r = 0; r < MarginTop && fits; r++)
            {
                fits = free(x, max.Y + 1 + r, z);
            }

            if (!fits)
            {
                continue;
            }

            for (int r = 0; r < MarginTop; r++)
            {
                set(x, max.Y + 1 + r, z, r == MarginTop - 1 ? light : dark, 0, 0, 0);
            }

            return true;
        }

        return false;
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
