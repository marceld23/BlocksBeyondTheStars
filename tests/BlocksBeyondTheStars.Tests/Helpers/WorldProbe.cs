// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The shared world-gen probe: reads generated blocks by world position, one cached chunk per coordinate.</summary>
public sealed class WorldProbe
{
    private readonly WorldGenerator _gen;
    private readonly PlanetType _planet;
    private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

    public WorldProbe(WorldGenerator gen, PlanetType planet)
    {
        _gen = gen;
        _planet = planet;
    }

    /// <summary>The chunks generated so far.</summary>
    public IEnumerable<ChunkData> Chunks => _chunks.Values;

    public BlockId At(int x, int y, int z)
    {
        var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            chunk = _gen.Generate(_planet, coord);
            _chunks[coord] = chunk;
        }

        var origin = WorldConstants.ChunkOrigin(coord);
        return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
    }

    /// <summary>The column's ground cell: the generator's own surface height (no chunk scan — the CI runner pays for every
    /// chunk this probe touches, #2068), or -1 where a cave mouth, hole or lake has taken the ground away.</summary>
    public int Surface(int x, int z)
    {
        int sy = _gen.SurfaceHeight(_planet, x, z);
        return sy is >= 1 and <= 150 && !At(x, sy, z).IsAir ? sy : -1;
    }
}
