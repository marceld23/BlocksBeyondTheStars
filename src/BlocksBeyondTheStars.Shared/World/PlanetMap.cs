// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>The eight biome classes a planet map carries (#3978). A map world's planet type lists its biomes in this
/// order, so class <c>i</c> is biome <c>i</c> of the type (a type with fewer biomes clamps to its last one).</summary>
public static class PlanetMapBiome
{
    public const byte Ice = 0;
    public const byte Tundra = 1;
    public const byte BorealForest = 2;
    public const byte Temperate = 3;
    public const byte Steppe = 4;
    public const byte Desert = 5;
    public const byte RainForest = 6;
    public const byte HighMountains = 7;

    /// <summary>How many classes there are — every biome byte of a valid map is below it.</summary>
    public const int Count = 8;
}

/// <summary>
/// A planet map (#3978): the terrain of a map world as a grid of cells — an equirectangular projection of the body onto
/// the torus. Column 0 is longitude 0 (world X 0) and the columns run east around the whole circumference; row 0 is the
/// SOUTH edge of the latitude band (world Z = −period/2) and the rows run north. Two layers: the elevation in metres
/// (signed, sea level 0) and the biome class (<see cref="PlanetMapBiome"/>).
/// <para>Read from <c>data/maps/&lt;key&gt;.bbmap</c> (<see cref="PlanetMapCodec"/>); immutable once loaded. The generator
/// samples it in integer arithmetic only, so server and client build the same ground from it.</para>
/// </summary>
public sealed class PlanetMap
{
    private readonly short[] _elevation;
    private readonly byte[] _biome;

    /// <summary>A map of <paramref name="width"/> × <paramref name="height"/> cells, row-major from the south edge. The
    /// arrays are taken over, not copied.</summary>
    public PlanetMap(int width, int height, short[] elevation, byte[] biome, ulong contentHash = 0)
    {
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A planet map needs at least 2 × 2 cells, got {width} × {height}.");
        }

        if (elevation is null || biome is null || elevation.Length != width * height || biome.Length != width * height)
        {
            throw new ArgumentException($"A {width} × {height} planet map needs {width * height} cells per layer.", nameof(elevation));
        }

        Width = width;
        Height = height;
        _elevation = elevation;
        _biome = biome;
        ContentHash = contentHash != 0 ? contentHash : PlanetMapCodec.HashCells(elevation, biome);
    }

    /// <summary>Cells per row (the full circumference).</summary>
    public int Width { get; }

    /// <summary>Rows (the full latitude period).</summary>
    public int Height { get; }

    /// <summary>The FNV-1a hash of the uncompressed cells — the map's identity (two maps with one hash are one map).</summary>
    public ulong ContentHash { get; }

    /// <summary>The elevation (metres) of a cell. <paramref name="column"/> and <paramref name="row"/> must be in range.</summary>
    public short ElevationAt(int column, int row) => _elevation[row * Width + column];

    /// <summary>The biome class of a cell. <paramref name="column"/> and <paramref name="row"/> must be in range.</summary>
    public byte BiomeAt(int column, int row) => _biome[row * Width + column];
}
