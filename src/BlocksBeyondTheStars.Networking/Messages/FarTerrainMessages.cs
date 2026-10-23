// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Server → client (#1820): everything the client's own generator needs to draw THIS world's far terrain
/// exactly as the server generates it — sent after every JoinAccepted / WorldReset. The preview generators (minimap,
/// orbit sphere) approximate; the far view sits next to real chunks, so it must match them.</summary>
public sealed class FarTerrainWorldInfo
{
    public int WorldId { get; set; }
    public string LocationId { get; set; } = string.Empty;
    public string PlanetType { get; set; } = string.Empty;
    public int Circumference { get; set; }
    public bool Cratered { get; set; }
    public bool ContinentsEnabled { get; set; }
    public bool LavaCoreVolcanoes { get; set; }
    public int TerrainGeneration { get; set; }

    /// <summary>A void world (station, ship interior) has no terrain: the far view switches off.</summary>
    public bool Void { get; set; }

    /// <summary>The world's flattened landing pads, <see cref="PadStride"/> ints each: centreX, centreZ, surfaceY,
    /// radius, islet (0/1), plateauRadius, isletRadius, classicShape (0/1).</summary>
    public int[] Pads { get; set; } = System.Array.Empty<int>();

    public const int PadStride = 8;
}

/// <summary>Client → server (#1821): which far-terrain tiles the client's far view needs. Tiles are
/// <see cref="FarTerrainTile.TileBlocks"/>-block squares on the canonical block grid; the server answers each with a
/// <see cref="FarTerrainTile"/> and re-sends it when an edit changes it.</summary>
public sealed class FarTerrainTileRequest
{
    /// <summary>The world the client asks about (the WorldId of its last JoinAccepted / WorldReset).</summary>
    public int WorldId { get; set; }

    /// <summary>Flat (tileX, tileZ) pairs, canonical tile indices. The server serves at most
    /// <see cref="FarTerrainTile.MaxTilesPerRequest"/> pairs per request.</summary>
    public int[] Tiles { get; set; } = System.Array.Empty<int>();
}

/// <summary>Server → client (#1821): the persisted builds of one far-terrain tile — cities, settlements, ruins and
/// player builds, which the client's seed-based far terrain cannot know. One entry per
/// <see cref="CellBlocks"/>×<see cref="CellBlocks"/> cell that holds any non-air edit: the highest such edit's height,
/// block and tint. The four arrays run in step; an empty tile has empty arrays.</summary>
public sealed class FarTerrainTile
{
    public const int TileBlocks = 64;
    public const int CellBlocks = 4;
    public const int CellsPerSide = TileBlocks / CellBlocks;
    public const int MaxTilesPerRequest = 48;

    public int WorldId { get; set; }
    public int TileX { get; set; }
    public int TileZ { get; set; }

    /// <summary>Bumped whenever an edit inside the tile changes it (the client keeps the newest).</summary>
    public int Version { get; set; }

    /// <summary>Cell index within the tile: cellZ × <see cref="CellsPerSide"/> + cellX.</summary>
    public byte[] Cells { get; set; } = System.Array.Empty<byte>();
    public short[] TopY { get; set; } = System.Array.Empty<short>();
    public ushort[] Blocks { get; set; } = System.Array.Empty<ushort>();
    public int[] Tints { get; set; } = System.Array.Empty<int>();
}
