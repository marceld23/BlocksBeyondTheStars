// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 14 (#2038, the fruit trees) — the fruit under a stamped crown, partial of <see cref="WorldGenerator"/>.
/// About a third of the leafy trees hang 2–5 fruit under their lowest leaves. Which of the world's active fruit shapes a
/// tree KIND bears and the colour of all its fruit are rolled per world and kind (<see cref="FruitRules"/>); the colour
/// is written into the fruit cell's tint modifier, which the chunk message already carries and the mesher already
/// renders (the dye recolour), so no client rule exists. The recorder wraps the one tree's <c>set</c> callback and
/// keeps the builder's whole cell sequence — clipped or not — so a tree straddling a chunk edge picks the same fruit
/// cells from every chunk; each chunk then writes only the cells it owns, into air only, and only under a leaf it can
/// see was really written. Nothing here runs below generation 14; the builders and their salts are untouched. Kept in
/// its own file (#1740: the big methods stay small).
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>Records the cells one tree writes (or tries to write) on its way into the chunk.</summary>
    internal sealed class FruitRecorder
    {
        private readonly System.Action<int, int, int, BlockId, bool> _inner;
        private readonly ushort _foliage;

        /// <summary>Every leaf cell the builder wrote or tried to write, in builder order.</summary>
        public readonly List<(int X, int Y, int Z)> Leaves = new();

        /// <summary>Every cell of the tree — leaves and logs — whether or not this chunk kept it.</summary>
        public readonly HashSet<(int X, int Y, int Z)> Cells = new();

        /// <summary>The fruit block this tree hangs.</summary>
        public readonly BlockId Fruit;

        /// <summary>The colour (0xRRGGBB) of every fruit on this tree kind of this world.</summary>
        public readonly int Tint;

        /// <summary>The foliage block the fruit hangs from (a leaf, a needle, a frond).</summary>
        public readonly BlockId Foliage;

        public FruitRecorder(System.Action<int, int, int, BlockId, bool> inner, BlockId foliage, BlockId fruit, int tint)
        {
            _inner = inner;
            _foliage = foliage.Value;
            Foliage = foliage;
            Fruit = fruit;
            Tint = tint;
        }

        /// <summary>The builder's <c>set</c>: record, then write as before.</summary>
        public void Set(int x, int y, int z, BlockId block, bool overwrite)
        {
            Cells.Add((x, y, z));
            if (block.Value == _foliage)
            {
                Leaves.Add((x, y, z));
            }

            _inner(x, y, z, block, overwrite);
        }
    }

    /// <summary>A recorder for the tree about to be built at this column, or null when it bears no fruit: below generation
    /// 14, a kind that bears none, a tree that did not roll fruit (<see cref="FruitRules.FruitTreeShare"/>), or a world
    /// whose roster gave the kind no shape.</summary>
    private FruitRecorder? FruitRecorderFor(PlanetType planet, long seed, TreeKind kind, int wx, int wz, BlockId foliage,
        System.Action<int, int, int, BlockId, bool> inner)
    {
        if (_terrainGeneration < WorldDescription.FruitTreesGeneration || !FruitRules.BearsFruit(kind))
        {
            return null;
        }

        ulong h = FruitRules.TreeHash(seed, WorldConstants.WrapX(wx, _circumference), wz);
        if (!FruitRules.TreeBearsFruit(h))
        {
            return null;
        }

        ResolveFlora(planet); // memoised — the active fruit shapes of this world
        string? shape = FruitRules.ShapeFor(RosterSeed, kind, _activeFruitKeys);
        if (shape == null || _content.GetBlock(shape) is not { } fruit || fruit.NumericId.IsAir)
        {
            return null;
        }

        return new FruitRecorder(inner, foliage, fruit.NumericId, FruitRules.TintFor(RosterSeed, kind));
    }

    /// <summary>Hangs the recorded tree's fruit: the cells <see cref="FruitRules.PickFruitCells"/> chose, written into this
    /// chunk only, into air only, and only under a leaf that was really written (a crown pressed into a slope grows no
    /// fruit under rock — when the leaf lies in the chunk above, the tree's own geometry is trusted).</summary>
    private void HangFruit(FruitRecorder rec, ChunkData chunk, Vector3i origin, long seed, int wx, int wz)
    {
        ulong h = FruitRules.TreeHash(seed, WorldConstants.WrapX(wx, _circumference), wz);
        int cs = WorldConstants.ChunkSize;
        foreach (var (x, y, z) in FruitRules.PickFruitCells(rec.Leaves, rec.Cells, h))
        {
            int lx = x - origin.X, ly = y - origin.Y, lz = z - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                continue; // a neighbour chunk hangs that one
            }

            if (!chunk.Get(lx, ly, lz).IsAir)
            {
                continue; // fruit fills air only — never terrain, a plant or the tree itself
            }

            if (ly + 1 < cs && chunk.Get(lx, ly + 1, lz).Value != rec.Foliage.Value)
            {
                continue; // the leaf it would hang from was never written
            }

            chunk.Set(lx, ly, lz, rec.Fruit);
            chunk.SetModifier(lx, ly, lz, rec.Tint, 0);
        }
    }
}
