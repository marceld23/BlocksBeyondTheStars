// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Growing an authored ship (#2119 – #2121): the geometry over a <see cref="SpaceStructure"/> — placed doors, extents,
/// the 15 × 15 × 15 fit, the sealed-air flood fill, door openings, hatch cells, door axis — is the static
/// <see cref="ShipExtensionGeometry"/>. This partial keeps the names the sibling partials call (constants and one-line
/// forwards to the geometry), the server-side glue (parked-ship edits, inventory, reach, door registration) and the
/// test seams.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The axis of an owner-built door, stored in the shape field of its structure edit.</summary>
    private const int PlacedDoorAxisX = ShipExtensionGeometry.PlacedDoorAxisX;
    private const int PlacedDoorAxisZ = ShipExtensionGeometry.PlacedDoorAxisZ;

    /// <summary>The shape field of an air edit the owner made on purpose (#2121) — a hit never writes it.</summary>
    private const int OwnerRemovedMark = ShipExtensionGeometry.OwnerRemovedMark;

    private static void AddPlacedShipDoor(SpaceStructure s, Vector3i cell, string kind, bool axisX)
        => ShipExtensionGeometry.AddPlacedShipDoor(s, cell, kind, axisX);

    private static bool RemovePlacedShipDoor(SpaceStructure s, Vector3i cell) => ShipExtensionGeometry.RemovePlacedShipDoor(s, cell);

    /// <summary>The owner-built door whose three-tall opening holds this cell, if any.</summary>
    private static Vector3i? PlacedDoorCovering(SpaceStructure s, Vector3i cell) => ShipExtensionGeometry.PlacedDoorCovering(s, cell);

    /// <summary>#2120: whether a new cell keeps the ship within 15 × 15 × 15 (per axis; a design already larger on an
    /// axis may not grow further on it).</summary>
    private static bool ShipExtensionFits(SpaceStructure s, Vector3i cell) => ShipExtensionGeometry.ShipExtensionFits(s, cell);

    /// <summary>#2120: whether an owner may build into this cell of their parked ship on foot: inside the design box
    /// always; outside it only an authored ship (a self-built one keeps its construction rules), only within
    /// 15 × 15 × 15, and only into open air of the world it is parked on — never into the ground or a building.</summary>
    private bool ShipCellBuildable(LandedShip rec, Vector3i cell)
    {
        var s = rec.Structure;
        if (cell.X >= 0 && cell.X < s.Width && cell.Y >= 0 && cell.Y <= s.Height && cell.Z >= 0 && cell.Z < s.Length)
        {
            return true;
        }

        if (_ship.IsCustom || !ShipExtensionFits(s, cell))
        {
            return false;
        }

        var world = WorldConstants.CanonicalBlock(
            new Vector3i(rec.Origin.X + cell.X, rec.Origin.Y + cell.Y, rec.Origin.Z + cell.Z), _world.Circumference);
        return _world.GetBlock(world).IsAir;
    }

    /// <summary>#2120: the ship's sealed air, cached per structure revision (the flood fill is
    /// <see cref="ShipExtensionGeometry.EnsureShipAir"/>, with this server's content deciding what is airtight).</summary>
    private void EnsureShipAir(SpaceStructure s) => ShipExtensionGeometry.EnsureShipAir(s, _content);

    /// <summary>The air cells every door of the ship fills: its gap along the wall, three cells high.</summary>
    private static HashSet<Vector3i> DoorOpeningCells(SpaceStructure s) => ShipExtensionGeometry.DoorOpeningCells(s);

    /// <summary>#2587: the doorways of the design's own doors that open to space (see
    /// <see cref="ShipExtensionGeometry.ShipHatchCells"/>).</summary>
    private static List<Vector3i> ShipHatchCells(SpaceStructure s) => ShipExtensionGeometry.ShipHatchCells(s);

    /// <summary>#2120: true when the position stands in a sealed pocket of the parked ship (feet or head cell).</summary>
    private bool InSealedShipAir(LandedShip rec, Vector3f p)
    {
        var s = rec.Structure;
        EnsureShipAir(s);
        var feet = rec.ToLocal(new Vector3i(
            (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y), (int)System.Math.Floor(p.Z)), _world.Circumference);
        return s.SealedAir!.Contains(feet) || s.SealedAir.Contains(new Vector3i(feet.X, feet.Y + 1, feet.Z));
    }

    /// <summary>#2120: true when the position is within the ship's real extents (+ a margin) — its design box plus
    /// every cell the owner built on. Outside of them, a walk out of the hull in space is a spacewalk.</summary>
    private bool WithinShipExtents(LandedShip rec, Vector3f pos, float margin)
    {
        var s = rec.Structure;
        EnsureShipAir(s);
        double lx = WorldConstants.WrapDeltaX(pos.X - rec.Origin.X, _world.Circumference);
        double ly = pos.Y - rec.Origin.Y, lz = pos.Z - rec.Origin.Z;
        return ly >= s.ExtentMin.Y - margin
            && lx >= s.ExtentMin.X - margin && lx <= s.ExtentMax.X + 1 + margin
            && lz >= s.ExtentMin.Z - margin && lz <= s.ExtentMax.Z + 1 + margin;
    }

    /// <summary>The wall axis a door built into a ship hangs along: the jambs around its cell decide, else the wall
    /// faces the builder (the shared placed-door rule, #1975). <paramref name="yawInShip"/> is the builder's heading in
    /// the ship's own frame.</summary>
    private static bool ShipDoorAxis(SpaceStructure s, Vector3i cell, double yawInShip)
        => ShipExtensionGeometry.ShipDoorAxis(s, cell, yawInShip);

    /// <summary>#2119: builds a door into a ship's cell — the cell stays a doorway (air), the door is registered as
    /// the ship's, and the edit is persisted as the door block with its axis.</summary>
    private void BuildShipDoor(SpaceStructure s, Vector3i cell, BlockDefinition doorBlock, double yawInShip)
    {
        bool axisX = ShipDoorAxis(s, cell, yawInShip);
        s.Set(cell, BlockId.Air);
        AddPlacedShipDoor(s, cell, DoorBlocks.KindForBlock(doorBlock.Key), axisX);
        s.OwnerRemoved.Remove(cell);
        _repo.SetStructureBlock(StructureEditStoreId(s), cell, doorBlock.NumericId.Value, axisX ? PlacedDoorAxisX : PlacedDoorAxisZ);
    }

    /// <summary>#2119: takes an owner-built door out of a ship again: the doorway is plain air, the door item goes back
    /// to the builder. Refused (false, with a message) when the item does not fit.</summary>
    private bool TakeShipDoor(PlayerSession session, SpaceStructure s, Vector3i doorCell)
    {
        string item = DoorBlocks.ItemFor(s.DoorKinds.TryGetValue(doorCell, out var kind) ? kind : DoorBlocks.Energy);
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.CanFit(new[] { new ItemAmount(item, 1) }))
        {
            Reject(session, "structure", "@inventory_full");
            return false;
        }

        RemovePlacedShipDoor(s, doorCell);
        _repo.SetStructureBlock(StructureEditStoreId(s), doorCell, BlockId.AirValue, OwnerRemovedMark);
        s.OwnerRemoved.Add(doorCell);
        pool.Add(item, 1);
        SendInventory(session);
        return true;
    }

    /// <summary>After a doorway change on a parked ship: its anchors, its doors in the world and everyone's view of it.</summary>
    private void RefreshParkedShipDoors(LandedShip rec)
    {
        DeriveLandedAnchors(rec);
        RegisterDoors();
    }

    /// <summary>#2119: a world mine aimed at a door the player built into their own parked ship picks it up (the door is
    /// the ship's, not the world's). True when it handled the mine.</summary>
    private bool TryPickUpShipDoor(PlayerSession session, Vector3i worldCell)
    {
        var rec = _worlds.Active.LandedFor(session.State.PlayerId);
        if (!rec.Placed)
        {
            return false;
        }

        var local = rec.ToLocal(worldCell, _world.Circumference);
        if (PlacedDoorCovering(rec.Structure, local) is not { } doorCell)
        {
            return false;
        }

        if (!WithinReach(session.State, worldCell))
        {
            Reject(session, "mine", "@out_of_reach");
            return true;
        }

        if (TakeShipDoor(session, rec.Structure, doorCell))
        {
            RefreshParkedShipDoors(rec);
        }

        return true;
    }

    /// <summary>Test/inspection: the owner-built doors of a player's parked ship (structure-local cell, kind, axis).</summary>
    public IReadOnlyList<(Vector3i Cell, string Kind, bool AxisX)> PlacedShipDoorsForTest(string playerId)
    {
        var s = _worlds.Active.LandedFor(playerId).Structure;
        return s.PlacedDoorAxes.Select(d => (d.Key, s.DoorKinds.TryGetValue(d.Key, out var k) ? k : string.Empty, d.Value)).ToList();
    }

    /// <summary>Test/inspection (#2587): the hatch cells a ship structure's design message carries.</summary>
    public static IReadOnlyList<Vector3i> ShipHatchCellsForTest(SpaceStructure s) => ShipHatchCells(s);

    /// <summary>Test/inspection: whether a structure-local cell of a player's parked ship holds sealed ship air.</summary>
    public bool ShipCellSealedForTest(string playerId, int x, int y, int z)
    {
        var s = _worlds.Active.LandedFor(playerId).Structure;
        EnsureShipAir(s);
        return s.SealedAir!.Contains(new Vector3i(x, y, z));
    }

    /// <summary>Resends the owner's ship design to everyone in the flight instance (a doorway changed, #2119).</summary>
    private void ResendShipDesignToInstance(SpaceInstance instance, SpaceStructure s, string ownerId)
    {
        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { } other)
            {
                SendShipDesign(other, s, pid == ownerId ? null : "ship_remote");
            }
        }
    }
}
