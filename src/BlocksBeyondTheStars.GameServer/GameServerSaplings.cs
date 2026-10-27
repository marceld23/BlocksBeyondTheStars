// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The saplings (#1774; #2977 the fruit trees, #2978 the conifer, #2979 the Fifi plant, #2980 the mahogany), partial of
/// <see cref="GameServer"/>. A sapling is flora for placement and harvest (host and hull rules) but no catalog species: it
/// sits in no world roster, no greenhouse grows it, and instead of regrowing it turns into its tree. Its clock rides the
/// flora regrow queue (persisted like a harvest), so a tree planted before a restart still comes. The tree's shape is
/// <see cref="SaplingTrees"/>; the blocks it is made of and its fruit are the kind's (<see cref="SaplingCatalog"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One sapling kind with its blocks resolved against the content (air where the content lacks one).</summary>
    private sealed record PlantedSapling(SaplingKind Kind, BlockId Log, BlockId Crown, BlockId Blossom, BlockId Berries, BlockId Fruit);

    private readonly Dictionary<ushort, PlantedSapling> _saplings = new();

    /// <summary>The classic sapling's id — what a woodcutter replants (#2644) and a base counts (0 when the content has none).</summary>
    private ushort _saplingId;

    /// <summary>Fewest and most fruit a planted fruit tree hangs (#2977) — it always bears some.</summary>
    private const int PlantedFruitMin = 3, PlantedFruitMax = 5;

    private void InitSaplings()
    {
        _saplings.Clear();
        _saplingId = 0;
        foreach (var kind in SaplingCatalog.All)
        {
            if (_content.GetBlock(kind.BlockKey) is not { } def || def.NumericId.IsAir)
            {
                continue;
            }

            ushort id = def.NumericId.Value;
            _saplings[id] = new PlantedSapling(kind, SaplingBlock(kind.Log), SaplingBlock(kind.Crown), SaplingBlock(kind.Blossom),
                SaplingBlock(kind.Berries), SaplingBlock(kind.Fruit));
            _floraIds.Add(id);
            _floraHostIds[id] = HostIds("dirt", "grass", "mud");
            if (kind.BlockKey == SaplingCatalog.All[0].BlockKey)
            {
                _saplingId = id;
            }
        }
    }

    private BlockId SaplingBlock(string? key) => key != null && _content.GetBlock(key) is { } d ? d.NumericId : BlockId.Air;

    /// <summary>A planted young tree of any kind: flora that grows UP instead of back.</summary>
    private bool IsSapling(ushort id) => id != 0 && _saplings.ContainsKey(id);

    /// <summary>A sapling was planted: its clock starts (its kind's growing time). No sprout cue — the sapling itself is the cue.</summary>
    private void ScheduleSaplingGrowth(Vector3i pos, ushort saplingId)
    {
        double seconds = _saplings.TryGetValue(saplingId, out var s) ? s.Kind.GrowSeconds : SaplingCatalog.DefaultGrowSeconds;
        _floraRegrow[pos] = (saplingId, seconds, 0);
        _repo.SaveFloraRegrow(_world.LocationId, pos, saplingId, seconds);
    }

    /// <summary>A sapling picked up before it grew is gone for good (it is in the pocket now) — drop its timer.</summary>
    private void ForgetSaplingGrowth(Vector3i pos) => ForgetFloraRegrow(pos);

    /// <summary>#2532: a sapling whose ground is gone — air or a fluid under it — grows nothing: no floating tree.</summary>
    private bool SaplingLostGround(Vector3i pos)
    {
        var below = _world.GetBlock(new Vector3i(pos.X, pos.Y - 1, pos.Z));
        return below.IsAir || IsFluid(below.Value) || IsLiquid(below);
    }

    /// <summary>Test seam (#2038, #2976): the fruit and colour a tree of <paramref name="kind"/> rooted at a column bears on
    /// this world — the kind's roll up to generation 29, the tree's own pick from 30 (null = none).</summary>
    public (string Shape, int Tint)? FruitForTreeForTest(TreeKind kind, int x, int z)
        => FruitRules.FruitForTree(_fruitRosterSeed, kind, FruitRules.TreeHash(_meta.Seed, x, z), _activeFruitKeys, _guaranteedFruit,
            _meta.Description.TerrainGeneration);

    /// <summary>Test seam (#1774): grows the sapling at a cell into its tree, if it can.</summary>
    public bool TryGrowTreeForTest(int x, int y, int z) => TryGrowTree(new Vector3i(x, y, z));

    /// <summary>Turns the sapling at <paramref name="pos"/> into its tree: the trunk over the sapling's own cell and the free
    /// cells above (and, for the young giant, beside) it; then the crown and the blossoms into air only, so a crown pressed
    /// against a wall is simply flatter on that side; then the fruit. On a void world the station hall must hold the tree
    /// (#1835: judged at the SAPLING's cell, which has the floor the enclosure probe starts from). Returns false when the
    /// tree could not grow yet.</summary>
    private bool TryGrowTree(Vector3i pos)
    {
        ushort id = _world.GetBlock(pos).Value;
        if (!_saplings.TryGetValue(id, out var sapling) || sapling.Log.IsAir || sapling.Crown.IsAir || SaplingLostGround(pos))
        {
            return false; // #2532: without ground under it a sapling grows no tree
        }

        ulong hash = FruitRules.TreeHash(_meta.Seed, pos.X, pos.Z);
        if (GrowableShape(pos, id, sapling, hash) is not { } shape || !IsFloraEnclosedForVoidWorld(pos))
        {
            return false; // no room for the trunk yet, or a station hall that would not hold the tree
        }

        var treeCells = new HashSet<(int X, int Y, int Z)>(); // what the tree wrote — the fruit hangs under its lowest leaves
        var crownCells = new List<(int X, int Y, int Z)>();   // every crown cell the tree tried to grow, in crown order
        foreach (var (dx, dy, dz) in shape.Trunk)
        {
            var c = new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz);
            WriteTreeCell(c, sapling.Log);
            treeCells.Add((c.X, c.Y, c.Z));
        }

        foreach (var (dx, dy, dz) in shape.Crown)
        {
            var c = new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz);
            crownCells.Add((c.X, c.Y, c.Z));
            if (WriteTreeCellIntoAir(c, sapling.Crown))
            {
                treeCells.Add((c.X, c.Y, c.Z));
            }
        }

        foreach (var (dx, dy, dz) in shape.Blossoms)
        {
            var c = new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz);
            if (!sapling.Blossom.IsAir && WriteTreeCellIntoAir(c, sapling.Blossom))
            {
                treeCells.Add((c.X, c.Y, c.Z));
            }
        }

        HangSaplingFruit(pos, sapling, shape, hash, crownCells, treeCells);
        FlushMirroredStationCells(); // #1857: one row write + one design refresh for the whole tree
        return true;
    }

    /// <summary>The tree this sapling grows here, or null while there is no room: the shape of its kind (the variant is fixed
    /// per cell), and for the young giant (#2980) the first of the four quarters around the sapling its 2×2 trunk fits in.</summary>
    private SaplingTrees.Shape? GrowableShape(Vector3i pos, ushort id, PlantedSapling sapling, ulong hash)
    {
        var shape = SaplingTrees.For(sapling.Kind.Shape, pos.X * 31 + pos.Z * 17 + pos.Y, hash);
        if (sapling.Kind.Shape != SaplingShape.YoungGiant)
        {
            return SaplingTreeHasRoom(pos, id, shape) ? shape : null;
        }

        foreach (var (sx, sz) in SaplingTrees.Mirrors)
        {
            var mirrored = SaplingTrees.Mirrored(shape, sx, sz);
            if (SaplingTreeHasRoom(pos, id, mirrored))
            {
                return mirrored;
            }
        }

        return null;
    }

    /// <summary>Whether every trunk cell but the sapling's own is free (and outside a ship's interior), and every trunk cell
    /// at the sapling's height has ground a sapling could root in — the young giant's 2×2 foot (#2980).</summary>
    private bool SaplingTreeHasRoom(Vector3i pos, ushort saplingId, SaplingTrees.Shape shape)
    {
        foreach (var (dx, dy, dz) in shape.Trunk)
        {
            if (dx == 0 && dy == 0 && dz == 0)
            {
                continue;
            }

            var c = new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz);
            if (!_world.GetBlock(c).IsAir || ShipInteriorContains(new Vector3f(c.X, c.Y, c.Z))
                || (dy == 0 && !IsValidFloraHost(saplingId, c)))
            {
                return false;
            }
        }

        return true;
    }

    private void WriteTreeCell(Vector3i c, BlockId block)
    {
        _world.SetBlock(c, block);
        MirrorStationCellDeferred(c, block); // #1857: a tree grown aboard a player station is part of its build
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = block.Value });
    }

    /// <summary>Writes a crown or blossom cell where there is air (never the trunk, a wall or a neighbour's crown) and
    /// outside a ship's interior. True when it was written.</summary>
    private bool WriteTreeCellIntoAir(Vector3i c, BlockId block)
    {
        if (!_world.GetBlock(c).IsAir || ShipInteriorContains(new Vector3f(c.X, c.Y, c.Z)))
        {
            return false;
        }

        WriteTreeCell(c, block);
        return true;
    }

    /// <summary>The fruit of a grown sapling: the Fifi plant's own berry cells (#2979); a fruit-tree sapling's fruit, always
    /// 3–5 pieces, in this world's colour for that fruit, on every world (#2977); the classic sapling's tree bears what the
    /// world's broadleaf trees bear (#2038, #2976: from generation 30 the tree's own pick among the world's fruit). The
    /// conifer and the young giant bear none. Fruit fills air only, under a crown cell that really grew.</summary>
    private void HangSaplingFruit(Vector3i pos, PlantedSapling sapling, SaplingTrees.Shape shape, ulong hash,
        List<(int X, int Y, int Z)> crownCells, HashSet<(int X, int Y, int Z)> treeCells)
    {
        if (sapling.Kind.Shape == SaplingShape.Fifi)
        {
            foreach (var (dx, dy, dz) in shape.Berries)
            {
                HangFruitCell(new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz), sapling.Berries, 0, sapling.Crown);
            }

            return;
        }

        if (SaplingFruit(sapling, hash) is not { } fruit)
        {
            return;
        }

        foreach (var (x, y, z) in FruitRules.PickFruitCells(crownCells, treeCells, hash, fruit.Min, fruit.Max))
        {
            HangFruitCell(new Vector3i(x, y, z), fruit.Block, fruit.Tint, sapling.Crown);
        }
    }

    /// <summary>The fruit block, colour and count a grown sapling's tree bears, or null for none.</summary>
    private (BlockId Block, int Tint, int Min, int Max)? SaplingFruit(PlantedSapling sapling, ulong hash)
    {
        if (!sapling.Fruit.IsAir)
        {
            return (sapling.Fruit, FruitRules.TintForShape(_fruitRosterSeed, sapling.Kind.Fruit!), PlantedFruitMin, PlantedFruitMax);
        }

        int generation = _meta.Description.TerrainGeneration;
        if (sapling.Kind.Shape != SaplingShape.Broadleaf
            || generation < BlocksBeyondTheStars.Shared.World.WorldDescription.FruitTreesGeneration
            || FruitRules.FruitForTree(_fruitRosterSeed, TreeKind.Broadleaf, hash, _activeFruitKeys, _guaranteedFruit, generation) is not { } borne
            || _content.GetBlock(borne.Shape) is not { } def || def.NumericId.IsAir)
        {
            return null;
        }

        return (def.NumericId, borne.Tint, FruitRules.MinFruitPerTree, FruitRules.MaxFruitPerTree);
    }

    private void HangFruitCell(Vector3i c, BlockId fruit, int tint, BlockId crown)
    {
        if (fruit.IsAir || !_world.GetBlock(c).IsAir || ShipInteriorContains(new Vector3f(c.X, c.Y, c.Z))
            || _world.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).Value != crown.Value)
        {
            return; // fruit fills air only, under a leaf that really grew
        }

        _world.SetBlock(c, fruit, tint);
        MirrorStationCellDeferred(c, fruit);
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = fruit.Value, Tint = tint });
    }
}
