// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The job state machine of the wilderness folk (#2641) on the server: the facade of <see cref="NpcWorkSystem"/>
/// (ADR 0019), which owns the cadence, the work RNG and the tick/walk/work/hunt steps. This partial keeps the four work
/// types the lodge residents, pioneers, wanderers and the NPC list name unqualified, the two members those partials call —
/// same names, same signatures, each one line delegating — the test seams, and the explicit
/// <see cref="INpcWorkHost"/> implementation.
/// </summary>
public sealed partial class GameServer : INpcWorkHost
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

        /// <summary>A wanderer's plan (#2646): the searches and cores also keep out of every lodge, settlement and ruin
        /// (<c>respectAllProtection</c>) — a lodge resident works its own plot and grove, a passer-by never does.</summary>
        public bool RespectAllProtection { get; init; }
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

    // ---------------- The work machine (NpcWorkSystem) ----------------

    private void TickNpcWork(ServerNpc npc, NpcWorkPlan plan, List<PlayerSession> targets) => _npcWorkSystem.TickNpcWork(npc, plan, targets);

    private void ResetNpcWork(ServerNpc npc, NpcWorkState ws) => _npcWorkSystem.ResetNpcWork(npc, ws);

    // INpcWorkHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    List<CombatEntity> INpcWorkHost.Creatures => _creatures;

    long INpcWorkHost.MarketDay => MarketDay;

    bool INpcWorkHost.IsFlora(ushort id) => IsFlora(id);

    List<Vector3i> INpcWorkHost.FindNpcGatherSites(Vector3f center, int radius, int max, bool respectAllProtection)
        => FindNpcGatherSites(center, radius, max, respectAllProtection);

    List<Vector3i> INpcWorkHost.FindNpcOutcrops(Vector3f center, int radius, int max) => FindNpcOutcrops(center, radius, max);

    List<Vector3i> INpcWorkHost.FindNpcTrees(Vector3f center, int radius, int max, bool respectAllProtection)
        => FindNpcTrees(center, radius, max, respectAllProtection);

    CombatEntity? INpcWorkHost.FindNpcHuntTarget(ServerNpc npc, float range) => FindNpcHuntTarget(npc, range);

    Vector3f? INpcWorkHost.KeeperCustomer(ServerNpc npc, List<PlayerSession> targets) => KeeperCustomer(npc, targets);

    Vector3f INpcWorkHost.Unwrapped(Vector3f from, Vector3f to) => Unwrapped(from, to);

    bool INpcWorkHost.TryNpcGroundFeetYAt(int x, int z, int refY, out int feetY) => TryNpcGroundFeetYAt(x, z, refY, out feetY);

    Vector3f? INpcWorkHost.StandableSpot(int x, int y, int z) => StandableSpot(x, y, z);

    void INpcWorkHost.SetNpcGoal(ServerNpc npc, Vector3f goal, NpcArrival arrival, float leashThere) => SetNpcGoal(npc, goal, arrival, leashThere);

    void INpcWorkHost.FaceTowards(ServerNpc npc, Vector3f at) => FaceTowards(npc, at);

    void INpcWorkHost.BroadcastNpcs() => BroadcastNpcs();

    void INpcWorkHost.ScareCreaturesFrom(Vector3f pos, float radius, double seconds) => ScareCreaturesFrom(pos, radius, seconds);

    bool INpcWorkHost.NpcHarvestFlora(ServerNpc npc, Vector3i cell, System.Action<string, int> bank, bool trustedSite, bool respectAllProtection)
        => NpcHarvestFlora(npc, cell, bank, trustedSite, respectAllProtection);

    bool INpcWorkHost.NpcMineCell(ServerNpc npc, Vector3i cell, System.Action<string, int> bank, double refillSeconds)
        => NpcMineCell(npc, cell, bank, refillSeconds);

    bool INpcWorkHost.NpcFellTree(ServerNpc npc, Vector3i trunkBase, System.Action<string, int> bank, bool respectAllProtection)
        => NpcFellTree(npc, trunkBase, bank, respectAllProtection);

    bool INpcWorkHost.NpcFellingActive(ServerNpc npc) => NpcFellingActive(npc);

    bool INpcWorkHost.NpcShootCreature(ServerNpc npc, CombatEntity target, System.Action<string, int> bank) => NpcShootCreature(npc, target, bank);
}
