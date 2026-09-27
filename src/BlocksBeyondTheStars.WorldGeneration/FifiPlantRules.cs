// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The Fifi plant's rules (#2085, terrain generation 16) — pure functions shared by world generation
/// (<c>WorldGenerator.FifiPlantsGen16.cs</c>) and the tests. Sophie's plant grows everywhere there is soft natural ground
/// and stands in groups: the world is cut into square grove cells, a share of the cells holds one grove, and a grove is
/// three to six plants around its centre, their trunks at least <see cref="MinSpacing"/> apart — every member lies inside
/// its own cell, so whether a column roots a plant is a pure function of that column. Every plant is built from its own
/// hash: a green trunk, a rounded crown of yellow leaves, pink blossoms on the crown's top and sides, and berries hanging
/// under the lowest leaves (the fruit trees' cell picker). The shape is relative to the trunk's root cell, so a plant
/// straddling a chunk edge is the same from every chunk that builds a part of it.
/// </summary>
public static class FifiPlantRules
{
    /// <summary>The ground a Fifi plant roots on — Sophie: "everywhere". Soft natural ground of every climate: meadow,
    /// earth, swamp and bog, alien meadows and mycelium, sand (the desert and the shore) and snow.</summary>
    public static readonly IReadOnlyList<string> HostKeys = new[] { "grass", "dirt", "mud", "peat", "alien_grass", "mycelium", "sand", "snow" };

    /// <summary>The widest a crown reaches from its trunk — the stamp's chunk-edge scan margin.</summary>
    public const int CrownRadiusMax = 3;

    /// <summary>The highest cell a plant writes above its column's surface (bare trunk ≤ 6 + crown ≤ 5 + the root row) —
    /// inside the stamps' common <c>MaxStampRise</c> of 18.</summary>
    public const int MaxRise = 12;

    /// <summary>The edge of a grove cell (blocks). A grove never leaves its cell.</summary>
    public const int GroveCell = 48;

    /// <summary>Share of the grove cells that hold a grove — about one grove per 6 500 columns of the world.</summary>
    public const double GroveChance = 0.35;

    /// <summary>Fewest and most plants of one grove (before the ground takes some away — a grove at the shore is smaller).</summary>
    public const int MinGroup = 3;

    /// <summary>Most plants of one grove.</summary>
    public const int MaxGroup = 6;

    /// <summary>The ring the members stand on around the grove's centre (blocks from the centre).</summary>
    public const int RingMin = 5;

    /// <summary>The outer edge of the ring.</summary>
    public const int RingMax = 8;

    /// <summary>Trunks of one grove stand at least this far apart (straight-line), so neighbouring crowns touch at most.</summary>
    public const int MinSpacing = 5;

    /// <summary>Fewest and most berry clusters on one plant ("it always bears berries").</summary>
    public const int MinBerries = 3;

    /// <summary>Most berry clusters on one plant.</summary>
    public const int MaxBerries = 6;

    /// <summary>Fewest and most pink blossoms on one crown (each one a small light source).</summary>
    public const int MinBlossoms = 5;

    /// <summary>Most pink blossoms on one crown.</summary>
    public const int MaxBlossoms = 8;

    private const long GroveSalt = 0xF1F1D8;
    private const long PlantSalt = 0xF1F1A5;
    private const long SizeSalt = 0xF1F1B6;

    /// <summary>The hash every decision of one plant derives from (the root column; X and Z already wrapped).</summary>
    public static ulong PlantHash(long seed, int wrappedX, int wrappedZ) => Mix(seed ^ PlantSalt, wrappedX, wrappedZ);

    /// <summary>The grove cell of a column and the column's place inside it: X wraps at the circumference, Z at the latitude
    /// period (counted from its southern edge). The last cell of each axis is cut short where the period does not divide
    /// evenly; a cell too short for a whole grove holds none.</summary>
    public static (int CellX, int CellZ, int LocalX, int LocalZ, int Width, int Depth) CellOf(int worldX, int worldZ, int circumference)
    {
        int period = WorldConstants.LatitudePeriodFor(circumference);
        int ux = WorldConstants.WrapX(worldX, circumference);
        int uz = WorldConstants.WrapZ(worldZ, circumference) + period / 2;
        int cellX = ux / GroveCell, cellZ = uz / GroveCell;
        int width = System.Math.Min(GroveCell, circumference - cellX * GroveCell);
        int depth = System.Math.Min(GroveCell, period - cellZ * GroveCell);
        return (cellX, cellZ, ux - cellX * GroveCell, uz - cellZ * GroveCell, width, depth);
    }

    /// <summary>Every integer offset on the grove ring (<see cref="RingMin"/>..<see cref="RingMax"/> from the centre), sorted
    /// once around the circle by EXACT integer comparisons (half plane, then the cross product, then the distance) — no
    /// trigonometry, so every platform places a grove's members on the very same cells (the golden hashes pin them).</summary>
    private static readonly (int X, int Z)[] Ring = BuildRing();

    private static readonly (int X, int Z)[] NoGrove = System.Array.Empty<(int X, int Z)>();

    private static (int X, int Z)[] BuildRing()
    {
        var ring = new List<(int X, int Z)>();
        for (int x = -RingMax; x <= RingMax; x++)
            for (int z = -RingMax; z <= RingMax; z++)
            {
                int d = x * x + z * z;
                if (d >= RingMin * RingMin && d <= RingMax * RingMax)
                {
                    ring.Add((x, z));
                }
            }

        static int Half((int X, int Z) p) => p.Z > 0 || (p.Z == 0 && p.X > 0) ? 0 : 1;
        ring.Sort((a, b) =>
        {
            int ha = Half(a), hb = Half(b);
            if (ha != hb)
            {
                return ha.CompareTo(hb);
            }

            long cross = (long)a.X * b.Z - (long)a.Z * b.X;
            if (cross != 0)
            {
                return cross > 0 ? -1 : 1; // counter-clockwise first
            }

            return (a.X * a.X + a.Z * a.Z).CompareTo(b.X * b.X + b.Z * b.Z);
        });
        return ring.ToArray();
    }

    /// <summary>The members of the grove in one cell, as places inside the cell (empty when the cell holds no grove). The
    /// centre sits far enough from the cell's edges that the whole ring fits; the members stand on the ring at even steps
    /// around it with a little jitter, one more in the centre of a big grove now and then, and a member that would crowd an
    /// earlier one is left out.</summary>
    public static IReadOnlyList<(int X, int Z)> GroveMembers(long seed, int cellX, int cellZ, int width, int depth)
    {
        int edge = RingMax + 1;
        if (width < 2 * edge + 1 || depth < 2 * edge + 1)
        {
            return NoGrove; // the short last cell of an axis
        }

        ulong h = Mix(seed ^ GroveSalt, cellX, cellZ);
        if ((h >> 11) * (1.0 / (1UL << 53)) >= GroveChance)
        {
            return NoGrove; // two cells in three — asked for every column of them, so nothing is allocated
        }

        var members = new List<(int X, int Z)>();

        int cx = edge + (int)((h >> 3) % (ulong)(width - 2 * edge));
        int cz = edge + (int)((h >> 17) % (ulong)(depth - 2 * edge));
        int n = MinGroup + (int)((h >> 31) % (ulong)(MaxGroup - MinGroup + 1));
        int len = Ring.Length;
        int start = (int)((h >> 37) % (ulong)len);
        int jitter = System.Math.Max(1, len / (3 * n)); // a third of the step each way at most
        if (n >= 5 && ((h >> 50) & 1UL) == 1UL)
        {
            members.Add((cx, cz)); // a big grove sometimes has one plant in its middle
        }

        for (int i = 0; i < n; i++)
        {
            ulong hi = Mix(unchecked((long)h), i, 0x6E);
            int idx = (start + i * len / n + (int)(hi % (ulong)jitter)) % len;
            int x = cx + Ring[idx].X;
            int z = cz + Ring[idx].Z;
            bool crowded = false;
            foreach (var (mx, mz) in members)
            {
                if ((mx - x) * (mx - x) + (mz - z) * (mz - z) < MinSpacing * MinSpacing)
                {
                    crowded = true;
                    break;
                }
            }

            if (!crowded)
            {
                members.Add((x, z));
            }
        }

        return members;
    }

    /// <summary>True when the column is a member of its cell's grove (the ground is checked by world generation).</summary>
    public static bool IsGroveMember(long seed, int worldX, int worldZ, int circumference)
    {
        var c = CellOf(worldX, worldZ, circumference);
        foreach (var (x, z) in GroveMembers(seed, c.CellX, c.CellZ, c.Width, c.Depth))
        {
            if (x == c.LocalX && z == c.LocalZ)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One plant's cells relative to its root cell (the air cell above the ground at the trunk column).</summary>
    public sealed class Shape
    {
        /// <summary>The trunk, from the root cell up into the crown's centre (written over whatever stands there).</summary>
        public readonly List<(int X, int Y, int Z)> Trunk = new();

        /// <summary>The yellow leaves (air only).</summary>
        public readonly List<(int X, int Y, int Z)> Leaves = new();

        /// <summary>The pink blossoms on the crown's top and sides (air only).</summary>
        public readonly List<(int X, int Y, int Z)> Blossoms = new();

        /// <summary>The berry clusters, each in the air cell directly under a leaf (air only).</summary>
        public readonly List<(int X, int Y, int Z)> Berries = new();

        /// <summary>The crown's horizontal radius (2 or 3).</summary>
        public int CrownRadius;

        /// <summary>The highest cell (relative Y) of the plant.</summary>
        public int Top;
    }

    /// <summary>Builds one plant from its hash and its bell-shaped size factor (about 0.75–1.25): a bigger plant has the wider
    /// crown and the longer bare trunk. The crown is a rounded, slightly flattened ball centred on the trunk's top; blossoms
    /// sit on its outside (never on its underside, which is where the berries hang), always one on the very top.</summary>
    public static Shape ShapeFor(ulong plantHash, double sizeFactor)
    {
        var s = new Shape();
        int r = sizeFactor >= 1.0 ? 3 : 2;
        int clear = System.Math.Clamp((int)System.Math.Round(5.0 * sizeFactor), 4, 6); // bare trunk under the crown
        int layers = r - 1;                                                           // crown layers above and below its centre
        int cy = clear + layers;                                                      // crown centre = the trunk's top
        double rh = r + 0.45, rv = layers + 0.45;
        s.CrownRadius = r;
        s.Top = cy + layers;

        var tree = new HashSet<(int X, int Y, int Z)>();
        for (int y = 0; y <= cy; y++)
        {
            s.Trunk.Add((0, y, 0));
            tree.Add((0, y, 0));
        }

        var crown = new List<(int X, int Y, int Z)>();
        var crownSet = new HashSet<(int X, int Y, int Z)>();
        for (int dy = layers; dy >= -layers; dy--) // top down: the blossom walk below starts at the crown's top
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    double e = (dx * dx + dz * dz) / (rh * rh) + (double)(dy * dy) / (rv * rv);
                    var cell = (dx, cy + dy, dz);
                    if (e <= 1.0 && !tree.Contains(cell))
                    {
                        crown.Add(cell);
                        crownSet.Add(cell);
                    }
                }

        // Blossom candidates: crown cells that show to the sky or to a side, with crown (or trunk) beneath them.
        var candidates = new List<(int X, int Y, int Z)>();
        foreach (var (x, y, z) in crown)
        {
            bool covered = crownSet.Contains((x, y - 1, z)) || tree.Contains((x, y - 1, z));
            bool outside = !crownSet.Contains((x, y + 1, z)) || !crownSet.Contains((x + 1, y, z)) || !crownSet.Contains((x - 1, y, z))
                || !crownSet.Contains((x, y, z + 1)) || !crownSet.Contains((x, y, z - 1));
            if (covered && outside)
            {
                candidates.Add((x, y, z));
            }
        }

        var blossoms = new HashSet<(int X, int Y, int Z)>();
        var top = (0, s.Top, 0);
        if (crownSet.Contains(top))
        {
            blossoms.Add(top); // the crown flower on the very top
        }

        int count = MinBlossoms + (int)((plantHash >> 24) % (ulong)(MaxBlossoms - MinBlossoms + 1));
        int n = candidates.Count;
        if (n > 0)
        {
            // Spread evenly around the crown from a hashed start (distinct indices while count ≤ n).
            int start = (int)((plantHash >> 32) % (ulong)n);
            for (int i = 0; i < count && blossoms.Count < count; i++)
            {
                blossoms.Add(candidates[(start + (int)((long)i * n / count)) % n]);
            }
        }

        foreach (var cell in crown)
        {
            if (blossoms.Contains(cell))
            {
                s.Blossoms.Add(cell);
            }
            else
            {
                s.Leaves.Add(cell);
            }

            tree.Add(cell);
        }

        // Berries under the lowest LEAVES (blossoms are never bottom-exposed, so every berry hangs from a leaf).
        s.Berries.AddRange(FruitRules.PickFruitCells(s.Leaves, tree, plantHash, MinBerries, MaxBerries));
        return s;
    }

    /// <summary>The size factor's salt (the generator turns it into a bell-shaped factor from the root column).</summary>
    public static long SizeSeed(long seed) => seed + SizeSalt;

    private static ulong Mix(long a, long b, long c)
    {
        unchecked
        {
            ulong h = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)c * 0x165667B19E3779F9UL;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 29;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 32;
            return h;
        }
    }
}
