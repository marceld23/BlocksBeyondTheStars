// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC pathfinding (#1866). Until now an NPC knew one point (its home) and a straight line to it: a goal behind a
/// wall was unreachable, and a closed door was a wall. A walking NPC now gets a route from <see cref="NpcGridPath"/>:
/// <list type="bullet">
/// <item><b>Budgeted:</b> a goal queues a search; <see cref="TickNpcPaths"/> runs at most one per tick, on
/// <see cref="ServerWorld.GetBlockIfLoaded"/> reads (a search never generates a chunk).</item>
/// <item><b>Doors:</b> slide and energy doors open for an NPC walking a route (<see cref="TickDoors"/>); a hinge or
/// wooden door in its way is swung open by the NPC and closes behind it — a door a player opened is left alone.</item>
/// <item><b>Stuck + fallback:</b> no progress for <see cref="NpcStuckSeconds"/> re-routes; after
/// <see cref="NpcPathMaxFailures"/> failed routes the NPC is set down at its goal — but only while no player is
/// within <see cref="NpcTeleportUnseenRange"/> blocks (Marcel 2026-09-13: pathfinding first, teleport as the
/// emergency exit). A watched NPC waits and tries again.</item>
/// <item>Routes are dropped when a block changes next to them (<see cref="MarkNpcPathsDirty"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Failed routes before the unobserved teleport fallback.</summary>
    private const int NpcPathMaxFailures = 3;

    /// <summary>Seconds without progress before a walking NPC re-routes.</summary>
    private const double NpcStuckSeconds = 4.0;

    /// <summary>Seconds before a failed search is tried again.</summary>
    private const double NpcPathRetrySeconds = 6.0;

    /// <summary>No player within this many blocks → the fallback may set an NPC down at its goal.</summary>
    private const float NpcTeleportUnseenRange = 24f;

    /// <summary>A waypoint counts as passed within this horizontal distance.</summary>
    private const float NpcWaypointRadius = 0.45f;

    /// <summary>The goal counts as reached within this horizontal distance.</summary>
    private const float NpcGoalRadius = 0.4f;

    /// <summary>Seconds a hinge/wooden door an NPC opened stays open before it swings shut behind them.</summary>
    private const double NpcDoorHoldSeconds = 2.5;

    /// <summary>#1874: a kit station spans up to 128 blocks and two decks, and a crew member's cabin may sit far
    /// from its post — the search reaches twice as far (and two decks) there than on a planet's surface.</summary>
    private static readonly NpcGridPath.Limits StationPathLimits = new(96, 16, 8000);

    /// <summary>Search statistics for the tests.</summary>
    public int NpcPathSearchesForTest { get; private set; }

    /// <summary>Sends an NPC walking to <paramref name="goal"/>; <paramref name="arrival"/> says what it does there.</summary>
    private void SetNpcGoal(ServerNpc npc, Vector3f goal, NpcArrival arrival, float leashThere)
    {
        if (npc.Goal is { } current && current.Equals(goal) && npc.Arrival == arrival)
        {
            npc.GoalLeash = leashThere;
            return; // already on the way
        }

        npc.Goal = goal;
        npc.Arrival = arrival;
        npc.GoalLeash = leashThere;
        npc.Path = null;
        npc.PathIndex = 0;
        npc.PathFailures = 0;
        npc.PathRetryAt = 0;
        npc.LastProgressPos = npc.Pos;
        npc.LastProgressAt = _uptime;
    }

    /// <summary>Queues a route search for an NPC (once).</summary>
    private void RequestNpcPath(ServerNpc npc)
    {
        if (npc.PathQueued)
        {
            return;
        }

        npc.PathQueued = true;
        _worlds.Active.NpcPathQueue.Enqueue(npc.Id);
    }

    /// <summary>Runs at most one queued route search (Guard-registered, before <see cref="TickNpcs"/>).</summary>
    private void TickNpcPaths(double dt)
    {
        var queue = _worlds.Active.NpcPathQueue;
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            var npc = _npcs.FirstOrDefault(n => n.Id == id);
            if (npc is null)
            {
                continue;
            }

            npc.PathQueued = false;
            if (npc.Goal is not { } goal)
            {
                continue;
            }

            SearchNpcPath(npc, goal);
            return; // one search per tick
        }
    }

    /// <summary>Finds and stores a route for an NPC to its goal (or counts a failure).</summary>
    private void SearchNpcPath(ServerNpc npc, Vector3f goal)
    {
        NpcPathSearchesForTest++;
        int circ = _world.Circumference;
        var start = npc.Pos.ToBlock();
        var g = goal.ToBlock();
        var goalCell = new Vector3i(start.X + WorldConstants.WrapDeltaX(g.X - start.X, circ), g.Y, g.Z);
        var doorCells = DoorCellsWhere(_ => true);
        var crewStation = ActivePlayerStation();

        bool Door(Vector3i c) => doorCells.Contains(WorldConstants.CanonicalBlock(c, circ));

        bool Standable(Vector3i c)
        {
            if (!NpcStandableAt(c.X, c.Y, c.Z)) // #1895: never on a table, a crate or a fence; through a rug
            {
                return false;
            }

            if (_shipPlaced && EntityBlockedByShip(new Vector3f(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f)))
            {
                return false; // never through a parked ship
            }

            // #1775: on a player station the crew walks only where there is air — its rooms and their doors.
            return crewStation == null || Door(c) || InSealedStationPocket(crewStation, c);
        }

        bool Free(Vector3i c) => !NpcBodyBlocked(_world.GetBlockIfLoaded(c), c);

        var limits = _world.Planet?.Void == true ? StationPathLimits : NpcGridPath.Limits.Default;
        var cells = NpcGridPath.Find(start, goalCell, Standable, Free, Door, limits, out _);
        if (cells is null)
        {
            npc.Path = null;
            npc.PathFailures++;
            npc.PathRetryAt = _uptime + NpcPathRetrySeconds;
            return;
        }

        npc.Path = NpcGridPath.Simplify(cells, start).Select(c => new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f)).ToList();
        npc.PathIndex = 0;
        npc.LastProgressPos = npc.Pos;
        npc.LastProgressAt = _uptime;
    }

    /// <summary>
    /// One movement step for an NPC with a goal (#1866): follows its route, opens doors in the way, detects being
    /// stuck and falls back to the unobserved teleport. Returns the locomotion intent and target for this tick, or
    /// null when the NPC stands still this tick (waiting for a route, or it just arrived).
    /// </summary>
    private (MoveMode Intent, Vector3f Target)? GoalStep(ServerNpc npc, Vector3f goal, List<PlayerSession> targets)
    {
        int circ = _world.Circumference;
        float gdx = (float)WorldConstants.WrapDeltaX((double)goal.X - npc.Pos.X, circ), gdz = goal.Z - npc.Pos.Z;
        float gdy = goal.Y - npc.Pos.Y;
        if (gdx * gdx + gdz * gdz <= NpcGoalRadius * NpcGoalRadius && System.Math.Abs(gdy) <= 1.5f)
        {
            ArriveAtGoal(npc);
            return null;
        }

        if (npc.Path != null)
        {
            // Stuck: a wall appeared, a crowd stands in the doorway, a door will not open — re-route.
            if (WrapDistSq(npc.Pos, npc.LastProgressPos) > 0.5 * 0.5)
            {
                npc.LastProgressPos = npc.Pos;
                npc.LastProgressAt = _uptime;
            }
            else if (_uptime - npc.LastProgressAt > NpcStuckSeconds)
            {
                npc.Path = null;
                npc.PathFailures++;
                npc.PathRetryAt = _uptime + 1.0;
                npc.LastProgressAt = _uptime;
                return null;
            }

            while (npc.PathIndex < npc.Path.Count)
            {
                var wp = npc.Path[npc.PathIndex];
                float wdx = (float)WorldConstants.WrapDeltaX((double)wp.X - npc.Pos.X, circ), wdz = wp.Z - npc.Pos.Z;
                if (wdx * wdx + wdz * wdz > NpcWaypointRadius * NpcWaypointRadius || System.Math.Abs(wp.Y - npc.Pos.Y) > 1.2f)
                {
                    return (MoveMode.Seek, Unwrapped(npc.Pos, wp));
                }

                npc.PathIndex++;
            }

            return (MoveMode.Seek, Unwrapped(npc.Pos, goal)); // the last stretch beside the route's end
        }

        if (npc.PathQueued || _uptime < npc.PathRetryAt)
        {
            return null; // waiting for a route
        }

        if (npc.PathFailures >= NpcPathMaxFailures)
        {
            TeleportNpcToGoalIfUnseen(npc, goal, targets);
            return null;
        }

        // A short hop in the open needs no search.
        if (gdx * gdx + gdz * gdz <= 1.5f * 1.5f && System.Math.Abs(gdy) < 0.5f && !PathBlockedByWorld(npc.Pos, goal))
        {
            npc.Path = new List<Vector3f>();
            npc.PathIndex = 0;
            npc.LastProgressPos = npc.Pos;
            npc.LastProgressAt = _uptime;
            return (MoveMode.Seek, Unwrapped(npc.Pos, goal));
        }

        RequestNpcPath(npc);
        return null;
    }

    /// <summary>The emergency exit (#1866): no route after several tries — put the NPC at its goal, but never in front
    /// of anyone. A watched NPC tries again later.</summary>
    private void TeleportNpcToGoalIfUnseen(ServerNpc npc, Vector3f goal, List<PlayerSession> targets)
    {
        foreach (var t in targets)
        {
            if (WrapDistSq(t.State.Position, npc.Pos) <= NpcTeleportUnseenRange * NpcTeleportUnseenRange
                || WrapDistSq(t.State.Position, goal) <= NpcTeleportUnseenRange * NpcTeleportUnseenRange)
            {
                npc.PathFailures = NpcPathMaxFailures - 1; // one more honest try before the next check
                npc.PathRetryAt = _uptime + NpcPathRetrySeconds * 2;
                return;
            }
        }

        npc.Pos = goal;
        npc.Loco.ModeTimer = 0f;
        ArriveAtGoal(npc);
        BroadcastNpcs();
    }

    /// <summary>The NPC reached its goal: it idles there (or climbs into bed / sits down).</summary>
    private void ArriveAtGoal(ServerNpc npc)
    {
        if (npc.Goal is { } goal)
        {
            npc.Home = goal;
        }

        npc.Goal = null;
        npc.Path = null;
        npc.PathIndex = 0;
        npc.PathFailures = 0;
        npc.Leash = npc.GoalLeash;
        switch (npc.Arrival)
        {
            case NpcArrival.LieInBed when npc.Bed is { } bed:
                LieDown(npc, bed);
                break;
            case NpcArrival.SitOnSeat when npc.Seat is { } seat:
                SitDown(npc, seat);
                break;
        }

        npc.Arrival = NpcArrival.None;
        BroadcastNpcs();
    }

    /// <summary>A closed door covering <paramref name="pos"/>, or null (the entity behind <see cref="ClosedDoorBlocks"/>).</summary>
    private ServerDoor? ClosedDoorAt(Vector3f pos)
    {
        int y = (int)System.Math.Floor(pos.Y);
        int circ = _world.Circumference;
        foreach (var d in _doors)
        {
            int floor = (int)System.Math.Floor(d.Pos.Y);
            if (d.Open || y < floor || y > floor + 2)
            {
                continue;
            }

            float cx = (float)(circ > 0 ? WorldConstants.WrapDeltaX((double)pos.X - d.Pos.X, circ) : pos.X - d.Pos.X);
            float cz = pos.Z - d.Pos.Z;
            float along = d.AxisX ? cx : cz, across = d.AxisX ? cz : cx;
            if (System.Math.Abs(across) < 0.5f && System.Math.Abs(along) < d.Width / 2f)
            {
                return d;
            }
        }

        return null;
    }

    /// <summary>The first closed door on the straight step from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private ServerDoor? ClosedDoorOnStep(Vector3f from, Vector3f to)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            if (ClosedDoorAt(new Vector3f(from.X + dx * f, to.Y, from.Z + dz * f)) is { } door)
            {
                return door;
            }
        }

        return null;
    }

    /// <summary>An NPC on its way swings a hinge or wooden door open (#1866, Marcel 2026-09-13: "yes, they open
    /// doors"); the door closes again behind them (<see cref="TickDoors"/>). Both leaves of a double door follow.</summary>
    private void OpenDoorForNpc(ServerDoor door)
    {
        if (door.Open || !DoorBlocks.IsHandOperated(door.Kind))
        {
            return;
        }

        door.Open = true;
        door.NpcHeldUntil = _uptime + NpcDoorHoldSeconds;
        MarkBaseWallsDirty(_world, door.Pos.ToBlock());
        foreach (var other in _doors)
        {
            if (!other.Open && DoorPairing.IsPartner(door, other))
            {
                other.Open = true;
                other.NpcHeldUntil = door.NpcHeldUntil;
                MarkBaseWallsDirty(_world, other.Pos.ToBlock());
            }
        }

        BroadcastDoors();
    }

    /// <summary>A block changed on a resident world: routes that pass next to it are recomputed.</summary>
    private void MarkNpcPathsDirty(LoadedWorld world, Vector3i cell)
    {
        foreach (var npc in world.Npcs)
        {
            if (npc.Path is not { Count: > 0 } path)
            {
                continue;
            }

            for (int i = npc.PathIndex; i < path.Count; i++)
            {
                var a = i == npc.PathIndex ? npc.Pos : path[i - 1];
                var b = path[i];
                int minX = (int)System.Math.Floor(System.Math.Min(a.X, b.X)) - 1, maxX = (int)System.Math.Floor(System.Math.Max(a.X, b.X)) + 1;
                int minZ = (int)System.Math.Floor(System.Math.Min(a.Z, b.Z)) - 1, maxZ = (int)System.Math.Floor(System.Math.Max(a.Z, b.Z)) + 1;
                int minY = (int)System.Math.Floor(System.Math.Min(a.Y, b.Y)) - 1, maxY = (int)System.Math.Floor(System.Math.Max(a.Y, b.Y)) + 2;
                if (cell.X >= minX && cell.X <= maxX && cell.Z >= minZ && cell.Z <= maxZ && cell.Y >= minY && cell.Y <= maxY)
                {
                    npc.Path = null;
                    npc.PathRetryAt = 0;
                    break;
                }
            }
        }
    }

    /// <summary>Test seam (#1866): send an NPC walking to a spot.</summary>
    public void SetNpcGoalForTest(int id, Vector3f goal)
    {
        if (_npcs.FirstOrDefault(n => n.Id == id) is { } npc)
        {
            npc.RoutineEnabled = false; // the test owns this NPC's goals now
            SetNpcGoal(npc, goal, NpcArrival.None, NpcWanderLeash);
        }
    }

    /// <summary>Test seam (#1866): whether the NPC still walks toward a goal.</summary>
    public bool NpcHasGoalForTest(int id) => _npcs.FirstOrDefault(n => n.Id == id)?.Goal is not null;
}
