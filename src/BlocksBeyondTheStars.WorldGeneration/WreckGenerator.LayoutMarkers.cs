// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>#3521: where a layout wreck's loot caches, modules and data terminals lie.</summary>
public static partial class WreckGenerator
{
    /// <summary>The fewest loot caches and modules a layout wreck has (the box wreck has had two and one).</summary>
    private const int MinLootMarkers = 2, MinModuleMarkers = 1;

    private static readonly (int X, int Z)[] Beside = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>The marker a station tile leaves in a wreck, or null for none.</summary>
    internal static string? MarkerForStation(string station) => station switch
    {
        "cargo" => "loot",
        "reactor" or "workshop" => "module",
        "cockpit" or "console" => "data_terminal",
        _ => null,
    };

    /// <summary>
    /// The markers of a layout wreck, from its intact hull: one per marker-bearing station tile (layout order) on a free
    /// floor cell beside it (else on top of it), then loot caches and a module on free floor cells drawn from
    /// <paramref name="rng"/> until there are <see cref="MinLootMarkers"/> and <see cref="MinModuleMarkers"/>. A free
    /// floor cell is inside the hull, has air above it and a block below it, and is no doorway or the cell right inside one.
    /// </summary>
    private static List<WreckMarker> LayoutMarkers(WreckLayoutHull hull, System.Random rng)
    {
        var markers = new List<WreckMarker>();
        var used = new HashSet<Vector3i>();
        var blocked = DoorwayClearance(hull);
        foreach (var (type, cell) in hull.Stations)
        {
            if (MarkerForStation(type) is { } marker && SpotBeside(hull, cell, used, blocked) is { } spot)
            {
                markers.Add(new WreckMarker(marker, spot));
                used.Add(spot);
            }
        }

        var free = new List<Vector3i>();
        for (int x = 0; x < hull.W; x++)
        {
            for (int y = 0; y < hull.H; y++)
            {
                for (int z = 0; z < hull.L; z++)
                {
                    var p = new Vector3i(x, y, z);
                    if (FreeFloor(hull, p) && !used.Contains(p) && !blocked.Contains(p))
                    {
                        free.Add(p);
                    }
                }
            }
        }

        TopUp(hull, markers, "loot", MinLootMarkers, free, rng);
        TopUp(hull, markers, "module", MinModuleMarkers, free, rng);
        return markers;
    }

    /// <summary>Adds markers of <paramref name="type"/> on cells drawn from <paramref name="free"/> until there are
    /// <paramref name="min"/>; with no free cell left, in the middle of the hull above its floor.</summary>
    private static void TopUp(WreckLayoutHull hull, List<WreckMarker> markers, string type, int min, List<Vector3i> free, System.Random rng)
    {
        int have = markers.FindAll(m => m.Type == type).Count;
        for (; have < min; have++)
        {
            Vector3i at;
            if (free.Count > 0)
            {
                int k = rng.Next(free.Count);
                at = free[k];
                free.RemoveAt(k);
            }
            else
            {
                at = new Vector3i(hull.W / 2, System.Math.Min(hull.H - 1, System.Math.Max(0, hull.FloorRow + 1)), hull.L / 2);
            }

            markers.Add(new WreckMarker(type, at));
        }
    }

    /// <summary>A free floor cell beside the station (the four sides in a fixed order), else on top of it; null without one.</summary>
    private static Vector3i? SpotBeside(WreckLayoutHull hull, Vector3i station, HashSet<Vector3i> used, HashSet<Vector3i> blocked)
    {
        foreach (var (dx, dz) in Beside)
        {
            var p = new Vector3i(station.X + dx, station.Y, station.Z + dz);
            if (FreeFloor(hull, p) && !used.Contains(p) && !blocked.Contains(p))
            {
                return p;
            }
        }

        var top = new Vector3i(station.X, station.Y + 1, station.Z);
        return FreeFloor(hull, top) && !used.Contains(top) && !blocked.Contains(top) ? top : null;
    }

    /// <summary>Inside the hull, air with air above it (or the roof's edge of the grid) and a block below it.</summary>
    private static bool FreeFloor(WreckLayoutHull hull, Vector3i p)
    {
        if (!hull.In(p.X, p.Y, p.Z) || p.Y == 0 || !hull.Interior[hull.Index(p.X, p.Y, p.Z)])
        {
            return false;
        }

        bool headroom = !hull.In(p.X, p.Y + 1, p.Z) || hull.IntactAt(p.X, p.Y + 1, p.Z) == 0;
        return headroom && hull.IntactAt(p.X, p.Y, p.Z) == 0 && hull.IntactAt(p.X, p.Y - 1, p.Z) != 0;
    }

    /// <summary>Every doorway cell and the cells right beside it at its row — a cache there would block the way in.</summary>
    private static HashSet<Vector3i> DoorwayClearance(WreckLayoutHull hull)
    {
        var cells = new HashSet<Vector3i>();
        foreach (var d in hull.Doorways)
        {
            cells.Add(d);
            foreach (var (dx, dz) in Beside)
            {
                cells.Add(new Vector3i(d.X + dx, d.Y, d.Z + dz));
            }
        }

        return cells;
    }
}
