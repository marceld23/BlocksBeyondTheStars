// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The tick schedule (#2904): the ONE place the order of the guarded tick systems lives. <see cref="Tick"/> keeps the
/// control flow (network poll, the held-world branch, the system clock, the occupied-world list and the per-world cursor,
/// autosave and backups) and walks four phases of steps: <see cref="TickPhase.Held"/> (a paused world),
/// <see cref="TickPhase.PreWorld"/>, <see cref="TickPhase.PerWorld"/> (once per occupied world, with the active cursor set
/// to it) and <see cref="TickPhase.PostWorld"/>. Each array is built once in the constructor, so walking the schedule
/// allocates no delegates. <see cref="RunTickSteps"/> hands every step to the two Guard overloads of
/// GameServerResilience.cs with the step's literal name, so the names below are the tick-timing (#1504) and tick-fault
/// bucket names — they must stay stable, and a new system is added as one entry at the position it should run.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The four phases of the tick schedule (see the class summary); a read-only test view names them.</summary>
    internal enum TickPhase : byte
    {
        Held,
        PreWorld,
        PerWorld,
        PostWorld,
    }

    /// <summary>When a step runs. <see cref="TickWhen.SweepDue"/> and <see cref="TickWhen.FarTilesDue"/> are only
    /// meaningful in the per-world phase: they gate a step on the once-per-tick throttle decisions Tick makes before the
    /// world loop (the chunk-sweep cadence and the far-tile refresh).</summary>
    private enum TickWhen : byte
    {
        Always,
        SweepDue,
        FarTilesDue,
    }

    /// <summary>One guarded tick system: its timing/fault bucket name, exactly one body (a no-argument body or a
    /// delta-time body, fixed by the factory that built it) and the condition it runs under.</summary>
    private readonly struct TickStep
    {
        public readonly string Name;
        public readonly Action? Body;
        public readonly Action<double>? DtBody;
        public readonly TickWhen When;

        private TickStep(string name, Action? body, Action<double>? dtBody, TickWhen when)
        {
            Name = name;
            Body = body;
            DtBody = dtBody;
            When = when;
        }

        /// <summary>A no-argument system, guarded like a direct no-argument Guard call.</summary>
        public static TickStep Plain(string name, Action body, TickWhen when = TickWhen.Always) => new(name, body, null, when);

        /// <summary>A delta-time system, guarded like a direct delta-time Guard call.</summary>
        public static TickStep Dt(string name, Action<double> body) => new(name, null, body, TickWhen.Always);
    }

    private readonly TickStep[] _heldSchedule;
    private readonly TickStep[] _preWorldSchedule;
    private readonly TickStep[] _perWorldSchedule;
    private readonly TickStep[] _postWorldSchedule;

    /// <summary>A held (paused) world: no simulation advances, but the control plane keeps running.</summary>
    private TickStep[] BuildHeldSchedule()
    {
        return new TickStep[]
        {
            TickStep.Dt("Moderation", TickModeration),
            TickStep.Dt("Maintenance", TickMaintenance),
            // The control plane must keep seeing a held world (#973): the /status snapshot is what the hosted
            // fleet polls, and freezing it would report a stale player count for as long as the pause lasts.
            // Safe to run here — with players joined it only republishes; the idle timer stays at zero.
            TickStep.Dt("HostedLifecycle", TickHostedLifecycle),
            // #996: an observer neither holds nor counts toward the pause (#973) and keeps flying — without
            // streaming they run off the already-sent chunks into void until somebody resumes the world.
            TickStep.Plain("SpectatorChunks", StreamChunksToSpectators),
        };
    }

    /// <summary>Once per tick, after the system clock advanced and before the occupied worlds are ticked.</summary>
    private TickStep[] BuildPreWorldSchedule()
    {
        return new TickStep[]
        {
            TickStep.Dt("TickSpace", TickSpace), // space instances are keyed by location and handle their own players
            TickStep.Dt("Transits", TickTransits), // #1614: per player, wherever they fly
            TickStep.Dt("SpaceEnemyEncounters", TickSpaceEnemyEncounters), // #2971: the Codex's enemies met in space (1 Hz; sets the ground beat)
        };
    }

    /// <summary>Once per occupied world, with the active cursor set to it.</summary>
    private TickStep[] BuildPerWorldSchedule()
    {
        // Each simulation system is contained on its own (see GameServerResilience.Guard): a system
        // that throws on edge-case data is logged + skipped for this tick, while every OTHER system in
        // this world — and every other occupied world — keeps simulating. A throw here can never reach
        // Run() and crash the process.
        return new TickStep[]
        {
            TickStep.Dt("TickEnvironment", TickEnvironment),
            TickStep.Dt("TickEnemies", TickEnemies),
            TickStep.Dt("TickBandits", TickBandits),
            TickStep.Plain("TickEnemyEncounters", TickEnemyEncounters), // #2971: the Codex's enemies met on the ground (1 Hz)
            TickStep.Dt("TickPresence", TickPresence),
            TickStep.Dt("TickMarkerPings", TickMarkerPings), // expire "look here" pings (#1217)
            TickStep.Dt("TickFluids", TickFluids),
            TickStep.Dt("TickGranular", TickGranular), // #1319: woken sand/ash/snow settles, sinks through fluid
            TickStep.Dt("TickFire", TickFire),
            TickStep.Dt("TickWeather", TickWeather),
            TickStep.Dt("TickFlora", TickFlora),
            TickStep.Dt("TickNpcFelling", TickNpcFelling), // #2644: a woodcutter's tree comes down from the top, a few cells a tick
            TickStep.Dt("TickNpcRestores", TickNpcRestores), // #2643: mined quarry cells fill up again (1 Hz)
            TickStep.Dt("TickCreatures", TickCreatures),
            TickStep.Dt("TickSreekmakra", TickSreekmakra), // 2026-09: Valuma's shapeshifter and mood (1 Hz)
            TickStep.Dt("TickGiants", TickGiants), // #1998: the colossus, the sandworms and the thumpers
            TickStep.Dt("TickTrains", TickTrains), // #2113: the monorail trains and their riders
            TickStep.Dt("TickBaseGuests", TickBaseGuests), // #2602: friendly visitors walk up to a base whose owner is home (1 Hz)
            TickStep.Plain("TickBaseLife", TickBaseLife), // #1120/#2718: settlers move in — per world, one base per 10 s, one scan per tick at most
            TickStep.Dt("TickLodges", TickLodges), // #2640/#2641: lodges found by passing players, the store crates (1 Hz)
            TickStep.Dt("TickWanderers", TickWanderers), // #2646: named travellers met in the open land (1 Hz)
            TickStep.Dt("TickNpcRoutine", TickNpcRoutine), // #1867/#1868: work by day, sit in the evening, sleep at night; jobs
            TickStep.Dt("TickNpcPaths", TickNpcPaths), // #1866: at most one NPC path search per tick
            TickStep.Dt("TickNpcs", TickNpcs),
            TickStep.Dt("TickNpcConversations", TickNpcConversations), // #2616: end dead talks, hold the talking NPC (1 Hz)
            TickStep.Dt("TickPassingGreetings", TickPassingGreetings), // #2593: NPCs greet and wave as you walk by (1 Hz)
            TickStep.Dt("TickProfessions", TickProfessions), // the streamer asks for photos, the tamer's pet follows (2026-09)
            TickStep.Dt("TickVillageLife", TickVillageLife), // #2600: villagers talk to each other (1 Hz, scan every 3 s)
            TickStep.Dt("TickVillageFestivals", TickVillageFestivals), // #2604: the village festival (1 Hz + the tune's notes)
            TickStep.Dt("TickAirFills", TickAirFills), // #2650: time-sliced base/station air fills move on
            TickStep.Dt("TickStationStaffing", TickStationStaffing), // #1487: crew only staffs posts in sealed rooms
            TickStep.Dt("TickPlanetTraders", TickPlanetTraders), // #2603: traders land on occupied bodies even when nobody flies (5 s)
            TickStep.Dt("TickLandedTraders", TickLandedTraders), // P3: materialize/lift-off a peaceful trader parked on this surface
            TickStep.Dt("TickDoors", TickDoors),
            TickStep.Dt("TickCrystalNet", TickCrystalNet), // #2046: the Crystal Net's logic + sensor beats
            TickStep.Dt("TickDropPackets", TickDropPackets), // #853: ground packets flow back into whoever walks over them
            TickStep.Plain("TickCompanionPayoff", TickCompanionPayoff), // #1210: companions growl at hostiles, stall robbers, drop produce (1 Hz)
            TickStep.Plain("TickCompanionScouting", TickCompanionScouting), // #1225: a deeply bonded companion shares a landmark now and then
            TickStep.Plain("TickSentries", TickSentries), // #1214: base sentry posts fire at hostiles near a home base (2 Hz)
            TickStep.Plain("TickBurning", TickBurning), // #1700: lava and fire hurt animals, robbers and machines too (2 Hz)
            TickStep.Dt("TickHealTanks", TickHealTanks), // base/station regen field: heal + feed + suit recharge
            TickStep.Dt("TickStationsInReach", TickStationsInReach), // #1070: Tab-menu station gates follow the player
            TickStep.Dt("TickVoidRescue", TickVoidRescue),
            TickStep.Dt("TickShipAi", TickShipAi), // VEGA advisor hints + memory-fragment redemption
            TickStep.Plain("StreamChunks", StreamChunks),
            TickStep.Plain("StreamWorldTextures", StreamWorldTextures), // #1958: one page of the texture list per client per tick
            TickStep.Plain("ServeFarTiles", ServeFarTiles), // #1871: far-terrain tile builds under a per-tick budget
            TickStep.Plain("FlushEntityLists", FlushEntityLists), // #1530: one list per type + one player state per session per tick
            TickStep.Plain("SweepFarChunks", SweepFarChunks, TickWhen.SweepDue), // only on a chunk-sweep tick (throttled)
            TickStep.Plain("RefreshFarTiles", RefreshFarTiles, TickWhen.FarTilesDue), // #1821: only when an edit changed a far tile
        };
    }

    /// <summary>Once per tick, after every occupied world was ticked; Tick then runs the autosave cadence and the
    /// backups after these.</summary>
    private TickStep[] BuildPostWorldSchedule()
    {
        return new TickStep[]
        {
            TickStep.Plain("DerelictSpacewalks", TickDerelictSpacewalks), // #2636: boarders the void check found outside a derelict's hull step out (a world change, so after the loop)
            TickStep.Dt("TickPvp", TickPvp), // #2972: whom each player may hit, duel timeouts, spawn protection (1 Hz)
            TickStep.Dt("ShipVisits", TickShipVisits), // #2723: guests who walked out go home; the 1 Hz check of every docking visit
            TickStep.Dt("TickStoryOutsideWorlds", TickStoryOutsideWorlds), // #2477: the 1 Hz story pass for players no loaded world reaches
            TickStep.Plain("TickCutscenes", TickCutscenes), // #2468: queued cutscenes for every joined player, wherever they are (space too)
            TickStep.Plain("TickJournal", TickJournal), // #2471: the Journal's coalesced pushes (only players whose stories changed)
            TickStep.Dt("TickAmbientWeather", TickAmbientWeather), // #2173: the weather of every body in an occupied system
            TickStep.Dt("SampleHistories", SampleHistories), // also advances _uptime
            TickStep.Plain("SilentSessions", SweepSilentSessions), // release names/slots held by dead clients (#964)
            TickStep.Plain("SweepExpiredLandedTraders", SweepExpiredLandedTraders), // P3: free pads of traders whose dwell ended on bodies nobody is on
            TickStep.Plain("TickGreetings", TickGreetings), // push any LLM NPC greetings finished off-thread (item 15)
            TickStep.Plain("TickDialogRadio", TickDialogRadio), // due "they said they'd call" dialogue consequences (#1127)
            TickStep.Plain("TickNpcRadio", TickNpcRadio), // NPC radio calls (#1119): per-player 30 s trigger scans
            TickStep.Plain("TickMissionTexts", TickMissionTexts), // push mission-list refreshes when L3 board texts arrive
            TickStep.Plain("TickAiMissions", TickAiMissions), // publish /ai_mission generations finished off-thread
            TickStep.Plain("TickVegaBanter", TickVegaBanter), // push VEGA's LLM banter lines finished off-thread
            TickStep.Dt("AccumulatePlaytime", AccumulatePlaytime),
            TickStep.Dt("HostedLifecycle", TickHostedLifecycle), // idle shutdown + /status snapshot (hosted worlds)
            TickStep.Dt("Maintenance", TickMaintenance), // announcement intake + restart countdown broadcasts
            TickStep.Dt("Moderation", TickModeration), // kick intake + the delayed close behind it
            TickStep.Dt("CrashReportFlush", MaybeFlushCrashReports), // best-effort background upload of queued reports
        };
    }

    /// <summary>Walks one phase of the schedule in order. Each step goes through the matching existing Guard overload
    /// with its literal name — nothing else is added here (no timing, no try/catch, no logging), so the guard
    /// semantics and the tick-timing / tick-fault buckets are exactly those of a direct call. The two throttle flags
    /// are decided once per tick by the caller and only gate the per-world sweep steps.</summary>
    private void RunTickSteps(TickStep[] steps, double deltaSeconds, bool sweepDue = false, bool farTilesDue = false)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            ref readonly var step = ref steps[i];
            if (step.When == TickWhen.SweepDue && !sweepDue)
            {
                continue;
            }

            if (step.When == TickWhen.FarTilesDue && !farTilesDue)
            {
                continue;
            }

            if (step.DtBody is not null)
            {
                Guard(step.Name, deltaSeconds, step.DtBody);
            }
            else
            {
                Guard(step.Name, step.Body!);
            }
        }
    }

    /// <summary>Test seam (#2904): the step names of one phase, in schedule order (a fresh array per call).</summary>
    internal string[] TickScheduleNamesForTest(TickPhase phase)
    {
        TickStep[] steps = phase switch
        {
            TickPhase.Held => _heldSchedule,
            TickPhase.PreWorld => _preWorldSchedule,
            TickPhase.PerWorld => _perWorldSchedule,
            TickPhase.PostWorld => _postWorldSchedule,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };
        var names = new string[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            names[i] = steps[i].Name;
        }

        return names;
    }
}
