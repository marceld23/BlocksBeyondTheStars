// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The job state machine of the wilderness folk (#2641) — shared by the lodge residents and the wanderers (#2646): one
/// call per tick, <see cref="TickNpcWork"/>, with a plan saying what the NPC works at and where the yield goes. The work
/// cores (#2642–#2645) do one action when asked; the cadence and the caps are this machine's.
/// <list type="number">
/// <item><b>Pick a site</b> through the cores: a gatherer's plants (the plan's fixed plot cells and
/// <c>FindNpcGatherSites</c>), a miner's face cells (the plan's fixed cells, else <c>FindNpcOutcrops</c>), a woodcutter's
/// trees (<c>FindNpcTrees</c>), a hunter's quarry (<c>FindNpcHuntTarget</c>), nearest first; sites the plan forbids and
/// sites dropped earlier are skipped.</item>
/// <item><b>Walk</b> to a standable spot beside it (<c>SetNpcGoal</c>, A*). A site that cannot be reached — no spot,
/// two failed routes, <see cref="WalkTimeoutSeconds"/> on the way — is dropped.</item>
/// <item><b>Work</b> a few seconds facing it with the tool (<see cref="NpcWorkPlan.ActivityKey"/>, <see cref="NpcWorkPlan.Held"/>),
/// then call the core: <c>NpcHarvestFlora</c>, <c>NpcMineCell</c> (the cell fills up again after
/// <c>NpcMineRefillSeconds</c>), <c>NpcFellTree</c> (and wait while <c>NpcFellingActive</c>), or — a hunter, after
/// walking to about <see cref="HuntApproachDistance"/> and aiming <see cref="AimSeconds"/> — <c>NpcShootCreature</c>.</item>
/// <item><b>Cadence</b>: after a yield the NPC idles near the work centre — gather and mine 60–90 s, chop 360–480 s, hunt
/// 240–360 s and at most <see cref="NpcWorkPlan.DailyKillCap"/> kills per in-game day. A hunter without a target walks a
/// round of standable points of its hunting ground (within the plan's radius).</item>
/// </list>
/// A player close by as a customer (a vendor's <c>KeeperCustomer</c>) holds the NPC at its spot: no new site is picked
/// while they trade. A gap in the calls (evening, night, nobody near, a talk) resets the machine to idle.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>What a working NPC does (<see cref="NpcWorkPlan.Kind"/>).</summary>
    internal enum NpcWorkKind : byte { Gather, Mine, Chop, Hunt }

    /// <summary>Where the machine is in one work cycle.</summary>
    internal enum NpcWorkStage : byte { Idle, Walking, Working, Felling, Aiming }

    /// <summary>
    /// What an NPC works at (#2641): the kind, the work centre and radius, optional fixed sites (a lodge's quarry face or
    /// crop plot), where the yield goes and the look while working. Built once per NPC by its owner (a lodge, a
    /// wanderer) and handed to <see cref="TickNpcWork"/> each tick.
    /// </summary>
    internal sealed class NpcWorkPlan
    {
        public NpcWorkKind Kind { get; init; }

        /// <summary>The work centre: where the NPC idles between yields, the middle of its search.</summary>
        public Vector3f Center { get; init; }

        /// <summary>How far around <see cref="Center"/> sites are looked for (a hunter's hunting ground).</summary>
        public int Radius { get; init; } = 16;

        /// <summary>Cells that are always this NPC's work (a lodge's face cells, its plot crops) — looked at first.</summary>
        public IReadOnlyList<Vector3i>? FixedSites { get; init; }

        /// <summary>Where the yield goes (<c>bank(itemKey, count)</c>).</summary>
        public System.Action<string, int> Bank { get; init; } = static (_, _) => { };

        /// <summary>An extra test for found sites (null = all): a lodge keeps its woodcutter off the house's log frame.</summary>
        public System.Func<Vector3i, bool>? SiteAllowed { get; init; }

        /// <summary>The nameplate activity while working ("npc.activity.gathering" …).</summary>
        public string ActivityKey { get; init; } = string.Empty;

        /// <summary>What the NPC holds while working (a render hint; the owner usually sets it for good).</summary>
        public string Held { get; init; } = string.Empty;

        /// <summary>A hunter's kills per in-game day at most.</summary>
        public int DailyKillCap { get; init; } = 3;
    }

    /// <summary>One NPC's place in the work cycle (<see cref="ServerNpc.WorkState"/>, runtime only).</summary>
    internal sealed class NpcWorkState
    {
        /// <summary>The plan the owner built for this NPC (a lodge resident's; a wanderer passes its own each call).</summary>
        public NpcWorkPlan? Plan;

        public NpcWorkStage Stage;
        public Vector3i Site;
        public CombatEntity? Quarry;
        public Vector3f? GoalSpot;
        public double StageUntil;
        public double NextYieldAt;
        public double LastTickAt = double.NegativeInfinity;
        public bool Patrolling;
        public int PatrolIndex;
        public int PatrolLeft;
        public List<Vector3f>? Patrol;
        public readonly List<Vector3i> Skip = new();
        public double SkipResetAt;
        public long KillDay = -1;
        public int KillsToday;
        public double NextScareAt;

        /// <summary>Yields the core accepted so far (inspection, tests).</summary>
        public int Yields;

        /// <summary>Core calls so far, accepted or not (inspection, tests).</summary>
        public int CoreCalls;
    }

    /// <summary>Seconds a gatherer works at one plant before the harvest.</summary>
    private const double GatherWorkSeconds = 4.0;

    /// <summary>Seconds a miner works at one cell before it breaks.</summary>
    private const double MineWorkSeconds = 5.0;

    /// <summary>Seconds a woodcutter swings at the trunk before the tree comes down.</summary>
    private const double ChopWorkSeconds = 6.0;

    /// <summary>Seconds a hunter aims before the shot.</summary>
    internal const double AimSeconds = 1.5;

    /// <summary>A walk to a site that takes longer than this drops the site.</summary>
    private const double WalkTimeoutSeconds = 45.0;

    /// <summary>The longest a felling is waited for.</summary>
    private const double FellingWaitSeconds = 45.0;

    /// <summary>How close a hunter walks to its quarry (the shot reaches 14).</summary>
    internal const float HuntApproachDistance = 11f;

    /// <summary>From this close a hunter aims right away.</summary>
    private const float HuntShootDistance = 12.5f;

    /// <summary>How far a hunter looks for quarry from where it stands.</summary>
    private const float HuntLookRange = 24f;

    /// <summary>A walking hunter is noticed this far (once a second, by the skittish animals round it).</summary>
    private const float HuntScareRadius = 8f;

    /// <summary>How long an animal that noticed a walking hunter stays wary of it.</summary>
    private const double HuntScareSeconds = 2.5;

    /// <summary>Seconds before the next look when nothing was found (no plant, no tree, no quarry).</summary>
    private const double NothingFoundRetrySeconds = 20.0;

    /// <summary>Seconds before the next try after a site was dropped or a core said no.</summary>
    private const double FailedSiteRetrySeconds = 6.0;

    /// <summary>A gap of more than this between two calls means the NPC was elsewhere — the machine starts over.</summary>
    private const double WorkGapSeconds = 5.0;

    /// <summary>How far an idle worker strolls around the work centre.</summary>
    private const float WorkIdleLeash = 2.5f;

    /// <summary>Sites dropped as unreachable or refused are remembered up to this many (then the list starts over).</summary>
    private const int WorkSkipCap = 24;

    /// <summary>How long a dropped site stays dropped (then every site gets another chance).</summary>
    private const double SkipMemorySeconds = 300.0;

    /// <summary>The patrol points of a hunter's round.</summary>
    private const int HuntPatrolPoints = 6;

    private readonly System.Random _npcWorkRng = new(0x2641);

    /// <summary>
    /// One tick of an NPC's work (see the class summary). The caller decides WHEN it works — a lodge resident by day,
    /// standing, in a player's area; a wanderer on its way — and passes the plan; <paramref name="targets"/> are the
    /// players on the surface of this world (the customer hold).
    /// </summary>
    private void TickNpcWork(ServerNpc npc, NpcWorkPlan plan, List<PlayerSession> targets)
    {
        var ws = npc.WorkState ??= new NpcWorkState { Plan = plan };
        if (_uptime - ws.LastTickAt > WorkGapSeconds && ws.Stage != NpcWorkStage.Idle)
        {
            ResetNpcWork(npc, ws); // the evening, the night, a talk or nobody near came between — start over
        }

        ws.LastTickAt = _uptime;
        switch (ws.Stage)
        {
            case NpcWorkStage.Idle:
                TickWorkIdle(npc, ws, plan, targets);
                break;
            case NpcWorkStage.Walking:
                TickWorkWalking(npc, ws, plan);
                break;
            case NpcWorkStage.Working:
                TickWorkWorking(npc, ws, plan);
                break;
            case NpcWorkStage.Felling:
                if (!NpcFellingActive(npc) || _uptime >= ws.StageUntil)
                {
                    FinishWork(npc, ws, plan, success: true);
                }

                break;
            case NpcWorkStage.Aiming:
                TickWorkAiming(npc, ws, plan);
                break;
        }
    }

    /// <summary>Back to idle without a yield: the walk the machine set (only its own) is dropped.</summary>
    private void ResetNpcWork(ServerNpc npc, NpcWorkState ws)
    {
        if (ws.GoalSpot is { } spot && npc.Goal is { } goal && goal.Equals(spot))
        {
            npc.Goal = null;
            npc.Path = null;
        }

        ws.Stage = NpcWorkStage.Idle;
        ws.GoalSpot = null;
        ws.Quarry = null;
        ws.Patrolling = false;
        if (npc.Leash <= 0.05f)
        {
            npc.Leash = WorkIdleLeash;
        }
    }

    private void TickWorkIdle(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan, List<PlayerSession> targets)
    {
        if (_uptime < ws.NextYieldAt || npc.Pose != 0 || KeeperCustomer(npc, targets) is not null)
        {
            return; // resting between yields, or a customer is here — the keeper stands at its spot for them
        }

        if (plan.Kind == NpcWorkKind.Hunt)
        {
            StartHunt(npc, ws, plan);
            return;
        }

        if (_uptime >= ws.SkipResetAt)
        {
            ws.Skip.Clear(); // dropped sites get another chance now and then (a wall came down, a plant grew back)
            ws.SkipResetAt = _uptime + SkipMemorySeconds;
        }

        var sites = WorkSitesFor(npc, plan);
        foreach (var site in sites)
        {
            if (ws.Skip.Contains(site))
            {
                continue;
            }

            if (NpcWorkSpotNear(site) is not { } spot)
            {
                SkipSite(ws, site);
                continue;
            }

            ws.Site = site;
            ws.Quarry = null;
            StartWalk(npc, ws, plan, spot);
            return;
        }

        // Nothing (left) to do: look again a little later.
        ws.NextYieldAt = _uptime + NothingFoundRetrySeconds;
        IdleNearCenter(npc, plan);
    }

    /// <summary>The candidate sites of a plan, nearest to the NPC first.</summary>
    private List<Vector3i> WorkSitesFor(ServerNpc npc, NpcWorkPlan plan)
    {
        var sites = new List<Vector3i>();
        switch (plan.Kind)
        {
            case NpcWorkKind.Gather:
                if (plan.FixedSites is { } plot)
                {
                    foreach (var c in plot)
                    {
                        if (IsFlora(_world.GetBlockIfLoaded(c).Value))
                        {
                            sites.Add(c); // a crop standing in the plot (a harvested one is air while it regrows)
                        }
                    }
                }

                sites.AddRange(FindNpcGatherSites(plan.Center, plan.Radius, 8));
                break;
            case NpcWorkKind.Mine:
                if (plan.FixedSites is { } face)
                {
                    foreach (var c in face)
                    {
                        if (!_world.GetBlockIfLoaded(c).IsAir)
                        {
                            sites.Add(c); // a mined cell is air until it fills up again
                        }
                    }
                }
                else
                {
                    sites.AddRange(FindNpcOutcrops(plan.Center, plan.Radius, 6));
                }

                break;
            case NpcWorkKind.Chop:
                sites.AddRange(FindNpcTrees(plan.Center, plan.Radius, 6));
                break;
        }

        return sites
            .Distinct()
            .Where(c => plan.SiteAllowed is null || plan.SiteAllowed(c))
            .OrderBy(c => WrapDistSq(npc.Pos, new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f)))
            .ToList();
    }

    /// <summary>A hunter looks for quarry in its hunting ground; nothing there → the next point of its round.</summary>
    private void StartHunt(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan)
    {
        long day = MarketDay;
        if (ws.KillDay != day)
        {
            ws.KillDay = day;
            ws.KillsToday = 0;
        }

        if (ws.KillsToday >= plan.DailyKillCap)
        {
            ws.NextYieldAt = _uptime + 60.0; // enough for today
            IdleNearCenter(npc, plan);
            return;
        }

        if (FindNpcHuntTarget(npc, HuntLookRange) is { } quarry && InHuntingGround(plan, quarry.Position))
        {
            ws.Quarry = quarry;
            ws.Patrolling = false;
            if (WrapDistSq(npc.Pos, quarry.Position) <= HuntShootDistance * (double)HuntShootDistance)
            {
                StartAim(npc, ws);
                return;
            }

            if (HuntApproachSpot(npc.Pos, quarry.Position) is { } spot)
            {
                StartWalk(npc, ws, plan, spot);
                return;
            }

            ws.Quarry = null; // no ground between here and there — let it go
        }

        // No quarry in sight: walk the round of the hunting ground, a few points per search.
        if (ws.Patrol is not { Count: > 0 })
        {
            ws.Patrol = HuntPatrol(plan); // again while the round is empty: its chunks may not have been loaded yet
        }

        if (ws.Patrol.Count == 0)
        {
            ws.NextYieldAt = _uptime + NothingFoundRetrySeconds;
            IdleNearCenter(npc, plan);
            return;
        }

        if (!ws.Patrolling)
        {
            ws.Patrolling = true;
            ws.PatrolLeft = ws.Patrol.Count;
        }

        if (ws.PatrolLeft-- <= 0)
        {
            ws.Patrolling = false;
            ws.NextYieldAt = _uptime + NothingFoundRetrySeconds * 1.5;
            IdleNearCenter(npc, plan);
            return;
        }

        ws.PatrolIndex = (ws.PatrolIndex + 1) % ws.Patrol.Count;
        StartWalk(npc, ws, plan, ws.Patrol[ws.PatrolIndex]);
    }

    private bool InHuntingGround(NpcWorkPlan plan, Vector3f at)
    {
        double dx = WorldConstants.WrapDeltaX((double)at.X - plan.Center.X, _world.Circumference), dz = at.Z - plan.Center.Z;
        return dx * dx + dz * dz <= plan.Radius * (double)plan.Radius;
    }

    /// <summary>The round of a hunting ground: standable points on a ring around the centre (the chunks there must be
    /// loaded — a point that is not standable now is left out).</summary>
    private List<Vector3f> HuntPatrol(NpcWorkPlan plan)
    {
        var points = new List<Vector3f>();
        float r = System.Math.Max(6f, plan.Radius * 0.6f);
        int refY = (int)System.Math.Floor(plan.Center.Y);
        for (int i = 0; i < HuntPatrolPoints; i++)
        {
            double a = i * System.Math.PI * 2.0 / HuntPatrolPoints;
            int x = WorldConstants.WrapX((int)System.Math.Floor(plan.Center.X + System.Math.Cos(a) * r), _world.Circumference);
            int z = (int)System.Math.Floor(plan.Center.Z + System.Math.Sin(a) * r);
            if (TryNpcGroundFeetYAt(x, z, refY, out int feet) && StandableSpot(x, feet, z) is { } spot)
            {
                points.Add(spot);
            }
        }

        return points;
    }

    /// <summary>A standable spot about <see cref="HuntApproachDistance"/> from the quarry, on the way from the hunter.</summary>
    private Vector3f? HuntApproachSpot(Vector3f from, Vector3f quarry)
    {
        var q = Unwrapped(from, quarry);
        float dx = from.X - q.X, dz = from.Z - q.Z;
        float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (d < 0.01f)
        {
            return null;
        }

        float keep = System.Math.Min(HuntApproachDistance, d);
        int x = WorldConstants.WrapX((int)System.Math.Floor(q.X + dx / d * keep), _world.Circumference);
        int z = (int)System.Math.Floor(q.Z + dz / d * keep);
        int refY = (int)System.Math.Floor(from.Y);
        return TryNpcGroundFeetYAt(x, z, refY, out int feet) ? StandableSpot(x, feet, z) : null;
    }

    private void StartWalk(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan, Vector3f spot)
    {
        ws.GoalSpot = spot;
        ws.StageUntil = _uptime + WalkTimeoutSeconds;
        ws.Stage = NpcWorkStage.Walking;
        npc.ActivityKey = plan.ActivityKey;
        if (WrapDistSq(npc.Pos, spot) <= 0.6 * 0.6 && System.Math.Abs(npc.Pos.Y - spot.Y) < 1f)
        {
            npc.Goal = null;
            npc.Leash = 0f; // already there — stand still and work
            ArrivedAtWork(npc, ws, plan);
            return;
        }

        SetNpcGoal(npc, spot, NpcArrival.None, 0f); // a zero leash: on arrival it stands exactly there
        BroadcastNpcs();
    }

    private void TickWorkWalking(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan)
    {
        if (ws.GoalSpot is not { } spot)
        {
            ResetNpcWork(npc, ws);
            return;
        }

        if (npc.Goal is null)
        {
            if (WrapDistSq(npc.Pos, spot) <= 1.2 * 1.2)
            {
                ArrivedAtWork(npc, ws, plan);
                return;
            }

            DropSite(npc, ws); // something else took the walk over
            return;
        }

        if (!npc.Goal.Value.Equals(spot) || npc.PathFailures >= 2 || _uptime >= ws.StageUntil)
        {
            DropSite(npc, ws); // unreachable (or the routine sent it elsewhere) — try another site
            return;
        }

        // #2645: a hunter on its round or closing in is noticed — skittish animals near it shy away from it.
        if (plan.Kind == NpcWorkKind.Hunt && _uptime >= ws.NextScareAt)
        {
            ws.NextScareAt = _uptime + 1.0;
            ScareCreaturesFrom(npc.Pos, HuntScareRadius, HuntScareSeconds);
        }
    }

    private void ArrivedAtWork(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan)
    {
        ws.GoalSpot = null;
        if (ws.Patrolling)
        {
            ws.Stage = NpcWorkStage.Idle; // a point of the round: look around again right away
            ws.NextYieldAt = _uptime + 1.0;
            return;
        }

        if (plan.Kind == NpcWorkKind.Hunt)
        {
            if (QuarryStillThere(ws.Quarry) is { } q && WrapDistSq(npc.Pos, q.Position) <= 14.0 * 14.0)
            {
                StartAim(npc, ws);
            }
            else
            {
                ws.Quarry = null;
                ws.Stage = NpcWorkStage.Idle;
                ws.NextYieldAt = _uptime + 1.0; // it moved off — look again
            }

            return;
        }

        ws.Stage = NpcWorkStage.Working;
        ws.StageUntil = _uptime + plan.Kind switch
        {
            NpcWorkKind.Mine => MineWorkSeconds,
            NpcWorkKind.Chop => ChopWorkSeconds,
            _ => GatherWorkSeconds,
        };
        npc.ActivityKey = plan.ActivityKey;
        FaceTowards(npc, new Vector3f(ws.Site.X + 0.5f, ws.Site.Y, ws.Site.Z + 0.5f));
        BroadcastNpcs();
    }

    private void TickWorkWorking(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan)
    {
        FaceTowards(npc, new Vector3f(ws.Site.X + 0.5f, ws.Site.Y, ws.Site.Z + 0.5f));
        if (_uptime < ws.StageUntil)
        {
            return;
        }

        ws.CoreCalls++;
        bool done = plan.Kind switch
        {
            NpcWorkKind.Gather => NpcHarvestFlora(npc, ws.Site, plan.Bank),
            NpcWorkKind.Mine => NpcMineCell(npc, ws.Site, plan.Bank, NpcMineRefillSeconds),
            NpcWorkKind.Chop => NpcFellTree(npc, ws.Site, plan.Bank),
            _ => false,
        };

        if (!done)
        {
            SkipSite(ws, ws.Site);
            ws.Stage = NpcWorkStage.Idle;
            ws.NextYieldAt = _uptime + FailedSiteRetrySeconds;
            npc.Leash = WorkIdleLeash;
            return;
        }

        if (plan.Kind == NpcWorkKind.Chop)
        {
            ws.Stage = NpcWorkStage.Felling; // the tree comes down from the top over the next ticks; keep swinging
            ws.StageUntil = _uptime + FellingWaitSeconds;
            return;
        }

        FinishWork(npc, ws, plan, success: true);
    }

    private void StartAim(ServerNpc npc, NpcWorkState ws)
    {
        ws.Stage = NpcWorkStage.Aiming;
        ws.StageUntil = _uptime + AimSeconds;
        if (ws.Quarry is { } q)
        {
            FaceTowards(npc, q.Position);
        }

        npc.Goal = null;
        npc.Path = null;
        npc.Leash = 0f;
        BroadcastNpcs();
    }

    private void TickWorkAiming(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan)
    {
        if (QuarryStillThere(ws.Quarry) is not { } q)
        {
            ws.Quarry = null;
            ws.Stage = NpcWorkStage.Idle;
            ws.NextYieldAt = _uptime + FailedSiteRetrySeconds;
            npc.Leash = WorkIdleLeash;
            return;
        }

        FaceTowards(npc, q.Position);
        if (_uptime < ws.StageUntil)
        {
            return;
        }

        ws.CoreCalls++;
        bool hit = NpcShootCreature(npc, q, plan.Bank);
        ws.Quarry = null;
        if (!hit)
        {
            ws.Stage = NpcWorkStage.Idle;
            ws.NextYieldAt = _uptime + FailedSiteRetrySeconds * 2;
            npc.Leash = WorkIdleLeash;
            return;
        }

        ws.KillsToday++;
        FinishWork(npc, ws, plan, success: true);
    }

    private CombatEntity? QuarryStillThere(CombatEntity? quarry)
        => quarry is not null && quarry.Hull > 0f && _creatures.Contains(quarry) ? quarry : null;

    /// <summary>A yield: the cadence starts, and the NPC strolls back toward its work centre.</summary>
    private void FinishWork(ServerNpc npc, NpcWorkState ws, NpcWorkPlan plan, bool success)
    {
        ws.Stage = NpcWorkStage.Idle;
        ws.GoalSpot = null;
        if (success)
        {
            ws.Yields++;
            ws.NextYieldAt = _uptime + NextWorkGap(plan.Kind);
        }

        npc.Leash = WorkIdleLeash;
        IdleNearCenter(npc, plan);
    }

    /// <summary>Seconds until the next yield (the caps of #2642–#2645).</summary>
    private double NextWorkGap(NpcWorkKind kind)
    {
        var (min, max) = kind switch
        {
            NpcWorkKind.Chop => (360.0, 480.0),
            NpcWorkKind.Hunt => (240.0, 360.0),
            _ => (60.0, 90.0),
        };
        return min + _npcWorkRng.NextDouble() * (max - min);
    }

    /// <summary>Between yields the NPC idles at its work centre (walking back when it strayed far).</summary>
    private void IdleNearCenter(ServerNpc npc, NpcWorkPlan plan)
    {
        npc.ActivityKey = string.Empty;
        if (npc.Goal is null && WrapDistSq(npc.Pos, plan.Center) > 4.0 * 4.0)
        {
            SetNpcGoal(npc, plan.Center, NpcArrival.None, WorkIdleLeash);
        }

        BroadcastNpcs();
    }

    private void DropSite(ServerNpc npc, NpcWorkState ws)
    {
        if (!ws.Patrolling && ws.Quarry is null)
        {
            SkipSite(ws, ws.Site);
        }

        ResetNpcWork(npc, ws);
        ws.NextYieldAt = _uptime + FailedSiteRetrySeconds;
    }

    private static void SkipSite(NpcWorkState ws, Vector3i site)
    {
        if (ws.Skip.Count >= WorkSkipCap)
        {
            ws.Skip.Clear();
        }

        if (!ws.Skip.Contains(site))
        {
            ws.Skip.Add(site);
        }
    }

    /// <summary>A standable spot right beside a work cell: its four sides at its own level and up to three below (a face
    /// cell two above the floor is reached from the floor in front of it), one above; then the diagonals.</summary>
    private Vector3f? NpcWorkSpotNear(Vector3i cell)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            foreach (int dy in new[] { 0, -1, -2, -3, 1 })
            {
                int x = WorldConstants.WrapX(cell.X + dx, _world.Circumference);
                if (StandableSpot(x, cell.Y + dy, cell.Z + dz) is { } spot)
                {
                    return spot;
                }
            }
        }

        return null;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection (#2641): an NPC's work stage, its site, its activity, yields / core calls so far, whether it
    /// walks a hunting round, how many sites it dropped, its walk goal and the seconds until its next work.</summary>
    public (string Stage, Vector3i Site, string Activity, int Yields, int CoreCalls, bool Patrolling, int Skipped, Vector3f? Goal, double NextIn)?
        NpcWorkForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { WorkState: { } ws } npc
            ? (ws.Stage.ToString(), ws.Site, npc.ActivityKey, ws.Yields, ws.CoreCalls, ws.Patrolling, ws.Skip.Count, npc.Goal, ws.NextYieldAt - _uptime)
            : null;

    /// <summary>Test seam (#2641): makes an NPC's next work due now.</summary>
    public void DueNpcWorkForTest(int npcId)
    {
        if (_npcs.FirstOrDefault(n => n.Id == npcId) is { WorkState: { } ws })
        {
            ws.NextYieldAt = 0;
        }
    }
}
