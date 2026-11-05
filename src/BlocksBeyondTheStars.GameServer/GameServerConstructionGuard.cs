// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3198: the world protections a growing ship construction site (#948) answers to. Only the keel went through the
/// hand-placement rules; every further hull cell was accepted on adjacency, the size cap and the helm count alone. A hull
/// could grow up to 15 cells into another player's base zone, over a landing pad or through walls and terrain — and the
/// site's box then refused every world block there to everybody (<see cref="ConstructionContains"/>), the base owner
/// included, until an admin cleared it. #3221: a parked ship its owner keeps building on — a commissioned self-built one,
/// and an authored one grown past its design box (#2120) — answers to the same rules (<see cref="ParkedShipCellRefusal"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Why the hull cell at structure-local <paramref name="local"/> of the site <paramref name="rec"/> may not be
    /// built, or null. The world cell under it is held to the rules hand placement checks for a block set down there,
    /// with their refusal tokens: the build band, a solid block in the way (a flower, grass tuft or a fluid is not — a
    /// parked hull stands in them too), a player's door, a landing pad, a station, a protected factory, somebody else's
    /// base zone (owner, allies and admins build there freely), a parked ship and another player's construction site.</summary>
    private string? ConstructionCellRefusal(PlayerSession session, LandedShip rec, Vector3i local)
    {
        var cell = HullWorldCell(rec, local);
        return HullCellBlocked(cell)
            ?? ProtectedCellRefusal(session, cell, padCounts: true)
            ?? (HullOrSiteAt(cell, exceptShipKey: null, exceptSiteKey: ConstructionKey(session.State.PlayerId))
                ? "@srv.place.no_ship_interior"
                : null);
    }

    /// <summary>#3221: why a new cell at structure-local <paramref name="local"/> of the player's parked ship
    /// <paramref name="rec"/> may not be built, or null — the rules of <see cref="ConstructionCellRefusal"/>, with the
    /// ship's own room left out. Inside the ship's box (its design box: its cells and the air between them) a cell takes
    /// nothing from the world the parked ship does not hold already: furnishing the cabin stays free, even where the ship
    /// was parked inside a zone it may not grow into. Only a <paramref name="selfBuilt"/> hull answers there for the world
    /// cell itself — it was laid cell by cell under the world checks (#3198), so none of its cells shares a world cell with
    /// a solid block or a player's door, nor leaves the build band; an authored design box holds air by design (#2120). A
    /// cell that grows the box — over the roof, or (an authored ship, #2120) past its sides — is held to all of it: the
    /// build band, a solid block, a player's door, someone else's base zone (owner, allies and admins pass), a station, a
    /// protected factory, a landing pad other than the one the ship stands on (a ship parked on a pad stands in its
    /// landing volume and may grow over it), every other parked ship and every construction site, the owner's own
    /// included.</summary>
    private string? ParkedShipCellRefusal(PlayerSession session, LandedShip rec, Vector3i local, bool selfBuilt)
    {
        var s = rec.Structure;
        var cell = HullWorldCell(rec, local);
        bool ownColumn = local.X >= 0 && local.X < s.Width && local.Z >= 0 && local.Z < s.Length;
        bool inBox = ownColumn && local.Y >= 0 && local.Y < s.Height;
        if ((selfBuilt || !inBox) && HullCellBlocked(cell) is { } blocked)
        {
            return blocked;
        }

        if (inBox)
        {
            return null;
        }

        return ProtectedCellRefusal(session, cell, padCounts: !ownColumn && !OnParkedShipsOwnPad(rec, cell))
            ?? (HullOrSiteAt(cell, exceptShipKey: session.State.PlayerId, exceptSiteKey: null)
                ? "@srv.place.no_ship_interior"
                : null);
    }

    /// <summary>#3221: the cell stands on a column of the pad (communal or base) the parked ship stands on — the one whose
    /// landing volume (<see cref="IsOnLandingPad"/>) holds the centre of the ship's floor. That pad is the ship's own while
    /// it is parked there: it may grow over it, sideways and up, and under its floor where there is room.</summary>
    private bool OnParkedShipsOwnPad(LandedShip rec, Vector3i cell)
    {
        var s = rec.Structure;
        var floor = HullWorldCell(rec, new Vector3i(s.Width / 2, 0, s.Length / 2));
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, floor.X, floor.Z) && floor.Y >= pad.CenterY - 2 && floor.Y <= pad.CenterY + PadClearanceHeight)
            {
                return OnPadColumn(pad, cell.X, cell.Z);
            }
        }

        foreach (var pad in _basePads)
        {
            if (pad.Planet == _world.LocationId && InBasePadSquare(pad, floor, -1, BasePadRules.Clearance))
            {
                return InBasePadSquare(pad, cell, AnyHeight, AnyHeight); // its column, at any height
            }
        }

        return false;
    }

    /// <summary>A height reach that makes <see cref="InBasePadSquare"/> a column test.</summary>
    private const int AnyHeight = 1 << 16;

    /// <summary>The canonical world cell under the structure-local cell <paramref name="local"/> of a placed hull.</summary>
    private Vector3i HullWorldCell(LandedShip rec, Vector3i local)
        => WorldConstants.CanonicalBlock(
            new Vector3i(rec.Origin.X + local.X, rec.Origin.Y + local.Y, rec.Origin.Z + local.Z), _world.Circumference);

    /// <summary>What no hull cell may share its world cell with, in the hand-placement order and tokens: the build band
    /// (checked before the first world read), a solid world block and a door a player hung in the world. Not
    /// DoorFillsCell: its stamped half counts a site's OWN slide door, whose three-cell opening would refuse the lintel
    /// above the doorway.</summary>
    private string? HullCellBlocked(Vector3i cell)
    {
        if (!WithinBuildHeight(cell.Y))
        {
            return "@srv.place.height_limit";
        }

        if (SolidWorldBlockAt(cell))
        {
            return "@srv.place.not_empty";
        }

        return PlayerDoorFillsCell(cell) ? "@srv.place.door_here" : null;
    }

    /// <summary>A world block a hull cell cannot share its cell with: anything but air and the blocks a parked hull may
    /// stand in (<see cref="HullPassableIds"/> — walk-through props such as flowers and grass, fluids).</summary>
    private bool SolidWorldBlockAt(Vector3i cell)
    {
        var existing = _world.GetBlock(cell);
        return !existing.IsAir && !HullPassableIds().Contains(existing.Value);
    }

    /// <summary>The protected structures of the hand-placement rules (landing_pad, station, factory, base), same order and
    /// tokens. <paramref name="padCounts"/> false leaves the landing pad out (#3221: a parked ship over its own footprint).</summary>
    private string? ProtectedCellRefusal(PlayerSession session, Vector3i cell, bool padCounts)
    {
        var p = session.State;
        if (padCounts && !p.IsAdmin && IsOnLandingPad(cell))
        {
            return "@srv.place.pad_reserved";
        }

        if (IsStationBlock(cell))
        {
            return "@srv.protect.station";
        }

        if (IsFactoryProtected(cell, p.PlayerId, p.IsAdmin))
        {
            return "@srv.protect.factory";
        }

        return IsBaseProtected(cell, p.PlayerId, p.IsAdmin) ? "@srv.protect.base" : null;
    }

    /// <summary>A parked ship (a player's or a landed trader's) or a construction site occupies the cell — probed the way
    /// the hand-placement ship_interior rule probes it. <paramref name="exceptShipKey"/> and <paramref name="exceptSiteKey"/>
    /// leave the hull that grows out: a site is not in its own way (the builder's parked ship is), nor is a parked ship
    /// (the builder's own site is).</summary>
    private bool HullOrSiteAt(Vector3i cell, string? exceptShipKey, string? exceptSiteKey)
    {
        var probe = new Vector3f(cell.X, cell.Y, cell.Z);
        return ShipInteriorContains(probe, exceptShipKey) || ConstructionContains(probe, exceptSiteKey);
    }
}
