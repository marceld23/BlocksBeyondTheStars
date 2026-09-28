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
/// Growing an authored ship (#2119 – #2121, a player report: "I extended my ship at the back, but as soon as I go
/// through the door my area doesn't count as ship"). An owner may build onto the ship — on a spacewalk and on foot —
/// up to the Ship Keel's 15 × 15 × 15; the ship then is its real cells, not its design box:
/// <list type="bullet">
/// <item><b>Doors</b> built into it become real doors (a doorway cell + its kind and axis), drawn in flight too.</item>
/// <item>Walking out of the ship in space starts a spacewalk only outside its real extents.</item>
/// <item>An extension has ship air only when it is <b>sealed</b>: airtight full cubes and door openings close it,
/// exactly like the Ship Keel's airtightness check and a player station; the design box always has air, so every
/// existing cabin keeps working.</item>
/// <item>Cells the owner took out on purpose are the new design — the repair never walls them up again.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The biggest a ship may grow on each axis — the Ship Keel's limit (a design already larger keeps its size).</summary>
    private const int ShipExtensionMaxSize = 15;

    /// <summary>The axis of an owner-built door, stored in the shape field of its structure edit.</summary>
    private const int PlacedDoorAxisX = 1;
    private const int PlacedDoorAxisZ = 2;

    /// <summary>The shape field of an air edit the owner made on purpose (#2121) — a hit never writes it.</summary>
    private const int OwnerRemovedMark = -1;

    private static readonly Vector3i[] AirSteps =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static void AddPlacedShipDoor(SpaceStructure s, Vector3i cell, string kind, bool axisX)
    {
        if (!s.DoorCells.Contains(cell))
        {
            s.DoorCells.Add(cell);
        }

        s.DoorKinds[cell] = kind;
        s.PlacedDoorAxes[cell] = axisX;
        s.Revision++;
    }

    private static bool RemovePlacedShipDoor(SpaceStructure s, Vector3i cell)
    {
        if (!s.PlacedDoorAxes.Remove(cell))
        {
            return false;
        }

        s.DoorCells.Remove(cell);
        s.DoorKinds.Remove(cell);
        s.Revision++;
        return true;
    }

    /// <summary>The owner-built door whose three-tall opening holds this cell, if any.</summary>
    private static Vector3i? PlacedDoorCovering(SpaceStructure s, Vector3i cell)
    {
        for (int dy = 0; dy <= 2; dy++)
        {
            var c = new Vector3i(cell.X, cell.Y - dy, cell.Z);
            if (s.PlacedDoorAxes.ContainsKey(c))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The design's own extents — the baseline cells, or the design box when it has none.</summary>
    private static (Vector3i Min, Vector3i Max) DesignExtents(SpaceStructure s)
    {
        var min = new Vector3i(0, 0, 0);
        var max = new Vector3i(System.Math.Max(0, s.Width - 1), System.Math.Max(0, s.Height), System.Math.Max(0, s.Length - 1));
        foreach (var c in s.Baseline)
        {
            min = new Vector3i(System.Math.Min(min.X, c.X), System.Math.Min(min.Y, c.Y), System.Math.Min(min.Z, c.Z));
            max = new Vector3i(System.Math.Max(max.X, c.X), System.Math.Max(max.Y, c.Y), System.Math.Max(max.Z, c.Z));
        }

        return (min, max);
    }

    /// <summary>Everything the ship is now: its cells, its doorways and the design box.</summary>
    private static (Vector3i Min, Vector3i Max) CellExtents(SpaceStructure s)
    {
        var (min, max) = DesignExtents(s);
        foreach (var c in s.Cells.Keys.Concat(s.DoorCells))
        {
            min = new Vector3i(System.Math.Min(min.X, c.X), System.Math.Min(min.Y, c.Y), System.Math.Min(min.Z, c.Z));
            max = new Vector3i(System.Math.Max(max.X, c.X), System.Math.Max(max.Y, c.Y), System.Math.Max(max.Z, c.Z));
        }

        return (min, max);
    }

    /// <summary>#2120: whether a new cell keeps the ship within 15 × 15 × 15 (per axis; a design already larger on an
    /// axis may not grow further on it).</summary>
    private static bool ShipExtensionFits(SpaceStructure s, Vector3i cell)
    {
        var (dMin, dMax) = DesignExtents(s);
        var (min, max) = CellExtents(s);
        min = new Vector3i(System.Math.Min(min.X, cell.X), System.Math.Min(min.Y, cell.Y), System.Math.Min(min.Z, cell.Z));
        max = new Vector3i(System.Math.Max(max.X, cell.X), System.Math.Max(max.Y, cell.Y), System.Math.Max(max.Z, cell.Z));
        return max.X - min.X + 1 <= System.Math.Max(ShipExtensionMaxSize, dMax.X - dMin.X + 1)
            && max.Y - min.Y + 1 <= System.Math.Max(ShipExtensionMaxSize, dMax.Y - dMin.Y + 1)
            && max.Z - min.Z + 1 <= System.Math.Max(ShipExtensionMaxSize, dMax.Z - dMin.Z + 1);
    }

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

    /// <summary>#2120: the ship's sealed air, cached per structure revision. A flood from the outside of the extents
    /// (+1) through everything that does not seal; the air cells it never reaches are sealed. Airtight full cubes seal
    /// (a shaped cell leaks — the base and station rule), and so does every door opening, whole: its gap width and three
    /// cells high, like the Ship Keel's check (a door panel fills its opening).</summary>
    private void EnsureShipAir(SpaceStructure s)
    {
        if (s.SealedAir is not null && s.AirRevision == s.Revision)
        {
            return;
        }

        var (min, max) = CellExtents(s);
        var doorOpenings = DoorOpeningCells(s);

        bool Seals(Vector3i c)
        {
            if (doorOpenings.Contains(c))
            {
                return true;
            }

            var b = s.Get(c);
            if (b.IsAir || _content.BlockById(b) is not { Airtight: true })
            {
                return false;
            }

            return !s.Shapes.TryGetValue(c, out int shape) || ShapeCode.IsCube(shape);
        }

        var lo = new Vector3i(min.X - 1, min.Y - 1, min.Z - 1);
        var hi = new Vector3i(max.X + 1, max.Y + 1, max.Z + 1);
        bool InBox(Vector3i c) => c.X >= lo.X && c.X <= hi.X && c.Y >= lo.Y && c.Y <= hi.Y && c.Z >= lo.Z && c.Z <= hi.Z;

        var outside = new HashSet<Vector3i>();
        var frontier = new Queue<Vector3i>();
        for (int x = lo.X; x <= hi.X; x++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int z = lo.Z; z <= hi.Z; z++)
                {
                    if (x != lo.X && x != hi.X && y != lo.Y && y != hi.Y && z != lo.Z && z != hi.Z)
                    {
                        continue;
                    }

                    var c = new Vector3i(x, y, z);
                    if (!Seals(c) && outside.Add(c))
                    {
                        frontier.Enqueue(c);
                    }
                }

        while (frontier.Count > 0)
        {
            var c = frontier.Dequeue();
            foreach (var d in AirSteps)
            {
                var n = new Vector3i(c.X + d.X, c.Y + d.Y, c.Z + d.Z);
                if (InBox(n) && !Seals(n) && outside.Add(n))
                {
                    frontier.Enqueue(n);
                }
            }
        }

        var sealedAir = new HashSet<Vector3i>();
        for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var c = new Vector3i(x, y, z);
                    if (s.Get(c).IsAir && !outside.Contains(c))
                    {
                        sealedAir.Add(c); // incl. the door openings themselves — standing in a doorway is inside
                    }
                }

        s.SealedAir = sealedAir;
        s.ExtentMin = min;
        s.ExtentMax = max;
        s.AirRevision = s.Revision;
    }

    /// <summary>The air cells every door of the ship fills: its gap along the wall, three cells high. An owner-built door
    /// is one cell wide on its recorded axis; a design door is measured like its registration measures it.</summary>
    private static HashSet<Vector3i> DoorOpeningCells(SpaceStructure s)
    {
        var cells = new HashSet<Vector3i>();
        bool Solid(int x, int y, int z) => !s.Get(new Vector3i(x, y, z)).IsAir;
        foreach (var d in s.DoorCells)
        {
            bool axisX;
            int lo, hi;
            if (s.PlacedDoorAxes.TryGetValue(d, out bool placedAxis))
            {
                axisX = placedAxis;
                lo = hi = 0;
            }
            else
            {
                var fit = DoorProbe.Measure(Solid, d.X, d.Y, d.Z, d.Z == 0 ? true : null);
                axisX = fit.AxisX;
                lo = fit.Lo;
                hi = fit.Hi;
            }

            for (int w = lo; w <= hi; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    var c = axisX ? new Vector3i(d.X + w, d.Y + dy, d.Z) : new Vector3i(d.X, d.Y + dy, d.Z + w);
                    if (!Solid(c.X, c.Y, c.Z))
                    {
                        cells.Add(c);
                    }
                }
            }
        }

        return cells;
    }

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
        => DoorProbe.AxisForPlacedDoor((x, y, z) => !s.Get(new Vector3i(x, y, z)).IsAir, cell.X, cell.Y, cell.Z, yawInShip);

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
