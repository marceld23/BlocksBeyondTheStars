// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Surface flora (World systems). Worldgen places one plant per eligible column (bounded — no
/// spreading). When a plant is harvested it <b>regrows on the same cell after a delay, as long
/// as its host block underneath is still intact</b> (mine the ground and it won't return).
/// Seeds let the player replant flora on a valid host block (validated here). Growth is capped:
/// one plant per host cell, never spreading.
///
/// Planned extension: per-species procedural appearance/effects and a maturity/"produces seeds"
/// state (normal harvest yields the species material — wood/berries/fibre; seeds only from a
/// matured, producing plant).
/// </summary>
public sealed partial class GameServer
{
    private const double FloraRegrowSeconds = 30.0;

    private readonly HashSet<ushort> _floraIds = new();
    private readonly Dictionary<ushort, HashSet<ushort>> _floraHostIds = new();
    private readonly Dictionary<ushort, BlocksBeyondTheStars.Shared.Definitions.FloraSpecies> _floraSpeciesByBlock = new();

    // This world's single tree species + the block ids (trunk + leaves) it covers, so a scan of either
    // reads as the same coined, edible/toxic tree (built in InitFlora; see TreeSpeciesForBlock).
    private BlocksBeyondTheStars.Shared.Definitions.TreeSpecies? _treeSpecies;
    private readonly HashSet<ushort> _treeBlockIds = new();
    private Dictionary<Vector3i, (ushort FloraId, double Timer)> _floraRegrow => _worlds.Active.FloraRegrow;

    private void InitFlora()
    {
        _floraIds.Clear();
        _floraHostIds.Clear();
        foreach (var sp in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.All)
        {
            if (_content.GetBlock(sp.Key) is not { } flora || flora.NumericId.Value == 0)
            {
                continue;
            }

            _floraIds.Add(flora.NumericId.Value);
            _floraHostIds[flora.NumericId.Value] = HostIds(sp.Hosts);
        }

        // Per-world flora roster: each archetype block gets this world's coined name + edible/toxic trait
        // (deterministic from seed + planet), surfaced when the player scans the plant.
        _floraSpeciesByBlock.Clear();
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet != null)
        {
            foreach (var fs in BlocksBeyondTheStars.WorldGeneration.FloraGenerator.GenerateRoster(planet, _meta.Seed))
            {
                if (_content.GetBlock(fs.BlockKey) is { } b && b.NumericId.Value != 0)
                {
                    _floraSpeciesByBlock[b.NumericId.Value] = fs;
                }
            }
        }

        // Per-world tree species: the trunk (wood_log) and crown (tree_leaves) share this world's one coined
        // name + edible/toxic trait (deterministic from seed + planet), surfaced when the player scans a tree.
        _treeSpecies = null;
        _treeBlockIds.Clear();
        if (planet != null && BlocksBeyondTheStars.WorldGeneration.TreeGenerator.Generate(planet, _meta.Seed) is { } tree)
        {
            _treeSpecies = tree;
            foreach (var key in new[] { "wood_log", "tree_leaves" })
            {
                if (_content.GetBlock(key) is { } b && b.NumericId.Value != 0)
                {
                    _treeBlockIds.Add(b.NumericId.Value);
                }
            }
        }
    }

    /// <summary>This world's tree species for a block key (name + toxic trait) if the block is a tree block
    /// (trunk or leaves), else null. Trunk and leaves both map to the same species — one tree, one identity.
    /// Used by the scanner to name + classify a scanned tree.</summary>
    public BlocksBeyondTheStars.Shared.Definitions.TreeSpecies? TreeSpeciesForBlock(string blockKey)
        => _treeSpecies != null && _content.GetBlock(blockKey) is { } b && _treeBlockIds.Contains(b.NumericId.Value) ? _treeSpecies : null;

    /// <summary>This world's generated flora species for a block key (name + toxic trait), or null if the
    /// block isn't flora here. Used by the scanner to name + classify a scanned plant.</summary>
    public BlocksBeyondTheStars.Shared.Definitions.FloraSpecies? FloraSpeciesForBlock(string blockKey)
        => _content.GetBlock(blockKey) is { } b && _floraSpeciesByBlock.TryGetValue(b.NumericId.Value, out var fs) ? fs : null;

    private HashSet<ushort> HostIds(params string[] keys)
    {
        var set = new HashSet<ushort>();
        foreach (var k in keys)
        {
            if (_content.GetBlock(k) is { } d)
            {
                set.Add(d.NumericId.Value);
            }
        }

        return set;
    }

    private bool IsFlora(ushort id) => id != 0 && _floraIds.Contains(id);

    /// <summary>True if the flora may be planted at the cell — the block below must be a valid host.</summary>
    private bool IsValidFloraHost(ushort floraId, Vector3i pos)
    {
        if (!_floraHostIds.TryGetValue(floraId, out var hosts))
        {
            return false;
        }

        ushort below = _world.GetBlock(new Vector3i(pos.X, pos.Y - 1, pos.Z)).Value;
        return hosts.Contains(below);
    }

    private static readonly Vector3i[] FloraHorizontalDirs =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>True if the cell holds a solid (collision) block — confirms flora has real ground/walls
    /// backing it rather than open space.</summary>
    private bool IsSolidCell(Vector3i p)
    {
        var b = _world.GetBlock(p);
        return !b.IsAir && (_world.Definition(b)?.Solid ?? false);
    }

    /// <summary>On a void world (a space station floating in the void) flora may only sit fully INSIDE the hull:
    /// a solid block directly below AND no horizontal side opening onto floorless space. Otherwise the billboard
    /// plant (rendered with no opaque face and no collider) shows the void behind it and lets the player walk
    /// through it out into space. On normal worlds terrain always backs the plant, so this is a no-op there.</summary>
    private bool IsFloraEnclosedForVoidWorld(Vector3i pos)
    {
        if (!_world.Planet.Void)
        {
            return true;
        }

        if (!IsSolidCell(new Vector3i(pos.X, pos.Y - 1, pos.Z)))
        {
            return false; // no solid floor under the plant
        }

        foreach (var d in FloraHorizontalDirs)
        {
            var n = new Vector3i(pos.X + d.X, pos.Y + d.Y, pos.Z + d.Z);
            // A side that is open (non-solid) AND has no floor beneath it opens onto the void — reject.
            if (!IsSolidCell(n) && !IsSolidCell(new Vector3i(n.X, n.Y - 1, n.Z)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Test/diagnostic: whether a flora block could be planted at a cell.</summary>
    public bool CanPlantFlora(string floraKey, int x, int y, int z)
    {
        var def = _content.GetBlock(floraKey);
        return def != null && IsFlora(def.NumericId.Value) && IsValidFloraHost(def.NumericId.Value, new Vector3i(x, y, z));
    }

    /// <summary>Schedules a harvested plant to regrow on its cell (if the host stays intact).</summary>
    private void ScheduleFloraRegrow(Vector3i pos, ushort floraId)
        => _floraRegrow[pos] = (floraId, FloraRegrowSeconds);

    private void TickFlora(double dt)
    {
        // Stations (void worlds) grow no flora; nothing to regrow there.
        if (_world.Planet.Void || _floraRegrow.Count == 0)
        {
            return;
        }

        List<Vector3i>? done = null;
        // Iterate over a copy of the keys so we can update/remove entries safely.
        foreach (var pos in new List<Vector3i>(_floraRegrow.Keys))
        {
            var (floraId, timer) = _floraRegrow[pos];
            timer -= dt;
            if (timer > 0)
            {
                _floraRegrow[pos] = (floraId, timer);
                continue;
            }

            (done ??= new List<Vector3i>()).Add(pos);

            // Regrow only if the cell is still air, not inside a landed ship, and the host below is a
            // valid ground for it (so flora never grows up through the ship hull/interior).
            if (!ShipInteriorContains(new Vector3f(pos.X, pos.Y, pos.Z)) && _world.GetBlock(pos).IsAir && IsValidFloraHost(floraId, pos))
            {
                _world.SetBlock(pos, new BlockId(floraId));
                BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = floraId });
            }
        }

        if (done != null)
        {
            foreach (var pos in done)
            {
                _floraRegrow.Remove(pos);
            }
        }
    }
}
