// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A synthetic giant ship for the revision-3 ruin tests (#3609): a hull of elliptic cross-section along x with tapered ends,
/// decks every six rows above the keel, bulkheads every 24 slices with doorways, window bands and a glass dome, ceiling lights, a
/// kept ladder shaft through every deck, a water pool on the first deck, an entrance, chests, a sound and the doorway markers.
/// Built from plain arithmetic only (no trig), so its cells are the same on every platform.
/// </summary>
internal static class GiantShipTestHulls
{
    internal sealed record Hull(int W, int H, int L, int Ground, List<(int X, int Y, int Z, string Key, bool Keep)> Blocks,
        List<TemplateCell> Markers)
    {
        public int Zc => L / 2;

        public int Deck(int k) => Ground + 1 + (6 * k);

        public Vector3i Entrance => new(W / 4, Deck(0) + 1, Zc);

        public Vector3i Ladder => new((W / 2) + 5, Deck(0) + 1, Zc + 3);

        /// <summary>The pool: 5 × 5 cells of water on the first deck.</summary>
        public (int X0, int Z0) Pool => (20, Zc - 2);
    }

    /// <summary>The hull, <paramref name="w"/> long (x), <paramref name="h"/> high, <paramref name="l"/> wide (z); the rows up to
    /// <paramref name="ground"/> are the keel.</summary>
    public static Hull Build(int w, int h, int l, int ground = 4)
    {
        var cells = new Dictionary<(int, int, int), (string Key, bool Keep)>();
        var hull = new Hull(w, h, l, ground, new List<(int, int, int, string, bool)>(), new List<TemplateCell>());
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    if (Inside(w, h, l, x, y, z) && Material(hull, x, y, z) is { } key)
                    {
                        cells[(x, y, z)] = (key, false);
                    }
                }
            }
        }

        AddFittings(hull, cells);
        foreach (var kv in cells)
        {
            hull.Blocks.Add((kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.Key, kv.Value.Keep));
        }

        AddMarkers(hull);
        return hull;
    }

    private static double Taper(int w, int x)
    {
        double t = w / 6.0;
        return System.Math.Min(1.0, System.Math.Min((x + 1) / t, (w - x) / t) * 0.6 + 0.4);
    }

    private static bool Inside(int w, int h, int l, int x, int y, int z)
    {
        double s = Taper(w, x), a = (l / 2.0) - 0.5, b = (h / 2.0) - 0.5;
        double dz = (z - ((l - 1) / 2.0)) / (a * s), dy = (y - ((h - 1) / 2.0)) / (b * s);
        return (dz * dz) + (dy * dy) <= 1.0;
    }

    /// <summary>What a cell inside the hull is: the shell (a window band, the dome), a deck, a bulkhead, or air (null).</summary>
    private static string? Material(Hull hull, int x, int y, int z)
    {
        int w = hull.W, h = hull.H, l = hull.L;
        bool shell = !Inside(w, h, l, x - 1, y, z) || !Inside(w, h, l, x + 1, y, z) || !Inside(w, h, l, x, y - 1, z)
            || !Inside(w, h, l, x, y + 1, z) || !Inside(w, h, l, x, y, z - 1) || !Inside(w, h, l, x, y, z + 1)
            || x == 0 || x == w - 1 || y == 0 || y == h - 1 || z == 0 || z == l - 1;
        if (shell)
        {
            bool dome = y > h * 0.7 && System.Math.Abs(x - (w / 2)) <= w / 10;
            bool window = y > hull.Ground && (y - hull.Ground - 1) % 6 == 3 && x % 4 != 0;
            return dome || window ? "glass" : "iron_wall";
        }

        if (y > hull.Ground && (y - hull.Ground - 1) % 6 == 0)
        {
            return "steel_floor";
        }

        bool bulkhead = x % 24 == 12 && y > hull.Ground;
        bool doorway = System.Math.Abs(z - hull.Zc) <= 1 && (y - hull.Ground - 1) % 6 is >= 1 and <= 3;
        if (bulkhead && !doorway)
        {
            return "iron_wall";
        }

        return (y - hull.Ground - 1) % 6 == 5 && x % 6 == 3 && z % 6 == 3 ? "light_white" : null;
    }

    /// <summary>The kept ladder shaft through every deck and the pool with its rim.</summary>
    private static void AddFittings(Hull hull, Dictionary<(int, int, int), (string Key, bool Keep)> cells)
    {
        var lad = hull.Ladder;
        for (int y = hull.Deck(0); y < hull.H; y++)
        {
            if (cells.ContainsKey((lad.X, y, lad.Z)) || Inside(hull.W, hull.H, hull.L, lad.X, y, lad.Z))
            {
                cells[(lad.X, y, lad.Z)] = ("ladder", true);
                cells[(lad.X, y, lad.Z + 1)] = ("iron_wall", true); // the shaft's back wall carries the ladder
            }
        }

        var (px, pz) = hull.Pool;
        int py = hull.Deck(0) + 1;
        for (int x = px - 1; x <= px + 5; x++)
        {
            for (int z = pz - 1; z <= pz + 5; z++)
            {
                bool rim = x == px - 1 || x == px + 5 || z == pz - 1 || z == pz + 5;
                cells[(x, py, z)] = (rim ? "iron_wall" : "water", false);
            }
        }
    }

    private static void AddMarkers(Hull hull)
    {
        void Marker(string id, int x, int y, int z, string config = "")
            => hull.Markers.Add(new TemplateCell { Kind = "marker", Id = id, X = x, Y = y, Z = z, Config = config });

        var e = hull.Entrance;
        Marker(GiantShipRules.EntranceMarker, e.X, e.Y, e.Z);
        for (int k = 0; k < 4; k++)
        {
            int x = (hull.W / 5) * (k + 1) + 3, deck = k % 3;
            if (hull.Deck(deck) + 3 < hull.H)
            {
                Marker(LandmarkRules.ChestMarker, x, hull.Deck(deck) + 1, hull.Zc - 3, "table=ruin_common");
            }
        }

        Marker(LandmarkRules.SoundMarker, hull.W / 2, hull.Deck(1) + 2, hull.Zc, "sound=amb_sounder;radius=40");
        Marker("npc", hull.W / 3, hull.Deck(0) + 1, hull.Zc + 2); // a ruin never keeps a resident
        for (int x = 12; x < hull.W; x += 24)
        {
            Marker(GiantShipGenerator.DoorMarker, x, hull.Deck(0) + 1, hull.Zc, "axis=z");
        }

        foreach (var m in hull.Markers)
        {
            hull.Blocks.RemoveAll(b => b.X == m.X && b.Z == m.Z && (b.Y == m.Y || b.Y == m.Y + 1));
        }
    }

    /// <summary>Every cell of a structure.</summary>
    public static IEnumerable<(int X, int Y, int Z)> Cells(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    yield return (x, y, z);
                }
            }
        }
    }

    /// <summary>The hull as a voxel grid (with its keep bits) and its markers.</summary>
    public static (VoxelGrid Grid, List<SettlementMarker> Markers) Grid(GameContent content, Hull hull)
    {
        var g = new VoxelGrid(hull.W, hull.H, hull.L);
        foreach (var (x, y, z, key, keep) in hull.Blocks)
        {
            g.Set(x, y, z, content.GetBlock(key)!.NumericId.Value);
            g.Keep[g.Index(x, y, z)] = keep;
        }

        var markers = new List<SettlementMarker>();
        foreach (var m in hull.Markers)
        {
            markers.Add(new SettlementMarker(m.Id, new Vector3i(m.X, m.Y, m.Z), data: m.Config));
        }

        return (g, markers);
    }

    /// <summary>The hull as a giant ship template with a compact body.</summary>
    public static StructureTemplate Template(Hull hull, string key = "test_hulk", string style = "")
    {
        var t = new StructureTemplate
        {
            Key = key,
            GiantShip = true,
            Width = hull.W,
            Height = hull.H,
            Length = hull.L,
            GroundRow = hull.Ground,
            Style = style,
            PinOnly = true,
        };
        foreach (var (x, y, z, k, keep) in hull.Blocks)
        {
            t.Cells.Add(new TemplateCell { Kind = "block", Id = k, X = x, Y = y, Z = z, Keep = keep });
        }

        t.Cells.AddRange(hull.Markers);
        TemplateBody.Compact(t);
        return t;
    }
}
