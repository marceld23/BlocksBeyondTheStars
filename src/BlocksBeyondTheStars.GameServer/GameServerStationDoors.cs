// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Doors of a player station (#2607, Screelit: "On the ground a door is a door. In space a door becomes a block!").
/// A door built onto a station on a spacewalk used to be a solid door BLOCK — an airtight wall with a door picture, the
/// station's "airlock" for commissioning, solid on the spacewalk and aboard. Now it is a real door, like the one built
/// into a ship (#2119):
/// <list type="bullet">
/// <item><b>A door record</b> — the cell stays air, the door's kind and wall axis are kept on the station
/// (<see cref="SpaceStructure.PlacedDoorAxes"/>), sent with the station's design (the flight view draws it as a door) and
/// persisted. It comes out again on a spacewalk. Doors that were built as BLOCKS before stay blocks (Marcel's decision:
/// only new doors); the commission rule takes a door block or a door record.</item>
/// <item><b>Persistence</b> — the station's row keeps its cells as before (an <c>x:y:z:block</c> list, where a door block
/// is a block); the records go to the per-cell structure-edit store under the station's id, which no station used so
/// far: the door block id in the cell, the wall axis in the shape field (the ship's own door encoding). That tells a new
/// door record from an old door block without a save-version bump, and no row rewrite (a rename, an older server) can
/// drop the records. They are written once the station is commissioned, as a whole set, so an uncommissioned station
/// whose id a later deploy reuses leaves no stale door behind.</item>
/// <item><b>The interior</b> — a record is a door entity in the station's void world (persisted like a door built
/// aboard). A door built or taken out aboard goes back into the records the same way, so the hull seen from a spacewalk
/// has a door there, not a hole. Every boarding brings the two together (<see cref="SyncStationDoors"/>).</item>
/// <item><b>Outside edits reach the inside</b> — once the station has an interior, a spacewalk edit is written into that
/// world at once (live for a boarder, else into its stored edits), so the next boarding's absorb pass never resurrects a
/// wall mined from outside.</item>
/// <item><b>Boarding at the door</b> — E on a spacewalk beside a door of the station boards it there: the player arrives
/// on the door's sealed inside, falling back to the arrival next to the core (#2606).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close (flight units = blocks) the suit has to float to a door's opening (its three-high centre) for E
    /// to board through it. The client prefers the station on E while its aim rests on a door cell within the spacewalk's
    /// build reach (6, measured to the cell's face from the suit), and the opening's centre lies up to ~1.7 beyond that
    /// face — so the server's reach is the larger, and every door the client aims at is found here.</summary>
    private const float StationDoorBoardReach = 8f;

    // ---------------- spacewalk: build + take ----------------

    /// <summary>#2607: hangs a door into a station's cell on a spacewalk — a door record, not a block — and lets the
    /// interior, the pilots outside, the commission check and the save know.</summary>
    private void BuildStationDoor(PlayerSession session, SpaceInstance instance, SpaceStructure s, Vector3i cell, BlockDefinition doorBlock, double yaw)
    {
        bool axisX = ShipDoorAxis(s, cell, yaw); // the station's grid is the flight frame: the suit's own heading
        string kind = DoorBlocks.KindForBlock(doorBlock.Key);
        s.Set(cell, BlockId.Air);
        AddPlacedShipDoor(s, cell, kind, axisX);
        MirrorStationDoorOutside(s, cell, kind, axisX, session.State.PlayerId);
        ResendStationDesign(instance, s);

        if (!s.Boardable)
        {
            TryCommissionStation(instance, s, session); // a door record is an airlock too
        }

        if (s.Boardable)
        {
            PersistStationDoors(s);
        }
    }

    /// <summary>#2607: takes a door out of a station again on a spacewalk — the doorway is plain air, the door item goes
    /// back to the suit. False (with a message) when it does not fit.</summary>
    private bool TakeStationDoor(PlayerSession session, SpaceInstance instance, SpaceStructure s, Vector3i doorCell)
    {
        string item = DoorBlocks.ItemFor(s.DoorKinds.TryGetValue(doorCell, out var kind) ? kind : DoorBlocks.Energy);
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.CanFit(new[] { new ItemAmount(item, 1) }))
        {
            Reject(session, "structure", "@inventory_full");
            return false;
        }

        RemovePlacedShipDoor(s, doorCell);
        MirrorStationDoorOutside(s, doorCell, null, false, session.State.PlayerId);
        pool.Add(item, 1);
        SendInventory(session);
        ResendStationDesign(instance, s);
        if (s.Boardable)
        {
            PersistStationDoors(s);
        }

        return true;
    }

    /// <summary>#2607: re-sends a station's design when a spacewalk edit moved its cell box. The flight view draws the
    /// hull around the box centre of the design it last got and keeps that centre through single block changes; the
    /// server measures in the CURRENT box (the board range, a door beside the suit). A wing built past the old box shifted
    /// the two apart, and E at a door of the new wing could miss it — so whoever floats there gets the hull re-centred.</summary>
    private void ResendStationDesignIfBoxChanged(SpaceInstance instance, SpaceStructure s, (Vector3i Min, Vector3i Max) before)
    {
        if (CellBox(s) != before)
        {
            ResendStationDesign(instance, s);
        }
    }

    /// <summary>Sends a station's design to everyone floating in its instance (its doorways changed).</summary>
    private void ResendStationDesign(SpaceInstance instance, SpaceStructure s)
    {
        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { } other)
            {
                SendShipDesign(other, s);
            }
        }
    }

    // ---------------- persistence ----------------

    /// <summary>#2607: writes a commissioned station's door records as its structure edits — the whole set, so a door
    /// taken out leaves no row behind.</summary>
    private void PersistStationDoors(SpaceStructure s)
    {
        _repo.RunInTransaction(() =>
        {
            _repo.DeleteStructureEdits(s.Id);
            foreach (var (cell, axisX) in s.PlacedDoorAxes)
            {
                string kind = s.DoorKinds.TryGetValue(cell, out var k) ? k : DoorBlocks.Energy;
                if (_content.GetBlock(DoorBlocks.ItemFor(kind)) is { } block)
                {
                    _repo.SetStructureBlock(s.Id, cell, block.NumericId.Value, axisX ? PlacedDoorAxisX : PlacedDoorAxisZ);
                }
            }
        });
    }

    /// <summary>#2607: restores a station's door records from its structure edits (server start). A record whose cell
    /// holds a block in the build is dropped — the block is what the station really has there.</summary>
    private void ApplyPersistedStationDoors(SpaceStructure s)
    {
        foreach (var edit in _repo.LoadStructureEdits(s.Id))
        {
            if (edit.Block == BlockId.AirValue || _content.BlockById(new BlockId(edit.Block)) is not { } def
                || !DoorBlocks.IsDoorBlock(def.Key) || !s.Get(edit.WorldPosition).IsAir)
            {
                continue;
            }

            AddPlacedShipDoor(s, edit.WorldPosition, DoorBlocks.KindForBlock(def.Key), edit.Shape != PlacedDoorAxisZ);
        }
    }

    // ---------------- outside edits reach the interior ----------------

    /// <summary>The interior world's cell of a station's build cell, once the station has an interior (#1481) — false
    /// for a station never boarded (its first stamp writes everything).</summary>
    private bool TryStationInterior(SpaceStructure s, out BoardableStation station, out string loc)
    {
        loc = StationLocationIdPrefix + s.Id;
        if (s.Kind == "station" && _stationsById.TryGetValue(s.Id, out var found) && found.Materialised
            && _playerStationCells.ContainsKey(s.Id))
        {
            station = found;
            return true;
        }

        station = null!;
        return false;
    }

    /// <summary>The walkable circumference of a station's void world — <see cref="LoadWorld"/>'s rule for a location
    /// the galaxy does not carry (a planet-class size band, no bias). Needed to key a stored edit of a world that is not
    /// loaded: every block write keys X canonically (#1773).</summary>
    private static int StationWorldCircumference(string loc)
        => WorldConstants.CircumferenceFor(loc, WorldConstants.SizeClassFor(CelestialKind.Planet, StationPlanetType), 0f);

    /// <summary>Runs <paramref name="live"/> with the station's interior world as the active world when it is loaded (a
    /// boarder is inside — they see the change at once), else <paramref name="stored"/> with the world's canonical
    /// circumference, to write into its stored edits for the next load.</summary>
    private void InStationInterior(string loc, System.Action live, System.Action<int> stored)
    {
        if (!_worlds.IsLoaded(loc))
        {
            stored(StationWorldCircumference(loc));
            return;
        }

        string? restore = _worlds.Active?.LocationId;
        SetActiveWorld(loc);
        try
        {
            live();
        }
        finally
        {
            if (!string.IsNullOrEmpty(restore))
            {
                SetActiveWorld(restore);
            }
        }
    }

    /// <summary>#2607: a block built or mined on a spacewalk reaches the station's interior right away. Before, the
    /// interior learned only at the next stamp, and the stamp's absorb pass (#1559) took the old block back from the
    /// world into the build — a wall mined from outside came back on the next boarding. A boarder inside gets the
    /// station's box (the air and gravity reach) grown with it at once, like a block built aboard.</summary>
    private void MirrorStationCellOutside(SpaceStructure s, Vector3i cell, BlockId block)
    {
        if (!TryStationInterior(s, out var station, out var loc))
        {
            return;
        }

        var w = StationCellToWorld(station, cell);
        InStationInterior(loc,
            () =>
            {
                _world.SetBlock(w, block);
                var c = WorldConstants.CanonicalBlock(w, _world.Circumference);
                BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = block.Value });
                _stationAir.Remove(station.Id);
                var (min, max) = CellBox(s);
                RefreshStationBounds(station, min, max);
            },
            circ => _repo.SetBlock(loc, WorldConstants.CanonicalBlock(w, circ), block.Value));
    }

    /// <summary>#2607: a door hung (<paramref name="kind"/> set) or taken out (null) on a spacewalk reaches the interior:
    /// the doorway's cell is cleared and the door entity is stored (and hung, for a boarder inside) or removed.</summary>
    private void MirrorStationDoorOutside(SpaceStructure s, Vector3i cell, string? kind, bool axisX, string owner)
    {
        if (!TryStationInterior(s, out var station, out var loc))
        {
            return;
        }

        var w = StationCellToWorld(station, cell);
        InStationInterior(loc,
            () =>
            {
                var c = WorldConstants.CanonicalBlock(w, _world.Circumference);
                if (kind is not null && !_world.GetBlock(c).IsAir)
                {
                    _world.SetBlock(c, BlockId.Air);
                    BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = BlockId.AirValue });
                }

                _doors.RemoveAll(d => d.PlayerBuilt && d.Pos.ToBlock() == c);
                _repo.DeleteDoor(loc, c.X, c.Y, c.Z);
                if (kind is not null)
                {
                    _repo.SaveDoor(new StoredDoor { Planet = loc, X = c.X, Y = c.Y, Z = c.Z, Kind = kind, AxisX = axisX, Owner = owner });
                    _doors.Add(new ServerDoor
                    {
                        Id = _nextDoorId++,
                        Kind = kind,
                        Pos = new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f),
                        AxisX = axisX,
                        Width = 1f,
                        PlayerBuilt = true,
                        Owner = owner,
                    });
                }

                BaseAirDoorsChanged();
                BroadcastDoors();
                _stationAir.Remove(station.Id);
                var (min, max) = CellBox(s);
                RefreshStationBounds(station, min, max);
            },
            circ =>
            {
                var c = WorldConstants.CanonicalBlock(w, circ);
                if (kind is not null)
                {
                    _repo.SetBlock(loc, c, BlockId.AirValue);
                    _repo.SaveDoor(new StoredDoor { Planet = loc, X = c.X, Y = c.Y, Z = c.Z, Kind = kind, AxisX = axisX, Owner = owner });
                }
                else
                {
                    _repo.DeleteDoor(loc, c.X, c.Y, c.Z);
                }
            });
    }

    // ---------------- the interior on boarding ----------------

    /// <summary>
    /// #2607: brings a station's door records and its interior world's doors together, on every boarding (the station
    /// world is active). A record without a door in the world — built on a spacewalk before the station had an interior,
    /// or by an older server's records — is hung there (its cell cleared, the door stored); a door the world holds
    /// without a record — one built aboard, including those from before this release — becomes a record, so the hull
    /// seen from outside shows it and the commission rule counts it. Nothing is removed: a door taken out on either side
    /// is removed on both at once. The doors are then (re)loaded into the registry, so the arrival search and the air
    /// pockets already see them.
    /// </summary>
    private void SyncStationDoors(BoardableStation station, SpaceStructure src)
    {
        string loc = _world.LocationId;
        int circ = _world.Circumference;
        var stored = _repo.ListDoors(loc);
        var storedCells = new HashSet<Vector3i>(stored.Select(d => new Vector3i(d.X, d.Y, d.Z)));
        var cleared = new List<Vector3i>();
        bool recordsChanged = false;

        _repo.RunInTransaction(() =>
        {
            foreach (var (cell, axisX) in src.PlacedDoorAxes)
            {
                var c = WorldConstants.CanonicalBlock(StationCellToWorld(station, cell), circ);
                if (storedCells.Contains(c))
                {
                    continue;
                }

                if (!_world.GetBlock(c).IsAir)
                {
                    _world.SetBlock(c, BlockId.Air); // a stale block in the doorway (an edit from before #2607)
                    cleared.Add(c);
                }

                string kind = src.DoorKinds.TryGetValue(cell, out var k) ? k : DoorBlocks.Energy;
                _repo.SaveDoor(new StoredDoor { Planet = loc, X = c.X, Y = c.Y, Z = c.Z, Kind = kind, AxisX = axisX, Owner = src.OwnerId });
            }

            foreach (var d in stored)
            {
                var cell = WorldToStationCell(station, StationLocalWorld(station, new Vector3i(d.X, d.Y, d.Z)));
                if (src.PlacedDoorAxes.ContainsKey(cell) || !src.Get(cell).IsAir)
                {
                    continue;
                }

                AddPlacedShipDoor(src, cell, d.Kind, d.AxisX);
                recordsChanged = true;
            }
        });

        foreach (var c in cleared)
        {
            BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = BlockId.AirValue }); // a boarder already inside
        }

        if (recordsChanged && src.Boardable)
        {
            PersistStationDoors(src);
        }

        LoadPlayerDoors();
    }

    /// <summary>#2607: a door was built or taken out INSIDE a player station's world: the station's door records follow
    /// what the world now holds, so the hull seen from a spacewalk has the door (or the doorway is gone again) and the
    /// pilots outside see it at once. No-op outside materialised player-station worlds.</summary>
    private void SyncStationDoorRecordsFromWorld()
    {
        if (!IsPlayerStationWorld(_world.LocationId))
        {
            return;
        }

        string stationId = _world.LocationId.Substring(StationLocationIdPrefix.Length);
        if (!_stationsById.TryGetValue(stationId, out var station) || !station.Materialised
            || !_playerStationCells.TryGetValue(stationId, out var s))
        {
            return;
        }

        var inWorld = new Dictionary<Vector3i, ServerDoor>();
        foreach (var d in _doors)
        {
            if (d.PlayerBuilt)
            {
                inWorld[WorldToStationCell(station, StationLocalWorld(station, d.Pos.ToBlock()))] = d;
            }
        }

        bool changed = false;
        foreach (var cell in s.PlacedDoorAxes.Keys.ToList())
        {
            if (!inWorld.ContainsKey(cell))
            {
                changed |= RemovePlacedShipDoor(s, cell);
            }
        }

        foreach (var (cell, door) in inWorld)
        {
            if (!s.PlacedDoorAxes.ContainsKey(cell) && s.Get(cell).IsAir)
            {
                AddPlacedShipDoor(s, cell, door.Kind, door.AxisX);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        if (s.Boardable)
        {
            PersistStationDoors(s);
        }

        if (_spaceInstances.Values.FirstOrDefault(i => i.Structures.ContainsKey(s.Id)) is { } instance)
        {
            ResendStationDesign(instance, s);
        }
    }

    // ---------------- boarding at the door ----------------

    /// <summary>#2607: the door of a station the suit floats beside (within <see cref="StationDoorBoardReach"/> of its
    /// opening), the nearest one — or null. The station floats with its cell box centred on its position, as the flight
    /// view draws it, so a cell's flight position is <c>position + cell − box centre</c>.</summary>
    private Vector3i? StationDoorNearSuit(SpaceInstance instance, string playerId, SpaceStructure s)
    {
        if (s.PlacedDoorAxes.Count == 0 || s.Cells.Count == 0)
        {
            return null;
        }

        var suit = PilotPositionIn(instance, playerId);
        var (min, max) = CellBox(s);
        float cx = (min.X + max.X + 1) * 0.5f, cy = (min.Y + max.Y + 1) * 0.5f, cz = (min.Z + max.Z + 1) * 0.5f;
        Vector3i? best = null;
        float bestSq = StationDoorBoardReach * StationDoorBoardReach;
        foreach (var cell in s.PlacedDoorAxes.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
        {
            var at = new Vector3f(s.Position.X + cell.X + 0.5f - cx, s.Position.Y + cell.Y + 1.5f - cy, s.Position.Z + cell.Z + 0.5f - cz);
            float sq = at.DistanceSquared(suit);
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = cell;
            }
        }

        return best;
    }

    /// <summary>
    /// #2607: where a boarder through a station door arrives — the door's sealed inside: the cell in front of the doorway
    /// on the side that holds air, standable with headroom (a step or two in). Null when neither side is such a spot (a
    /// door into the vacuum, a doorway blocked inside); the caller then keeps the arrival next to the core (#2606). Runs
    /// after the stamp, with the station's doors registered, so the doorway seals the pocket it leads into.
    /// </summary>
    private Vector3f? StationDoorArrival(BoardableStation station, SpaceStructure src, Vector3i doorCell)
    {
        if (!src.PlacedDoorAxes.TryGetValue(doorCell, out bool axisX))
        {
            return null;
        }

        var w = StationCellToWorld(station, doorCell);
        int dx = axisX ? 0 : 1, dz = axisX ? 1 : 0; // across the wall
        for (int depth = 1; depth <= 2; depth++)
        {
            foreach (int side in new[] { 1, -1 })
            {
                var feet = new Vector3i(w.X + dx * side * depth, w.Y, w.Z + dz * side * depth);
                if (StandableAt(feet.X, feet.Y, feet.Z) && !InDoorway(feet.X, feet.Y, feet.Z) && InSealedStationPocket(station, feet))
                {
                    return new Vector3f(feet.X + 0.5f, feet.Y, feet.Z + 0.5f);
                }
            }
        }

        return null;
    }

    /// <summary>#2607: whether a boarder standing with their feet at this cell would stand in a doorway — a door (built
    /// or stamped) fills the feet or the head cell. No arrival is placed there: the door entity is no block, so the
    /// standable test sees air, and the air fill accepts a door cell through the pocket beside it — a player set down in
    /// a closed hinge or wooden door stood inside its leaf.</summary>
    private bool InDoorway(int x, int y, int z)
        => DoorFillsCell(new Vector3i(x, y, z)) || DoorFillsCell(new Vector3i(x, y + 1, z));

    // ---------------- test seams ----------------

    /// <summary>Test/inspection (#2607): whether a station build counts as having its airlock (door block or record).</summary>
    public bool StationHasAirlockForTest(SpaceStructure s) => StationHasAirlock(s);

    /// <summary>Test/inspection (#2607): a player station's door records (build cell, kind, axis).</summary>
    public IReadOnlyList<(Vector3i Cell, string Kind, bool AxisX)> StationDoorsForTest(string stationId)
        => _playerStationCells.TryGetValue(stationId, out var s)
            ? s.PlacedDoorAxes.Select(d => (d.Key, s.DoorKinds.TryGetValue(d.Key, out var k) ? k : string.Empty, d.Value)).ToList()
            : new List<(Vector3i, string, bool)>();
}
