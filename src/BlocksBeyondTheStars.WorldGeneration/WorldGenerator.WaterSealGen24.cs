// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 24 (#2516): sealed water. The fluid automaton treats generated water above the sea line as finite
/// (#2515), and water the generator leaves open — a pond surface stepping with the ground, a cave carved beside the sea —
/// drains or spills the moment a player touches it. From this generation the generator encloses what it fills: one flat
/// level per pond (the lowest ground on its rim), a cave shield around the water of the neighbouring columns (see the
/// chunk fill), and rivers no higher than their banks (<see cref="RiverField"/>). (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>A pond component larger than this many columns is not resolved: it keeps the classic per-column fill. The
    /// verdict is the same from every column of the component (each search stops at the cap), so it is deterministic.</summary>
    private const int PondComponentCap = 4096;

    /// <summary>The pond-level memo is dropped beyond this many columns.</summary>
    private const int PondLevelCacheCap = 262_144;

    /// <summary>The memo's answer for a component too large to resolve: fill each column to its own ground, as before.</summary>
    private const int PondLevelClassic = int.MinValue;

    /// <summary>How far a pond column's ground may stand above the pond's level and still be dug out to hold water — the
    /// highest bank a sealed pond cuts. Higher ground inside the pond mask stays dry land.</summary>
    private const int PondMaxBank = 2;

    private readonly Dictionary<(string, long), int> _pondLevels = new();
    private readonly object _pondLevelLock = new object();
    private int _pondLevelSearches;
    private long _pondLevelVisits;

    /// <summary>Test seam: a smaller component cap for this generator instance (0 = <see cref="PondComponentCap"/>), so a
    /// test can reach the "too big" path on an ordinary world.</summary>
    internal int PondComponentCapOverride { get; set; }

    /// <summary>Test seam: how many pond-level searches ran, and how many columns they examined in all.</summary>
    internal (int Searches, long Visits) PondLevelStatsForTest => (_pondLevelSearches, System.Threading.Interlocked.Read(ref _pondLevelVisits));

    /// <summary>True on a world of the sealed-water generation (#2516).</summary>
    private bool WaterSealed => _terrainGeneration >= WorldDescription.WaterSealGeneration;

    /// <summary>Generation 24: the pond gate's look at the direct neighbours — the ±2 slope sample cannot see a one-block
    /// spike, and a pond carved into one stood as a free column of water over the land around it.</summary>
    private bool PondFitsItsNeighbours(PlanetType planet, int worldX, int worldZ)
    {
        int h = SurfaceHeight(planet, worldX, worldZ);
        return System.Math.Abs(SurfaceHeight(planet, worldX + 1, worldZ) - h) <= 2
            && System.Math.Abs(SurfaceHeight(planet, worldX - 1, worldZ) - h) <= 2
            && System.Math.Abs(SurfaceHeight(planet, worldX, worldZ + 1) - h) <= 2
            && System.Math.Abs(SurfaceHeight(planet, worldX, worldZ - 1) - h) <= 2;
    }

    /// <summary>
    /// The water of an upland pond at a column — the one rule the chunk fill and every surface-water query share. Below
    /// generation 24 a pond column fills to its own ground over a bed carved <c>depth</c> deep. From generation 24 the
    /// whole pond fills to one level, the lowest ground on its rim; a column whose carved bed would not reach below that
    /// level stays dry land, and a pond whose rim dips to the sea line is no pond at all.
    /// </summary>
    private bool TryGetPondWater(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, int fluidLevel,
        double threshold, double mask, out int top, out int bed)
    {
        top = bed = 0;
        int depth = PondDepthFromMask(planet, seed, worldX, worldZ, threshold, mask);
        if (depth <= 0)
        {
            return false;
        }

        bed = surfaceY - depth;
        top = surfaceY;
        if (!WaterSealed)
        {
            return true;
        }

        int level = PondLevelAt(planet, seed, worldX, worldZ, threshold, fluidLevel);
        if (level == PondLevelClassic)
        {
            return true;
        }

        if (level <= fluidLevel || surfaceY > level + PondMaxBank)
        {
            return false;
        }

        top = level;
        bed = System.Math.Min(bed, level - 1);
        return true;
    }

    private bool IsPondColumn(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, double threshold, int fluidLevel)
        => surfaceY > fluidLevel
           && PondDepthFromMask(planet, seed, worldX, worldZ, threshold, PondMaskAt(planet, seed, worldX, worldZ)) > 0;

    /// <summary>The level of the pond this column belongs to: the lowest ground among the columns that touch the pond
    /// (4-connected) — a pure function of the component, whichever of its columns asks. Memoised for every column of the
    /// component at once.</summary>
    private int PondLevelAt(PlanetType planet, long seed, int worldX, int worldZ, double threshold, int fluidLevel)
    {
        var key = (planet.Key, ColumnKey(worldX, worldZ));
        lock (_pondLevelLock)
        {
            if (_pondLevels.TryGetValue(key, out int cached))
            {
                return cached;
            }
        }

        System.Threading.Interlocked.Increment(ref _pondLevelSearches);
        int cap = PondComponentCapOverride > 0 ? PondComponentCapOverride : PondComponentCap;
        var seen = new HashSet<long> { key.Item2 };
        var component = new List<long> { key.Item2 };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue((worldX, worldZ));
        int rimMin = int.MaxValue;
        int? known = null; // the verdict of a column of this same component that an earlier search already settled
        while (queue.Count > 0 && known is null)
        {
            var (x, z) = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                long nk = ColumnKey(nx, nz);
                if (!seen.Add(nk))
                {
                    continue;
                }

                System.Threading.Interlocked.Increment(ref _pondLevelVisits);
                int nh = SurfaceHeight(planet, nx, nz); // once per visit: the pond gate and the rim both read it (#2759)
                if (IsPondColumn(planet, seed, nx, nz, nh, threshold, fluidLevel))
                {
                    // A pond column is only ever reached through its own component, so a memoised one carries this
                    // component's verdict — above all "too big", which a search from the far side of a huge component
                    // would otherwise have to rediscover column by column.
                    lock (_pondLevelLock)
                    {
                        if (_pondLevels.TryGetValue((planet.Key, nk), out int theirs))
                        {
                            known = theirs;
                            break;
                        }
                    }

                    component.Add(nk);
                    queue.Enqueue((nx, nz));
                    if (component.Count > cap)
                    {
                        known = PondLevelClassic;
                        break;
                    }
                }
                else
                {
                    rimMin = System.Math.Min(rimMin, nh);
                }
            }
        }

        int level = known ?? (rimMin == int.MaxValue ? PondLevelClassic : rimMin);
        lock (_pondLevelLock)
        {
            if (_pondLevels.Count + component.Count > PondLevelCacheCap)
            {
                _pondLevels.Clear();
            }

            foreach (long k in component)
            {
                _pondLevels[(planet.Key, k)] = level;
            }
        }

        return level;
    }

    /// <summary>The chunk fill's cave shield around the neighbouring columns' fluid: true when <paramref name="y"/> lies in
    /// one of the first <paramref name="count"/> spans.</summary>
    private static bool InSealSpans(System.Span<(int Lo, int Hi)> spans, int count, int y)
    {
        for (int i = 0; i < count; i++)
        {
            if (y >= spans[i].Lo && y <= spans[i].Hi)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the pond-level memo (with the other column memos — it depends on the same world mode).</summary>
    private void InvalidatePondLevels()
    {
        lock (_pondLevelLock)
        {
            _pondLevels.Clear();
        }
    }
}
