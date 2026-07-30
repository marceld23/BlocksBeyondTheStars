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
/// as its host block underneath is still intact</b> (mine the ground and it won't return). Since #628 that
/// holds <b>aboard a station too</b> — a void world grows nothing of its own, but a hydroponics bay's crops
/// come back like any other plant, provided the cell stays sealed inside the hull.
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

        // Per-BODY flora roster (#478): each archetype block gets this world's coined name + edible/toxic
        // trait, surfaced when the player scans the plant. The seed is salted with the body's location id —
        // the SAME formula as WorldGenerator.RosterSeed, or the scanned names would disagree with what
        // worldgen actually planted. (Previously every world of the same planet type shared one roster.)
        _floraSpeciesByBlock.Clear();
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        long rosterSeed = _meta.Seed ^ BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(_world.LocationId);
        if (planet != null)
        {
            foreach (var fs in BlocksBeyondTheStars.WorldGeneration.FloraGenerator.GenerateRoster(planet, rosterSeed))
            {
                if (_content.GetBlock(fs.BlockKey) is { } b && b.NumericId.Value != 0)
                {
                    _floraSpeciesByBlock[b.NumericId.Value] = fs;
                }
            }
        }

        // Per-body tree species (#478): the trunk (wood_log) and crown (tree_leaves) share this world's one
        // coined name + edible/toxic trait, surfaced when the player scans a tree.
        _treeSpecies = null;
        _treeBlockIds.Clear();
        if (planet != null && BlocksBeyondTheStars.WorldGeneration.TreeGenerator.Generate(planet, rosterSeed) is { } tree)
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

    // How far below a floorless cell to look for something solid before calling that cell open space (#628).
    // A hole in a station deck — a stair shaft between two floors — drops onto the room below within a couple of
    // cells and is NOT the void; the hull's outer edge has nothing under it for the rest of the world. Deep
    // enough to clear any deck-to-deck gap the station generator builds (room height is at most 8).
    private const int FloraVoidDropProbe = 10;

    /// <summary>Core void-enclosure test, shared by live placement and the void-world stamp paths. Returns true if
    /// the flora cell is NOT fully enclosed — i.e. a billboard plant there would show the void behind it and let the
    /// player walk out into space. <paramref name="get"/> reads the block id at a cell (0 = empty) over whichever
    /// space is being checked (the live world, or a structure's own cell map at stamp time).
    ///
    /// A flora cell is exposed when either the floor directly under it is not solid, OR a bounded flood-fill of the
    /// walkable space at foot level (stepping through non-solid cells) reaches a cell that opens onto SPACE — a drop
    /// with nothing solid anywhere below it. The flood-fill (rather than the old single-neighbour check) also
    /// catches a plant standing one or more cells in from an open ledge, which the one-cell test missed.
    ///
    /// A reachable hole is only the void if the fall never lands: a stair shaft cut through a station deck has the
    /// floor below it a few cells down (#628), and treating that as open space would have barred crops from every
    /// multi-deck hydroponics bay. The plant's OWN cell still demands solid ground directly beneath it.</summary>
    private bool FloraCellOpensToVoid(System.Func<Vector3i, ushort> get, Vector3i flora)
    {
        bool Solid(Vector3i p)
        {
            ushort id = get(p);
            return id != 0 && (_content.BlockById(new BlockId(id))?.Solid ?? false);
        }

        // True when a cell has nothing to land on: no solid block within probe range below it.
        bool OpensToSpace(Vector3i p)
        {
            for (int drop = 1; drop <= FloraVoidDropProbe; drop++)
            {
                if (Solid(new Vector3i(p.X, p.Y - drop, p.Z)))
                {
                    return false; // a deck / the ground below — a fall, not a departure
                }
            }

            return true;
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

                if (OpensToSpace(n))
                {
                    return true; // a reachable cell with nothing to land on — open to the void
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
    /// brings the plant back instead of leaving it gone for good). Void worlds (stations) load these too since
    /// #628 — their hydroponics bay is the one place a plant grows out there, and the enclosure test in
    /// <see cref="TickFlora"/> is what keeps it inside the hull.</summary>
    private void LoadFloraRegrow()
    {
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
        if (_floraRegrow.Count == 0)
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
            // valid ground for it (so flora never grows up through the ship hull/interior). On a void world
            // (#628) the cell must additionally be sealed inside a hull — a plant regrowing in an open cell
            // out there would show the void through its billboard and let the player walk off into space.
            if (!ShipInteriorContains(new Vector3f(pos.X, pos.Y, pos.Z)) && _world.GetBlock(pos).IsAir
                && IsValidFloraHost(floraId, pos) && IsFloraEnclosedForVoidWorld(pos))
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
