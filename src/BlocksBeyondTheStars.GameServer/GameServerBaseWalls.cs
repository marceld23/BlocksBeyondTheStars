// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Walled base areas (#1315): wild animals do not spawn inside a closed ring of walls within a founded
/// base's reach. Lyxette's yard has high walls and no roof — correctly NOT an airtight room for the air
/// system (#794), and far outside the radius-8 build-protection cube — so neither existing predicate
/// answered "is this spot fenced in?". This one does, with an <b>outside-in fill</b>:
/// <list type="bullet">
/// <item>Take the base's reach box (Chebyshev <see cref="SealedRoomMaxReach"/> = 48 → a 97×97 footprint),
/// seed every boundary column at the query's feet level, and flood INWARD through cells a walking animal
/// could pass. Everything inside the box the fill never reaches is <b>enclosed</b> — one fill answers every
/// yard, courtyard and room at that level, however many there are.</item>
/// <item>Cached per (base, feet level) like <c>_baseAir</c>: the same recompute interval, a bounded budget,
/// <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base never drags chunk generation. <b>Fail-open</b>:
/// an unloaded column reads as air, the fill leaks in, the spawn is allowed — the same direction the air
/// system fails.</item>
/// <item><b>Closed doors count as walls</b> (a deliberate divergence from the air model, where mechanical
/// doors leak): a shut wooden gate keeps animals out; an open one is a gap.</item>
/// <item>Fliers spawn above the walls and stay ungated; cave dwellers below them too; hostile machines are
/// out of scope on purpose — they SHOULD threaten a base, that is what the sentry post is for.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Cells the outside-in fill may visit per level — the whole 97×97 footprint and a little.</summary>
    private const int WalledFillBudget = 12000;

    /// <summary>Levels cached per base before the stalest are dropped (a hilly base is queried at a few dozen feet levels).</summary>
    private const int WalledLevelsPerBase = 24;

    /// <summary>One base's reachable-from-outside set at one feet level (everything else in the box is enclosed).</summary>
    private sealed class WalledLevel
    {
        public string Body = string.Empty;
        public HashSet<Vector3i> Reachable = new();
        public double ComputedAt = double.NegativeInfinity;
    }

    private readonly Dictionary<(int BaseId, int FeetY), WalledLevel> _baseWalls = new();

    /// <summary>True if the cell lies inside a founded base's reach box on this world and the outside-in fill
    /// at that feet level cannot reach it — i.e. walls (or shut doors) close it off from the open terrain.
    /// Ownership is ignored, like every base predicate: a visitor's yard is a yard too.</summary>
    private bool InWalledBaseArea(Vector3i cell)
    {
        string body = _world.LocationId;
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet != body
                || WrapAbs(canonical.X - b.Cell.X) > SealedRoomMaxReach
                || System.Math.Abs(canonical.Y - b.Cell.Y) > SealedRoomMaxReach
                || System.Math.Abs(canonical.Z - b.Cell.Z) > SealedRoomMaxReach)
            {
                continue;
            }

            if (!RefreshBaseWalls(b, canonical.Y).Reachable.Contains(canonical))
            {
                return true;
            }
        }

        return false;
    }

    private WalledLevel RefreshBaseWalls(ServerBase b, int feetY)
    {
        string body = _world.LocationId;
        var key = (b.Id, feetY);
        if (!_baseWalls.TryGetValue(key, out var level))
        {
            // Keep the cache bounded: a base queried at many terrain levels drops its stalest ones.
            if (_baseWalls.Count(kv => kv.Key.BaseId == b.Id) >= WalledLevelsPerBase)
            {
                var stalest = _baseWalls.Where(kv => kv.Key.BaseId == b.Id).OrderBy(kv => kv.Value.ComputedAt).First().Key;
                _baseWalls.Remove(stalest);
            }

            _baseWalls[key] = level = new WalledLevel();
        }

        if (level.Body != body || _uptime - level.ComputedAt >= SealedRoomRecomputeInterval)
        {
            level.Body = body;
            level.ComputedAt = _uptime;
            level.Reachable = ComputeReachableFromOutside(b, feetY);
        }

        return level;
    }

    /// <summary>The 4-neighbour flood from the reach box's boundary at <paramref name="feetY"/> through
    /// passable cells. A cell is passable when a walking animal could stand in it: not a colliding block
    /// (fluids and walk-through props pass, exactly as the creature body gate reads them) and not covered by
    /// a shut door.</summary>
    private HashSet<Vector3i> ComputeReachableFromOutside(ServerBase b, int feetY)
    {
        int circ = _world.Circumference;
        var shutDoors = DoorCellsWhere(d => !d.Open);
        var reachable = new HashSet<Vector3i>();
        var frontier = new Queue<Vector3i>();
        int r = SealedRoomMaxReach;

        void Seed(int x, int z)
        {
            var c = WorldConstants.CanonicalBlock(new Vector3i(x, feetY, z), circ);
            if (reachable.Add(c))
            {
                frontier.Enqueue(c);
            }
        }

        for (int d = -r; d <= r; d++)
        {
            Seed(b.Cell.X + d, b.Cell.Z - r);
            Seed(b.Cell.X + d, b.Cell.Z + r);
            Seed(b.Cell.X - r, b.Cell.Z + d);
            Seed(b.Cell.X + r, b.Cell.Z + d);
        }

        int budget = WalledFillBudget;
        while (frontier.Count > 0 && budget-- > 0)
        {
            var cell = frontier.Dequeue();
            if (!Passable(cell))
            {
                continue; // a boundary seed inside a wall block stays in the set (harmless: solid cells never host a spawn)
            }

            foreach (var n in new[]
            {
                new Vector3i(cell.X + 1, feetY, cell.Z), new Vector3i(cell.X - 1, feetY, cell.Z),
                new Vector3i(cell.X, feetY, cell.Z + 1), new Vector3i(cell.X, feetY, cell.Z - 1),
            })
            {
                var c = WorldConstants.CanonicalBlock(n, circ);
                if (WrapAbs(c.X - b.Cell.X) > r || System.Math.Abs(c.Z - b.Cell.Z) > r || reachable.Contains(c))
                {
                    continue;
                }

                if (Passable(c))
                {
                    reachable.Add(c);
                    frontier.Enqueue(c);
                }
            }
        }

        return reachable;

        bool Passable(Vector3i c)
            => !shutDoors.Contains(c) && !IsCollidingBlock(_world.GetBlockIfLoaded(c), fluidsPass: true, foliagePasses: false);
    }

    /// <summary>Cells covered by the doors that satisfy <paramref name="pick"/> in the active world: the door's
    /// width along its wall axis × the 3-tall doorway column, canonical. The wall fill takes every SHUT door
    /// (a gate keeps animals out); the air fill keeps its own energy-door map (the curtain seals open or shut).</summary>
    private HashSet<Vector3i> DoorCellsWhere(System.Func<ServerDoor, bool> pick)
    {
        var cells = new HashSet<Vector3i>();
        int circ = _world.Circumference;
        foreach (var d in _doors)
        {
            if (!pick(d))
            {
                continue;
            }

            int by = (int)System.Math.Floor(d.Pos.Y);
            int low = (int)System.Math.Round((d.AxisX ? d.Pos.X : d.Pos.Z) - 0.5f - (d.Width - 1f) * 0.5f);
            int fixedAxis = d.AxisX ? (int)System.Math.Floor(d.Pos.Z) : (int)System.Math.Floor(d.Pos.X);
            for (int w = 0; w < (int)d.Width; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    cells.Add(WorldConstants.CanonicalBlock(d.AxisX
                        ? new Vector3i(low + w, by + dy, fixedAxis)
                        : new Vector3i(fixedAxis, by + dy, low + w), circ));
                }
            }
        }

        return cells;
    }

    /// <summary>Drops a removed base's cached wall levels (called when its core is mined).</summary>
    private void ForgetBaseWalls(int baseId)
    {
        foreach (var key in _baseWalls.Keys.Where(k => k.BaseId == baseId).ToList())
        {
            _baseWalls.Remove(key);
        }
    }

    /// <summary>Test seam: whether a cell reads as fenced in by a base's walls right now (cache refreshed).</summary>
    public bool InWalledBaseAreaForTest(int x, int y, int z) => InWalledBaseArea(new Vector3i(x, y, z));
}
