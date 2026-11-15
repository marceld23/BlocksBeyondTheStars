// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3658: a train brakes for an animal on its track, honks, and lets it step aside — it never runs through one. Before
/// this, nothing on the server knew a wagon takes up room: a train drove straight through a herd on the line.
/// <list type="bullet">
/// <item><b>The look-ahead</b> (<see cref="CreatureGapAhead"/>): ahead of the train's front (the leading wagon's nose the
/// way it runs) the line is sampled block by block for its braking distance plus a margin, and every animal near the
/// front is tested against the wagon box at each sample (the box of <c>GameServerRailWildlife</c>, half a block wider on
/// each side): an ordinary animal by its footprint and collision height, a colossus by its four legs (the train runs under
/// its belly), the burrowing and flying giants not at all. Nothing is sampled while no animal stands near the front.</item>
/// <item><b>The hold</b> (<see cref="TrainRoomBeforeCreature"/>, <see cref="HoldShortOfCreature"/>): the nearest body ahead
/// is a target like a stop — the train brakes on <see cref="RailMotion.ObstacleSpeed"/> to stand
/// <see cref="TrainCreatureClearance"/> short of it, by hand as on autopilot, and waits there (sent as halted, without a
/// countdown, so the drawing stands still). Once the track is clear it sets off from rest.</item>
/// <item><b>The horn</b>: within <see cref="TrainHornReach"/> of where it would hold it honks — at most every
/// <see cref="TrainHornSeconds"/> — by counting up <see cref="ServerTrain.Horn"/>, which the clients hear as the cab's
/// horn. Each honk asks the animal to step aside (<see cref="AskToStepAside"/>): it wakes and hurries off the track
/// sideways for a few seconds, on the side it stands on.</item>
/// <item><b>The patience</b>: an animal that still holds the train after <see cref="TrainMoveAsideSeconds"/> (it is held in
/// stasis, boxed in, a lurking arachnid, a pet) is set down beside the track (<see cref="MoveOffTrack"/>), or — with no
/// free ground there — leaves unseen like a town intruder. It is never hurt. A giant is waited for.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far short of an animal's body a train holds.</summary>
    internal const float TrainCreatureClearance = 3f;

    /// <summary>Looked ahead past the braking distance (and the clearance), and the most it ever looks.</summary>
    private const float TrainLookMargin = 8f;

    private const float TrainLookMax = 120f;

    /// <summary>The swept box's widening across the line, on each side.</summary>
    private const float TrainSideMargin = 0.5f;

    /// <summary>Within this much room the train counts as standing at its hold spot.</summary>
    private const float TrainHoldSlack = 0.05f;

    /// <summary>The horn: at most one honk this often, from this far before the hold spot on.</summary>
    internal const double TrainHornSeconds = 3.0;

    private const float TrainHornReach = 12f;

    /// <summary>How long an animal the horn asked keeps stepping off the track.</summary>
    private const double TrainAsideSeconds = 3.5;

    /// <summary>How long one animal may hold a train before it is set down beside the track.</summary>
    internal const double TrainMoveAsideSeconds = 10.0;

    /// <summary>The point a stepping-aside animal runs from lies this far across the track from it.</summary>
    private const float TrainAsideFromOffset = 4f;

    /// <summary>The samples of the line ahead (pose, blocks ahead of the front) and the bodies near it — reused per call.</summary>
    private readonly List<(Vector3f Pos, float Yaw, float Ahead)> _trainLook = new();

    private readonly List<(CombatEntity Creature, Vector3f Feet, float Radius, float Height)> _trainLookBodies = new();

    /// <summary>The nearest body on the track ahead: the animal, the sample's pose, its feet in that frame, and the gap from
    /// the train's front to the body's near edge.</summary>
    private readonly record struct TrackHit(CombatEntity Creature, Vector3f Pos, float Yaw, Vector3f Local, float Gap);

    internal sealed partial class ServerTrain
    {
        /// <summary>#3658: the animal that holds the train (its id, empty for none) and since when (uptime). RAM only.</summary>
        public string HeldBy = string.Empty;

        public double HeldSince;

        /// <summary>#3658: the train stands at its hold spot short of an animal (sent as halted).</summary>
        public bool HeldByCreature;

        /// <summary>#3658: how often it honked (<c>NetTrain.Horn</c>), and when it may honk next. RAM only.</summary>
        public int Horn;

        public double NextHornAt;
    }

    /// <summary>The room the train has before its hold spot short of the nearest animal on the track ahead (+∞ for none) —
    /// and the horn, the step-aside and the patience that go with it (see the type summary).</summary>
    private float TrainRoomBeforeCreature(ServerTrain t, RailLine line, float span)
    {
        if (!CreatureGapAhead(t, line, span, out var hit))
        {
            ForgetCreatureHold(t);
            return float.PositiveInfinity;
        }

        float room = hit.Gap - TrainCreatureClearance;
        RespondToCreatureAhead(t, hit, room);
        return room;
    }

    /// <summary>The run of this tick, cut to the hold spot; at the spot the train stands (and is sent as halted).</summary>
    private float HoldShortOfCreature(ServerTrain t, float room, float run)
    {
        if (float.IsPositiveInfinity(room))
        {
            return run;
        }

        bool hold = room <= TrainHoldSlack;
        if (hold != t.HeldByCreature)
        {
            t.HeldByCreature = hold;
            Rails.CreatureSignal = true;
        }

        if (hold)
        {
            t.Velocity = 0f;
            return 0f;
        }

        return Math.Min(run, room);
    }

    /// <summary>A train that does not run this tick (halted, or a manual train nobody drives): no speed, and no animal holds it.</summary>
    private void StandStill(ServerTrain t)
    {
        t.Velocity = 0f;
        ForgetCreatureHold(t);
    }

    /// <summary>The train is held by no animal (the track is clear, or it stands for another reason).</summary>
    private void ForgetCreatureHold(ServerTrain t)
    {
        if (t.HeldByCreature)
        {
            t.HeldByCreature = false;
            Rails.CreatureSignal = true;
        }

        t.HeldBy = string.Empty;
        t.HeldSince = 0;
    }

    /// <summary>The horn (and the step-aside it asks for) near the hold spot, and the patience while the train stands there.</summary>
    private void RespondToCreatureAhead(ServerTrain t, TrackHit hit, float room)
    {
        if (room <= TrainHornReach && _uptime >= t.NextHornAt)
        {
            t.Horn++;
            t.NextHornAt = _uptime + TrainHornSeconds;
            Rails.CreatureSignal = true;
            AskToStepAside(hit);
        }

        if (room > TrainHoldSlack)
        {
            t.HeldBy = string.Empty; // still rolling up to the spot
            return;
        }

        if (!string.Equals(t.HeldBy, hit.Creature.Id, StringComparison.Ordinal))
        {
            t.HeldBy = hit.Creature.Id;
            t.HeldSince = _uptime;
            return;
        }

        if (_uptime - t.HeldSince >= TrainMoveAsideSeconds)
        {
            // Cleared: the patience starts over for whatever comes next. Not yet (someone watches an animal with no free
            // ground beside it): try again in a second, not every tick.
            t.HeldSince = MoveOffTrack(hit) ? _uptime : _uptime - TrainMoveAsideSeconds + 1.0;
        }
    }

    /// <summary>The horn asks an animal to step aside: it wakes and flees from a point across the track for a few seconds,
    /// so it leaves the track on the side it stands on. Giants are not asked.</summary>
    private void AskToStepAside(TrackHit hit)
    {
        var c = hit.Creature;
        if (c.IsGiant)
        {
            return;
        }

        int side = TrackSide(c, hit.Local);
        var from = RailRules.LocalToWorld(hit.Pos, hit.Yaw, new Vector3f(-side * TrainAsideFromOffset, hit.Local.Y, hit.Local.Z));
        int circ = _world.Circumference;
        c.TrackAsideFrom = new Vector3f((float)WorldConstants.WrapX((double)from.X, circ), from.Y, (float)WorldConstants.WrapZ((double)from.Z, circ));
        c.TrackAsideUntil = _uptime + TrainAsideSeconds;
        c.AwakeOverrideTimer = Math.Max(c.AwakeOverrideTimer, CreatureWakeSeconds); // the horn wakes a sleeper on the track
    }

    /// <summary>The patience ran out: the animal is set down beside the track, or — with no free ground there — leaves unseen
    /// (a wild one nobody is taming). Never hurt. A giant is waited for. Returns whether the track was cleared.</summary>
    private bool MoveOffTrack(TrackHit hit)
    {
        var c = hit.Creature;
        if (c.IsGiant || !_speciesById.TryGetValue(c.SpeciesId, out var sp))
        {
            return false;
        }

        var (radius, height) = RailBodyOf(c, sp);
        if (TrySpotBesideTrack(c, sp, hit.Pos, hit.Yaw, hit.Local, radius, height, 0.5f + radius, out var spot))
        {
            c.Position = spot;
            c.TrackAsideUntil = 0;
            BroadcastCreatures();
            return true;
        }

        if (c.IsCompanion || IsTamingTarget(c.Id) || SeenByAnyPlayer(c.Position, TrainWatchers()))
        {
            return false;
        }

        _creatures.Remove(c);
        BroadcastCreatures();
        return true;
    }

    /// <summary>Everyone on the active world who could be looking (the intruder sweep's watchers, refilled here).</summary>
    private List<PlayerSession> TrainWatchers()
    {
        _intruderWatchers.Clear();
        _intruderWatchers.AddRange(JoinedInActiveWorld().Where(s => !InSpace(s.State.PlayerId)));
        return _intruderWatchers;
    }

    /// <summary>The nearest body on the track ahead of the train (see the type summary), false when there is none.</summary>
    private bool CreatureGapAhead(ServerTrain t, RailLine line, float span, out TrackHit hit)
    {
        hit = default;
        if (_creatures.Count == 0)
        {
            return false;
        }

        int dir = t.Direction < 0 ? -1 : 1;
        float front = RailRules.LeadArc(t.Arc, dir, t.Facing, span) + (dir * WagonHalfLength);
        float look = Math.Min(TrainLookMax,
            (t.Velocity * t.Velocity / (2f * RailMotion.Acceleration)) + TrainCreatureClearance + TrainLookMargin);
        var frontPos = line.Spline.PoseAt(line.Spline.NormalizeArc(front), t.Facing).Pos;
        if (!GatherTrackBodies(frontPos, look))
        {
            return false;
        }

        SampleLineAhead(t, line, front, dir, look);
        float travel = dir * (t.Facing < 0 ? -1 : 1); // +1: the sample frames' +Z (the facing) is the way it runs
        float best = float.PositiveInfinity;
        foreach (var body in _trainLookBodies)
        {
            if (NearestOnTrack(body, travel, ref best, out var found))
            {
                hit = found;
            }
        }

        return !float.IsPositiveInfinity(best);
    }

    /// <summary>The bodies within the look-ahead's reach of the train's front (<see cref="_trainLookBodies"/>); false for none.</summary>
    private bool GatherTrackBodies(Vector3f frontPos, float look)
    {
        _trainLookBodies.Clear();
        foreach (var c in _creatures)
        {
            if (!_speciesById.TryGetValue(c.SpeciesId, out var sp))
            {
                continue;
            }

            if (c.Giant is { } g)
            {
                if (g.Kind == CreatureBodyPlan.Colossus)
                {
                    AddColossusLegs(c, g, sp, frontPos, look);
                }

                continue; // a sandworm or a leviathan under the ground, a sky giant over it
            }

            if (c.IsCompanion && FindSessionByPlayerId(c.OwnerId) is { } owner && RidesTrain(owner))
            {
                continue; // a pet keeping up with its owner on the train
            }

            var (radius, height) = RailBodyOf(c, sp);
            AddTrackBody(c, c.Position, radius, height, frontPos, look);
        }

        return _trainLookBodies.Count > 0;
    }

    /// <summary>A colossus is in the way with its legs only — the wagons pass under its belly.</summary>
    private void AddColossusLegs(CombatEntity c, GiantRuntime g, CreatureSpecies sp, Vector3f frontPos, float look)
    {
        var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
        for (int leg = 0; leg < 4; leg++)
        {
            var (hx, _, hz) = body.Hip(leg);
            var foot = ColossusBody.ToWorld(c.Position, g.Facing, hx, 0f, hz);
            AddTrackBody(c, new Vector3f(foot.X, c.Position.Y, foot.Z), body.LegThickness * 0.5f, body.LegLength, frontPos, look);
        }
    }

    private void AddTrackBody(CombatEntity c, Vector3f feet, float radius, float height, Vector3f frontPos, float look)
    {
        float reach = look + radius + WagonHalfWidth + TrainSideMargin + 1f;
        if (Math.Abs(feet.Y - frontPos.Y) <= look + 16f && RailFlatDistSq(feet, frontPos) <= (double)reach * reach)
        {
            _trainLookBodies.Add((c, feet, radius, height));
        }
    }

    /// <summary>The line's poses every block from the train's front for <paramref name="look"/> blocks the way it runs (an
    /// open line's end stops the samples; a loop wraps).</summary>
    private void SampleLineAhead(ServerTrain t, RailLine line, float front, int dir, float look)
    {
        _trainLook.Clear();
        float total = line.Spline.TotalArc;
        int steps = (int)Math.Ceiling(look);
        for (int k = 0; k <= steps; k++)
        {
            float s = front + (dir * k);
            if (!line.Closed && (s < 0f || s > total))
            {
                break;
            }

            var (pos, yaw) = line.Spline.PoseAt(line.Spline.NormalizeArc(s), t.Facing);
            _trainLook.Add((pos, yaw, k));
        }
    }

    /// <summary>One body against the samples: the gap from the front to its near edge where the swept box first holds it,
    /// when that beats <paramref name="best"/>.</summary>
    private bool NearestOnTrack((CombatEntity Creature, Vector3f Feet, float Radius, float Height) body, float travel, ref float best,
        out TrackHit hit)
    {
        hit = default;
        bool found = false;
        float reach = WagonBoxReach + body.Radius + TrainSideMargin;
        double reachSq = (double)reach * reach;
        foreach (var (pos, yaw, ahead) in _trainLook)
        {
            if (ahead - 0.5f - (2f * body.Radius) > best)
            {
                break; // every later sample lies farther ahead than the best already found
            }

            if (RailFlatDistSq(body.Feet, pos) > reachSq)
            {
                continue;
            }

            var local = RailRules.WorldToLocal(pos, yaw, Unwrapped(pos, body.Feet));
            float along = local.Z * travel;
            if (Math.Abs(along) > 0.5f + body.Radius || Math.Abs(local.X) >= WagonHalfWidth + body.Radius + TrainSideMargin
                || !ReachesWagonBand(local.Y, body.Height))
            {
                continue;
            }

            float gap = Math.Max(0f, ahead + along - body.Radius);
            if (gap < best)
            {
                best = gap;
                hit = new TrackHit(body.Creature, pos, yaw, local, gap);
                found = true;
            }
        }

        return found;
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#3658): a train's horn count and whether an animal on the track holds it.</summary>
    public (int Horn, bool HeldByCreature, string HeldBy) TrainCreatureHoldForTest(string trainId)
        => Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } t ? (t.Horn, t.HeldByCreature, t.HeldBy) : (0, false, string.Empty);

    /// <summary>Test seam (#3658): the gap from a train's front to the nearest body on its track ahead (+∞ for none).</summary>
    public float TrainCreatureGapForTest(string trainId)
    {
        if (Rails.Trains.FirstOrDefault(t => t.Id == trainId) is not { } t || !Rails.Lines.TryGetValue(t.LineId, out var line))
        {
            return float.PositiveInfinity;
        }

        return CreatureGapAhead(t, line, TrainSpan(t), out var hit) ? hit.Gap : float.PositiveInfinity;
    }
}
