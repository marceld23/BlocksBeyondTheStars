// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The basic training's server side (#2469, synthesis §3.3) next to the generic player-pack runtime
/// (<c>GameServerStoryPacks.cs</c>): the teaching verbs the gameplay handlers raise only while a step waits for them
/// (walk, jump, fly, walked back to the ship, a data fragment carried), the client's UI teaching flags
/// (<see cref="UiTutorialEvent"/>, tag 298), the tutorial's world predicates (plants and trees in reach, a shaped block
/// carried, knowledge for a blueprint, every blueprint handed out, an unvisited world in the system), the
/// <c>nearest_food_plant</c> marker probe, the "one teaching voice" hold over VEGA's hints and tips (a step's
/// <c>hintRefs</c>, G12) and the one-time migration of the old onboarding chain into the official tutorial pack.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The official tutorial pack — the one the legacy onboarding state migrates into.</summary>
    internal const string OfficialTutorialPackId = "tutorial";

    /// <summary>A move report further than this (blocks) from the last one is a teleport, a respawn or a streamed pose —
    /// never walking.</summary>
    private const double TutorialWalkMaxStep = 4.0;

    /// <summary>A ship pose further than this (flight units) from the last one is a jump, a warp or a fresh entry.</summary>
    private const double TutorialFlightMaxStep = 60.0;

    /// <summary>Upward speed (blocks/s) that reads as a jump.</summary>
    private const float TutorialJumpSpeed = 2.5f;

    /// <summary>The "walked back to the ship" check: far means at least this far from the landed ship's heal tank …</summary>
    private const double TutorialShipFarDistance = 40.0;

    /// <summary>… and back means aboard or within this distance again.</summary>
    private const double TutorialShipNearDistance = 10.0;

    /// <summary>Half-size (blocks, horizontal) of the probe behind <c>food_plants_near</c>, <c>trees_near</c> and the
    /// <c>nearest_food_plant</c> marker.</summary>
    private const int TutorialFloraProbeRadius = 20;

    /// <summary>Half-height (blocks) of that probe.</summary>
    private const int TutorialFloraProbeHeight = 10;

    /// <summary>Shortest gap between two accepted UI teaching flags of the same id from one player (server uptime).</summary>
    private const double UiTutorialEventGapSeconds = 0.25;

    /// <summary>The item a data fragment is.</summary>
    private const string DataFragmentItem = "data_fragment";

    /// <summary>The verbs whose counters show a player really played (the E30 "thin evidence" check).</summary>
    private static readonly string[] BasicVerbCounters =
    {
        AchievementCounters.MineAny, AchievementCounters.CraftAny, AchievementCounters.BuildAny,
        AchievementCounters.ScanAny, AchievementCounters.ResearchAny,
    };

    /// <summary>Spoken once to a player the old multiplayer bug may have auto-skipped (E30).</summary>
    internal const string TutorialRefresherLine = "story.tutorial.refresher";

    // ---------------------------------------------------------------------------------------------
    // Teaching verbs raised only while a step waits (synthesis G13: no tick scans otherwise).
    // ---------------------------------------------------------------------------------------------

    /// <summary>From <c>HandleMove</c>: walked metres (<c>move:walked</c>), a jump or the start of a crouch
    /// (<c>move:jump</c>). Measures nothing unless a step listens.</summary>
    private void TutorialMovementEvents(PlayerSession session, Vector3f before, Vector3f now, bool onSurface, bool crouching)
    {
        bool wasCrouching = session.TutorialWasCrouching;
        session.TutorialWasCrouching = crouching;
        if (!onSurface || session.State.InSpeeder.Length > 0 || session.State.InTrain.Length > 0)
        {
            return;
        }

        // #2482: metres climbed up a wall in the climbing pose (the "On the move" course) — the server sees the pose, the
        // client owns the motion; a report over 4 blocks from the last one never counts.
        double rise = now.Y - before.Y;
        if (session.State.Climbing && rise > 0 && rise <= TutorialWalkMaxStep && HasGameEventListener(GameEventKeys.MoveClimbed))
        {
            session.TutorialClimbAccum += rise;
            int metres = (int)session.TutorialClimbAccum;
            if (metres >= 1)
            {
                session.TutorialClimbAccum -= metres;
                RaiseGameEvent(session, GameEventKeys.MoveClimbed, string.Empty, metres);
            }
        }

        if (HasGameEventListener(GameEventKeys.MoveWalked))
        {
            double dx = now.X - before.X;
            double dz = now.Z - before.Z;
            int circ = _world.Circumference;
            if (circ > 0)
            {
                dx -= Math.Round(dx / circ) * circ; // across the seam of a round world
            }

            double step = Math.Sqrt((dx * dx) + (dz * dz));
            if (step > 0 && step <= TutorialWalkMaxStep)
            {
                session.TutorialWalkAccum += step;
                int whole = (int)session.TutorialWalkAccum;
                if (whole >= 1)
                {
                    session.TutorialWalkAccum -= whole;
                    RaiseGameEvent(session, GameEventKeys.MoveWalked, string.Empty, whole);
                }
            }
        }

        if (HasGameEventListener(GameEventKeys.MoveJump) && _uptime - session.TutorialJumpAt >= 1.0)
        {
            bool jumped = session.VerticalSpeed > TutorialJumpSpeed && !session.State.Climbing;
            bool crouched = crouching && !wasCrouching;
            if (jumped || crouched)
            {
                session.TutorialJumpAt = _uptime;
                RaiseGameEvent(session, GameEventKeys.MoveJump, jumped ? GameEventKeys.MoveJumpArg : GameEventKeys.MoveCrouchArg);
            }
        }
    }

    /// <summary>From <c>ShipMove</c>: flight units flown (<c>flight:flown</c>). Measures nothing unless a step listens.</summary>
    private void TutorialFlightEvents(PlayerSession pilot, Vector3f pos)
    {
        var last = pilot.TutorialFlightLast;
        pilot.TutorialFlightLast = pos;
        if (last is not { } from || !HasGameEventListener(GameEventKeys.FlightFlown))
        {
            return;
        }

        double dx = pos.X - from.X, dy = pos.Y - from.Y, dz = pos.Z - from.Z;
        double step = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (step <= 0 || step > TutorialFlightMaxStep)
        {
            return;
        }

        pilot.TutorialFlightAccum += step;
        int whole = (int)pilot.TutorialFlightAccum;
        if (whole >= 1)
        {
            pilot.TutorialFlightAccum -= whole;
            RaiseGameEvent(pilot, GameEventKeys.FlightFlown, string.Empty, whole);
        }
    }

    /// <summary>1 Hz from <c>TickShipAi</c> (the player's world is the active cursor there): walked back to the landed
    /// ship after having been far from it (<c>ship:returned</c> <c>walk</c>), a data fragment carried (<c>data:found</c>
    /// <c>fragment</c>), and since #2480 a base's sealed room or wall ring (<c>base:sealed</c>, <c>base:walled</c>). Each
    /// check only while a step waits for it.</summary>
    private void TutorialStateEvents(PlayerSession session)
    {
        var p = session.State;
        if (HasGameEventListener(GameEventKeys.ShipReturned))
        {
            var landed = _worlds.Active.LandedFor(p.PlayerId);
            if (landed.Placed && !InSpace(p.PlayerId) && !InStation(p.PlayerId))
            {
                double d2 = WrapDistSq(p.Position, landed.HealTank);
                bool onFoot = !p.AboardShip && !p.InEva && !ShipInteriorContains(p.Position);
                if (onFoot && d2 >= TutorialShipFarDistance * TutorialShipFarDistance)
                {
                    session.TutorialFarFromShip = true;
                }
                else if (session.TutorialFarFromShip && (p.AboardShip || d2 <= TutorialShipNearDistance * TutorialShipNearDistance))
                {
                    session.TutorialFarFromShip = false;
                    RaiseGameEvent(session, GameEventKeys.ShipReturned, "walk");
                }
            }
        }

        if (HasGameEventListener(GameEventKeys.DataFound)
            && (p.Inventory.Has(DataFragmentItem, 1)
                || (session.Ships.TryGetValue(session.ActiveShipId, out var ship) && ship.Cargo.Has(DataFragmentItem, 1))))
        {
            RaiseGameEvent(session, GameEventKeys.DataFound, "fragment");
        }

        TutorialBaseStateEvents(session); // #2480: a sealed room, a closed wall ring — only while this player's step waits
    }

    // ---------------------------------------------------------------------------------------------
    // UI teaching flags (tag 298).
    // ---------------------------------------------------------------------------------------------

    /// <summary>A UI moment the client reports (<see cref="UiTutorialEvent"/>): a known id becomes <c>ui:&lt;id&gt;</c> on the
    /// event bus, where only a step waiting for exactly that flag listens. Teaching only — it ticks the sender's own lesson
    /// off and nothing else (never pays, unlocks or counts for the story); an unknown id is ignored, the same id at most
    /// every <see cref="UiTutorialEventGapSeconds"/>.</summary>
    private void HandleUiTutorialEvent(PlayerSession session, UiTutorialEvent message)
    {
        string id = message.Id ?? string.Empty;
        if (!GameEventKeys.IsKnownUiEventId(id))
        {
            return;
        }

        if (session.UiTutorialEventAt.TryGetValue(id, out double at) && _uptime - at < UiTutorialEventGapSeconds && _uptime >= at)
        {
            return;
        }

        session.UiTutorialEventAt[id] = _uptime;
        RaiseGameEvent(session, GameEventKeys.UiPrefix + id);
    }

    // ---------------------------------------------------------------------------------------------
    // World predicates of the tutorial (registered in GameServerStoryConditions).
    // ---------------------------------------------------------------------------------------------

    private HashSet<ushort>? _foodPlantBlocks;
    private HashSet<ushort>? _treeBlocks;

    /// <summary>Flora whose harvest is food (a drop with hunger value) — berries and the like.</summary>
    private HashSet<ushort> FoodPlantBlocks => _foodPlantBlocks ??= BlocksDropping(item => _content.GetItem(item)?.ConsumeHunger > 0f, floraOnly: true);

    /// <summary>Blocks that drop wood (tree trunks).</summary>
    private HashSet<ushort> TreeBlocks => _treeBlocks ??= BlocksDropping(item => item == "wood_log", floraOnly: false);

    private HashSet<ushort> BlocksDropping(Func<string, bool> drop, bool floraOnly)
    {
        var set = new HashSet<ushort>();
        foreach (var block in _content.Blocks.Values)
        {
            if ((!floraOnly || string.Equals(block.Category, "flora", StringComparison.OrdinalIgnoreCase))
                && block.Drops.Any(d => drop(d.Item)))
            {
                set.Add(block.NumericId.Value);
            }
        }

        return set;
    }

    /// <summary>The nearest block of <paramref name="blocks"/> around <paramref name="who"/> on their own body (a bounded probe
    /// of the loaded blocks), or null — also when the player is in space or their world is not loaded.</summary>
    private Vector3i? NearestBlockOf(PlayerSession who, HashSet<ushort> blocks)
    {
        if (blocks.Count == 0 || InSpace(who.State.PlayerId) || _worlds.Find(who.CurrentLocationId)?.World is not { } world)
        {
            return null;
        }

        var pos = who.State.Position;
        var origin = WorldConstants.CanonicalBlock(new Vector3i((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z)), world.Circumference);
        int r = TutorialFloraProbeRadius;
        int h = TutorialFloraProbeHeight;
        int bestDist = int.MaxValue;
        Vector3i? found = null;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dy = -h; dy <= h; dy++)
                {
                    int d = (dx * dx) + (dy * dy) + (dz * dz);
                    if (d >= bestDist)
                    {
                        continue;
                    }

                    var cell = WorldConstants.CanonicalBlock(new Vector3i(origin.X + dx, origin.Y + dy, origin.Z + dz), world.Circumference);
                    if (blocks.Contains(world.GetBlockIfLoaded(cell).Value))
                    {
                        bestDist = d;
                        found = cell;
                    }
                }
            }
        }

        return found;
    }

    /// <summary><c>world:shape_block_carried</c>: a shaped building material (a form from the shape tool) or a prop block
    /// (a crafted staircase, furniture) the rotate key turns.</summary>
    private bool ShapeBlockCarried(PlayerSession who)
    {
        foreach (var stack in who.State.Inventory.Slots)
        {
            if (stack is not { IsEmpty: false })
            {
                continue;
            }

            string block = _content.GetItem(ItemKey.Base(stack.Item))?.PlacesBlock ?? string.Empty;
            if (block.Length == 0)
            {
                continue;
            }

            if ((ItemKey.Shape(stack.Item) != 0 && _content.GetBlock(block)?.Shapeable == true) || PropShapes.DefaultPlaceShape(block) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:research_ready</c>: the player's knowledge covers a blueprint they could research now —
    /// prerequisites unlocked, not owned, no data fragment in its cost.</summary>
    private bool ResearchReady(PlayerSession who)
    {
        var p = who.State;
        foreach (var bp in _content.Blueprints.Values)
        {
            if (!p.UnlockedBlueprints.Contains(bp.Key)
                && bp.KnowledgeCost <= p.KnowledgePoints
                && bp.Prerequisites.All(pr => p.UnlockedBlueprints.Contains(pr))
                && bp.UnlockCost.All(c => c.Item != DataFragmentItem))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:new_world_in_system</c>: a planet, moon or asteroid in the player's system they never landed on.</summary>
    private bool NewWorldInSystem(PlayerSession who)
    {
        string systemId = _galaxy?.FindBody(who.CurrentLocationId)?.SystemId ?? string.Empty;
        var system = systemId.Length > 0 ? _galaxy!.Systems.FirstOrDefault(sys => sys.Id == systemId) : null;
        return system is not null
               && system.Bodies.Any(b => (b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField)
                                         && !who.State.LandedBodies.Contains(b.Id));
    }

    // ---------------------------------------------------------------------------------------------
    // One teaching voice (G12): a step's hintRefs hold VEGA's matching hints and tips.
    // ---------------------------------------------------------------------------------------------

    private Dictionary<string, List<CompiledStep>>? _stepsByHint;

    /// <summary>The steps (of every runnable player pack) that absorb a hint or tip id.</summary>
    private Dictionary<string, List<CompiledStep>> StepsByHint
    {
        get
        {
            if (_stepsByHint is not null)
            {
                return _stepsByHint;
            }

            var map = new Dictionary<string, List<CompiledStep>>(StringComparer.Ordinal);
            foreach (var pack in PlayerPacks)
            {
                foreach (var step in pack.StepsById.Values)
                {
                    foreach (string hint in step.Def.HintRefs ?? new List<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(hint))
                        {
                            if (!map.TryGetValue(hint, out var list))
                            {
                                map[hint] = list = new List<CompiledStep>();
                            }

                            list.Add(step);
                        }
                    }
                }
            }

            return _stepsByHint = map;
        }
    }

    /// <summary>Whether a step teaches this hint or tip itself right now, so VEGA's own telling waits (#2469, G12): a do or
    /// read step that names it in <c>hintRefs</c> is active in, or still ahead of the player in, a mandatory chapter of a
    /// running pack (an active step of a started deep dive too) — not once it is done or skipped, not as an open extra past
    /// its hint time, not after its chapter is through. React steps hold nothing: their event is raised right before the
    /// hint it absorbs, so the step speaks first and the hint finds itself told.</summary>
    private bool HintHeldByTutorial(PlayerSession s, string hintId)
    {
        if (PlayerPacks.Count == 0 || !StepsByHint.TryGetValue(hintId, out var steps))
        {
            return false;
        }

        foreach (var step in steps)
        {
            if (step.IsReact || !PackRunsFor(s, step.Pack) || !StepRunsForClient(s, step) || StepDoneOrSkipped(s, step))
            {
                continue;
            }

            bool active = StepActive(s, step);
            if (active && s.StepEscalation.TryGetValue(step.ProgressKey, out var esc) && esc.Deferred)
            {
                continue; // an open extra: VEGA may remind about it the usual way
            }

            if (step.Chapter.Optional)
            {
                if (active && ChapterRuns(s, step.Chapter))
                {
                    return true;
                }

                continue;
            }

            if ((active || !step.IsAlternative) && !ChapterFinished(s, step.Chapter))
            {
                return true; // an alternative only while it stands in
            }
        }

        return false;
    }

    /// <summary>A step's line was shown: the hints and tips it absorbs count as told — a once-hint's flag is burned, a
    /// context tip's first telling is booked and its cooldown starts (the tool-tier tip also forgets the swing that armed
    /// it), so VEGA does not say the same thing again right after.</summary>
    private void MarkHintsToldByStep(PlayerSession s, CompiledStep step)
    {
        var refs = step.Def.HintRefs;
        if (refs is not { Count: > 0 })
        {
            return;
        }

        var p = s.State;
        foreach (string hint in refs)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            if (VegaTips.TryGetValue(hint, out var spec))
            {
                if (VegaTipCount(p, hint) == 0)
                {
                    p.Milestones.Add("vega:hint:" + hint);
                }

                s.VegaTipCooldownUntil[hint] = _uptime + spec.Cooldown;
                s.VegaTipSince.Remove(hint);
                if (hint == "tier_gate")
                {
                    s.VegaTierGateBlock = string.Empty;
                    s.VegaTierGateTool = string.Empty;
                }
            }
            else
            {
                p.Milestones.Add("vega:hint:" + hint);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The one-time migration of the old onboarding chain (synthesis §3.8, d1 §6.2).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The official tutorial's evidence mapping, run once per player by <c>MigratePackIfNeeded</c> (marker
    /// <c>story:tutorial:migrated:v1</c>; the generic part — a step's <c>legacyKey</c> held means the step is done — has
    /// already run). Legacy keys are evidence only, never live state (synthesis §5.3):
    /// <list type="bullet">
    /// <item>all nine <c>vega:stage:*</c> (a finished chain, or the old veteran auto-skip) → the whole basic training done,
    /// silently; with thin evidence (fewer than three of the basic verbs in the achievement counters) the player hears one
    /// refresher offer (E30);</item>
    /// <item>no onboarding state at all, but veteran evidence in the join snapshot (a save from before VEGA's lessons) →
    /// the basic training skipped silently, with VEGA's old one-line "systems online" (<c>vega.veteran</c>);</item>
    /// <item>otherwise per step: <c>vega:intro</c> → VEGA, menu and Codex were introduced (b1.3, b1.5, b1.6); any stage →
    /// walking, jumping and the quick bar (b1.1, b1.2, b1.4); the counters <c>mine:iron_ore</c>, <c>craft:iron_ingot</c>,
    /// <c>craft:iron_plate</c>, <c>build:any</c>, <c>craft:campfire</c> → b3.2, b4.1, b4.2, b4.3, b4.5; gear worn beyond the
    /// starter lamp → b3.5; a board or chain job taken → b6.6; then every mandatory chapter whose counting steps are all
    /// evidenced is marked done, so its extras do not replay.</item>
    /// </list>
    /// The old keys stay (older builds keep reading them). Nothing is sent but the two lines named above.
    /// </summary>
    private void MigrateOfficialTutorial(PlayerSession s, CompiledPack pack)
    {
        var m = MilestonesOf(s);
        var p = s.State;
        bool hadIntro = m.Contains(StoryLegacy.IntroMilestone); // read before the pack starts (StartPack writes it)
        bool allStages = StoryLegacy.StageIds.All(id => m.Contains(StoryLegacy.StagePrefix + id));
        bool anyStage = StoryLegacy.StageIds.Any(id => m.Contains(StoryLegacy.StagePrefix + id));
        string prefix = StoryPackKeys.Prefix(pack.Id);
        bool anyPackKey = m.Any(k => k.StartsWith(prefix, StringComparison.Ordinal) && k != StoryPackKeys.Migrated(pack.Id));

        if (allStages)
        {
            FinishTutorialByMigration(s, pack, skipped: false);
            int verbs = BasicVerbCounters.Count(c => p.AchievementCounters.TryGetValue(c, out int n) && n > 0);
            if (verbs < 3)
            {
                SendPackLine(s, pack, TutorialRefresherLine); // E30: one offer, never forced (the migration runs once)
            }

            return;
        }

        if (!hadIntro && !anyStage && !anyPackKey && s.Join.Veteran)
        {
            FinishTutorialByMigration(s, pack, skipped: true);
            SendVegaLine(s, "vega.veteran", 3);
            return;
        }

        void Evidence(string stepId, bool holds)
        {
            if (holds && pack.StepsById.ContainsKey(stepId))
            {
                m.Add(StoryPackKeys.StepDone(pack.Id, stepId));
            }
        }

        int Counter(string key) => p.AchievementCounters.TryGetValue(key, out int n) ? n : 0;

        Evidence("b1.3", hadIntro);
        Evidence("b1.5", hadIntro);
        Evidence("b1.6", hadIntro);
        Evidence("b1.1", anyStage);
        Evidence("b1.2", anyStage);
        Evidence("b1.4", anyStage);
        Evidence("b3.2", Counter(AchievementCounters.Mine("iron_ore")) > 0);
        Evidence("b4.1", Counter(AchievementCounters.Craft("iron_ingot")) > 0);
        Evidence("b4.2", Counter(AchievementCounters.Craft("iron_plate")) > 0);
        Evidence("b4.3", Counter(AchievementCounters.BuildAny) > 0);
        Evidence("b4.5", Counter(AchievementCounters.Craft("campfire")) > 0);
        Evidence("b3.5", p.Equipment.Slots.Any(e => e is { IsEmpty: false } && ItemKey.Base(e.Item) != "suit_lamp"));
        Evidence("b6.6", p.Missions.Any(mp => GetMissionDef(mp.MissionId) is { } def && MissionAcceptCountsAsTrade(def)));

        foreach (var ch in pack.Chapters)
        {
            if (!ch.Optional && ch.Steps.Any(StepCountsForChapter) && ch.Steps.Where(StepCountsForChapter).All(st => StepDoneOrSkipped(s, st)))
            {
                m.Add(StoryPackKeys.ChapterDone(pack.Id, ch.Id));
            }
        }
    }

    /// <summary>Marks the whole basic training through for a migrated player — every step of the mandatory chapters done
    /// (or skipped), the chapters, the pack started and done — and writes the legacy graduation, all silently: no intro, no
    /// cutscene, no closing line, no achievement.</summary>
    private void FinishTutorialByMigration(PlayerSession s, CompiledPack pack, bool skipped)
    {
        var m = MilestonesOf(s);
        foreach (var ch in pack.Chapters)
        {
            if (ch.Optional)
            {
                continue;
            }

            foreach (var step in ch.Steps)
            {
                if (!StepDoneOrSkipped(s, step))
                {
                    m.Add(skipped ? StoryPackKeys.StepSkipped(pack.Id, step.Id) : StoryPackKeys.StepDone(pack.Id, step.Id));
                }
            }

            m.Add(skipped ? StoryPackKeys.ChapterSkipped(pack.Id, ch.Id) : StoryPackKeys.ChapterDone(pack.Id, ch.Id));
        }

        m.Add(StoryPackKeys.Started(pack.Id));
        m.Add(StoryPackKeys.Done(pack.Id));
        WriteLegacyGraduation(s);
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: <see cref="HintHeldByTutorial"/>.</summary>
    internal bool HintHeldByTutorialForTest(PlayerSession s, string hintId) => HintHeldByTutorial(s, hintId);

    /// <summary>Test seam: the per-player answer of a <c>world:</c> predicate.</summary>
    internal bool WorldPredicateForTest(PlayerSession s, string predicate) => WorldPredicates.TryGetValue(predicate, out var test) && test(s);

    /// <summary>Test seam: the 1 Hz state checks of the tutorial (walked back, a data fragment carried).</summary>
    internal void TutorialStateEventsForTest(PlayerSession s) => TutorialStateEvents(s);

    /// <summary>Test seam: "I know the game" for a player — the tutorial packs' mandatory chapters skipped, as the Settings
    /// button does (for tests that need a player past the basic training).</summary>
    public void SkipTutorialForTest(PlayerSession s) => SkipOrRestartTutorialPacks(s, restart: false);
}
