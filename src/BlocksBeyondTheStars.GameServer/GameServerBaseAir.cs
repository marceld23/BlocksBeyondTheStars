// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sealed-room life support for founded bases (issue #794). Beyond the unconditional radius-8 cube
/// (issue #782), a base also breathes air into every room that is <b>sealed</b> — enclosed by airtight
/// full-cube blocks (<see cref="Shared.Definitions.BlockDefinition.Airtight"/>) — and <b>connected</b> to
/// the base. The model is per-POCKET, because the typical Grundstein stands in the open:
/// <list type="bullet">
/// <item>A <b>pocket</b> is a maximal 6-connected region of non-airtight cells (air, flora, loose dirt,
/// shaped cells — leaks flow right through those), bounded by airtight full-cube blocks and by ENERGY
/// door cells. Energy doors are walk-through air curtains (#793): they separate pockets without ever
/// depressurising on open/close; mechanical doors (wood/hinge/slide) are no boundary at all — they leak.</item>
/// <item>A pocket is <b>sealed</b> when its bounded fill terminates: a cell budget plus a hard reach box
/// around the core double as the leak detector — escaping into open terrain explodes the frontier.</item>
/// <item>A sealed pocket is <b>supplied</b> when it touches the base's radius-8 cube (built at the
/// Grundstein) or connects through an energy door to another supplied pocket — so airlocked room chains
/// extend the base outward, door by door.</item>
/// </list>
/// The supplied volume is derived state — recomputed lazily (rate-limited per base) straight from the
/// world's blocks, so every mutation path (mining, placing, fire, fluids, admin edits) is covered without
/// dirty hooks, and nothing needs persisting. Gated by the existing OxygenEnabled rule only (no new game
/// rule). When a recompute turns a previously supplied volume empty on a world without breathable air,
/// everyone in reach gets a "no longer airtight" warning (the mined-wall case must never be silent).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Cell budget for one pocket's fill. Exceeding it means "leaked" — an open volume within
    /// the reach box is ~450k cells, so a real leak blows this long before the box edge matters. 16k cells
    /// is roughly a 40×20×20 interior: generous for a hand-built room, cheap to fill (&lt;1 ms).</summary>
    private const int MaxSealedCellsPerBase = 16000;

    /// <summary>Hard Chebyshev half-extent around the base core a pocket may reach. A fill that steps past
    /// it counts as leaked (rooms that far from the Grundstein are out of the base's support range).</summary>
    private const int SealedRoomMaxReach = 48;

    /// <summary>Minimum seconds between recomputes of one base's supplied volume. The fills are cheap;
    /// the limit just keeps rapid building/mining from re-filling every oxygen tick.</summary>
    private const double SealedRoomRecomputeInterval = 1.5;

    /// <summary>A base's cached supplied-air volume (empty = nothing sealed / leaked / over budget).</summary>
    private sealed class BaseAirVolume
    {
        public string Body = string.Empty;          // world the fill was computed on
        public HashSet<Vector3i> Cells = new();     // canonical cells that hold base air
        public double ComputedAt = double.NegativeInfinity;
    }

    /// <summary>Supplied-volume cache per base id. Entries recompute lazily when stale (see
    /// <see cref="SealedRoomRecomputeInterval"/>) and are dropped with their base.</summary>
    private readonly Dictionary<int, BaseAirVolume> _baseAir = new();

    /// <summary>True if the cell lies inside some founded base's SEALED room volume on the current world.
    /// Ownership is ignored just like <see cref="InAnyBaseZone"/> — visitors breathe too. The radius-8
    /// cube is checked separately (and unconditionally); this only adds the sealed rooms beyond it.</summary>
    private bool InSealedBaseRoom(Vector3i cell)
    {
        string body = _world.LocationId;
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet != body)
            {
                continue;
            }

            // Outside the base's hard reach box the fill can never contain the cell — skip the cache.
            if (WrapAbs(canonical.X - b.Cell.X) > SealedRoomMaxReach
                || System.Math.Abs(canonical.Y - b.Cell.Y) > SealedRoomMaxReach
                || System.Math.Abs(canonical.Z - b.Cell.Z) > SealedRoomMaxReach)
            {
                continue;
            }

            if (!_baseAir.TryGetValue(b.Id, out var vol))
            {
                _baseAir[b.Id] = vol = new BaseAirVolume();
            }

            if (vol.Body != body || _uptime - vol.ComputedAt >= SealedRoomRecomputeInterval)
            {
                bool wasSealed = vol.Body == body && vol.Cells.Count > 0;
                vol.Body = body;
                vol.ComputedAt = _uptime;
                vol.Cells = ComputeSuppliedCells(b);

                // The seal just broke (someone mined a wall, pulled a door, fire ate a plank …): warn
                // everyone in the base's reach so the loss isn't silent — but only where it matters, i.e.
                // when the world's own air is NOT breathable and oxygen is on (issue: Marcel 2026-08-08).
                if (wasSealed && vol.Cells.Count == 0 && Rules.OxygenEnabled && !AtmosphereBreathable)
                {
                    WarnBaseAirLost(b);
                }
            }

            if (vol.Cells.Contains(canonical))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Shortest absolute longitude distance across the wrap seam (Y/Z use plain Math.Abs).</summary>
    private int WrapAbs(int dx) => System.Math.Abs(WorldConstants.WrapDeltaX(dx, _world.Circumference));

    /// <summary>
    /// Computes one base's supplied air volume: flood every pocket reachable from the core's surroundings
    /// and from each energy door, keep the SEALED ones, then propagate "supplied" from the radius-8 cube
    /// outwards across energy-door connections. Returns the union of the supplied pockets' cells (empty
    /// when nothing seals). Reads blocks via <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base can
    /// never drag chunk generation; around an actively-used base the chunks are resident anyway (an
    /// unloaded cell reads as air, joins a fill and at worst leaks that pocket).
    /// </summary>
    private HashSet<Vector3i> ComputeSuppliedCells(ServerBase b)
    {
        int circ = _world.Circumference;
        var doorCells = EnergyDoorCells();            // canonical door cell → door index
        var cellPocket = new Dictionary<Vector3i, int>();
        var pockets = new List<Pocket>();

        // Seeds: the core's own neighbours (the pocket the Grundstein physically sits in — often open
        // air, in which case that pocket simply leaks) plus both sides of every energy door in reach.
        var seeds = new List<Vector3i>();
        var core = WorldConstants.CanonicalBlock(b.Cell, circ);
        AddNeighbours(core, seeds);
        foreach (var doorCell in doorCells.Keys)
        {
            if (WrapAbs(doorCell.X - b.Cell.X) <= SealedRoomMaxReach
                && System.Math.Abs(doorCell.Y - b.Cell.Y) <= SealedRoomMaxReach
                && System.Math.Abs(doorCell.Z - b.Cell.Z) <= SealedRoomMaxReach)
            {
                AddNeighbours(doorCell, seeds);
            }
        }

        foreach (var seed in seeds)
        {
            var c = WorldConstants.CanonicalBlock(seed, circ);
            if (!cellPocket.ContainsKey(c) && !doorCells.ContainsKey(c) && !IsAirtightCell(c))
            {
                FillPocket(c);
            }
        }

        // Supplied = sealed AND (touches the base cube OR reachable from a supplied pocket through an
        // energy door). Fixpoint over the door graph — each round can light up the next room in a chain.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in pockets)
            {
                if (p.Supplied || !p.Sealed)
                {
                    continue;
                }

                bool connect = p.TouchesCube;
                if (!connect)
                {
                    foreach (int door in p.Doors)
                    {
                        foreach (var q in pockets)
                        {
                            if (q.Supplied && q.Doors.Contains(door))
                            {
                                connect = true;
                                break;
                            }
                        }

                        if (connect)
                        {
                            break;
                        }
                    }
                }

                if (connect)
                {
                    p.Supplied = true;
                    changed = true;
                }
            }
        }

        var supplied = new HashSet<Vector3i>();
        foreach (var p in pockets)
        {
            if (p.Supplied)
            {
                supplied.UnionWith(p.Cells);
            }
        }

        // The door cells themselves breathe when a supplied pocket borders them (standing IN the air
        // curtain must not read as "no life support" for the head cell).
        foreach (var doorCell in doorCells.Keys)
        {
            if (supplied.Overlaps(NeighboursOf(doorCell)))
            {
                supplied.Add(doorCell);
            }
        }

        return supplied;

        void AddNeighbours(Vector3i pos, List<Vector3i> into)
        {
            into.Add(new Vector3i(pos.X + 1, pos.Y, pos.Z));
            into.Add(new Vector3i(pos.X - 1, pos.Y, pos.Z));
            into.Add(new Vector3i(pos.X, pos.Y + 1, pos.Z));
            into.Add(new Vector3i(pos.X, pos.Y - 1, pos.Z));
            into.Add(new Vector3i(pos.X, pos.Y, pos.Z + 1));
            into.Add(new Vector3i(pos.X, pos.Y, pos.Z - 1));
        }

        IEnumerable<Vector3i> NeighboursOf(Vector3i pos)
        {
            var list = new List<Vector3i>(6);
            AddNeighbours(pos, list);
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = WorldConstants.CanonicalBlock(list[i], circ);
            }

            return list;
        }

        bool IsAirtightCell(Vector3i c)
        {
            var id = _world.GetBlockIfLoaded(c);
            if (id.IsAir)
            {
                return false;
            }

            var def = _content.BlockById(id);
            return def is { Airtight: true } && _world.GetShape(c) == 0; // shaped cells leak
        }

        void FillPocket(Vector3i start)
        {
            var pocket = new Pocket();
            int index = pockets.Count;
            pockets.Add(pocket);

            var frontier = new Queue<Vector3i>();
            pocket.Cells.Add(start);
            cellPocket[start] = index;
            frontier.Enqueue(start);

            bool leaked = false;
            while (frontier.Count > 0 && !leaked && pocket.Cells.Count <= MaxSealedCellsPerBase)
            {
                var cell = frontier.Dequeue();
                if (WrapAbs(cell.X - b.Cell.X) <= WorldConstants.BaseZoneRadius
                    && System.Math.Abs(cell.Y - b.Cell.Y) <= WorldConstants.BaseZoneRadius
                    && System.Math.Abs(cell.Z - b.Cell.Z) <= WorldConstants.BaseZoneRadius)
                {
                    pocket.TouchesCube = true;
                }

                foreach (var n in NeighboursOf(cell))
                {
                    if (pocket.Cells.Contains(n))
                    {
                        continue;
                    }

                    if (WrapAbs(n.X - b.Cell.X) > SealedRoomMaxReach
                        || System.Math.Abs(n.Y - b.Cell.Y) > SealedRoomMaxReach
                        || System.Math.Abs(n.Z - b.Cell.Z) > SealedRoomMaxReach)
                    {
                        leaked = true; // stepped past the reach box → this pocket is open, not a room
                        break;
                    }

                    if (doorCells.TryGetValue(n, out int door))
                    {
                        pocket.Doors.Add(door); // the curtain bounds the pocket AND links it onward
                        continue;
                    }

                    if (IsAirtightCell(n))
                    {
                        continue;
                    }

                    pocket.Cells.Add(n);
                    cellPocket[n] = index;
                    frontier.Enqueue(n);
                }
            }

            pocket.Sealed = !leaked && pocket.Cells.Count <= MaxSealedCellsPerBase;
        }
    }

    /// <summary>One flood-filled air pocket (see the class doc for the model).</summary>
    private sealed class Pocket
    {
        public readonly HashSet<Vector3i> Cells = new();
        public readonly HashSet<int> Doors = new(); // energy-door indices bounding this pocket
        public bool Sealed;
        public bool TouchesCube;
        public bool Supplied;
    }

    /// <summary>Cells covered by ENERGY doors in the active world (ship hatches + player-built energy
    /// doors + stamped door_energy markers), keyed to the door's index: the door's width along its wall
    /// axis × the 3-tall doorway column. These bound pockets AND connect them — the field is an air
    /// curtain whether the panels are open or not, so auto-opening on approach never depressurises a
    /// room. Mechanical door kinds are deliberately absent: they leak.</summary>
    private Dictionary<Vector3i, int> EnergyDoorCells()
    {
        var cells = new Dictionary<Vector3i, int>();
        int circ = _world.Circumference;
        for (int i = 0; i < _doors.Count; i++)
        {
            var d = _doors[i];
            if (d.Kind != "energy")
            {
                continue;
            }

            // Reconstruct the covered cells from the gap centre + width (see MakeDoor): the lowest cell
            // sits (Width-1)/2 left of the centre. Round kills the float error in the exact .5 centres.
            int by = (int)System.Math.Floor(d.Pos.Y);
            int low = (int)System.Math.Round((d.AxisX ? d.Pos.X : d.Pos.Z) - 0.5f - (d.Width - 1f) * 0.5f);
            int fixedAxis = d.AxisX ? (int)System.Math.Floor(d.Pos.Z) : (int)System.Math.Floor(d.Pos.X);
            for (int w = 0; w < (int)d.Width; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    cells[WorldConstants.CanonicalBlock(d.AxisX
                        ? new Vector3i(low + w, by + dy, fixedAxis)
                        : new Vector3i(fixedAxis, by + dy, low + w), circ)] = i;
                }
            }
        }

        return cells;
    }

    /// <summary>Warns every player within the base's reach box that its rooms just lost their air seal
    /// (localized client-side via the <c>@base_air_lost</c> token). Transition-triggered only — the next
    /// recompute after the seal breaks — so it cannot spam.</summary>
    private void WarnBaseAirLost(ServerBase b)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            var pos = session.State.Position;
            if (WrapAbs((int)System.Math.Floor(pos.X) - b.Cell.X) <= SealedRoomMaxReach
                && System.Math.Abs((int)System.Math.Floor(pos.Y) - b.Cell.Y) <= SealedRoomMaxReach
                && System.Math.Abs((int)System.Math.Floor(pos.Z) - b.Cell.Z) <= SealedRoomMaxReach)
            {
                Send(session, new Networking.Messages.ServerMessage { Text = "@base_air_lost" });
            }
        }
    }

    /// <summary>Drops a removed base's cached air volume (called when its core is mined).</summary>
    private void ForgetBaseAir(int baseId) => _baseAir.Remove(baseId);
}
