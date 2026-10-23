// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The basic training's server side (#2469, synthesis §3.3) next to the generic player-pack runtime
/// (<c>GameServerStoryPacks.cs</c>): the facade of <see cref="TutorialSystem"/> (ADR 0019), which owns the teaching verbs
/// the gameplay handlers raise only while a step waits for them (walk, jump, fly, walked back to the ship, a data fragment
/// carried), the client's UI teaching flags (<see cref="UiTutorialEvent"/>, tag 298), the tutorial's world predicates
/// (plants and trees in reach, a shaped block carried, knowledge for a blueprint, an unvisited world in the system) and the
/// <c>nearest_food_plant</c> marker probe — same names, same signatures, each one line delegating, for the move and flight
/// handlers, the 1 Hz VEGA pass, the story packs, the story conditions and the dispatch table. This partial itself keeps
/// the story-pack runtime: the "one teaching voice" hold over VEGA's hints and tips (a step's <c>hintRefs</c>, G12), the
/// pack's hold list for hints about later topics (<c>gates.holdHints</c>, #2549) and the one-time migration of the old
/// onboarding chain into the official tutorial pack; plus the test seams and the explicit <see cref="ITutorialHost"/>
/// implementation.
/// </summary>
public sealed partial class GameServer : ITutorialHost
{
    /// <summary>The official tutorial pack — the one the legacy onboarding state migrates into.</summary>
    internal const string OfficialTutorialPackId = "tutorial";

    /// <summary>The verbs whose counters show a player really played (the E30 "thin evidence" check).</summary>
    private static readonly string[] BasicVerbCounters =
    {
        AchievementCounters.MineAny, AchievementCounters.CraftAny, AchievementCounters.BuildAny,
        AchievementCounters.ScanAny, AchievementCounters.ResearchAny,
    };

    /// <summary>Spoken once to a player the old multiplayer bug may have auto-skipped (E30).</summary>
    internal const string TutorialRefresherLine = "story.tutorial.refresher";

    // ---------------------------------------------------------------------------------------------
    // Teaching verbs, UI teaching flags and world predicates: facades of TutorialSystem (ADR 0019).
    // ---------------------------------------------------------------------------------------------

    /// <summary>From <c>HandleMove</c>: walked metres (<c>move:walked</c>), a jump or the start of a crouch
    /// (<c>move:jump</c>). Measures nothing unless a step listens.</summary>
    private void TutorialMovementEvents(PlayerSession session, Vector3f before, Vector3f now, bool onSurface, bool crouching)
        => _tutorialSystem.TutorialMovementEvents(session, before, now, onSurface, crouching);

    /// <summary>From <c>ShipMove</c>: flight units flown (<c>flight:flown</c>). Measures nothing unless a step listens.</summary>
    private void TutorialFlightEvents(PlayerSession pilot, Vector3f pos) => _tutorialSystem.TutorialFlightEvents(pilot, pos);

    /// <summary>1 Hz from <c>TickShipAi</c>: walked back to the landed ship, a data fragment carried, a base's sealed room
    /// or wall ring — each check only while one of this player's steps waits for it.</summary>
    private void TutorialStateEvents(PlayerSession session) => _tutorialSystem.TutorialStateEvents(session);

    /// <summary>#2550: a step that waits for <c>ship:returned</c> activates (also in a replay): the walk starts afresh.</summary>
    private static void ResetTutorialWalk(PlayerSession s, CompiledStep step) => TutorialSystem.ResetTutorialWalk(s, step);

    /// <summary>Test seam: <see cref="TutorialSystem.ShipCompassDistSq"/> for a player's own landed ship (as a distance), or
    /// null without one.</summary>
    internal double? ShipCompassDistanceForTest(PlayerSession s)
    {
        var landed = _worlds.Active.LandedFor(s.State.PlayerId);
        return landed.Placed ? Math.Sqrt(_tutorialSystem.ShipCompassDistSq(s.State.Position, landed)) : null;
    }

    /// <summary>A UI moment the client reports (<see cref="UiTutorialEvent"/>): a known id becomes <c>ui:&lt;id&gt;</c> on the
    /// event bus — teaching only.</summary>
    private void HandleUiTutorialEvent(PlayerSession session, UiTutorialEvent message) => _tutorialSystem.HandleUiTutorialEvent(session, message);

    /// <summary>Flora whose harvest is food (a drop with hunger value) — berries and the like.</summary>
    private HashSet<ushort> FoodPlantBlocks => _tutorialSystem.FoodPlantBlocks;

    /// <summary>Blocks that drop wood (tree trunks).</summary>
    private HashSet<ushort> TreeBlocks => _tutorialSystem.TreeBlocks;

    /// <summary>The nearest block of <paramref name="blocks"/> around <paramref name="who"/> on their own body, or null.</summary>
    private Vector3i? NearestBlockOf(PlayerSession who, HashSet<ushort> blocks) => _tutorialSystem.NearestBlockOf(who, blocks);

    /// <summary><c>world:shape_block_carried</c>.</summary>
    private bool ShapeBlockCarried(PlayerSession who) => _tutorialSystem.ShapeBlockCarried(who);

    /// <summary><c>world:research_ready</c>.</summary>
    private bool ResearchReady(PlayerSession who) => _tutorialSystem.ResearchReady(who);

    /// <summary><c>world:new_world_in_system</c>.</summary>
    private bool NewWorldInSystem(PlayerSession who) => _tutorialSystem.NewWorldInSystem(who);

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
            if (active && s.Story.StepEscalation.TryGetValue(step.ProgressKey, out var esc) && esc.Deferred)
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

    /// <summary>#2549: whether the basic training holds this one-time hint back for now — a running tutorial pack names it in
    /// <c>gates.holdHints</c> and the condition that releases it does not hold yet for this player (today: the crystal and
    /// the ruins-and-wrecks hints until part 5 is through, the tool-tier wall until part 3 runs). A held hint is <b>not
    /// spent</b> — its once-flag stays unset — so it comes at its next occasion once released. Never held, which keeps
    /// today's behaviour: no tutorial pack names it, the tutorial does not run for the player (world rule <c>Off</c>,
    /// <c>OptIn</c> not started, opted out, not started yet), the pack is done (graduated or skipped — a skip marks every
    /// mandatory chapter skipped, which the <c>chapter:</c> conditions count too), or an older client (no <c>packs1</c>: its
    /// nine legacy lessons never mark the chapters the conditions ask for). Readers: <see cref="ShipAiHintOnce"/> and the
    /// context-tip pass (<c>TickVegaContextTips</c>).</summary>
    private bool TutorialHoldsHint(PlayerSession s, string hintId)
    {
        if (!s.SupportsPacks || PlayerPacks.Count == 0)
        {
            return false;
        }

        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsTutorial || !pack.HoldHints.TryGetValue(hintId, out var release))
            {
                continue;
            }

            if (PackRunsFor(s, pack) && !MilestonesOf(s).Contains(StoryPackKeys.Done(pack.Id)) && !EvaluateStoryCondition(release, s))
            {
                return true;
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

        foreach (string hint in refs)
        {
            if (!string.IsNullOrWhiteSpace(hint))
            {
                BookHintAsTold(s, hint);
            }
        }
    }

    /// <summary>One hint or tip counts as told without VEGA saying it: a once-hint's flag is burned; a context tip's first
    /// telling is booked and its cooldown starts (the tool-tier tip also forgets the swing that armed it). An id the server
    /// does not speak itself (a client-only lesson a step names, #2459) is booked like a once-hint — harmless, and the tips log
    /// lists the lesson.</summary>
    private void BookHintAsTold(PlayerSession s, string hint)
    {
        var p = s.State;
        if (VegaTips.TryGetValue(hint, out var spec))
        {
            if (VegaTipCount(p, hint) == 0)
            {
                p.Milestones.Add("vega:hint:" + hint);
            }

            s.Vega.VegaTipCooldownUntil[hint] = _uptime + spec.Cooldown;
            s.Vega.VegaTipSince.Remove(hint);
            if (hint == "tier_gate")
            {
                s.Vega.VegaTierGateBlock = string.Empty;
                s.Vega.VegaTierGateTool = string.Empty;
            }
        }
        else
        {
            p.Milestones.Add("vega:hint:" + hint);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // "The tip becomes the offer" (#2459, synthesis §4.3): at a deep dive's moment VEGA offers the course, not the tip first.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Seconds of play after a course offer in which a tip the course teaches counts as told by the offer (the
    /// tool-tier wall's tip is armed by the same swing that makes the offer due; the place tips have a dwell of 3–5 s).</summary>
    internal const double OfferTipWindowSeconds = 10.0;

    /// <summary>Whether a hint or tip VEGA is about to say stands at the moment a course is due to be offered and the course
    /// teaches it (one of the due chapter's steps names it in <c>hintRefs</c>): the offer line comes on the next 1 Hz pass
    /// instead — VEGA's data cache, factory, settlement, animal, ruin, treasure or wreck tip, the tool-tier wall, the bio
    /// lab or the comm radio's Crystal Net line would otherwise come a second before "New short course: …" and say half of
    /// it twice. Cheap: a hint no step names returns at once, the due offer is checked only then.</summary>
    private bool OfferAbsorbsHint(PlayerSession s, string hintId)
        => PlayerPacks.Count > 0 && StepsByHint.ContainsKey(hintId) && DueChapterOffer(s) is { } due && ChapterTeachesHint(due, hintId);

    /// <summary>Whether VEGA offered a course that teaches this hint or tip within the last <see cref="OfferTipWindowSeconds"/>
    /// — the offer was its telling (the offer pass runs before the tips pass, so a tip armed by the same moment comes up
    /// right after it).</summary>
    private bool RecentOfferTeaches(PlayerSession s, string hintId)
    {
        if (s.Story.RecentChapterOfferKey.Length == 0 || s.Story.StoryPackPlaySeconds - s.Story.RecentChapterOfferAt > OfferTipWindowSeconds
            || PlayerPacks.Count == 0 || !StepsByHint.ContainsKey(hintId))
        {
            return false;
        }

        foreach (var pack in PlayerPacks)
        {
            foreach (var ch in pack.Chapters)
            {
                if (PresentedChapterKey(ch) == s.Story.RecentChapterOfferKey)
                {
                    return ChapterTeachesHint(ch, hintId);
                }
            }
        }

        return false;
    }

    /// <summary>A once-hint stands in a course offer's moment (due now, or made a moment ago) — the offer tells it.</summary>
    private bool OfferTellsHint(PlayerSession s, string hintId) => OfferAbsorbsHint(s, hintId) || RecentOfferTeaches(s, hintId);

    /// <summary>The optional chapter the next offer pass would offer this player (<see cref="OfferChapters"/> walks the packs
    /// in this order and offers one at a time), or null when none is due.</summary>
    private CompiledChapter? DueChapterOffer(PlayerSession s)
    {
        foreach (var pack in PlayerPacks)
        {
            if (DueOffer(s, pack) is { } due)
            {
                return due;
            }
        }

        return null;
    }

    private static bool ChapterTeachesHint(CompiledChapter ch, string hintId)
        => ch.Steps.Any(step => step.Def.HintRefs is { } refs && refs.Contains(hintId));

    /// <summary>A course was offered: it is remembered as the recent offer (<see cref="RecentOfferTeaches"/>), and the
    /// context tips of the moment it stands in for (tips the course teaches whose situation held on the last tip pass) count
    /// as told — the first telling booked, the cooldown started — while the offer takes the tips' cadence slot, so no tip
    /// follows the offer line a few seconds later. A safety tip (the hull, the air …) is never folded into an offer.</summary>
    private void MarkTipsToldByOffer(PlayerSession s, CompiledChapter ch)
    {
        s.Story.RecentChapterOfferKey = PresentedChapterKey(ch);
        s.Story.RecentChapterOfferAt = s.Story.StoryPackPlaySeconds;
        bool booked = false;
        foreach (var step in ch.Steps)
        {
            foreach (string hint in step.Def.HintRefs ?? new List<string>())
            {
                if (VegaTips.TryGetValue(hint, out var spec) && spec.Priority != VegaTipPriority.Safety && s.Vega.VegaTipLive.Contains(hint))
                {
                    BookHintAsTold(s, hint);
                    booked = true;
                }
            }
        }

        if (booked)
        {
            s.Vega.VegaTipReadyAt = Math.Max(s.Vega.VegaTipReadyAt, _uptime + VegaTipGap);
        }
    }

    /// <summary>Test seam: <see cref="OfferAbsorbsHint"/>.</summary>
    internal bool OfferAbsorbsHintForTest(PlayerSession s, string hintId) => OfferAbsorbsHint(s, hintId);

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

    /// <summary>Test seam: <see cref="TutorialHoldsHint"/>.</summary>
    internal bool TutorialHoldsHintForTest(PlayerSession s, string hintId) => TutorialHoldsHint(s, hintId);

    /// <summary>Test seam: <see cref="ShipAiHintOnce"/>.</summary>
    internal void ShipAiHintOnceForTest(PlayerSession s, string hintId) => ShipAiHintOnce(s, hintId);

    /// <summary>Test seam: the per-player answer of a <c>world:</c> predicate.</summary>
    internal bool WorldPredicateForTest(PlayerSession s, string predicate) => WorldPredicates.TryGetValue(predicate, out var test) && test(s);

    /// <summary>Test seam: the 1 Hz state checks of the tutorial (walked back, a data fragment carried).</summary>
    internal void TutorialStateEventsForTest(PlayerSession s) => TutorialStateEvents(s);

    /// <summary>Test seam: "I know the game" for a player — the tutorial packs' mandatory chapters skipped, as the Settings
    /// button does (for tests that need a player past the basic training).</summary>
    public void SkipTutorialForTest(PlayerSession s) => SkipOrRestartTutorialPacks(s, restart: false);

    // ITutorialHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    Galaxy ITutorialHost.Galaxy => _galaxy;

    bool ITutorialHost.HasGameEventListener(string key) => HasGameEventListener(key);

    bool ITutorialHost.StepWaitsFor(PlayerSession s, string key) => StepWaitsFor(s, key);

    void ITutorialHost.SpeakStepMidLines(PlayerSession s, string eventKey) => SpeakStepMidLines(s, eventKey);

    void ITutorialHost.TutorialBaseStateEvents(PlayerSession session) => TutorialBaseStateEvents(session);

    bool ITutorialHost.InStation(string playerId) => InStation(playerId);
}
