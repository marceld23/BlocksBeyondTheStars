// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Derelict stations, phase 2 (#2636, Screelit: "You can fall through holes into space and then slowly suffocate"):
/// falling out of a generated derelict through a breach is a real spacewalk.
/// <list type="bullet">
/// <item><b>Stepping out</b> — a boarder <see cref="DerelictSpacewalkMargin"/> blocks clear of the hull's box (caught by
/// the 1 Hz void check, <see cref="RescueDriftingBoarder"/>, run after the world loop) leaves the derelict like an undock
/// (<see cref="LeaveStation(string, SpacePlayerPose?)"/>) and opens the flight on an EVA: the suit floats at the flight
/// position matching where they left the hull, their ship waits beside the derelict (at the float spot remembered when
/// they came over on a spacewalk if that lies clear of the hull, else off the hull's face toward the launch point). The
/// suit tank drains as on every EVA, and
/// VEGA says so once. No flight to open (free flight off, a ship that can't fly): the suit's pull-back as before.</item>
/// <item><b>Coming back</b> — E on the spacewalk boards through the nearer of a door (#2607) and an opening the suit
/// floats at (a breach, the open hangar, a torn panel — <see cref="DerelictBreachNearSuit"/>), onto the deck behind it;
/// with neither in reach at the arrival on the command deck. Or fly back to the ship.</item>
/// <item><b>The mapping</b> — a build cell floats at <c>position + cell − box centre</c> in flight (the flight view
/// centres the design on its cell box; the derelict is never rotated), and sits at <c>cell + stamp origin − stamp min</c>
/// in the interior world (<see cref="StationCellToWorld"/>). Both directions go through the build cell.</item>
/// </list>
/// The Long Quiet keeps the pull-back rescue: a tiny story site with blanket air, never a place to fall out of.
/// See docs/developer/DERELICT_STATIONS.md §5.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Blocks beyond a generated derelict's hull box at which a boarder who left the hull steps out into the
    /// spacewalk. Measured from the hull's own cell box — not the gravity margin — so the switch follows a fall at once,
    /// while nothing aboard (a deck, the roof, a jump) ever reaches it.</summary>
    private const int DerelictSpacewalkMargin = 2;

    /// <summary>How far out from the hull box the suit may start: a fall the 1 Hz check caught late still starts just
    /// outside the hull where it left, not far below it.</summary>
    private const float DerelictSpacewalkStartReach = 6f;

    /// <summary>The suit's flight position is its eye (the EVA camera); a boarder's position is their feet.</summary>
    private const float SuitEyeHeight = 1.5f;

    /// <summary>Where the ship of a boarder who docked by ship waits: this far out from the hull box's face toward the
    /// launch point (a derelict floats behind it, at negative Z).</summary>
    private const float DerelictShipStandOff = 20f;

    /// <summary>How far outside the hull box a remembered ship float spot must lie to be kept for the spacewalk.</summary>
    private const float DerelictShipClearance = 12f;

    /// <summary>How close (blocks, to the hull box) the suit has to float for E to board through an opening.</summary>
    private const float DerelictBreachBoardReach = 3f;

    /// <summary>Face-to-face steps through open cells from the suit to a deck behind an opening.</summary>
    private const int DerelictBreachSearchSteps = 6;

    /// <summary>Boarders the void check found clear of a derelict's hull this tick — stepped out after the world loop, so
    /// the world change never runs inside another world's tick.</summary>
    private readonly HashSet<string> _derelictSpacewalks = new(System.StringComparer.Ordinal);

    private void QueueDerelictSpacewalk(PlayerSession session) => _derelictSpacewalks.Add(session.State.PlayerId);

    /// <summary>Steps every queued boarder out (once per tick, after the per-world loop) — when they are still aboard that
    /// derelict and still clear of its hull (a death, a leave or a rejoin in between drops the request).</summary>
    private void TickDerelictSpacewalks()
    {
        if (_derelictSpacewalks.Count == 0)
        {
            return;
        }

        var queued = _derelictSpacewalks.OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        _derelictSpacewalks.Clear();
        foreach (var playerId in queued)
        {
            if (FindSessionByPlayerId(playerId) is { Joined: true } session
                && TryGetBoardedPlayerStation(playerId, out var station) && IsGeneratedDerelictId(station.Id)
                && BeyondStationBox(station, session.State.Position, DerelictSpacewalkMargin))
            {
                StartDerelictSpacewalk(session, station);
            }
        }
    }

    /// <summary>The boarder left the derelict's hull: the flight opens on an EVA at the matching spot next to the hull,
    /// the ship waiting beside the derelict, the suit tank draining, VEGA's warning. Falls back to the pull-back rescue
    /// when no flight can open.</summary>
    private void StartDerelictSpacewalk(PlayerSession session, BoardableStation station)
    {
        var p = session.State;
        string playerId = p.PlayerId;
        Serve(session); // the derelict's interior world (the mapping reads it) + THIS player's ship (the launch gate does)
        if (!_playerStationCells.TryGetValue(station.Id, out var build) || build.Cells.Count == 0
            || SpaceLaunchProblem(session, requireAboard: false) is not null)
        {
            ReturnDriftingDerelictBoarder(session, station);
            return;
        }

        var suit = DerelictSuitStart(station, build, p.Position);
        // A spacewalk boarding remembers where the ship floated (#2702). It still only counts when it lies clear of the hull
        // box — a ship parked against the derelict would stick in the hull it is about to be drawn next to.
        var ship = _dockedFromEva.TryGetValue(playerId, out var floated) && ClearOfHull(build, floated.Pos, DerelictShipClearance)
            ? floated
            : new SpacePlayerPose(DerelictShipWaitPoint(build), 0f, false);
        _dockedFromEva[playerId] = ship; // the undock resumes the ship there and opens the flight on an EVA
        LeaveStation(playerId, new SpacePlayerPose(suit, p.Yaw, true));
        if (!InSpace(playerId))
        {
            // The launch gate passed above, so this is a refusal it does not know: the undock left them at their ship
            // on the body below — alive, aboard, never in the void.
            _log.Warn($"Player '{p.Name}' fell out of derelict '{station.Name}' but no flight opened — left at the ship.");
            return;
        }

        SendVegaLine(session, "vega.derelict.spacewalk", 3, station.Name); // system kind: a safety line is never muted
        _log.Info($"Player '{p.Name}' fell out of derelict '{station.Name}' — spacewalk at ({suit.X:0.0}, {suit.Y:0.0}, {suit.Z:0.0}).");
    }

    /// <summary>The flight spot (the suit's eye) matching a boarder's interior feet position: through the build cell,
    /// held to within <see cref="DerelictSpacewalkStartReach"/> of the hull box.</summary>
    private Vector3f DerelictSuitStart(BoardableStation station, SpaceStructure build, Vector3f feet)
    {
        var box = CellBox(build);
        var cell = InteriorToStationCell(station, feet);
        float reach = DerelictSpacewalkStartReach;
        float x = System.Math.Clamp(cell.X, box.Min.X - reach, box.Max.X + 1 + reach);
        float y = System.Math.Clamp(cell.Y, box.Min.Y - reach, box.Max.Y + 1 + reach);
        float z = System.Math.Clamp(cell.Z, box.Min.Z - reach, box.Max.Z + 1 + reach);
        return StationCellToFlight(build, box, new Vector3f(x, y + SuitEyeHeight, z));
    }

    /// <summary>Where the ship of a boarder who docked by ship waits for the spacewalk: off the hull box's face toward the
    /// launch point, level with its centre.</summary>
    private static Vector3f DerelictShipWaitPoint(SpaceStructure build)
    {
        var (min, max) = CellBox(build);
        float halfZ = (max.Z - min.Z + 1) * 0.5f;
        return new Vector3f(build.Position.X, build.Position.Y, build.Position.Z + halfZ + DerelictShipStandOff);
    }

    /// <summary>Whether a flight position lies at least <paramref name="clearance"/> blocks outside the build's cell box.</summary>
    private static bool ClearOfHull(SpaceStructure build, Vector3f flight, float clearance)
    {
        var box = CellBox(build);
        var c = FlightToStationCell(build, box, flight);
        return c.X < box.Min.X - clearance || c.X > box.Max.X + 1 + clearance
            || c.Y < box.Min.Y - clearance || c.Y > box.Max.Y + 1 + clearance
            || c.Z < box.Min.Z - clearance || c.Z > box.Max.Z + 1 + clearance;
    }

    // ---------------- coming back through an opening ----------------

    /// <summary>
    /// The deck cell (a build cell, feet) behind an opening of a generated derelict's hull the suit floats at — a breach,
    /// the open hangar, a torn panel — or null. Searched from the suit's body cell through open cells only (air, no
    /// doorway) for at most <see cref="DerelictBreachSearchSteps"/> face-to-face steps, so E beside a closed wall finds
    /// nothing and the boarder arrives in the command room as before. The deck lies inside the hull box with headroom and
    /// a roof above it somewhere — never the top of the hull. The nearest to the suit's feet wins. With a door in reach
    /// (<paramref name="door"/>, <see cref="StationDoorNearSuit"/>) the nearer entry wins: floating inside a breached room
    /// beside an inner door, E sets the boarder down where they float, not behind that door.
    /// </summary>
    private Vector3i? DerelictBreachNearSuit(SpaceInstance instance, string playerId, SpaceStructure build, Vector3i? door)
    {
        if (build.Cells.Count == 0)
        {
            return null;
        }

        var box = CellBox(build);
        var suitAt = PilotPositionIn(instance, playerId);
        var eye = FlightToStationCell(build, box, suitAt);
        var deck = DerelictDeckBehindOpening(build, box, eye);
        if (deck is not { } d || door is not { } doorCell)
        {
            return deck;
        }

        // Both in reach: the door's opening centre (as StationDoorNearSuit measures it) against the deck's eye height.
        var doorAt = StationCellToFlight(build, box, new Vector3f(doorCell.X + 0.5f, doorCell.Y + 1.5f, doorCell.Z + 0.5f));
        var deckAt = StationCellToFlight(build, box, new Vector3f(d.X + 0.5f, d.Y + SuitEyeHeight, d.Z + 0.5f));
        return deckAt.DistanceSquared(suitAt) < doorAt.DistanceSquared(suitAt) ? deck : null;
    }

    /// <summary>The search of <see cref="DerelictBreachNearSuit"/>: from the suit's eye (in the build's cell space).</summary>
    private static Vector3i? DerelictDeckBehindOpening(SpaceStructure build, (Vector3i Min, Vector3i Max) box, Vector3f eye)
    {
        var (min, max) = box;
        float r = DerelictBreachBoardReach;
        if (eye.X < min.X - r || eye.X > max.X + 1 + r || eye.Y < min.Y - r || eye.Y > max.Y + 1 + r
            || eye.Z < min.Z - r || eye.Z > max.Z + 1 + r)
        {
            return null;
        }

        var start = new Vector3i(FloorInt(eye.X), FloorInt(eye.Y - 0.5f), FloorInt(eye.Z)); // the suit's body
        if (!OpenStationCell(build, start))
        {
            start = new Vector3i(start.X, FloorInt(eye.Y), start.Z);
            if (!OpenStationCell(build, start))
            {
                return null;
            }
        }

        var feet = new Vector3f(eye.X, eye.Y - SuitEyeHeight, eye.Z);
        int pad = (int)r + 1;
        var steps = new Dictionary<Vector3i, int> { [start] = 0 };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(start);
        Vector3i? best = null;
        float bestSq = float.MaxValue;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            int d = steps[c];
            if (InsideCellBox(c, min, max) && StationDeckCell(build, c, max.Y))
            {
                float dx = c.X + 0.5f - feet.X, dy = c.Y - feet.Y, dz = c.Z + 0.5f - feet.Z;
                float sq = dx * dx + dy * dy + dz * dz;
                if (sq < bestSq) // ties: the first found — the search order is fixed
                {
                    bestSq = sq;
                    best = c;
                }
            }

            if (d >= DerelictBreachSearchSteps)
            {
                continue;
            }

            foreach (var n in new[]
            {
                new Vector3i(c.X + 1, c.Y, c.Z), new Vector3i(c.X - 1, c.Y, c.Z),
                new Vector3i(c.X, c.Y + 1, c.Z), new Vector3i(c.X, c.Y - 1, c.Z),
                new Vector3i(c.X, c.Y, c.Z + 1), new Vector3i(c.X, c.Y, c.Z - 1),
            })
            {
                if (n.X < min.X - pad || n.X > max.X + pad || n.Y < min.Y - pad || n.Y > max.Y + pad
                    || n.Z < min.Z - pad || n.Z > max.Z + pad || steps.ContainsKey(n) || !OpenStationCell(build, n))
                {
                    continue;
                }

                steps[n] = d + 1;
                queue.Enqueue(n);
            }
        }

        return best;
    }

    /// <summary>Where a boarder through an opening arrives (after the stamp): that deck cell, else the standable cell
    /// nearest to it within two blocks (never in a doorway). Null sends them to the usual arrival.</summary>
    private Vector3f? DerelictBreachArrival(BoardableStation station, Vector3i deckCell)
    {
        var w = StationCellToWorld(station, deckCell);
        Vector3i? best = null;
        long bestD = long.MaxValue;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int x = w.X + dx, y = w.Y + dy, z = w.Z + dz;
                    if (!StandableAt(x, y, z) || InDoorway(x, y, z))
                    {
                        continue;
                    }

                    long d = (long)dx * dx + (long)dy * dy * 4 + (long)dz * dz;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = new Vector3i(x, y, z);
                    }
                }

        return best is { } b ? new Vector3f(b.X + 0.5f, b.Y, b.Z + 0.5f) : null;
    }

    // ---------------- the mapping ----------------

    /// <summary>An interior-world position → the build's cell space (a float position; the inverse of
    /// <see cref="StationCellToWorld"/>), X taken on the lap nearest the stamp origin (#1773).</summary>
    private Vector3f InteriorToStationCell(BoardableStation station, Vector3f world)
    {
        float dx = world.X - station.Origin.X;
        int circ = _world.Circumference;
        if (circ > 0)
        {
            dx -= circ * (float)System.Math.Floor((dx + circ / 2f) / circ);
        }

        return new Vector3f(dx + station.StampMin.X, world.Y - station.Origin.Y + station.StampMin.Y, world.Z - station.Origin.Z + station.StampMin.Z);
    }

    /// <summary>A position in the build's cell space → the flight frame: the build floats with its cell box centred on its
    /// position, as the flight view draws it (the same rule as <see cref="StationDoorNearSuit"/>).</summary>
    private static Vector3f StationCellToFlight(SpaceStructure build, (Vector3i Min, Vector3i Max) box, Vector3f cell)
        => new(build.Position.X + cell.X - (box.Min.X + box.Max.X + 1) * 0.5f,
            build.Position.Y + cell.Y - (box.Min.Y + box.Max.Y + 1) * 0.5f,
            build.Position.Z + cell.Z - (box.Min.Z + box.Max.Z + 1) * 0.5f);

    /// <summary>A flight-frame position → the build's cell space (the inverse of <see cref="StationCellToFlight"/>).</summary>
    private static Vector3f FlightToStationCell(SpaceStructure build, (Vector3i Min, Vector3i Max) box, Vector3f flight)
        => new(flight.X - build.Position.X + (box.Min.X + box.Max.X + 1) * 0.5f,
            flight.Y - build.Position.Y + (box.Min.Y + box.Max.Y + 1) * 0.5f,
            flight.Z - build.Position.Z + (box.Min.Z + box.Max.Z + 1) * 0.5f);

    private static int FloorInt(float v) => (int)System.Math.Floor(v);

    private static bool InsideCellBox(Vector3i c, Vector3i min, Vector3i max)
        => c.X >= min.X && c.X <= max.X && c.Y >= min.Y && c.Y <= max.Y && c.Z >= min.Z && c.Z <= max.Z;

    /// <summary>A build cell the suit can float through: air and no door's opening (a door is three cells high).</summary>
    private static bool OpenStationCell(SpaceStructure build, Vector3i c)
        => build.Get(c).IsAir
           && !build.PlacedDoorAxes.ContainsKey(c)
           && !build.PlacedDoorAxes.ContainsKey(new Vector3i(c.X, c.Y - 1, c.Z))
           && !build.PlacedDoorAxes.ContainsKey(new Vector3i(c.X, c.Y - 2, c.Z));

    /// <summary>A deck cell of the build: open with headroom, a block underfoot and a roof somewhere above it.</summary>
    private static bool StationDeckCell(SpaceStructure build, Vector3i c, int topY)
    {
        if (!OpenStationCell(build, c) || !OpenStationCell(build, new Vector3i(c.X, c.Y + 1, c.Z))
            || build.Get(new Vector3i(c.X, c.Y - 1, c.Z)).IsAir)
        {
            return false;
        }

        for (int y = c.Y + 2; y <= topY; y++)
        {
            if (!build.Get(new Vector3i(c.X, y, c.Z)).IsAir)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#2636): the flight-frame suit spot matching an interior position of a boarded derelict (the
    /// interior world must be active), unclamped and at the feet.</summary>
    public Vector3f DerelictInteriorToFlightForTest(string stationId, Vector3f interior)
    {
        if (!_stationsById.TryGetValue(stationId, out var station) || !_playerStationCells.TryGetValue(stationId, out var build))
        {
            return interior;
        }

        return StationCellToFlight(build, CellBox(build), InteriorToStationCell(station, interior));
    }

    /// <summary>Test seam (#2636): the flight-frame centre of a derelict build cell's floor (where a suit's feet would
    /// stand in it).</summary>
    public Vector3f DerelictCellToFlightForTest(string stationId, Vector3i cell)
        => _playerStationCells.TryGetValue(stationId, out var build)
            ? StationCellToFlight(build, CellBox(build), new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f))
            : default;

    /// <summary>Test seam (#2636): whether the void check queued this boarder to step out of a derelict.</summary>
    public bool DerelictSpacewalkQueuedForTest(string playerId) => _derelictSpacewalks.Contains(playerId);

    /// <summary>Test seam (#2636): where the player's ship floats in their flight instance, or null.</summary>
    public Vector3f? ShipFloatForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
           && inst.ShipPoses.TryGetValue(playerId, out var pose)
            ? pose.Pos
            : null;

    /// <summary>Test seam (#2636): the suit's own pose in its flight instance (the EVA position), or null.</summary>
    public Vector3f? SuitPoseForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
           && inst.PlayerPoses.TryGetValue(playerId, out var pose)
            ? pose.Pos
            : null;
}
