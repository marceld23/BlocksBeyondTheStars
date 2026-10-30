// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Several bases per world (#2996, Marcel 2026-10-29): a player may found up to <c>GameRules.BasesPerWorld</c> base cores on
/// one body, and touching or overlapping bases are fine. What that needs beyond the single-base code:
/// <list type="bullet">
/// <item><b>One base per thing.</b> A bed, post, crate or machine inside the reach of two bases of one partner group (the
/// player and their allies) counts for the base whose core is nearest — ties go to the lower core cell — so an overlap never
/// counts a bed twice. A stranger's base never takes anything (it could otherwise be planted beside a yard to pull its beds).</item>
/// <item><b>Residents per body.</b> One player's bases on one body hold <see cref="BaseRules.ResidentCapPerBody"/> residents
/// together; every base keeps at least its founder.</item>
/// <item><b>Addressed by the core cell.</b> Renaming and the resident list name the base by its cell; a request without one
/// (an older client) takes the player's first base on the body.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many base cores the player owns on the body.</summary>
    private int CountPlayerBasesOn(string ownerId, string body) => _bases.Count(b => b.OwnerId == ownerId && b.Planet == body);

    /// <summary>The player's base on <paramref name="body"/> at <paramref name="cell"/>, or — without a cell — their first
    /// base there; null when there is none.</summary>
    private ServerBase? FindOwnBase(string ownerId, string body, bool hasCell, Vector3i cell)
        => hasCell
            ? _bases.FirstOrDefault(b => b.OwnerId == ownerId && b.Planet == body && b.Cell == cell)
            : _bases.FirstOrDefault(b => b.OwnerId == ownerId && b.Planet == body);

    /// <summary>Horizontal-and-vertical wrap-aware squared distance between two cells (for "nearest core").</summary>
    private long CellDistSq(Vector3i a, Vector3i b)
    {
        long dx = WorldConstants.WrapDeltaX(a.X - b.X, _world.Circumference);
        long dz = WorldConstants.WrapDeltaZ(a.Z - b.Z, _world.Circumference);
        long dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>True when <paramref name="a"/>'s core is nearer to <paramref name="cell"/> than <paramref name="b"/>'s, ties
    /// broken by the core cell so the answer never depends on load order.</summary>
    private bool CoreNearer(ServerBase a, ServerBase b, Vector3i cell)
    {
        long da = CellDistSq(a.Cell, cell), db = CellDistSq(b.Cell, cell);
        if (da != db)
        {
            return da < db;
        }

        return a.Cell.X != b.Cell.X ? a.Cell.X < b.Cell.X : a.Cell.Y != b.Cell.Y ? a.Cell.Y < b.Cell.Y : a.Cell.Z < b.Cell.Z;
    }

    /// <summary>The other bases of <paramref name="b"/>'s partner group (the owner and their allies) on its body — the only
    /// ones a thing inside <paramref name="b"/> can go to. Built once per index rebuild.</summary>
    private List<ServerBase> PartnerBasesOf(ServerBase b)
    {
        var list = new List<ServerBase>();
        foreach (var o in _bases)
        {
            if (o.Id != b.Id && o.Planet == b.Planet && (o.OwnerId == b.OwnerId || AreAllied(o.OwnerId, b.OwnerId)))
            {
                list.Add(o);
            }
        }

        return list;
    }

    /// <summary>Whether a cell lies in the 17³ core zone of a base (where everything counts as inside).</summary>
    private bool InCoreZone(ServerBase b, Vector3i cell)
        => Math.Abs(WorldConstants.WrapDeltaX(cell.X - b.Cell.X, _world.Circumference)) <= BaseProtectionRadius
           && Math.Abs(cell.Y - b.Cell.Y) <= BaseProtectionRadius
           && Math.Abs(WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, _world.Circumference)) <= BaseProtectionRadius;

    /// <summary>Whether a base's index reads this cell at all — the box <c>BaseIndexEdits</c> asks the store for.</summary>
    private bool InBaseIndexWindow(ServerBase b, Vector3i cell)
    {
        int reach = Math.Min(BaseWallReach(b), BaseIndexHalfXZ);
        return Math.Abs(WorldConstants.WrapDeltaX(cell.X - b.Cell.X, _world.Circumference)) <= reach
               && Math.Abs(cell.Y - b.Cell.Y) <= BaseIndexHalfY
               && Math.Abs(WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, _world.Circumference)) <= reach;
    }

    /// <summary>
    /// Whether a thing at <paramref name="cell"/> inside <paramref name="b"/>'s reach counts for <paramref name="b"/>: no other
    /// base of its partner group takes it. Another base takes it only where it would count it itself — its core is nearer, its
    /// index reads the cell, and the cell is not in <paramref name="b"/>'s core zone unless it is in the other's core zone too
    /// (a core zone counts everything, so a bed there must not go to a base that would not see it as inside).
    /// </summary>
    private bool BaseOwnsCell(ServerBase b, Vector3i cell, List<ServerBase>? partners = null)
    {
        partners ??= PartnerBasesOf(b);
        if (partners.Count == 0)
        {
            return true;
        }

        bool inOwnCoreZone = InCoreZone(b, cell);
        foreach (var o in partners)
        {
            if (CoreNearer(o, b, cell) && InBaseIndexWindow(o, cell) && (!inOwnCoreZone || InCoreZone(o, cell)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The base that counts a thing a player placed at <paramref name="cell"/> — among the player's and their allies'
    /// bases reaching it, the one <see cref="BaseOwnsCell"/> gives it to (the player's own first), or null.</summary>
    private ServerBase? BaseCountingCell(Vector3i cell, string playerId)
    {
        ServerBase? fallback = null;
        foreach (var b in _bases.Where(x => x.Planet == _world.LocationId && OwnOrAllied(x.OwnerId, playerId) && WithinBaseWallReach(x, cell))
                     .OrderBy(x => CellDistSq(x.Cell, cell)))
        {
            if (BaseOwnsCell(b, cell))
            {
                return b;
            }

            fallback ??= b;
        }

        return fallback;
    }

    /// <summary>A base came or went, or an alliance changed: the indexes of the partner bases on the body are rebuilt, since a
    /// thing between two of them may now count for the other one.</summary>
    private void MarkPartnerIndexesDirty(string body, string ownerId)
    {
        foreach (var o in _bases)
        {
            if (o.Planet == body && (o.OwnerId == ownerId || AreAllied(o.OwnerId, ownerId)) && _baseIndex.TryGetValue(o.Id, out var idx))
            {
                idx.Dirty = true;
            }
        }
    }

    /// <summary>The base on the active world that a thing at <paramref name="cell"/> belongs to among those
    /// <paramref name="filter"/> admits and whose reach covers it — the nearest core.</summary>
    private ServerBase? NearestBaseCovering(Vector3i cell, Func<ServerBase, bool> filter)
    {
        ServerBase? best = null;
        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId && filter(b) && WithinBaseWallReach(b, cell) && (best is null || CoreNearer(b, best, cell)))
            {
                best = b;
            }
        }

        return best;
    }

    /// <summary>How many residents <paramref name="b"/> may hold now: the per-base cap, less what the owner's other bases on
    /// the body already hold (their saved plans), never below the founder.</summary>
    private int BaseResidentRoom(ServerBase b)
    {
        int others = 0;
        foreach (var o in _bases)
        {
            if (o.Id != b.Id && o.OwnerId == b.OwnerId && o.Planet == b.Planet
                && _meta.BaseResidents.TryGetValue(BaseBoardKey(o), out var plan))
            {
                others += plan.Count;
            }
        }

        return Math.Max(1, Math.Min(BaseResidentCap, BaseRules.ResidentCapPerBody - others));
    }

    /// <summary>The players' bases a body holds, by owner — for the planet scan's settler list (#2998).</summary>
    private Dictionary<string, int> BaseCountsByOwner(string body)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in _bases)
        {
            if (b.Planet == body)
            {
                counts[b.OwnerId] = counts.TryGetValue(b.OwnerId, out int n) ? n + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>Test seam: rename the player's base at a cell on a body (the E-at-the-core path, #2996).</summary>
    public void RenameBaseAtForTest(PlayerSession session, Vector3i cell, string name)
        => HandleSetBaseName(session, new Networking.Messages.SetBaseNameIntent
        {
            BodyId = session.CurrentLocationId,
            Name = name,
            HasCell = true,
            CellX = cell.X,
            CellY = cell.Y,
            CellZ = cell.Z,
        });

    /// <summary>Test seam: how many residents a base may hold right now (#2996).</summary>
    public int BaseResidentRoomForTest(Vector3i core)
        => _bases.FirstOrDefault(b => b.Planet == _world.LocationId && b.Cell == core) is { } b ? BaseResidentRoom(b) : 0;

    /// <summary>Test seam: whether the base at <paramref name="core"/> counts a thing at <paramref name="cell"/>.</summary>
    public bool BaseOwnsCellForTest(Vector3i core, Vector3i cell)
        => _bases.FirstOrDefault(b => b.Planet == _world.LocationId && b.Cell == core) is { } b && BaseOwnsCell(b, cell);

    /// <summary>Test seam: stands in for a base's saved residents (slots 0..n-1, no beds) — the per-body cap reads the plans.</summary>
    public void SeedBasePlanForTest(Vector3i core, int residents)
    {
        if (_bases.FirstOrDefault(b => b.Planet == _world.LocationId && b.Cell == core) is { } b)
        {
            _meta.BaseResidents[BaseBoardKey(b)] = Enumerable.Range(0, residents).Select(i => new BaseResidentRecord { Slot = i }).ToList();
        }
    }

    /// <summary>Test seam: the residents' saved plan of a base, as the scan leaves it (#2996).</summary>
    public IReadOnlyList<BaseResidentRecord> BasePlanForTest(Vector3i core)
        => _bases.FirstOrDefault(b => b.Planet == _world.LocationId && b.Cell == core) is { } b
           && _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan)
            ? plan
            : Array.Empty<BaseResidentRecord>();
}
