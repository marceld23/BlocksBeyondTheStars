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

    // Cap on the void-enclosure flood-fill: a plant boxed in by this many reachable floor cells without finding
    // an open drop is treated as enclosed. Far larger than any single station room, so real interiors always pass.
    private const int FloraEnclosureFloodBudget = 512;

    /// <summary>Core void-enclosure test, shared by live placement and the void-world stamp paths. Returns true if
    /// the flora cell is NOT fully enclosed — i.e. a billboard plant there would show the void behind it and let the
    /// player walk out into space. <paramref name="get"/> reads the block id at a cell (0 = empty) over whichever
    /// space is being checked (the live world, or a structure's own cell map at stamp time).
    ///
    /// A flora cell is exposed when either the floor directly under it is not solid, OR a bounded flood-fill of the
    /// walkable space at foot level (stepping through non-solid cells) reaches a cell with nothing solid beneath it
    /// — a drop the player would fall through into the void. The flood-fill (rather than the old single-neighbour
    /// check) also catches a plant standing one or more cells in from an open ledge, which the one-cell test missed.</summary>
    private bool FloraCellOpensToVoid(System.Func<Vector3i, ushort> get, Vector3i flora)
    {
        bool Solid(Vector3i p)
        {
            ushort id = get(p);
            return id != 0 && (_content.BlockById(new BlockId(id))?.Solid ?? false);
        }

        // The plant must stand on a solid floor; nothing under it is an immediate fall-through.
        if (!Solid(new Vector3i(flora.X, flora.Y - 1, flora.Z)))
        {
            return true;
        }

        // Flood-fill the reachable walkable cells at the plant's own level. Any reachable open cell with no solid
        // floor below opens onto the void (you would walk off the edge and fall). Bounded so this stays cheap.
        var seen = new HashSet<Vector3i> { flora };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(flora);
        while (queue.Count > 0 && seen.Count <= FloraEnclosureFloodBudget)
        {
            var c = queue.Dequeue();
            foreach (var d in FloraHorizontalDirs)
            {
                var n = new Vector3i(c.X + d.X, c.Y + d.Y, c.Z + d.Z);
                if (Solid(n) || !seen.Add(n))
                {
                    continue; // a wall blocks movement here, or the cell was already visited
                }

                if (!Solid(new Vector3i(n.X, n.Y - 1, n.Z)))
                {
                    return true; // a reachable floorless cell — open to the void
                }

                queue.Enqueue(n);
            }
        }

        return false; // boxed in within the budget — no escape to the void
    }

    /// <summary>On a void world (a space station floating in the void) flora may only sit fully INSIDE the hull, so
    /// the billboard plant (no opaque face, no collider) never shows the void behind it nor lets the player walk
    /// out into space. On normal worlds terrain always backs the plant, so this is a no-op there.</summary>
    private bool IsFloraEnclosedForVoidWorld(Vector3i pos)
        => !_world.Planet.Void || !FloraCellOpensToVoid(p => _world.GetBlock(p).Value, pos);

    /// <summary>Test seam: run the void-enclosure predicate against an arbitrary cell map, so the flood-fill /
    /// ledge logic can be unit-tested without boarding a full void world. Returns true if the cell opens to the void.</summary>
    public bool FloraCellOpensToVoidForTest(System.Func<int, int, int, ushort> get, int x, int y, int z)
        => FloraCellOpensToVoid(p => get(p.X, p.Y, p.Z), new Vector3i(x, y, z));

    /// <summary>Test/diagnostic: whether a flora block could be planted at a cell.</summary>
    public bool CanPlantFlora(string floraKey, int x, int y, int z)
    {
        var def = _content.GetBlock(floraKey);
        return def != null && IsFlora(def.NumericId.Value) && IsValidFloraHost(def.NumericId.Value, new Vector3i(x, y, z));
    }

    /// <summary>Restores this world's persisted flora regrowths into the queue (so a harvest-then-restart
    /// brings the plant back instead of leaving it gone for good). Non-void worlds only — stations grow none.</summary>
    private void LoadFloraRegrow()
    {
        if (_world.Planet.Void)
        {
            return;
        }

        foreach (var fr in _repo.ListFloraRegrow(_world.LocationId))
        {
            // Drop stale rows whose block is no longer flora in this content set (defensive — keeps the queue clean).
            if (IsFlora(fr.Block))
            {
                _floraRegrow[fr.WorldPosition] = (fr.Block, fr.Timer);
            }
            else
            {
                _repo.DeleteFloraRegrow(_world.LocationId, fr.WorldPosition);
            }
        }
    }

    /// <summary>Schedules a harvested plant to regrow on its cell (if the host stays intact). Persisted so the
    /// regrow survives a restart — without it the harvest's air edit would keep the cell bare for good.</summary>
    private void ScheduleFloraRegrow(Vector3i pos, ushort floraId)
    {
        _floraRegrow[pos] = (floraId, FloraRegrowSeconds);
        _repo.SaveFloraRegrow(_world.LocationId, pos, floraId, FloraRegrowSeconds);

        // Cosmetic cue: tell clients the spawn source has started regrowing so they can render a sprout that
        // grows in over the delay (the plant pops back on its own via BlockChanged regardless of this).
        BroadcastToWorld(new FloraRegrowStarted
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Block = floraId,
            Seconds = (float)FloraRegrowSeconds,
        });
    }

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
                _repo.DeleteFloraRegrow(_world.LocationId, pos); // consumed (regrew or host lost) — clear the persisted row
            }
        }
    }
}
