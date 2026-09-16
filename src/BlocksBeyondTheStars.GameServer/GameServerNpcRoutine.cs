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
/// The daily routine (#1867, Marcel 2026-09-13): people work by day, sit down in the evening and sleep in a bed at
/// night — base residents, villagers and station crew alike.
/// <list type="bullet">
/// <item><b>By the local sun.</b> The phase follows <see cref="LocalDayFraction"/>, so an NPC sleeps when the sky
/// above it is dark. A station (a void world) has no longitude: the whole crew shares the station clock, and the
/// client dims the deck lights at station night.</item>
/// <item><b>Day</b> (<see cref="RoutineEveningStart"/> > t ≥ <see cref="RoutineNightEnd"/>): at the work spot — a
/// resident's post, workbench, garden or patrol (#1868), a villager's or crew member's own marker.
/// <b>Evening</b>: to a chair or bench nearby and sit down (none → stand at the resting spot). <b>Night</b>: to
/// their bed and lie down (none → rest standing at the resting spot).</item>
/// <item><b>The guard has the night shift</b> (Marcel 2026-09-13): patrols from the evening through the night and
/// sleeps from morning to late afternoon.</item>
/// <item>Beds and seats: a base resident's come from the base index; a villager or a crew member looks around its
/// marker once (<see cref="FurnitureSearchReach"/>), taking the nearest bed and seat nobody else claimed —
/// furnished settlement houses (#1828) have a bed in every room.</item>
/// <item>Checked every <see cref="RoutineCheckInterval"/> seconds per NPC inside a player's area of interest; the
/// walking itself is the pathfinding's (#1866). Guardians and visiting traders keep to their posts.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Local day fraction from which people sleep (to <see cref="RoutineNightEnd"/>).</summary>
    private const double RoutineNightStart = 0.78;

    /// <summary>Local day fraction until which people sleep.</summary>
    private const double RoutineNightEnd = 0.22;

    /// <summary>Local day fraction from which people stop working and sit down.</summary>
    private const double RoutineEveningStart = 0.68;

    /// <summary>Seconds between two looks at one NPC's routine.</summary>
    private const double RoutineCheckInterval = 2.0;

    /// <summary>How far a base resident strolls around its spot (the old 1.6 looked like standing on a stamp).</summary>
    private const float ResidentLeash = 3.0f;

    /// <summary>How far someone at a post or a workbench strays from it.</summary>
    private const float WorkLeash = 1.0f;

    /// <summary>How far someone resting (no bed) strays from its spot.</summary>
    private const float RestLeash = 0.6f;

    /// <summary>Horizontal reach around a villager's or crew member's marker in which it finds its bed and seat.</summary>
    private const int FurnitureSearchReach = 8;

    /// <summary>Villagers / crew members that may look for their bed and seat in one tick (each look reads ~1700 cells).</summary>
    private const int FurnitureScansPerTick = 6;

    private readonly List<PlayerSession> _routineTargets = new();

    /// <summary>The routine pass (Guard-registered, before the path search and the movement).</summary>
    private void TickNpcRoutine(double dt)
    {
        if (_npcs.Count == 0)
        {
            return;
        }

        _routineTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if ((!_shipPlaced || !s.State.AboardShip) && !InSpace(s.State.PlayerId))
            {
                _routineTargets.Add(s);
            }
        }

        if (_routineTargets.Count == 0)
        {
            return; // nobody here — the world stands still
        }

        float aoi = MaxStreamRadiusBlocks(_routineTargets) + 2 * WorldConstants.ChunkSize;
        float aoiSq = aoi * aoi;
        int furnitureScans = FurnitureScansPerTick; // a city full of people arriving at dusk looks around over several ticks
        foreach (var npc in _npcs)
        {
            if (!npc.RoutineEnabled)
            {
                continue;
            }

            bool inReach = false;
            foreach (var t in _routineTargets)
            {
                if (WrapDistSq(t.State.Position, npc.Pos) <= aoiSq)
                {
                    inReach = true;
                    break;
                }
            }

            if (!inReach)
            {
                continue;
            }

            bool forced = double.IsNegativeInfinity(npc.PhaseCheckedAt);
            if (forced || _uptime >= npc.PhaseCheckedAt + RoutineCheckInterval)
            {
                var phase = RoutinePhaseFor(npc);
                bool needsFurniture = phase != NpcPhase.Day && npc.BaseId == 0 && !npc.FurnitureScanned;
                if (!needsFurniture || furnitureScans > 0) // else: look around next tick
                {
                    if (needsFurniture)
                    {
                        furnitureScans--;
                        EnsureNpcFurniture(npc);
                    }

                    npc.PhaseCheckedAt = _uptime;
                    if (forced || phase != npc.Phase)
                    {
                        ApplyRoutinePhase(npc, phase);
                    }
                }
            }


            TickNpcJob(npc, dt, _routineTargets); // #1868
        }
    }

    /// <summary>The phase an NPC should be in right now (see the class summary; the guard works the night).</summary>
    private NpcPhase RoutinePhaseFor(ServerNpc npc)
    {
        double t = LocalDayFraction(npc.Pos);
        bool night = t >= RoutineNightStart || t < RoutineNightEnd;
        bool evening = !night && t >= RoutineEveningStart;
        if (npc.Job == "guard")
        {
            return night || evening ? NpcPhase.Day : NpcPhase.Night; // "Day" = on duty, "Night" = asleep
        }

        return night ? NpcPhase.Night : evening ? NpcPhase.Evening : NpcPhase.Day;
    }

    /// <summary>Starts a phase: stands up if needed and walks the NPC to where the phase wants it.</summary>
    private void ApplyRoutinePhase(ServerNpc npc, NpcPhase phase)
    {
        npc.Phase = phase;
        StandUp(npc);
        npc.SiteUntil = 0;
        switch (phase)
        {
            case NpcPhase.Night when npc.Bed is { } bed && BedStillThere(bed):
                npc.ActivityKey = string.Empty;
                GoTo(npc, BedApproach(npc, bed), NpcArrival.LieInBed, RestLeash);
                break;
            case NpcPhase.Night:
                npc.ActivityKey = "npc.activity.resting";
                GoTo(npc, npc.Rest, NpcArrival.None, RestLeash);
                break;
            case NpcPhase.Evening when npc.Seat is { } seat && SeatStillThere(seat):
                npc.ActivityKey = string.Empty;
                GoTo(npc, SeatApproach(npc, seat), NpcArrival.SitOnSeat, RestLeash);
                break;
            case NpcPhase.Evening:
                npc.ActivityKey = "npc.activity.resting";
                GoTo(npc, npc.Rest, NpcArrival.None, npc.BaseId > 0 ? ResidentLeash : NpcWanderLeash);
                break;
            default:
                npc.ActivityKey = JobActivityKey(npc.Job);
                float leash = npc.Job is "vendor" or "quartermaster" or "craftsman" or "innkeeper" || IsStandingProfession(npc.Job) ? WorkLeash
                    : npc.BaseId > 0 ? ResidentLeash : NpcWanderLeash;
                GoTo(npc, npc.HasWork ? npc.Work : npc.Rest, NpcArrival.None, leash);
                break;
        }

        BroadcastNpcs();
    }

    /// <summary>Walks to a spot, or — already there — just settles in (lies down / sits down / idles with the leash).</summary>
    private void GoTo(ServerNpc npc, Vector3f spot, NpcArrival arrival, float leash)
    {
        if (WrapDistSq(npc.Pos, spot) <= 0.5 * 0.5 && System.Math.Abs(npc.Pos.Y - spot.Y) < 1f)
        {
            npc.Goal = spot;
            npc.Arrival = arrival;
            npc.GoalLeash = leash;
            ArriveAtGoal(npc);
            return;
        }

        SetNpcGoal(npc, spot, arrival, leash);
    }

    /// <summary>The nameplate activity of someone at work (#1868).</summary>
    private static string JobActivityKey(string job) => job switch
    {
        "vendor" => "npc.activity.trading",
        "quartermaster" => "npc.activity.working",
        "craftsman" => "npc.activity.working",
        "innkeeper" => "npc.activity.working", // #1887: the tavern's keeper behind the counter
        "gardener" => "npc.activity.tending",
        "guard" => "npc.activity.patrolling",
        _ => NpcProfessions.ByJob(job)?.ActivityKey ?? string.Empty,
    };

    /// <summary>Gets up from a chair or out of bed: back onto the spot it came from.</summary>
    private void StandUp(ServerNpc npc)
    {
        if (npc.Pose == 0)
        {
            return;
        }

        npc.Pose = 0;
        npc.Pos = npc.Home; // the approach spot beside the bed / chair (the goal it arrived at)
        npc.Loco.ModeTimer = 0f;
        if (npc.ActivityKey is "npc.activity.sleeping" or "npc.activity.resting")
        {
            npc.ActivityKey = string.Empty;
        }
    }

    /// <summary>Climbs into bed: the body lies along the bed, the head at the headboard (#1867). Facing points from the
    /// foot to the head, so the client lays the avatar out without knowing the bed's shape.</summary>
    private void LieDown(ServerNpc npc, Vector3i bed)
    {
        if (!BedStillThere(bed))
        {
            npc.Bed = null;
            return;
        }

        int desc = _world.GetShape(bed);
        if (FurnitureShapes.TryBedPartnerOffset(desc, out int dx, out int dz))
        {
            npc.Pos = new Vector3f(bed.X + 0.5f + dx * 0.5f, bed.Y, bed.Z + 0.5f + dz * 0.5f);
            npc.Facing = (float)System.Math.Atan2(-dx, -dz);
        }
        else
        {
            npc.Pos = new Vector3f(bed.X + 0.5f, bed.Y, bed.Z + 0.5f); // a legacy one-cell bed
        }

        npc.Pose = 2;
        npc.ActivityKey = "npc.activity.sleeping";
        npc.Path = null;
    }

    /// <summary>Sits down on a chair or bench, facing away from its backrest (#1867).</summary>
    private void SitDown(ServerNpc npc, Vector3i seat)
    {
        if (!SeatStillThere(seat))
        {
            npc.Seat = null;
            return;
        }

        var (bx, bz) = ShapeCode.YawDirection(ShapeCode.OrientationOf(_world.GetShape(seat)));
        npc.Pos = new Vector3f(seat.X + 0.5f, seat.Y, seat.Z + 0.5f);
        npc.Facing = (float)System.Math.Atan2(-bx, -bz);
        npc.Pose = 1;
        npc.ActivityKey = "npc.activity.resting";
        npc.Path = null;
    }

    private bool BedStillThere(Vector3i bed)
    {
        var id = _world.GetBlockIfLoaded(bed);
        return !id.IsAir && _content.BlockById(id)?.Key == BedBlock;
    }

    private bool SeatStillThere(Vector3i seat)
    {
        var id = _world.GetBlockIfLoaded(seat);
        return !id.IsAir && FurnitureShapes.IsSeat(ShapeCode.ShapeOf(_world.GetShape(seat)));
    }

    /// <summary>Where the NPC stands before climbing into its bed: its resting spot when that is beside the bed, else
    /// any free cell beside the head or foot.</summary>
    private Vector3f BedApproach(ServerNpc npc, Vector3i bed)
    {
        if (WrapDistSq(npc.Rest, new Vector3f(bed.X + 0.5f, bed.Y, bed.Z + 0.5f)) <= 2.3 * 2.3)
        {
            return npc.Rest;
        }

        return BedSideSpot(bed, new HashSet<Vector3i>()) ?? npc.Rest;
    }

    /// <summary>Where the NPC stands before sitting down: a free cell in front of the seat first, then one beside it
    /// (#1895: a chair pulled up to its table has the table in front — the furnisher turns every backrest away from
    /// it), then any cell around it.</summary>
    private Vector3f SeatApproach(ServerNpc npc, Vector3i seat)
    {
        var (bx, bz) = ShapeCode.YawDirection(ShapeCode.OrientationOf(_world.GetShape(seat)));
        foreach (var (dx, dz) in new[] { (-bx, -bz), (bz, -bx), (-bz, bx) })
        {
            if (StandableSpot(seat.X + dx, seat.Y, seat.Z + dz) is { } spot)
            {
                return spot;
            }
        }

        return SpotBeside(seat, new HashSet<Vector3i>()) ?? npc.Rest;
    }

    /// <summary>A villager or a crew member finds its bed and its seat once (#1867): the nearest ones around its marker
    /// nobody else has claimed. Base residents get theirs from the base index instead.</summary>
    private void EnsureNpcFurniture(ServerNpc npc, int reach = FurnitureSearchReach)
    {
        if (npc.BaseId > 0 || npc.FurnitureScanned)
        {
            return;
        }

        npc.FurnitureScanned = true;
        var takenBeds = new HashSet<Vector3i>(_npcs.Where(n => n.Bed.HasValue).Select(n => n.Bed!.Value));
        var takenSeats = new HashSet<Vector3i>(_npcs.Where(n => n.Seat.HasValue).Select(n => n.Seat!.Value));
        var crewStation = ActivePlayerStation();
        var home = npc.Rest.ToBlock();
        ushort bedId = _content.GetBlock(BedBlock)?.NumericId.Value ?? 0;
        double bestBed = double.MaxValue, bestSeat = double.MaxValue;
        Vector3i? bed = null, seat = null;
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
                for (int dy = -2; dy <= 3; dy++)
                {
                    var c = new Vector3i(home.X + dx, home.Y + dy, home.Z + dz);
                    var id = _world.GetBlockIfLoaded(c);
                    if (id.IsAir)
                    {
                        continue;
                    }

                    int form = ShapeCode.ShapeOf(_world.GetShape(c));
                    bool isBed = bedId != 0 && id.Value == bedId && form != (int)BlockShape.BedFoot;
                    bool isSeat = !isBed && FurnitureShapes.IsSeat(form);
                    if (!isBed && !isSeat)
                    {
                        continue;
                    }

                    // On a player station the crew only uses what stands in the air (#1775).
                    if (crewStation != null && !InSealedStationPocket(crewStation, new Vector3i(c.X, c.Y + 1, c.Z)))
                    {
                        continue;
                    }

                    double d = dx * dx + dz * dz + dy * dy * 4;
                    if (isBed && d < bestBed && !takenBeds.Contains(c))
                    {
                        bestBed = d;
                        bed = c;
                    }
                    else if (isSeat && d < bestSeat && !takenSeats.Contains(c))
                    {
                        bestSeat = d;
                        seat = c;
                    }
                }

        npc.Bed = bed;
        npc.Seat = seat;
    }

    /// <summary>Test seam (#1867): an NPC's pose (0 stand, 1 sit, 2 lie), activity key and position.</summary>
    public (byte Pose, string Activity, Vector3f Pos, string Held) NpcRoutineForTest(int id)
        => _npcs.FirstOrDefault(n => n.Id == id) is { } n ? (n.Pose, n.ActivityKey, n.Pos, n.Held) : ((byte)0, string.Empty, default, string.Empty);
}
