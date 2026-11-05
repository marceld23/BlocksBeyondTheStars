// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3198: the world protections a growing ship construction site (#948) answers to. Only the keel went through the
/// hand-placement rules; every further hull cell was accepted on adjacency, the size cap and the helm count alone. A hull
/// could grow up to 15 cells into another player's base zone, over a landing pad or through walls and terrain — and the
/// site's box then refused every world block there to everybody (<see cref="ConstructionContains"/>), the base owner
/// included, until an admin cleared it.
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
        var p = session.State;
        var cell = WorldConstants.CanonicalBlock(
            new Vector3i(rec.Origin.X + local.X, rec.Origin.Y + local.Y, rec.Origin.Z + local.Z), _world.Circumference);
        if (!WithinBuildHeight(cell.Y))
        {
            return "@srv.place.height_limit"; // before the first world read, like the hand-placement rule
        }

        if (SolidWorldBlockAt(cell))
        {
            return "@srv.place.not_empty";
        }

        // A door a player hung in the world. Not DoorFillsCell: its stamped half counts the site's OWN slide door, whose
        // three-cell opening would refuse the lintel above the doorway.
        if (PlayerDoorFillsCell(cell))
        {
            return "@srv.place.door_here";
        }

        return ProtectedCellRefusal(session, cell)
            ?? (HullOrSiteAt(cell, p.PlayerId) ? "@srv.place.no_ship_interior" : null);
    }

    /// <summary>A world block a hull cell cannot share its cell with: anything but air and the blocks a parked hull may
    /// stand in (<see cref="HullPassableIds"/> — walk-through props such as flowers and grass, fluids).</summary>
    private bool SolidWorldBlockAt(Vector3i cell)
    {
        var existing = _world.GetBlock(cell);
        return !existing.IsAir && !HullPassableIds().Contains(existing.Value);
    }

    /// <summary>The protected structures of the hand-placement rules (landing_pad, station, factory, base), same order and
    /// tokens.</summary>
    private string? ProtectedCellRefusal(PlayerSession session, Vector3i cell)
    {
        var p = session.State;
        if (!p.IsAdmin && IsOnLandingPad(cell))
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

    /// <summary>A parked ship (any player's, the builder's own included, or a landed trader) or another player's
    /// construction site occupies the cell — probed the way the hand-placement ship_interior rule probes it.</summary>
    private bool HullOrSiteAt(Vector3i cell, string builderId)
    {
        var probe = new Vector3f(cell.X, cell.Y, cell.Z);
        return ShipInteriorContains(probe) || ConstructionContains(probe, exceptKey: ConstructionKey(builderId));
    }
}
