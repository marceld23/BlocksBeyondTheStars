// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3522: the server side of the structure builders in <c>WorldGeneration</c> (the vaults, the unique sites, Soaniri's places,
/// the Guardian-core chamber; the hunter camp has a sink of its own). The builders emit their cells in the order the server
/// used to write them and this sink writes each straight into the active world — one <see cref="ServerWorld.SetBlock"/> per
/// cell, as before the move — so the world stays byte-identical.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>
    /// Writes a builder's cells into a world. The seam is wrapped east–west (<see cref="WorldConstants.WrapX(int, int)"/>), or
    /// both ways with <c>canonical</c> (Soaniri's cells); <c>write</c> false drops the block writes but still reads and still
    /// passes the markers on (a vault's re-derive on a later entry, #467). Markers go to <c>onMark</c> — the markers this
    /// writer acts on; without it they are the editor's only.
    /// </summary>
    private sealed class WorldCellSink : IStructureCellSink
    {
        private readonly ServerWorld _world;
        private readonly bool _write;
        private readonly bool _canonical;
        private readonly Action<string, int, int, int, string>? _onMark;

        public WorldCellSink(ServerWorld world, bool write = true, bool canonical = false, Action<string, int, int, int, string>? onMark = null)
        {
            _world = world;
            _write = write;
            _canonical = canonical;
            _onMark = onMark;
        }

        public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
        {
            if (_write)
            {
                _world.SetBlock(Cell(x, y, z), block, tint, glow, shape);
            }
        }

        public BlockId Get(int x, int y, int z) => _world.GetBlock(Cell(x, y, z));

        public void Mark(string type, int x, int y, int z, string data) => _onMark?.Invoke(type, x, y, z, data);

        private Vector3i Cell(int x, int y, int z)
            => _canonical
                ? WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference)
                : new Vector3i(WorldConstants.WrapX(x, _world.Circumference), y, z);
    }

    /// <summary>The active world's natural surface row of a column, the seam wrapped east–west — the ground function the
    /// builders follow (a pillar ring, an aperture's plating).</summary>
    private Func<int, int, int> WrappedSurfaceHeight()
    {
        var planet = _world.Planet;
        int circ = _world.Circumference;
        return (x, z) => _generator.SurfaceHeight(planet, WorldConstants.WrapX(x, circ), z);
    }
}
