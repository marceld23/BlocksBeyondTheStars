// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A planet map prepared for sampling on the torus (#3978) — the map base form's integer core. Everything here is
/// integer arithmetic (no floating point, no trigonometry), so the server and every client derive the same ground from
/// the same map on any runtime.
/// <para>The map is equirectangular: column 0 = world X 0, the columns span the circumference; row 0 = world Z −P/2
/// (the south edge), the rows span the latitude period P. Both axes wrap like the world. A map's north and south rows
/// are different places on the real body, so the outermost <see cref="SeamBandPercent"/> % of rows on each side are
/// blended toward one common height and the ice class (<see cref="PlanetMapBiome.Ice"/>) once, when the map is
/// prepared — the Z seam then joins without a cliff.</para>
/// </summary>
internal sealed class MapTerrain
{
    /// <summary>The share of rows (percent, each edge) blended toward the seam height.</summary>
    public const int SeamBandPercent = 4;

    /// <summary>Fixed-point scale of the sampled elevation: metres × 256.</summary>
    public const int FixedOne = 256;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<ulong, MapTerrain> Cache = new();
    private const int CacheCap = 8;

    private readonly int[] _elevation; // metres, seam-blended
    private readonly byte[] _biome;    // classes, seam-blended

    private MapTerrain(PlanetMap map)
    {
        Width = map.Width;
        Height = map.Height;
        int cells = Width * Height;
        _elevation = new int[cells];
        _biome = new byte[cells];
        long edgeSum = 0;
        for (int col = 0; col < Width; col++)
        {
            edgeSum += map.ElevationAt(col, 0) + map.ElevationAt(col, Height - 1);
        }

        SeamElevation = (int)(edgeSum / (2L * Width));
        SeamBandRows = Math.Max(1, (Height * SeamBandPercent + 50) / 100);
        for (int row = 0; row < Height; row++)
        {
            int keep = SeamKeep(row); // 0 at the edge … 256 at the band's inner end and beyond
            for (int col = 0; col < Width; col++)
            {
                int i = row * Width + col;
                int e = map.ElevationAt(col, row);
                _elevation[i] = keep >= FixedOne ? e : SeamElevation + (e - SeamElevation) * keep / FixedOne;
                _biome[i] = keep < FixedOne / 2 ? PlanetMapBiome.Ice : map.BiomeAt(col, row);
            }
        }
    }

    /// <summary>Cells per row.</summary>
    public int Width { get; }

    /// <summary>Rows.</summary>
    public int Height { get; }

    /// <summary>The common height (metres) the north and south edges blend to: the mean of the two edge rows.</summary>
    public int SeamElevation { get; }

    /// <summary>How many rows on each edge the seam blend covers.</summary>
    public int SeamBandRows { get; }

    /// <summary>The prepared terrain of <paramref name="map"/> — built once per map content and shared (it is immutable).</summary>
    public static MapTerrain For(PlanetMap map)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(map.ContentHash, out var cached))
            {
                return cached;
            }

            if (Cache.Count >= CacheCap)
            {
                Cache.Clear();
            }

            var terrain = new MapTerrain(map);
            Cache[map.ContentHash] = terrain;
            return terrain;
        }
    }

    /// <summary>The elevation at a world column in fixed point (metres × <see cref="FixedOne"/>): the bilinear blend of
    /// the four cell centres around it, wrapping on both axes.</summary>
    public long ElevationFixed(int worldX, int worldZ, int circumference, int period)
    {
        Locate(worldX, circumference, Width, out int c0, out int c1, out int fx);
        Locate(Canonical(worldZ, period), period, Height, out int r0, out int r1, out int fz);
        long south = (long)_elevation[r0 * Width + c0] * (FixedOne - fx) + (long)_elevation[r0 * Width + c1] * fx;
        long north = (long)_elevation[r1 * Width + c0] * (FixedOne - fx) + (long)_elevation[r1 * Width + c1] * fx;
        return (south * (FixedOne - fz) + north * fz) >> 8; // (metres × 65536) → metres × 256, floored
    }

    /// <summary>The biome class of the cell a world column lies in (no blending: a class is a category).</summary>
    public byte BiomeClassAt(int worldX, int worldZ, int circumference, int period)
    {
        int col = (int)((long)WorldConstants.WrapX(worldX, circumference) * Width / circumference);
        int row = (int)((long)Canonical(worldZ, period) * Height / period);
        return _biome[Math.Min(row, Height - 1) * Width + Math.Min(col, Width - 1)];
    }

    /// <summary>The height curve (#3978): the blocks above (or, negative, below) the map's 0 m for an elevation in fixed
    /// point — the lowlands stretched up to the knee, the high mountains compressed above it, the sea floor at its own
    /// scale and held at the type's deepest depth. Floored, so 0 m and up is never below the sea and anything below
    /// 0 m always is.</summary>
    public static int HeightBlocks(long elevationFixed, PlanetType planet)
    {
        long blocksFixed;
        if (elevationFixed >= 0)
        {
            long knee = (long)planet.MapKneeMetres * FixedOne;
            blocksFixed = elevationFixed <= knee
                ? elevationFixed / planet.MapLowlandMetresPerBlock
                : knee / planet.MapLowlandMetresPerBlock + (elevationFixed - knee) / planet.MapHighlandMetresPerBlock;
        }
        else
        {
            blocksFixed = FloorDiv(elevationFixed, planet.MapOceanMetresPerBlock);
        }

        int blocks = (int)(blocksFixed >> 8);
        return Math.Max(blocks, -planet.MapOceanMaxDepth);
    }

    /// <summary>How much of a row's own elevation survives the seam blend (fixed point, 0..<see cref="FixedOne"/>): a
    /// smoothstep from 0 on the edge row to <see cref="FixedOne"/> at the band's inner end.</summary>
    private int SeamKeep(int row)
    {
        int d = Math.Min(row, Height - 1 - row);
        if (d >= SeamBandRows)
        {
            return FixedOne;
        }

        int t = (2 * d + 1) * FixedOne / (2 * SeamBandRows); // the row centre's place in the band
        return t * t * (3 * FixedOne - 2 * t) / (FixedOne * FixedOne);
    }

    /// <summary>The two cells around a coordinate in [0, <paramref name="span"/>) on an axis of <paramref name="cells"/>
    /// wrapping cells, and the fixed-point weight of the second — measured between cell centres.</summary>
    private static void Locate(int coordinate, int span, int cells, out int first, out int second, out int weight)
    {
        long wrapped = ((coordinate % (long)span) + span) % span;
        long position = wrapped * cells * FixedOne / span - FixedOne / 2; // cell centres sit at i + ½
        int index = (int)(position >> 8);
        weight = (int)(position & (FixedOne - 1));
        first = ((index % cells) + cells) % cells;
        second = (first + 1) % cells;
    }

    /// <summary>A world Z as a row coordinate in [0, period): the canonical latitude shifted so the south edge is 0.</summary>
    private static int Canonical(int worldZ, int period)
    {
        int half = period / 2;
        return (((worldZ + half) % period) + period) % period;
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
    }
}
