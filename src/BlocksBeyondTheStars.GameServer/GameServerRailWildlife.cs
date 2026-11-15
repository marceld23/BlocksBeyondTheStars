// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3658: until when (server uptime) the animal steps off a train's track — the horn asked it to; 0 = it was
    /// not asked. RAM only.</summary>
    public double TrackAsideUntil { get; set; }

    /// <summary>#3658: the point across the track the animal steps away from, so it leaves the track sideways on the side
    /// it already stands on.</summary>
    public Vector3f TrackAsideFrom { get; set; }
}

/// <summary>
/// Wild animals and the monorail's wagons (#3656, #3658). A train exists on the server only as an arc along its line and a
/// list of wagons — nothing knew that a wagon takes up room, so a hunter walked into a halted wagon and bit the seated
/// rider, and a herd stood in the aisle. Here the wagons get a body for the animals:
/// <list type="bullet">
/// <item><b>The wagon box</b> (<see cref="InAnyWagon"/>): the voxel grid every client draws (<see cref="RailWagonLayout"/>,
/// across ±1.5, the floor block's underside to the roof's top, along ±3) at each wagon's pose. An animal's body is a
/// cylinder of its footprint radius (at the spacing share, <see cref="CreatureFootprint.SeparationFactor"/>) and its
/// collision height; it reaches a wagon only when it rises more than <see cref="WagonHeadBrush"/> into the box, so a small
/// animal walks under an elevated line, where the floor floats 3.7 blocks over the ground. The poses are cached per tick
/// (<see cref="CurrentWagonBoxes"/>) and every check leaves at once on a world without trains.</item>
/// <item><b>The barrier</b> (<see cref="RailStepBlocked"/>, one line of <see cref="StepBlocked"/>): a wild animal's step
/// into a wagon is a wall, so it turns away there as at a fence. One already in a box is never trapped: it may move, and
/// <see cref="TryPushOutOfWagon"/> (next to the ship push-out in the move loop) sets it down beside the wagon on free
/// ground — the side it is nearer to first.</item>
/// <item><b>The corridor</b> (<see cref="InTrainCorridor"/>): a line that carries a train sweeps the wagon box along its
/// whole length (sampled once per line, <see cref="RailLine.Corridor"/>). A ground animal that reaches into it does not
/// roam into it (seeking and fleeing still cross — the train brakes for them), and no animal spawns in it or in a
/// wagon (<see cref="RailSpawnBlocked"/>).</item>
/// <item><b>Riders are nobody's prey</b> (<see cref="RidesTrain"/>): a player in a train stays in the creature targets — the
/// land round the line keeps living and pruning for them, and the cap still counts them — but is no reactor (no hunt, no
/// bite, no startle) and the spawner never places animals round a rider (<see cref="CreatureSpawnAnchor"/>): a moving rider
/// leaves any spawn behind within seconds, and a halted one would only gather a crowd round the wagon. Giants do not see
/// riders either.</item>
/// </list>
/// Companions and giants are left to their own rules (a pet follows its owner anywhere, a giant moves by
/// <c>TickGiants</c>); the train itself brakes for every one of them (<c>GameServerRailCreatureBrake</c>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The wagon box across (half its width) and along (half its length) — the drawn grid.</summary>
    private const float WagonHalfWidth = RailWagonLayout.Width * 0.5f;

    private const float WagonHalfLength = RailWagonLayout.Length * 0.5f;

    /// <summary>The box's half-diagonal on the ground (√(1.5² + 3²) ≈ 3.35): the cheap distance pre-check.</summary>
    private const float WagonBoxReach = 3.4f;

    /// <summary>How far a body must rise into the box from below to reach it: a head may brush the floor's underside.</summary>
    private const float WagonHeadBrush = 0.5f;

    /// <summary>How far round the box a wild animal keeps its body (the step barrier, a set-down spot).</summary>
    private const float WagonStepMargin = 0.3f;

    /// <summary>Half the corridor's width across the line (the box's 1.5, a half block more on each side).</summary>
    private const float CorridorHalfWidth = 2f;

    /// <summary>The corridor's sampling step along the line.</summary>
    private const float CorridorSampleStep = 0.5f;

    /// <summary>How far beyond the box (on top of the body's radius) a set-down spot is tried, nearest first.</summary>
    private static readonly float[] TrackSideSteps = { 0.75f, 2.5f, 4.5f };

    /// <summary>How many cells up or down a set-down spot's ground is looked for.</summary>
    private const int TrackSideScan = 6;

    /// <summary>The bottom and the top of the wagon box over the wagon's floor (the floor block's underside, the roof's top).</summary>
    private static float WagonUnderside => RailWagonLayout.GridOrigin.Y;

    private static float WagonRoofTop => RailWagonLayout.GridOrigin.Y + RailWagonLayout.Height;

    /// <summary>One wagon's pose as the wildlife rules see it this tick.</summary>
    internal readonly record struct WagonBox(ServerTrain Train, int Wagon, Vector3f Pos, float Yaw);

    internal sealed partial class RailWorldState
    {
        /// <summary>#3656: every wagon's pose (<see cref="CurrentWagonBoxes"/>), and what it was computed for.</summary>
        public List<WagonBox> WagonBoxes { get; } = new();

        public (double At, float ArcSum, int Wagons) WagonBoxesKey = (double.NaN, 0f, 0);

        /// <summary>#3658: a horn sounded or an animal held or let go a train this tick — the train list goes out at once.</summary>
        public bool CreatureSignal;

        public void ClearWagonBoxes()
        {
            WagonBoxes.Clear();
            WagonBoxesKey = (double.NaN, 0f, 0);
            CreatureSignal = false;
        }

        /// <summary>Reads and resets <see cref="CreatureSignal"/>.</summary>
        public bool TakeCreatureSignal()
        {
            bool signal = CreatureSignal;
            CreatureSignal = false;
            return signal;
        }
    }

    internal sealed partial class RailLine
    {
        /// <summary>#3658: the wagon box swept along the whole line — per column (canonical x, z) the lowest underside and the
        /// highest roof top over it. Sampled on first use; a rebuilt line is a new object, so it never goes stale.</summary>
        public Dictionary<long, (float Lo, float Hi)>? Corridor;
    }

    /// <summary>#3656: whether a player rides a train — seated or standing in a wagon (the server places them from the
    /// wagon every tick): nobody's prey, and no spawn anchor.</summary>
    private static bool RidesTrain(PlayerSession s) => s.State.InTrain.Length > 0;

    /// <summary>The body the rail rules see of an animal: its footprint radius at the spacing share and its collision height.</summary>
    private static (float Radius, float Height) RailBodyOf(CombatEntity c, CreatureSpecies sp)
        => (CreatureFootprint.Radius(sp, c.SizeScale) * CreatureFootprint.SeparationFactor, CreatureBodyHeight(sp));

    /// <summary>Whether a body whose feet stand <paramref name="feetLocalY"/> over a wagon's floor (negative: under it) and
    /// that is <paramref name="height"/> tall reaches into the wagon box vertically — more than <see cref="WagonHeadBrush"/>
    /// over the floor block's underside and not above the roof.</summary>
    internal static bool ReachesWagonBand(float feetLocalY, float height)
        => feetLocalY < WagonRoofTop && feetLocalY + height > WagonUnderside + WagonHeadBrush;

    /// <summary>The flat distance² between two points (X and Z wrap), for the cheap pre-checks.</summary>
    private double RailFlatDistSq(Vector3f a, Vector3f b)
    {
        double dx = WorldConstants.WrapDeltaX((double)a.X - b.X, _world.Circumference);
        double dz = WorldConstants.WrapDeltaZ((double)a.Z - b.Z, _world.Circumference);
        return (dx * dx) + (dz * dz);
    }

    /// <summary>Every wagon's pose on the active world, cached until the clock, a train's arc or the wagon count changes.</summary>
    private List<WagonBox> CurrentWagonBoxes()
    {
        var rails = Rails;
        if (rails.Trains.Count == 0)
        {
            rails.WagonBoxes.Clear();
            return rails.WagonBoxes;
        }

        float arcSum = 0f;
        int wagons = 0;
        foreach (var t in rails.Trains)
        {
            arcSum += t.Arc;
            wagons += t.Wagons.Count;
        }

        var key = (_uptime, arcSum, wagons);
        if (rails.WagonBoxesKey == key)
        {
            return rails.WagonBoxes;
        }

        rails.WagonBoxesKey = key;
        rails.WagonBoxes.Clear();
        foreach (var t in rails.Trains)
        {
            if (!rails.Lines.TryGetValue(t.LineId, out var line))
            {
                continue;
            }

            for (int i = 0; i < t.Wagons.Count; i++)
            {
                var (pos, yaw) = WagonPose(t, line, i);
                rails.WagonBoxes.Add(new WagonBox(t, i, pos, yaw));
            }
        }

        return rails.WagonBoxes;
    }

    /// <summary>Whether a body (feet, radius, height) overlaps one wagon's box grown by <paramref name="margin"/> across and
    /// along; <paramref name="local"/> is the feet in that wagon's frame.</summary>
    private bool BodyInWagonBox(in WagonBox box, Vector3f feet, float radius, float height, float margin, out Vector3f local)
    {
        local = RailRules.WorldToLocal(box.Pos, box.Yaw, Unwrapped(box.Pos, feet));
        return Math.Abs(local.X) < WagonHalfWidth + radius + margin
               && Math.Abs(local.Z) < WagonHalfLength + radius + margin
               && ReachesWagonBand(local.Y, height);
    }

    /// <summary>#3656: whether a body overlaps any wagon's box (see the type summary) — the first one it does, and the feet
    /// in that wagon's frame. False at once on a world without trains.</summary>
    private bool InAnyWagon(Vector3f feet, float radius, float height, float margin, out WagonBox hit, out Vector3f local)
    {
        hit = default;
        local = default;
        var boxes = CurrentWagonBoxes();
        if (boxes.Count == 0)
        {
            return false;
        }

        float reach = WagonBoxReach + radius + margin;
        double reachSq = (double)reach * reach;
        foreach (var box in boxes)
        {
            float dy = feet.Y - box.Pos.Y;
            if (dy > WagonRoofTop || dy + height < WagonUnderside || RailFlatDistSq(feet, box.Pos) > reachSq)
            {
                continue;
            }

            if (BodyInWagonBox(box, feet, radius, height, margin, out local))
            {
                hit = box;
                return true;
            }
        }

        local = default;
        return false;
    }

    /// <summary>The movement half (one line of <see cref="StepBlocked"/>): a wild animal's step into a wagon — or, for a
    /// roaming ground animal that reaches it, into a running line's corridor — is a wall. A step from inside is free.</summary>
    private bool RailStepBlocked(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f cand, int? nextFeet)
    {
        var rails = Rails;
        if (rails.Trains.Count == 0 || c.IsCompanion || c.IsGiant)
        {
            return false;
        }

        var (radius, height) = RailBodyOf(c, sp);
        var to = nextFeet is { } feet ? new Vector3f(cand.X, feet, cand.Z) : cand;
        if (InAnyWagon(to, radius, height, WagonStepMargin, out _, out _) && !InAnyWagon(cur, radius, height, WagonStepMargin, out _, out _))
        {
            return true;
        }

        return CorridorStepBlocked(c, sp, motion, cur, to, height);
    }

    /// <summary>#3658: a roaming ground animal tall enough to reach the wagons does not wander onto a running line (seeking
    /// and fleeing still may; the train brakes for them). One already in the corridor may go anywhere.</summary>
    private bool CorridorStepBlocked(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f to, float height)
    {
        if (motion is not (MotionClass.Walker or MotionClass.Crawler) || c.Loco.Mode is MoveMode.Seek or MoveMode.Flee
            || sp.Habitat is not (CreatureHabitat.Land or CreatureHabitat.Amphibian or CreatureHabitat.Cave))
        {
            return false;
        }

        return InTrainCorridor(to, height) && !InTrainCorridor(cur, height);
    }

    /// <summary>#3658: whether a body standing at <paramref name="feet"/> reaches into the corridor of a line a train runs on.</summary>
    private bool InTrainCorridor(Vector3f feet, float height)
    {
        var rails = Rails;
        if (rails.Trains.Count == 0)
        {
            return false;
        }

        int circ = _world.Circumference;
        long key = CorridorKey(WorldConstants.WrapX((int)Math.Floor(feet.X), circ), WorldConstants.WrapZ((int)Math.Floor(feet.Z), circ));
        foreach (var t in rails.Trains)
        {
            if (!rails.Lines.TryGetValue(t.LineId, out var line))
            {
                continue;
            }

            line.Corridor ??= BuildCorridor(line);
            if (line.Corridor.TryGetValue(key, out var band) && feet.Y < band.Hi && feet.Y + height > band.Lo + WagonHeadBrush)
            {
                return true;
            }
        }

        return false;
    }

    private static long CorridorKey(int x, int z) => ((long)x << 32) | (uint)z;

    /// <summary>The corridor of a line (see <see cref="RailLine.Corridor"/>): the wagon box's columns every
    /// <see cref="CorridorSampleStep"/> along the spline, <see cref="CorridorHalfWidth"/> to each side.</summary>
    private Dictionary<long, (float Lo, float Hi)> BuildCorridor(RailLine line)
    {
        var map = new Dictionary<long, (float Lo, float Hi)>();
        int circ = _world.Circumference;
        float total = line.Spline.TotalArc;
        int samples = (int)Math.Ceiling(total / CorridorSampleStep);
        for (int i = 0; i <= samples; i++)
        {
            var (pos, yaw) = line.Spline.PoseAt(Math.Min(total, i * CorridorSampleStep));
            float rx = (float)Math.Sin(yaw), rz = -(float)Math.Cos(yaw); // right of the heading
            float lo = pos.Y + WagonUnderside, hi = pos.Y + WagonRoofTop;
            for (float off = -CorridorHalfWidth; off <= CorridorHalfWidth; off += 1f)
            {
                int x = WorldConstants.WrapX((int)Math.Floor(pos.X + (rx * off)), circ);
                int z = WorldConstants.WrapZ((int)Math.Floor(pos.Z + (rz * off)), circ);
                long key = CorridorKey(x, z);
                map[key] = map.TryGetValue(key, out var band) ? (Math.Min(band.Lo, lo), Math.Max(band.Hi, hi)) : (lo, hi);
            }
        }

        return map;
    }

    /// <summary>#3656: the push-out (next to the ship push-out in the move loop): a wild animal whose body is inside a wagon
    /// is set down beside it on free ground. False when it is in none — or when no spot is free yet (the next tick tries
    /// again; the barrier keeps it from going deeper meanwhile).</summary>
    private bool TryPushOutOfWagon(CombatEntity c, CreatureSpecies sp, out Vector3f outside)
    {
        outside = c.Position;
        if (c.IsCompanion || Rails.Trains.Count == 0)
        {
            return false;
        }

        var (radius, height) = RailBodyOf(c, sp);
        return InAnyWagon(c.Position, radius, height, 0f, out var box, out var local)
               && TrySpotBesideTrack(c, sp, box.Pos, box.Yaw, local, radius, height, WagonHalfLength, out outside);
    }

    /// <summary>A free spot beside the track for an animal whose feet are at <paramref name="local"/> in the frame of the pose
    /// (<paramref name="pos"/>, <paramref name="yaw"/>): across the line just past the box (its own side first, then the
    /// other), level with where it is (its along-position held within <paramref name="halfAlong"/>), on real ground with room
    /// for the body — never in a ship, never in another wagon.</summary>
    private bool TrySpotBesideTrack(CombatEntity c, CreatureSpecies sp, Vector3f pos, float yaw, Vector3f local, float radius, float height,
        float halfAlong, out Vector3f spot)
    {
        int circ = _world.Circumference;
        int side = TrackSide(c, local);
        float along = Math.Clamp(local.Z, -halfAlong, halfAlong);
        int refY = (int)Math.Floor(c.Position.Y);
        int headroom = CreatureHeadroom(sp);
        float shipMargin = CreatureShipMargin(sp);
        for (int pass = 0; pass < 2; pass++)
        {
            int s = pass == 0 ? side : -side;
            foreach (float extra in TrackSideSteps)
            {
                var w = RailRules.LocalToWorld(pos, yaw, new Vector3f(s * (WagonHalfWidth + radius + extra), 0f, along));
                float wx = (float)WorldConstants.WrapX((double)w.X, circ), wz = (float)WorldConstants.WrapZ((double)w.Z, circ);
                if (!TryGroundFeetYAt((int)Math.Floor(wx), (int)Math.Floor(wz), refY, headroom, TrackSideScan, out int feet, CanopyCarries(sp)))
                {
                    continue;
                }

                var cand = new Vector3f(wx, feet, wz);
                if (CreatureBodyBlocked(sp, cand) || EntityBlockedByShip(cand, shipMargin) || InAnyWagon(cand, radius, height, WagonStepMargin, out _, out _))
                {
                    continue;
                }

                spot = cand;
                return true;
            }
        }

        spot = c.Position;
        return false;
    }

    /// <summary>Which side of the track an animal stands on in a wagon's (or a sample's) frame: +1 its right, −1 its left;
    /// one standing on the middle line takes a side by its id.</summary>
    private static int TrackSide(CombatEntity c, Vector3f local)
    {
        if (Math.Abs(local.X) > 0.05f)
        {
            return local.X > 0f ? 1 : -1;
        }

        return (StableStringHash(c.Id) & 1) == 0 ? 1 : -1;
    }

    /// <summary>#3658: the spawn half (one line of <see cref="SpawnSpotClear"/>): no animal spawns in a wagon, and none that
    /// reaches the wagons in a running line's corridor.</summary>
    private bool RailSpawnBlocked(CreatureSpecies sp, Vector3f pos)
    {
        if (Rails.Trains.Count == 0)
        {
            return false;
        }

        float radius = CreatureFootprint.Radius(sp, 1f) * CreatureFootprint.SeparationFactor;
        int height = CreatureBodyHeight(sp);
        return InAnyWagon(pos, radius, height, WagonStepMargin, out _, out _) || InTrainCorridor(pos, height);
    }

    /// <summary>#3656: the player the next spawn goes round — the round robin on the wild count, skipping every rider (see
    /// the type summary); null when everyone who could anchor one rides a train.</summary>
    private static PlayerSession? CreatureSpawnAnchor(List<PlayerSession> targets, int wild)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var s = targets[(wild + i) % targets.Count];
            if (!RidesTrain(s))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>#3658: the step-aside the horn asked for (<see cref="CombatEntity.TrackAsideUntil"/>): the animal hurries off
    /// the track — a flee from the point across it — until the time runs out. Called from the move loop after the
    /// temperament's own intent, so it wins over a roam, a hunt or a beg while it lasts.</summary>
    private void ApplyTrackAsideIntent(CombatEntity c, ref MoveMode intent, ref Vector3f? target)
    {
        if (c.TrackAsideUntil <= 0)
        {
            return;
        }

        if (_uptime >= c.TrackAsideUntil)
        {
            c.TrackAsideUntil = 0;
            return;
        }

        intent = MoveMode.Flee;
        target = Unwrapped(c.Position, c.TrackAsideFrom);
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#3656): whether a body (feet, radius, height) overlaps a wagon of the active world.</summary>
    public bool InAnyWagonForTest(Vector3f feet, float radius, float height) => InAnyWagon(feet, radius, height, 0f, out _, out _);

    /// <summary>Test seam (#3658): whether a body at <paramref name="feet"/> reaches into a running line's corridor.</summary>
    public bool InTrainCorridorForTest(Vector3f feet, float height) => InTrainCorridor(feet, height);

    /// <summary>Test seam (#3656): the player ids the animals react to this tick (the reactors) and those that keep the
    /// population going (the targets).</summary>
    public (IReadOnlyList<string> Reactors, IReadOnlyList<string> Targets) CreaturePlayersForTest()
    {
        FillCreatureTargets();
        return (_creatureReactors.ConvertAll(s => s.State.PlayerId), _creatureTargets.ConvertAll(s => s.State.PlayerId));
    }

    /// <summary>Test seam (#3656): the player the next spawn would go round, for a wild count (null: nobody may anchor one).</summary>
    public string? CreatureSpawnAnchorForTest(int wild)
    {
        FillCreatureTargets();
        return CreatureSpawnAnchor(_creatureTargets, wild)?.State.PlayerId;
    }
}
