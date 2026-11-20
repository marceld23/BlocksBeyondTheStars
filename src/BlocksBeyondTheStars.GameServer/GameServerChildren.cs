// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the children (docs/developer/LIVELY_PLACES.md): a family house of a lively place has a third bed
/// with a <see cref="SettlementPlanBaker.ChildMarker"/> beside it, and whoever lives there is a child — two per hamlet or
/// village, three per town or city at most, in addition to the place's residents.
/// <list type="bullet">
/// <item><b>A child plays.</b> By day it makes short runs with short stops: to another child (and cheers when it gets
/// there — tag), to a grown-up of the place, to a spot on the plaza. In the rain it goes home; in the evening it is home
/// before everybody else and in bed earlier.</item>
/// <item><b>A child is not a counterpart for the game's systems.</b> Role <see cref="ChildRole"/>, no person key: no wish,
/// no trade, no mission, no present, no friendship or standing, no rail trip, no visit to a base, no authored story face,
/// no night watch, no startle — and never an AI line. It answers a player with one of its own written lines and waves.</item>
/// <item><b>It costs the old worlds nothing.</b> Only a bed with the marker makes a child, and the marker exists only in
/// the modules of a lively place. Children are spawned after everybody else from a generator of their own, so no
/// grown-up's name, face or job moves.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The role of a child villager (also the suffix of nothing: a child has no person key).</summary>
    internal const string ChildRole = "child";

    /// <summary>The client's look key of a child: its own proportions (a bigger head, shorter limbs).</summary>
    internal const string ChildLook = "child";

    private const string ChildNameKey = "npc.role.child";
    private const string PlayingActivityKey = "npc.activity.playing";

    /// <summary>How many answers a child has for a player who speaks to it, and how many hellos in passing.</summary>
    internal const int ChildTalkLines = 10;
    internal const int ChildHelloLines = 4;

    /// <summary>A child's height as a share of a grown-up's (plus up to <see cref="ChildSizeSpread"/>).</summary>
    private const float ChildSize = 0.6f;
    private const float ChildSizeSpread = 0.08f;

    /// <summary>Seconds a child stands still between two runs.</summary>
    private const double ChildPauseMin = 3.0;
    private const double ChildPauseMax = 9.0;

    /// <summary>How far from home a child plays, and how far a playmate or a grown-up may be to run to.</summary>
    private const float ChildReach = 32f;
    private const float ChildPlaymateReach = 40f;

    /// <summary>How close a child must get to a playmate to have caught it.</summary>
    private const float ChildTagRange = 2.4f;

    /// <summary>Seconds before a child waves at a passing player again, and how close the player must be.</summary>
    private const double ChildWaveCooldown = 45.0;
    private const float ChildWaveRange = 6f;

    private readonly Random _childRng = new(0xC411D);

    /// <summary>The most children a settlement of a tier has.</summary>
    internal static int ChildCap(string tier) => tier is "hamlet" or "village" ? 2 : 3;

    /// <summary>
    /// Takes the children's beds out of <paramref name="beds"/> and returns them: for every child marker of the settlement
    /// the bed head nearest to it on its floor (within a cell and a half) — so no grown-up is housed there and the count of
    /// residents is what it was without them.
    /// </summary>
    private List<Vector3i> TakeChildBeds(SettlementInstance settlement, List<Vector3i> beds)
    {
        var taken = new List<Vector3i>();
        foreach (var (type, pos) in settlement.Markers)
        {
            if (type != SettlementPlanBaker.ChildMarker)
            {
                continue;
            }

            var mark = pos.ToBlock();
            int best = -1;
            double bestSq = 1.5 * 1.5 * 2;
            for (int i = 0; i < beds.Count; i++)
            {
                if (beds[i].Y != mark.Y)
                {
                    continue;
                }

                double dx = WorldConstants.WrapDeltaX(beds[i].X - mark.X, _world.Circumference), dz = beds[i].Z - mark.Z;
                double sq = (dx * dx) + (dz * dz);
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            if (best >= 0)
            {
                taken.Add(beds[best]);
                beds.RemoveAt(best);
            }
        }

        return taken;
    }

    /// <summary>Spawns the children of a settlement into their beds (after every grown-up, from a generator of their own).</summary>
    private void SpawnChildren(SettlementInstance settlement, List<Vector3i> childBeds, List<Vector3f> spots)
    {
        int cap = Math.Min(childBeds.Count, ChildCap(settlement.Tier));
        string theme = SettlementTrade(settlement);
        for (int i = 0; i < cap; i++)
        {
            var bed = childBeds[i];
            var rng = new Random(unchecked((int)WorldGenerator.StableHash($"child:{SettlementPlaceKey(settlement)}:{bed.X}:{bed.Y}:{bed.Z}")));
            var anchor = new Vector3f(bed.X + 0.5f, bed.Y + 0.5f, bed.Z + 0.5f);
            var home = anchor;
            double homeSq = double.MaxValue;
            foreach (var spot in spots)
            {
                double sq = WrapDistSq(spot, anchor);
                if (sq < homeSq)
                {
                    homeSq = sq;
                    home = spot;
                }
            }

            var standing = new Vector3f(home.X, (float)Math.Floor(Math.Max(settlement.Min.Y + 1f, home.Y)), home.Z);
            var child = MakeNpc(ChildRole, theme, robotic: false, standing, rng);
            child.IsChild = true;
            child.Look = ChildLook;
            child.Size = ChildSize + ((float)rng.NextDouble() * ChildSizeSpread);
            child.NameKey = ChildNameKey;
            child.Settlement = settlement.Name;
            child.Work = standing;
            child.HasWork = true;
            SetNpcBed(child, bed);
            MarkFurnitureLooked(child);
            child.RoutineEnabled = true;
            _npcs.Add(child);
        }
    }

    /// <summary>A child's day (from <see cref="TickSettlementDay"/>): runs and stops; a wave for a player who walks by.</summary>
    private void TickChildPlay(ServerNpc npc)
    {
        if (npc.Phase != NpcPhase.Day || npc.Pose != 0)
        {
            return; // the evening, the night and a festival are the routine's
        }

        WaveAtAPasserBy(npc);
        if (npc.Goal is not null || (npc.TalkWith is not null && _uptime < npc.TalkUntil) || _uptime < npc.AttendUntil)
        {
            return; // on a run, chatting, or looking at the player it just greeted
        }

        if (npc.Playing)
        {
            ArriveAtPlay(npc);
            return;
        }

        if (_uptime < npc.PlayNextAt)
        {
            return;
        }

        if (npc.PlayNextAt <= 0)
        {
            npc.PlayNextAt = _uptime + 1.0 + (StableUnit("child:first", npc.Id) * 20.0); // not all at once when a player walks in
            return;
        }

        StartPlayRun(npc);
    }

    private void StartPlayRun(ServerNpc npc)
    {
        npc.PlayNextAt = _uptime + 2.0 + (_childRng.NextDouble() * 4.0); // when this run does not start: a little later
        if (SettlementOfNpc(npc) is not { Ruined: false } settlement || NpcInPlayerDialog(npc))
        {
            return;
        }

        if (ErrandWeatherIsBad(npc.Pos))
        {
            ShelterAtHome(npc);
            npc.PlayNextAt = _uptime + 15.0;
            return;
        }

        if (_worlds.Active.NpcPathQueue.Count > ErrandQueueSlack || ChildPlaySpot(npc, settlement) is not { } spot)
        {
            return; // the routes are busy, or there is nowhere to run to from here
        }

        if (npc.ActivityKey != PlayingActivityKey)
        {
            npc.ActivityKey = PlayingActivityKey;
            BroadcastNpcs();
        }

        npc.Playing = true;
        SetNpcGoal(npc, spot, NpcArrival.None, ErrandLeash);
    }

    /// <summary>The end of a run: whoever stands right there has been caught (a cheer, and the caught one runs next).</summary>
    private void ArriveAtPlay(ServerNpc npc)
    {
        npc.Playing = false;
        npc.PlayNextAt = _uptime + ChildPauseMin + (_childRng.NextDouble() * (ChildPauseMax - ChildPauseMin));
        foreach (var other in _npcs)
        {
            if (other.IsChild && !ReferenceEquals(other, npc) && other.Pose == 0 && other.Goal is null
                && WrapDistSq(other.Pos, npc.Pos) <= ChildTagRange * ChildTagRange)
            {
                BroadcastNpcEmote(npc, NpcGestures.Cheer);
                other.PlayNextAt = Math.Min(other.PlayNextAt, _uptime + 0.8);
                return;
            }
        }
    }

    /// <summary>Where a child runs next: mostly to another child, sometimes to a grown-up of its place, else to a spot on
    /// the plaza, at a bench, the well or the fire — never far from home.</summary>
    private Vector3f? ChildPlaySpot(ServerNpc npc, SettlementInstance settlement)
    {
        double roll = _childRng.NextDouble();
        if (roll < 0.65 && NearestOfThePlace(npc, child: roll < 0.45) is { } friend
            && AnchorSpotNear(friend.Pos, 1, 0.8f, npc.Pos) is { } beside)
        {
            return beside;
        }

        var home = npc.HasWork ? npc.Work : npc.Rest;
        var anchors = PlaceAnchorsOf(settlement);
        int start = anchors.Count == 0 ? 0 : _childRng.Next(anchors.Count);
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors[(start + i) % anchors.Count];
            bool playground = a.Kind is PlaceAnchorKind.Plaza or PlaceAnchorKind.Bench or PlaceAnchorKind.Well or PlaceAnchorKind.Fire
                or PlaceAnchorKind.Stall;
            if (playground && WrapDistSq(a.Spot, home) <= ChildReach * ChildReach && Math.Abs(a.Spot.Y - home.Y) <= ErrandRise
                && WrapDistSq(a.Spot, npc.Pos) >= 2.0 * 2.0 && !ErrandSpotTaken(npc, a.Spot))
            {
                return a.Spot;
            }
        }

        return null;
    }

    /// <summary>The nearest child (or grown-up) of the same place that stands awake within a child's reach, or null.</summary>
    private ServerNpc? NearestOfThePlace(ServerNpc npc, bool child)
    {
        ServerNpc? best = null;
        double bestSq = ChildPlaymateReach * ChildPlaymateReach;
        foreach (var other in _npcs)
        {
            if (ReferenceEquals(other, npc) || other.IsChild != child || other.Pose != 0 || other.VisitingAway || other.Role == "guardian"
                || !string.Equals(other.Settlement, npc.Settlement, StringComparison.Ordinal) || Math.Abs(other.Pos.Y - npc.Pos.Y) > ErrandRise)
            {
                continue;
            }

            double sq = WrapDistSq(other.Pos, npc.Pos);
            if (sq < bestSq && sq > ChildTagRange * ChildTagRange)
            {
                bestSq = sq;
                best = other;
            }
        }

        return best;
    }

    /// <summary>A child waves at a player who walks by — every time, with a breath in between; no line, no voice.</summary>
    private void WaveAtAPasserBy(ServerNpc npc)
    {
        if (_uptime < npc.ChildWaveAt)
        {
            return;
        }

        npc.ChildWaveAt = _uptime + 1.0; // look again in a second
        foreach (var s in JoinedInActiveWorld())
        {
            if (OnFootHere(s) && WrapDistSq(s.State.Position, npc.Pos) <= ChildWaveRange * ChildWaveRange)
            {
                npc.ChildWaveAt = _uptime + ChildWaveCooldown;
                BroadcastNpcEmote(npc, NpcGestures.Wave);
                return;
            }
        }
    }

    /// <summary>A player spoke to a child: one of its own written answers, in turn — no dialogue, no wish, no memory.</summary>
    private void SayChildLine(PlayerSession session, ServerNpc npc)
    {
        int line = (npc.ChildLine++ % ChildTalkLines) + 1;
        Send(session, new NpcGreeting
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Role = npc.Role,
            Text = Localize(session.Locale, "npc.child.talk." + line.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            GreetingId = NextGreetingId(),
            Gesture = NpcGestures.Wave,
        });
        npc.AttendUntil = _uptime + PassingAttendSeconds;
        npc.AttendPos = session.State.Position;
    }

    /// <summary>A child's hello to a player who walks by for the first time today: a short line and a wave. No standing, no
    /// "seen" stamp, no AI re-wording.</summary>
    private void SendChildHello(PlayerSession session, ServerNpc npc)
    {
        int line = (int)(StableUnit("child:hello:" + MarketDay.ToString(System.Globalization.CultureInfo.InvariantCulture), npc.Id) * ChildHelloLines) + 1;
        Send(session, new NpcGreeting
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Role = npc.Role,
            Text = Localize(session.Locale, "npc.child.hello." + line.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            GreetingId = NextGreetingId(),
            Kind = NpcGreetingKinds.Passing,
            Gesture = NpcGestures.Wave,
        });
        npc.ChildWaveAt = _uptime + ChildWaveCooldown;
    }

    /// <summary>Test seam: the children of the active world (id, settlement, name, size, bed).</summary>
    internal IReadOnlyList<(int Id, string Settlement, string Name, float Size, Vector3i? Bed)> ChildrenForTest
    {
        get
        {
            var list = new List<(int, string, string, float, Vector3i?)>();
            foreach (var n in _npcs)
            {
                if (n.IsChild)
                {
                    list.Add((n.Id, n.Settlement, n.Name, n.Size, n.Bed));
                }
            }

            return list;
        }
    }

    /// <summary>Test seam: makes a villager spawned with <c>SpawnVillagerForTest</c> a child (as a bed with the marker does).</summary>
    internal void MakeChildForTest(int npcId)
    {
        foreach (var n in _npcs)
        {
            if (n.Id == npcId)
            {
                n.IsChild = true;
                n.Role = ChildRole;
                n.Look = ChildLook;
                n.Size = ChildSize;
                n.NameKey = ChildNameKey;
                n.PersonKey = string.Empty;
                n.Job = string.Empty;
                n.PlayNextAt = 0;
            }
        }
    }

    /// <summary>Test seam: whether a child is on a run right now.</summary>
    internal bool ChildPlayingForTest(int npcId)
    {
        foreach (var n in _npcs)
        {
            if (n.Id == npcId)
            {
                return n.Playing;
            }
        }

        return false;
    }
}
