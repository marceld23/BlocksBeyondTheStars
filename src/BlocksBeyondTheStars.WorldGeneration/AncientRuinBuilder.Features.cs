// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The entrance tube, the basins and the ore, the crates and the remains of a buried ruin (#3334, #3338, #3342).</summary>
internal sealed partial class AncientRuinBuilder
{
    // ---------------------------------------------------------------- the entrance

    /// <summary>The entrance tube: a 3 × 3 column from the floor to the ceiling — purple with red bands and a red light over its
    /// doorway for a G.D.S. ruin (<paramref name="gds"/>), plain stone with a warm light for an enclosed structure — the ladder in
    /// its middle, the doorway onto the free lane (+X or +Z). Returns the doorway's face.</summary>
    public int Tube(bool gds)
    {
        var (sx, sz) = Shaft;
        bool alongX = U(LaneDoor, sx, 0, sz) < 0.5;
        int ddx = alongX ? 1 : 0, ddz = alongX ? 0 : 1;
        for (int y = 1; y < _h; y++)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        Set(sx, y, sz, _ladder);
                    }
                    else if (gds)
                    {
                        Set(sx + dx, y, sz + dz, _wall, y % 4 == 3 ? Red : Purple);
                    }
                    else
                    {
                        Set(sx + dx, y, sz + dz, _stone);
                    }
                }

        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!Solid(Get(sx + dx, 0, sz + dz)))
                {
                    Set(sx + dx, 0, sz + dz, _floor); // the tube stands on floor, never on a pool
                }
            }

        Set(sx + ddx, 1, sz + ddz, 0);
        Set(sx + ddx, 2, sz + ddz, 0);
        Set(sx + ddx, 3, sz + ddz, gds ? _lightRed : _warmLight); // a light over the way back up
        for (int side = -1; side <= 1; side++)
        {
            int x = sx + 2 * ddx + ddz * side, z = sz + 2 * ddz + ddx * side;
            if (!In(x, 1, z))
            {
                continue;
            }

            if (!Solid(Get(x, 0, z)))
            {
                Set(x, 0, z, _floor);
            }

            for (int y = 1; y <= 3; y++)
            {
                Set(x, y, z, 0); // the landing in front of the doorway stays clear
            }
        }

        return alongX ? 2 : 4;
    }

    // ---------------------------------------------------------------- the flood and the ore

    /// <summary>The floor columns that sink into a flooded basin: every Ressaw cell at floor level away from the footprint's
    /// rim and the tube (those become floor).</summary>
    public List<(int X, int Z)> Basins()
    {
        var cols = new List<(int X, int Z)>();
        for (int x = 0; x < _w; x++)
            for (int z = 0; z < _l; z++)
            {
                if (Get(x, 0, z) != _ressaw)
                {
                    continue;
                }

                if (x == 0 || z == 0 || x == _w - 1 || z == _l - 1 || Chebyshev(x, z) <= 2)
                {
                    Set(x, 0, z, _floor);
                    continue;
                }

                cols.Add((x, z));
            }

        return cols;
    }

    /// <summary>A handful of Ressaw crystal ore cells in the stone under the basins (#3338) — one per twelve basin columns,
    /// clamped to <see cref="AncientRuinGenerator.MinOre"/>…<see cref="AncientRuinGenerator.MaxOre"/>.</summary>
    public List<Vector3i> Ore(List<(int X, int Z)> basins)
    {
        var ranked = new List<(double U, int X, int Z)>(basins.Count);
        foreach (var (x, z) in basins)
        {
            ranked.Add((U(LaneOre, x, 0, z), x, z));
        }

        ranked.Sort((a, b) => a.U.CompareTo(b.U));
        int n = System.Math.Min(ranked.Count,
            System.Math.Clamp(ranked.Count / 12, AncientRuinGenerator.MinOre, AncientRuinGenerator.MaxOre));
        var ore = new List<Vector3i>(n);
        for (int i = 0; i < n; i++)
        {
            ore.Add(new Vector3i(ranked[i].X, -AncientRuinGenerator.BasinDepth - 1, ranked[i].Z));
        }

        return ore;
    }

    // ---------------------------------------------------------------- the crates

    /// <summary>The crate cells: every crate marker of the modules that still stands on dry floor, ordered ground floor first,
    /// then nearest the entrance — so crates 0–3 (the diving suit) are the easiest to reach without it. Fewer than
    /// <see cref="AncientRuinGenerator.MinCaches"/> are topped up with crates on the street around the entrance. Each cell gets a
    /// crate block; the server puts the container (<c>gds_cache:n</c>) into it.</summary>
    public List<Vector3i> Caches(IReadOnlyList<SettlementMarker> markers)
    {
        var seen = new HashSet<Vector3i>();
        var spots = new List<Vector3i>();
        foreach (var m in markers)
        {
            var p = m.LocalPos;
            if ((m.Type == "loot" || m.Type == "chest") && DryFloor(p.X, p.Y, p.Z) && seen.Add(p))
            {
                spots.Add(p);
            }
        }

        for (int r = 3; r < System.Math.Max(_w, _l) && spots.Count < AncientRuinGenerator.MinCaches; r++)
            for (int dx = -r; dx <= r && spots.Count < AncientRuinGenerator.MinCaches; dx++)
                for (int dz = -r; dz <= r && spots.Count < AncientRuinGenerator.MinCaches; dz++)
                {
                    int x = Shaft.X + dx, z = Shaft.Z + dz;
                    var p = new Vector3i(x, 1, z);
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) == r && DryFloor(x, 1, z) && Get(x, 2, z) == 0 && seen.Add(p))
                    {
                        spots.Add(p);
                    }
                }

        spots.Sort(CacheOrder);
        foreach (var p in spots)
        {
            Set(p.X, p.Y, p.Z, _crate); // the container sits in the crate block's cell (the loot code's contract)
        }

        return spots;
    }

    private int CacheOrder(Vector3i a, Vector3i b)
    {
        int c = (a.Y > 1 ? 1 : 0).CompareTo(b.Y > 1 ? 1 : 0);
        if (c != 0)
        {
            return c;
        }

        int da = (a.X - Shaft.X) * (a.X - Shaft.X) + (a.Z - Shaft.Z) * (a.Z - Shaft.Z);
        int db = (b.X - Shaft.X) * (b.X - Shaft.X) + (b.Z - Shaft.Z) * (b.Z - Shaft.Z);
        c = da.CompareTo(db);
        if (c == 0)
        {
            c = a.X.CompareTo(b.X);
        }

        if (c == 0)
        {
            c = a.Z.CompareTo(b.Z);
        }

        return c != 0 ? c : a.Y.CompareTo(b.Y);
    }

    /// <summary>A free cell over solid, dry floor (not Ressaw), outside the tube.</summary>
    private bool DryFloor(int x, int y, int z)
        => In(x, y, z) && y >= 1 && Get(x, y, z) == 0 && Solid(Get(x, y - 1, z)) && Chebyshev(x, z) > 1;

    // ---------------------------------------------------------------- the remains

    /// <summary>The remains in corners (#3342): a share of the free corner cells over solid floor gets an empty diving
    /// helmet, a broken guardian head or — rarely — a skull, its front turned into the room. Never on a crate, at the
    /// entrance or beside a staircase.</summary>
    public List<(Vector3i Cell, ushort Block)> Props(List<Vector3i> caches)
    {
        var placed = new List<(Vector3i Cell, ushort Block)>();
        if (_helmet == 0 || _head == 0 || _skull == 0)
        {
            return placed;
        }

        var taken = new HashSet<Vector3i>(caches);
        var candidates = new List<(double U, int X, int Y, int Z)>();
        for (int x = 0; x < _w; x++)
            for (int y = 1; y < _h - 1; y++)
                for (int z = 0; z < _l; z++)
                {
                    if (Get(x, y, z) == 0 && !Protected(x, z) && Solid(Get(x, y - 1, z)) && Corner(x, y, z)
                        && !BesideShaped(x, y, z) && !taken.Contains(new Vector3i(x, y, z)))
                    {
                        double u = U(LaneProp, x, y, z);
                        if (u < AncientRuinGenerator.PropChance)
                        {
                            candidates.Add((u, x, y, z));
                        }
                    }
                }

        candidates.Sort((a, b) => a.U.CompareTo(b.U));
        for (int i = 0; i < candidates.Count && i < AncientRuinGenerator.MaxProps; i++)
        {
            var (_, x, y, z) = candidates[i];
            double k = U(LanePropKind, x, y, z);
            ushort block = k < AncientRuinGenerator.SkullShare ? _skull
                : k < AncientRuinGenerator.SkullShare + AncientRuinGenerator.HelmetShare ? _helmet : _head;
            Set(x, y, z, block, 0, CubeFacing.Pack(OpenFace(x, y, z)));
            placed.Add((new Vector3i(x, y, z), block));
        }

        return placed;
    }

    private bool Corner(int x, int y, int z)
        => (Solid(Get(x - 1, y, z)) || Solid(Get(x + 1, y, z))) && (Solid(Get(x, y, z - 1)) || Solid(Get(x, y, z + 1)));

    private bool BesideShaped(int x, int y, int z)
        => ShapeAt(x - 1, y, z) != 0 || ShapeAt(x + 1, y, z) != 0 || ShapeAt(x, y, z - 1) != 0 || ShapeAt(x, y, z + 1) != 0
           || ShapeAt(x, y - 1, z) != 0;

    /// <summary>The face (2 +X, 3 −X, 4 +Z, 5 −Z) a prop in a corner shows: one that looks into the open, picked by hash.</summary>
    private int OpenFace(int x, int y, int z)
    {
        var open = new List<int>(4);
        if (Get(x + 1, y, z) == 0) open.Add(2);
        if (Get(x - 1, y, z) == 0) open.Add(3);
        if (Get(x, y, z + 1) == 0) open.Add(4);
        if (Get(x, y, z - 1) == 0) open.Add(5);
        return open.Count == 0 ? 4 : open[(int)(U(LanePropFace, x, y, z) * open.Count)];
    }

    /// <summary>Hands the voxels and the decisions to the layout (counting the Ressaw blocks the finished ruin holds).</summary>
    public AncientRuinLayout Build(AncientRuinFeatures features)
    {
        features.RessawBlocks = 0;
        foreach (ushort b in _blocks)
        {
            features.RessawBlocks += b == _ressawBlock ? 1 : 0;
        }

        return new(_w, _h, _l, _blocks, _mods, _shapes, features);
    }
}
