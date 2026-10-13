// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>The help-escalation clock of one active story-pack step for one session (#2466). RAM only.</summary>
public sealed class StepEscalationState
{
    /// <summary><see cref="PlayerSession.StoryPackPlaySeconds"/> when the step activated or last made progress.</summary>
    public double Since { get; set; }

    /// <summary>The hint line went out.</summary>
    public bool HintSent { get; set; }

    /// <summary>The marker went up (#2490: only then — a provider that found nothing retries).</summary>
    public bool MarkerSent { get; set; }

    /// <summary>#2490: where the last marker probe found nothing (<see cref="GameServer"/>'s location key), or null before
    /// the first miss. A new location retries at once.</summary>
    public string? MarkerMissAt { get; set; }

    /// <summary>#2490: <see cref="PlayerSession.StoryPackPlaySeconds"/> of the last miss — on the same spot the probe retries
    /// after <c>MarkerRetrySeconds</c>.</summary>
    public double MarkerMissTime { get; set; }

    /// <summary>The skip offer went out.</summary>
    public bool OfferSent { get; set; }

    /// <summary>An open CORE step stopped blocking its chapter (an "open extra" that still counts silently).</summary>
    public bool Deferred { get; set; }
}

/// <summary>
/// The per-player step runtime of the PLAYER story packs (#2466, synthesis §5.2–§5.7, option C): the tutorial and side
/// stories. A pack is compiled once from its manifest (conditions parsed against the server's <c>world:</c> predicates; a
/// broken pack is logged and left out). Per player the runtime starts packs (world <see cref="TutorialPolicy"/>, the
/// pack's <c>start</c> rule), runs their chapters in order, activates steps (<c>requires</c>, <c>feasibleIf</c> →
/// <c>alternative</c>, <c>alreadyDoneIf</c>), counts game events into <see cref="Shared.State.PlayerState.StepProgress"/>
/// (it registers on the event bus only for the keys of steps that are active somewhere), completes steps and chapters,
/// offers optional chapters, escalates help (hint → a marker ping for this session only → a skip offer, on active play
/// time) and handles skip / replay / opt-out. State is milestone strings (<see cref="StoryPackKeys"/>, §5.6); the running
/// counters are the only typed additions. A client without <c>packs1</c> gets the legacy tutorial mode: steps with a
/// <c>legacyKey</c> speak the old <c>vega.s.&lt;id&gt;.*</c> keys, other tutorial steps do not run for it, and lines it
/// cannot localize arrive as server-composed text. The world story (VEGA) is not run here — its per-player track is
/// step 11. Since #2469 the official tutorial pack (<c>data/stories/tutorial/</c>) is the basic training; its server
/// extras (teaching verbs, UI flags, predicates, hint hold, migration) live in <c>GameServerTutorial.cs</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Shortest gap between two chapter offers to one player (seconds of active play).</summary>
    private const double ChapterOfferGapSeconds = 90.0;

    /// <summary>Half-size of the block probe of the <c>nearest_ore</c> marker provider (one probe per step and session).</summary>
    private const int RevealProbeRadius = 16;

    /// <summary>Spoken when the help marker of a "where" step went up.</summary>
    internal const string StepMarkedLine = "vega.step.marked";

    /// <summary>Spoken when a MUST step has been open for the skip-offer time.</summary>
    internal const string StepOfferSkipLine = "vega.step.offer_skip";

    /// <summary>Spoken when an optional chapter is offered ({0} = the chapter title) — to a client without buttons; one with
    /// them (#2499) hears <see cref="ChapterOfferChoiceLine"/> with [Later] / [Start now].</summary>
    internal const string ChapterOfferLine = "vega.chapter.offer";

    /// <summary>#2500: spoken instead of a silent nothing when the player starts or replays a chapter with nothing left to
    /// practise ({0} = the chapter title) — <see cref="ChapterSpentFor"/>.</summary>
    internal const string ChapterNothingLeftLine = StoryNarratorLines.ChapterNothingLeft;

    // ---------------------------------------------------------------------------------------------
    // The compiled pack model.
    // ---------------------------------------------------------------------------------------------

    /// <summary>A player pack with its conditions parsed and its steps indexed.</summary>
    internal sealed class CompiledPack
    {
        public CompiledPack(StoryDefinition def)
        {
            Def = def;
            Id = def.Id;
            IsTutorial = string.Equals(def.Kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase);
            Narrator = string.IsNullOrEmpty(def.Narrator?.Id) ? StoryNarrator.DefaultId : def.Narrator!.Id;
        }

        public StoryDefinition Def { get; }

        public string Id { get; }

        public bool IsTutorial { get; }

        public string Narrator { get; }

        public List<CompiledChapter> Chapters { get; } = new();

        public Dictionary<string, CompiledChapter> ChaptersById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CompiledStep> StepsById { get; } = new(StringComparer.Ordinal);

        public StoryCondition? StartAfter { get; set; }

        public Dictionary<string, StoryCondition> Gates { get; } = new(StringComparer.Ordinal);

        /// <summary>#2476: a world story's story missions, compiled as a per-player sub-track of that story (each mission an
        /// optional chapter, keyed by the story's id) — run by <c>GameServerStoryMissions.cs</c>, never by the player-pack
        /// walk (<see cref="AdvancePack"/> hands it over).</summary>
        public bool IsStoryMissions { get; set; }

        /// <summary>#2476: the missions in arc order (only for <see cref="IsStoryMissions"/>).</summary>
        public List<CompiledMission> Missions { get; } = new();
    }

    /// <summary>#2476: one story mission of a world story — the chapter that runs its steps, the arc chapter it belongs to and
    /// its feasibility.</summary>
    internal sealed class CompiledMission
    {
        public CompiledMission(CompiledChapter chapter, StoryMission def, string storyChapterId, int storyChapterIndex)
        {
            Chapter = chapter;
            Def = def;
            StoryChapterId = storyChapterId;
            StoryChapterIndex = storyChapterIndex;
        }

        /// <summary>The optional chapter of the missions pack that runs the mission's steps (its id is the mission's).</summary>
        public CompiledChapter Chapter { get; }

        public StoryMission Def { get; }

        public string Id => Chapter.Id;

        /// <summary>The world story's chapter the mission belongs to, and its index in the arc.</summary>
        public string StoryChapterId { get; }

        public int StoryChapterIndex { get; }

        public StoryCondition? FeasibleIf { get; set; }

        public CompiledMission? Alternative { get; set; }

        /// <summary>This mission is another one's alternative: it runs only standing in for it.</summary>
        public bool IsAlternative { get; set; }
    }

    /// <summary>A chapter of a compiled pack.</summary>
    internal sealed class CompiledChapter
    {
        public CompiledChapter(CompiledPack pack, StoryChapter def)
        {
            Pack = pack;
            Def = def;
            Id = def.Id;
            Optional = def.Optional;
            AutoStart = def.AutoStart ?? !def.Optional;
        }

        public CompiledPack Pack { get; }

        public StoryChapter Def { get; }

        public string Id { get; }

        public bool Optional { get; }

        public bool AutoStart { get; }

        public List<CompiledStep> Steps { get; } = new();

        public StoryCondition? Requires { get; set; }

        public StoryCondition? OfferWhen { get; set; }

        public StoryCondition? AutoCompleteIf { get; set; }

        /// <summary>#2482: the chapter exists for the player only while this holds (null = always).</summary>
        public StoryCondition? VisibleIf { get; set; }
    }

    /// <summary>A step of a compiled pack.</summary>
    internal sealed class CompiledStep
    {
        public CompiledStep(CompiledChapter chapter, StoryStep def)
        {
            Chapter = chapter;
            Def = def;
            Id = def.Id;
            ProgressKey = StoryPackKeys.ProgressKey(chapter.Pack.Id, def.Id);
            Kind = (def.Kind ?? StoryStepKinds.Do).Trim().ToLowerInvariant();
            Tier = (def.Tier ?? StoryStepTiers.Must).Trim().ToLowerInvariant();
            var objective = def.Objective ?? new StoryStepObjective();
            if (objective.IsMission)
            {
                Event = GameEventKeys.MissionTurnedIn;
                Args = new[] { objective.Target };
            }
            else if (objective.IsLead)
            {
                // #2476: a lead bridge listens through the lead listener (GameServerStoryLeads) — not on the step listener.
                Event = string.Empty;
                Args = Array.Empty<string>();
                LeadType = objective.Target ?? string.Empty;
            }
            else
            {
                Event = objective.Event ?? string.Empty;
                if (Event.Length == 0 && Kind == StoryStepKinds.Read)
                {
                    Event = GameEventKeys.UiVegaContinue; // a read step completes on "continue"
                }

                Args = (objective.Arg ?? string.Empty).Split('|').Select(a => a.Trim()).Where(a => a.Length > 0).ToArray();
            }

            Required = Math.Max(1, objective.Required);
            LegacyStageId = chapter.Pack.IsTutorial ? StoryLegacy.StageIdOf(def.LegacyKey) : null;
        }

        public CompiledChapter Chapter { get; }

        public CompiledPack Pack => Chapter.Pack;

        public StoryStep Def { get; }

        public string Id { get; }

        public string ProgressKey { get; }

        public string Kind { get; }

        public string Tier { get; }

        public string Event { get; }

        public string[] Args { get; }

        public int Required { get; }

        /// <summary>The legacy stage of a tutorial step (<c>mine</c> …), or null.</summary>
        public string? LegacyStageId { get; }

        public CompiledStep? Alternative { get; set; }

        /// <summary>This step is another step's alternative: it runs only standing in for that step.</summary>
        public bool IsAlternative { get; set; }

        public StoryCondition? Requires { get; set; }

        public StoryCondition? FeasibleIf { get; set; }

        public StoryCondition? AlreadyDoneIf { get; set; }

        public List<(StoryCondition When, StoryStepVariant Variant)> Variants { get; } = new();

        public bool IsDo => Kind == StoryStepKinds.Do;

        public bool IsRead => Kind == StoryStepKinds.Read;

        public bool IsReact => Kind == StoryStepKinds.React;

        public bool IsMission => Def.Objective?.IsMission == true;

        /// <summary>#2476: the bridged lead word of a lead bridge (<c>unique_site:observatory</c> …), else empty.</summary>
        public string LeadType { get; } = string.Empty;

        /// <summary>#2476: a lead bridge — completed the way its lead is followed.</summary>
        public bool IsLeadBridge => LeadType.Length > 0;

        public string LeadKind => StoryLeadTypes.KindOf(LeadType);

        public string LeadArg => StoryLeadTypes.ArgOf(LeadType);
    }

    private List<CompiledPack>? _playerPacks;
    private readonly Dictionary<string, CompiledStep> _stepByProgressKey = new(StringComparer.Ordinal);

    /// <summary>#2476: the story missions of every installed world story that has any, by story id (compiled with the player
    /// packs; only the active world story's run — <see cref="StoryPackEnabledFor"/>).</summary>
    private readonly Dictionary<string, CompiledPack> _storyMissionPacks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The installed player packs, compiled once (highest priority first, then by id).</summary>
    internal IReadOnlyList<CompiledPack> PlayerPacks => _playerPacks ??= CompilePlayerPacks();

    private List<CompiledPack> CompilePlayerPacks()
    {
        var result = new List<CompiledPack>();
        _stepByProgressKey.Clear();
        _storyMissionPacks.Clear();
        foreach (var def in _content.Stories.Values)
        {
            if (StoryRegistry.IsReservedPackId(def.Id) || def.Chapters is not { Count: > 0 })
            {
                continue;
            }

            if (!string.Equals(StoryRegistry.EffectiveScope(def), StoryRegistry.ScopePlayer, StringComparison.Ordinal))
            {
                if (def.Chapters.Any(c => c.Missions is { Count: > 0 }))
                {
                    try
                    {
                        _storyMissionPacks[def.Id] = CompileStoryMissions(def); // #2476
                    }
                    catch (FormatException ex)
                    {
                        _log.Warn($"The story missions of '{def.Id}' are not run: {ex.Message}");
                    }
                }

                continue;
            }

            try
            {
                result.Add(CompilePack(def));
            }
            catch (FormatException ex)
            {
                _log.Warn($"Story pack '{def.Id}' is not run: {ex.Message}");
            }
        }

        result.Sort((a, b) => a.Def.Priority != b.Def.Priority
            ? b.Def.Priority.CompareTo(a.Def.Priority)
            : string.CompareOrdinal(a.Id, b.Id));
        foreach (var pack in result.Concat(_storyMissionPacks.Values))
        {
            foreach (var step in pack.StepsById.Values)
            {
                _stepByProgressKey[step.ProgressKey] = step;
            }
        }

        return result;
    }

    /// <summary>Parses one pack's conditions and indexes its chapters and steps. Throws <see cref="FormatException"/> on a
    /// structural or condition error (the validator reports the same things to authors).</summary>
    private CompiledPack CompilePack(StoryDefinition def)
    {
        var predicates = WorldPredicateNames;
        var pack = new CompiledPack(def)
        {
            StartAfter = StoryConditionParser.ParseList(def.Start?.After, predicates),
        };
        foreach (var gate in def.Gates ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(gate.Value))
            {
                pack.Gates[gate.Key] = StoryConditionParser.Parse(gate.Value, predicates);
            }
        }

        foreach (var chapterDef in def.Chapters)
        {
            if (string.IsNullOrEmpty(chapterDef.Id) || pack.ChaptersById.ContainsKey(chapterDef.Id))
            {
                throw new FormatException($"chapter id '{chapterDef.Id}' is empty or used twice");
            }

            var chapter = new CompiledChapter(pack, chapterDef)
            {
                Requires = StoryConditionParser.ParseList(chapterDef.Requires, predicates),
                OfferWhen = StoryConditionParser.ParseList(chapterDef.OfferWhen, predicates),
                AutoCompleteIf = StoryConditionParser.ParseList(chapterDef.AutoCompleteIf, predicates),
                VisibleIf = StoryConditionParser.ParseList(chapterDef.VisibleIf, predicates),
            };
            pack.Chapters.Add(chapter);
            pack.ChaptersById[chapter.Id] = chapter;
            CompileSteps(pack, chapter, predicates);
        }

        LinkStepAlternatives(pack);
        return pack;
    }

    /// <summary>Compiles a chapter's steps (its <see cref="StoryChapter.Steps"/>) into the pack. Throws <see cref="FormatException"/>
    /// on a structural or condition error.</summary>
    private static void CompileSteps(CompiledPack pack, CompiledChapter chapter, IReadOnlyCollection<string> predicates)
    {
        foreach (var stepDef in chapter.Def.Steps)
        {
            if (string.IsNullOrEmpty(stepDef.Id) || pack.StepsById.ContainsKey(stepDef.Id))
            {
                throw new FormatException($"step id '{stepDef.Id}' is empty or used twice");
            }

            var step = new CompiledStep(chapter, stepDef)
            {
                Requires = StoryConditionParser.ParseList(stepDef.Requires, predicates),
                FeasibleIf = StoryConditionParser.ParseList(stepDef.FeasibleIf, predicates),
                AlreadyDoneIf = StoryConditionParser.ParseList(stepDef.AlreadyDoneIf, predicates),
            };
            if (!StoryStepKinds.All.Contains(step.Kind) || !StoryStepTiers.All.Contains(step.Tier))
            {
                throw new FormatException($"step '{step.Id}' has an unknown kind '{step.Kind}' or tier '{step.Tier}'");
            }

            if (GameEventKeys.IsUiEvent(step.Event) && step.Event.EndsWith("*", StringComparison.Ordinal))
            {
                throw new FormatException($"step '{step.Id}' listens to a 'ui:' pattern — a teaching flag must be named exactly");
            }

            foreach (var variant in stepDef.Variants)
            {
                step.Variants.Add((StoryConditionParser.Parse(variant.When ?? string.Empty, predicates), variant));
            }

            chapter.Steps.Add(step);
            pack.StepsById[step.Id] = step;
        }
    }

    /// <summary>Resolves every step's <c>alternative</c> (another step of the same chapter).</summary>
    private static void LinkStepAlternatives(CompiledPack pack)
    {
        foreach (var chapter in pack.Chapters)
        {
            foreach (var step in chapter.Steps)
            {
                string alt = step.Def.Alternative ?? string.Empty;
                if (alt.Length == 0)
                {
                    continue;
                }

                if (!pack.StepsById.TryGetValue(alt, out var alternative) || alternative.Chapter != chapter || alternative == step)
                {
                    throw new FormatException($"step '{step.Id}' names the alternative '{alt}', which is not another step of its chapter");
                }

                step.Alternative = alternative;
                alternative.IsAlternative = true;
            }
        }
    }

    /// <summary>The compiled player pack with this id, if installed and runnable.</summary>
    internal bool TryGetPlayerPack(string? packId, out CompiledPack pack)
    {
        foreach (var p in PlayerPacks)
        {
            if (string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase))
            {
                pack = p;
                return true;
            }
        }

        pack = null!;
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Per-player state readers.
    // ---------------------------------------------------------------------------------------------

    private static HashSet<string> MilestonesOf(PlayerSession s) => s.State.Milestones;

    private static bool PackStarted(PlayerSession s, CompiledPack pack) => MilestonesOf(s).Contains(StoryPackKeys.Started(pack.Id));

    private static bool PackOptedOut(PlayerSession s, CompiledPack pack) => MilestonesOf(s).Contains(StoryPackKeys.OptOut(pack.Id));

    private static bool PackDone(PlayerSession s, CompiledPack pack) => MilestonesOf(s).Contains(StoryPackKeys.Done(pack.Id));

    /// <summary>The pack runs for this player right now: enabled (policy, opt-out), started.</summary>
    private bool PackRunsFor(PlayerSession s, CompiledPack pack)
        => PackStarted(s, pack) && StoryPackEnabledFor(s, pack.Id);

    /// <summary>#2498: the world's "Short courses only" rule applies to this pack — a tutorial on a
    /// <see cref="TutorialPolicy.Courses"/> world starts with its mandatory part skipped silently.</summary>
    private bool SkipsBasicTraining(CompiledPack pack) => pack.IsTutorial && Rules.TutorialPolicy == TutorialPolicy.Courses;

    private static bool StepDoneOrSkipped(PlayerSession s, CompiledStep step)
        => MilestonesOf(s).Contains(StoryPackKeys.StepDone(step.Pack.Id, step.Id))
           || MilestonesOf(s).Contains(StoryPackKeys.StepSkipped(step.Pack.Id, step.Id));

    private static bool ChapterReplaying(PlayerSession s, CompiledChapter ch)
        => MilestonesOf(s).Contains(StoryPackKeys.ChapterReplay(ch.Pack.Id, ch.Id));

    private static bool ChapterDoneOrSkipped(PlayerSession s, CompiledChapter ch)
        => MilestonesOf(s).Contains(StoryPackKeys.ChapterDone(ch.Pack.Id, ch.Id))
           || MilestonesOf(s).Contains(StoryPackKeys.ChapterSkipped(ch.Pack.Id, ch.Id));

    /// <summary>Whether a step is through for the chapter's sequence: done or skipped — during a replay, completed again in
    /// this replay (the done keys stay so conditions keep holding, but the replay walks every step anew).</summary>
    private static bool StepComplete(PlayerSession s, CompiledStep step)
        => ChapterReplaying(s, step.Chapter)
            ? MilestonesOf(s).Contains(StoryPackKeys.StepReplay(step.Pack.Id, step.Id))
            : StepDoneOrSkipped(s, step);

    /// <summary>A chapter is through for the chapter order: done or skipped and not being replayed.</summary>
    private static bool ChapterFinished(PlayerSession s, CompiledChapter ch)
        => ChapterDoneOrSkipped(s, ch) && !ChapterReplaying(s, ch);

    private static bool StepActive(PlayerSession s, CompiledStep step) => s.State.StepProgress.ContainsKey(step.ProgressKey);

    /// <summary>A client without <c>packs1</c> follows only a tutorial's steps with a legacy stage (synthesis §5.7) — and no
    /// story mission (#2476: it has neither the Journal nor the chip text for one; it keeps the story chip it knows).</summary>
    private static bool StepRunsForClient(PlayerSession s, CompiledStep step)
        => s.SupportsPacks || (!step.Pack.IsTutorial && !step.Pack.IsStoryMissions) || step.LegacyStageId is not null;

    /// <summary>Whether a do step holds up its chapter: every do step of an optional chapter; in a non-optional chapter a
    /// MUST step, and a CORE step until its hint time has passed (then it is an open extra).</summary>
    private static bool StepBlocks(PlayerSession s, CompiledStep step)
    {
        if (!step.IsDo)
        {
            return false;
        }

        if (step.Chapter.Optional || step.IsAlternative)
        {
            return true;
        }

        return step.Tier switch
        {
            StoryStepTiers.Must => true,
            StoryStepTiers.Core => !(s.StepEscalation.TryGetValue(step.ProgressKey, out var esc) && esc.Deferred),
            _ => false,
        };
    }

    /// <summary>Whether a do step counts for its chapter being done.</summary>
    private static bool StepCountsForChapter(CompiledStep step)
        => step.IsDo && !step.IsAlternative && (step.Chapter.Optional || step.Tier == StoryStepTiers.Must);

    /// <summary>A legacy client (tutorial pack) may pass a chapter once its legacy MUST steps are done — the old nine
    /// stages then run across the new chapters.</summary>
    private static bool LegacyChapterPassable(PlayerSession s, CompiledChapter ch)
        => ch.Steps.Where(st => st.LegacyStageId is not null && StepCountsForChapter(st)).All(st => StepComplete(s, st));

    /// <summary>Whether a non-optional chapter is through for the readers that ask "does the basic training still run?" —
    /// the story start, the veteran path, the chip's tutorial flag and the banter gate (#2487): finished, or — for a client
    /// without <c>packs1</c> in a tutorial — passable on its legacy MUST steps. Such a client can never finish the new
    /// lessons, so without this the VEGA story never started for it. Nothing is written: the same player on a current
    /// client later still gets the new lessons.</summary>
    private static bool MandatoryChapterThrough(PlayerSession s, CompiledChapter ch)
        => ChapterFinished(s, ch) || (!s.SupportsPacks && ch.Pack.IsTutorial && LegacyChapterPassable(s, ch));

    /// <summary>Every non-optional chapter of the pack is through (<see cref="MandatoryChapterThrough"/>).</summary>
    private static bool MandatoryChaptersThrough(PlayerSession s, CompiledPack pack)
        => pack.Chapters.Where(c => !c.Optional).All(c => MandatoryChapterThrough(s, c));

    /// <summary>Whether a chapter runs for this player now.</summary>
    private bool ChapterRuns(PlayerSession s, CompiledChapter ch)
    {
        bool replay = ChapterReplaying(s, ch);
        if (!replay && ChapterDoneOrSkipped(s, ch))
        {
            return false;
        }

        bool legacyClient = !s.SupportsPacks && ch.Pack.IsTutorial;
        bool active = MilestonesOf(s).Contains(StoryPackKeys.ChapterActive(ch.Pack.Id, ch.Id));
        if (ch.Optional)
        {
            return !legacyClient && (replay || active);
        }

        if (!replay && !ch.AutoStart && !active)
        {
            return false;
        }

        // The chapter order: the previous non-optional chapter must be through (a legacy client may pass on its stages).
        int index = ch.Pack.Chapters.IndexOf(ch);
        for (int i = index - 1; i >= 0; i--)
        {
            var previous = ch.Pack.Chapters[i];
            if (previous.Optional)
            {
                continue;
            }

            if (!ChapterFinished(s, previous) && !(legacyClient && LegacyChapterPassable(s, previous)))
            {
                return false;
            }

            break;
        }

        return replay || EvaluateStoryCondition(ch.Requires, s); // a replay needs no second admission
    }

    /// <summary>The first step of a variant list whose condition holds, for one text slot (empty fields fall back).</summary>
    private string StepText(PlayerSession s, CompiledStep step, Func<StoryStepVariant, string> variantField, Func<StoryStepExplain, string> explainField)
    {
        foreach (var (when, variant) in step.Variants)
        {
            if (EvaluateStoryCondition(when, s))
            {
                string v = variantField(variant) ?? string.Empty;
                if (v.Length > 0)
                {
                    return v;
                }

                break;
            }
        }

        return explainField(step.Def.Explain ?? new StoryStepExplain()) ?? string.Empty;
    }

    private string StepStartKey(PlayerSession s, CompiledStep step) => StepText(s, step, v => v.StartKey, e => e.StartKey);

    private string StepChipKey(PlayerSession s, CompiledStep step) => StepText(s, step, v => v.ChipKey, e => e.ChipKey);

    private string StepHintKey(PlayerSession s, CompiledStep step) => StepText(s, step, v => v.HintKey, e => e.HintKey);

    private string StepDoneKey(PlayerSession s, CompiledStep step) => StepText(s, step, v => v.DoneKey, e => e.DoneKey);

    // ---------------------------------------------------------------------------------------------
    // Sending lines (packs1 and the legacy mode).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Sends one pack line. A client with <c>packs1</c> gets the key (and the pack + speaker). An older client
    /// gets <paramref name="legacyKey"/> when the line has an old equivalent (a tutorial step with a legacy stage — no
    /// <c>Text</c>, it localizes and sets its own keys), else the line composed on the server in its language with
    /// device-neutral key words and an empty <c>LineKey</c> (synthesis §5.7: its <c>Text</c> path fills neither tokens
    /// nor slots). <paramref name="waypointId"/> rides along as the line's space waypoint (#2471, the station marker).
    /// <paramref name="choices"/> are the line's buttons (#2499, <see cref="ShipAiLine.Choices"/>) — only a client that draws
    /// them (<c>choices1</c>) gets them.</summary>
    private void SendPackLine(PlayerSession s, CompiledPack pack, string key, string arg = "", string? legacyKey = null, byte kind = 0, string waypointId = "", string[]? choices = null)
    {
        if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(legacyKey))
        {
            return;
        }

        var line = new ShipAiLine { Kind = kind, PackId = pack.Id, Speaker = pack.Narrator, SpaceWaypointId = waypointId ?? string.Empty };
        if (s.SupportsPacks)
        {
            line.LineKey = key;
            line.LineArg = arg ?? string.Empty;
            if (choices is { Length: > 0 } && s.SupportsChoices)
            {
                line.Choices = choices;
            }
        }
        else if (!string.IsNullOrEmpty(legacyKey))
        {
            line.LineKey = legacyKey!;
        }
        else
        {
            line.Text = ComposeLegacyText(s.Locale, key, arg);
        }

        SendVegaWithObjective(s, line);
    }

    /// <summary>A line in the player's language for a client that cannot localize its key: tokens become device-neutral
    /// bracket words (<c>ui.vega.act.&lt;Action&gt;</c>), slots are filled from <paramref name="arg"/>.</summary>
    internal string ComposeLegacyText(string localeCode, string key, string? arg)
    {
        var loc = LocalizerFor(localeCode);
        return LineText.ComposeNeutral(loc.Get(key), arg, k => loc.Has(k) ? loc.Get(k) : null);
    }

    /// <summary>The (cached) content localizer of a locale code.</summary>
    private Shared.Localization.Localizer LocalizerFor(string localeCode)
    {
        GameLocaleExtensions.TryParse(localeCode, out var locale); // English on an unknown code
        if (!_localizers.TryGetValue(locale, out var loc))
        {
            loc = _content.CreateLocalizer(locale);
            _localizers[locale] = loc;
        }

        return loc;
    }

    /// <summary>Speaks a step's start line (its variant for the world's rules); the hints and tips the step absorbs count as
    /// told from now on (#2469, G12).</summary>
    private void SpeakStepStart(PlayerSession s, CompiledStep step)
    {
        SendPackLine(s, step.Pack, StepStartKey(s, step), legacyKey: step.LegacyStageId is { } stage ? StoryLegacy.StartLine(stage) : null);
        MarkHintsToldByStep(s, step);
    }

    // ---------------------------------------------------------------------------------------------
    // The walk: start packs, run chapters, activate steps.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Brings every player pack of <paramref name="s"/> up to date: starts what may start, activates what may
    /// activate, completes what is through — and then the world story's track (#2470: a graduation, a skipped training or
    /// the tutorial switched off starts it at once, so the graduation chains into chapter 1). Cheap (milestone lookups); safe
    /// to call on every event and tick.</summary>
    private void AdvancePlayer(PlayerSession s, bool offers = false)
    {
        if (!s.Joined || s.Spectating || PlayerPacks.Count == 0)
        {
            return;
        }

        foreach (var pack in PlayerPacks)
        {
            AdvancePack(s, pack);
            if (offers)
            {
                OfferChapters(s, pack);
            }
        }

        SyncStepListeners();
        TryStartStoryTrack(s);
    }

    private void AdvancePack(PlayerSession s, CompiledPack pack)
    {
        if (pack.IsStoryMissions)
        {
            AdvanceStoryMissions(s); // #2476: a world story's missions have their own rules (no start, no migration, no "done")
            return;
        }

        if (!StoryPackEnabledFor(s, pack.Id))
        {
            return; // switched off for this player or this world — every key stays, nothing runs
        }

        MigratePackIfNeeded(s, pack);
        if (!PackStarted(s, pack))
        {
            if (!EvaluateStoryCondition(pack.StartAfter, s))
            {
                return;
            }

            if (SkipsBasicTraining(pack))
            {
                // #2498 "Short courses only": the basic training is skipped silently for a player who never started it —
                // no welcome card, no line, no achievement (an older client hears no old intro or send-off either). The
                // story track starts right after (AdvancePlayer), the courses come at their moment. A player already in
                // the training when the rule changes keeps it: only a pack that has not started takes this path.
                StartPack(s, pack, playIntro: false, legacyIntro: false);
                SkipMandatory(s, pack, legacySendOff: false);
                _repo.SavePlayer(s.State);
            }
            else
            {
                // #2499: a newcomer whose client draws buttons is asked once whether they want the training — the walk below
                // asks it right before the first lesson's line (after part 1's welcome card), AskSkipBeforeFirstStep.
                s.AskSkipDuePack = AsksBeforeTraining(s, pack) ? pack.Id : string.Empty;
                StartPack(s, pack);
            }
        }

        for (int pass = 0; pass < 64; pass++) // bounded: each pass that changes something completes a step or a chapter
        {
            bool changed = false;
            foreach (var chapter in pack.Chapters)
            {
                if (ChapterRuns(s, chapter))
                {
                    changed |= WalkChapter(s, chapter);
                }
            }

            changed |= CheckPackDone(s, pack);
            if (!changed)
            {
                break;
            }
        }

        s.AskSkipDuePack = string.Empty; // #2499: asked with the first lesson, or there was none to ask before
    }

    /// <summary>Starts a pack for a player: the started key, and for a tutorial the legacy intro key (older builds read it
    /// as "the intro was shown") — with the old intro lines for an older client that has not heard them (unless
    /// <paramref name="legacyIntro"/> is off: the silent start of a "Short courses only" world, #2498). The pack's
    /// <c>introCutscene</c> plays unless <paramref name="playIntro"/> is off ("I know the game") or the pack would complete
    /// silently for this player (#2467: a veteran never gets the welcome card).</summary>
    private void StartPack(PlayerSession s, CompiledPack pack, bool playIntro = true, bool legacyIntro = true)
    {
        MigratePackIfNeeded(s, pack); // #2469: the old state is read before anything of this pack is written (skip, restart, start on request)
        var m = MilestonesOf(s);
        if (!m.Add(StoryPackKeys.Started(pack.Id)))
        {
            return;
        }

        MarkJournalDirty(s); // #2471
        if (pack.IsTutorial && m.Add(StoryLegacy.IntroMilestone) && !s.SupportsPacks && legacyIntro)
        {
            foreach (string intro in StoryLegacy.IntroLines)
            {
                SendVegaLine(s, intro, 0);
            }
        }

        _repo.SavePlayer(s.State);
        if (playIntro && !string.IsNullOrEmpty(pack.Def.IntroCutscene) && !PackWouldCompleteSilently(s, pack))
        {
            PlayBoundCutscene(s, pack.Def, pack.Def.IntroCutscene, pack.Id);
        }
    }

    /// <summary>Whether starting the pack would present nothing to this player (#2467): it has mandatory chapters and each
    /// of them is through, completes by its <c>autoCompleteIf</c> evidence, or has every step (but react steps and
    /// stand-ins) done or covered by <c>alreadyDoneIf</c> evidence — a veteran, typically after the migration.</summary>
    private bool PackWouldCompleteSilently(PlayerSession s, CompiledPack pack)
        => pack.Chapters.Any(c => !c.Optional)
           && pack.Chapters.Where(c => !c.Optional).All(c =>
               ChapterFinished(s, c)
               || (c.AutoCompleteIf is not null && EvaluateStoryCondition(c.AutoCompleteIf, s))
               || c.Steps.Where(st => !st.IsReact && !st.IsAlternative).All(st =>
                   StepComplete(s, st) || (st.AlreadyDoneIf is not null && EvaluateStoryCondition(st.AlreadyDoneIf, s))));

    /// <summary>The key of a chapter in <see cref="PlayerSession.PresentedPackChapters"/>.</summary>
    private static string PresentedChapterKey(CompiledChapter ch) => ch.Pack.Id + ":" + ch.Id;

    /// <summary>One pass over a running chapter's steps in order. Returns true when something completed (the caller walks
    /// again: a completion can open the next chapter).</summary>
    private bool WalkChapter(PlayerSession s, CompiledChapter ch)
    {
        bool changed = false;
        bool replay = ChapterReplaying(s, ch);
        if (!replay && ch.AutoCompleteIf is not null && EvaluateStoryCondition(ch.AutoCompleteIf, s))
        {
            CompleteChapter(s, ch, skipped: false, silent: true);
            return true; // veteran evidence: the chapter completes silently
        }

        // React steps are armed from the chapter's start, wherever they stand in the list; they never block.
        foreach (var step in ch.Steps)
        {
            if (step.IsReact && !StepActive(s, step) && !StepComplete(s, step) && StepRunsForClient(s, step)
                && EvaluateStoryCondition(step.Requires, s))
            {
                ArmStep(s, step);
            }
        }

        foreach (var step in ch.Steps)
        {
            if (StepComplete(s, step))
            {
                // A step standing in for this one (it was infeasible) holds the chapter where this step was.
                if (step.Alternative is { } stand && StepActive(s, stand) && !StepComplete(s, stand) && StepBlocks(s, stand))
                {
                    break;
                }

                continue;
            }

            if (step.IsReact || !StepRunsForClient(s, step) || (step.IsAlternative && !StepActive(s, step)))
            {
                continue; // armed above — not for this client — or an alternative nobody needs
            }

            if (!StepActive(s, step))
            {
                if (!EvaluateStoryCondition(step.Requires, s))
                {
                    break; // the chapter waits here
                }

                if (!EvaluateStoryCondition(step.FeasibleIf, s))
                {
                    // No dead ends (G7): skip it silently and let its alternative stand in, if it has one — an alternative
                    // that cannot be done here either is skipped too (#2469: no flight → neither the landing nor its
                    // stand-in on another pad).
                    MarkStepSkipped(s, step);
                    changed = true;
                    if (step.Alternative is { } alt && !StepComplete(s, alt) && StepRunsForClient(s, alt))
                    {
                        if (!EvaluateStoryCondition(alt.FeasibleIf, s))
                        {
                            MarkStepSkipped(s, alt);
                            continue;
                        }

                        if (ActivateStep(s, alt))
                        {
                            changed = true;
                        }

                        if (StepActive(s, alt) && StepBlocks(s, alt))
                        {
                            break;
                        }
                    }

                    continue;
                }

                if (replay ? StepSpentForReplay(s, step) : StepAlreadyDone(s, step))
                {
                    CompleteStep(s, step, silent: true);
                    changed = true;
                    continue;
                }

                if (ActivateStep(s, step))
                {
                    changed = true; // completed at activation (a mission already turned in)
                    continue;
                }
            }

            if (step.IsRead)
            {
                continue; // read and continue — never blocks
            }

            if (StepBlocks(s, step))
            {
                break;
            }
        }

        if (ChapterSatisfied(s, ch))
        {
            CompleteChapter(s, ch, skipped: false);
            changed = true;
        }

        return changed;
    }

    /// <summary>Evidence that a step is known already when it activates: its <c>alreadyDoneIf</c>, and (#2476) for a lead bridge
    /// the lead's own evidence (the blueprint researched, the module fitted, the site landed on before).</summary>
    private bool StepAlreadyDone(PlayerSession s, CompiledStep step)
        => (step.AlreadyDoneIf is not null && EvaluateStoryCondition(step.AlreadyDoneIf, s))
           || (step.IsLeadBridge && LeadBridgeEvidence(s, step));

    /// <summary>#2459: a step a replay cannot ask for again — research when every blueprint it names is known already (the
    /// tech tree never researches a blueprint twice). A replay walks every step anew and ignores <c>alreadyDoneIf</c>, so such
    /// a step held the replayed course (the jump drive's research in Navigator, the Crystal Net blueprint in the Crystal
    /// workshop) until the player skipped it; it now passes silently, like a bridged mission already turned in.</summary>
    private static bool StepSpentForReplay(PlayerSession s, CompiledStep step)
        => step.Event == GameEventKeys.ResearchAny && step.Args.Length > 0
           && step.Args.All(blueprint => s.State.UnlockedBlueprints.Contains(blueprint));

    /// <summary>Whether a mission bridge's mission is turned in already (a bridged step then completes at once, silently).</summary>
    private static bool BridgedMissionTurnedIn(PlayerSession s, CompiledStep step)
        => step.IsMission && step.Args.Length > 0
           && s.State.Missions.Any(m => m.MissionId == step.Args[0] && m.Status == Shared.Missions.MissionStatus.TurnedIn);

    /// <summary>#2481: whether every counting step of a chapter is already through for this player — done, or covered by its
    /// evidence (<c>alreadyDoneIf</c>, a bridged mission turned in, a lead bridge's evidence). Such a deep dive would complete
    /// silently at its start, so it is never offered (the Crystal workshop course to a player who did the whole chain on the
    /// radio before — "never offered twice").</summary>
    private bool ChapterThroughByEvidence(PlayerSession s, CompiledChapter ch)
        => ch.Steps.Where(StepCountsForChapter).All(st => StepComplete(s, st) || StepAlreadyDone(s, st) || BridgedMissionTurnedIn(s, st));

    /// <summary>#2500: whether a chapter has nothing left to practise for this player — a Replay or a Start would end at once,
    /// silently. A finished chapter (done or skipped, not being replayed) whose every counting step a replay passes at once
    /// (<see cref="StepSpentForReplay"/>: research of blueprints known already; <see cref="BridgedMissionTurnedIn"/>: the Crystal
    /// workshop's jobs handed in); a course not started yet that is through by evidence (<see cref="ChapterThroughByEvidence"/>
    /// — never offered for that reason, #2481). A running or replaying chapter, a mandatory one still ahead and a chapter
    /// without counting steps never are. The Journal hides Replay / Start for it (<see cref="NetStoryChapter.NothingLeft"/>).</summary>
    private bool ChapterSpentFor(PlayerSession s, CompiledChapter ch)
    {
        var counting = ch.Steps.Where(StepCountsForChapter).ToList();
        if (counting.Count == 0 || ChapterReplaying(s, ch))
        {
            return false;
        }

        if (ChapterDoneOrSkipped(s, ch))
        {
            return counting.All(st => StepSpentForReplay(s, st) || BridgedMissionTurnedIn(s, st));
        }

        return ch.Optional && !MilestonesOf(s).Contains(StoryPackKeys.ChapterActive(ch.Pack.Id, ch.Id)) && ChapterThroughByEvidence(s, ch);
    }

    /// <summary>#2482: whether an optional chapter exists for this player — its <c>visibleIf</c> (a rule-gated deep dive: combat
    /// switched on, a world for more than one player). One that does not is never offered, suggested or started.</summary>
    private bool ChapterVisible(PlayerSession s, CompiledChapter ch)
        => ch.VisibleIf is null || EvaluateStoryCondition(ch.VisibleIf, s);

    /// <summary>#2481 (B55): whether one of this player's running story-pack steps bridges onto <paramref name="missionId"/> —
    /// an active mission-bridge step of a pack that runs for them. Its start line then is the chain's follow-up, so VEGA's own
    /// "the next part waits" line stays quiet (<c>VegaChainFollowUp</c>).</summary>
    private bool MissionBridgedByRunningStep(PlayerSession s, string missionId)
    {
        if (PlayerPacks.Count == 0 || s.State.StepProgress.Count == 0)
        {
            return false;
        }

        foreach (string progressKey in s.State.StepProgress.Keys)
        {
            if (_stepByProgressKey.TryGetValue(progressKey, out var step) && step.IsMission && step.Args.Length > 0
                && string.Equals(step.Args[0], missionId, StringComparison.Ordinal)
                && PackRunsFor(s, step.Pack) && StepRunsForClient(s, step))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A chapter is through when every step that counts is through (MUST do steps of a non-optional chapter,
    /// every do step of an optional one) and nothing still holds it up — an open CORE step that has not had its hint time
    /// yet, or an alternative standing in for an infeasible step. A CORE step after the last MUST step is therefore still
    /// presented before the chapter closes.</summary>
    private static bool ChapterSatisfied(PlayerSession s, CompiledChapter ch)
        => ch.Steps.Where(StepCountsForChapter).All(st => StepComplete(s, st))
           && !ch.Steps.Any(st => st.IsDo && StepActive(s, st) && !StepComplete(s, st) && StepBlocks(s, st));

    /// <summary>Activates a step: its counter starts at 0, its help clock starts, its start line is spoken. A mission bridge
    /// whose mission is already turned in completes at once (returns true).</summary>
    private bool ActivateStep(PlayerSession s, CompiledStep step)
    {
        if (!s.State.StepProgress.ContainsKey(step.ProgressKey))
        {
            s.State.StepProgress[step.ProgressKey] = 0;
        }

        s.StepEscalation[step.ProgressKey] = new StepEscalationState { Since = s.StoryPackPlaySeconds };
        _stepListenersDirty = true;
        MarkJournalDirty(s); // #2471: a new task in the Journal
        if (BridgedMissionTurnedIn(s, step))
        {
            CompleteStep(s, step, silent: true);
            return true;
        }

        // #2467: the chapter's opener when its first step is presented (not in a replay), then the step's own scene — both
        // before the start line, so a client that plays them knows which lines they show. #2471: a step the player meets for
        // the first time inside a replay (a running chapter restarted from the Journal) still gets its own scene.
        bool replay = ChapterReplaying(s, step.Chapter);
        if (s.PresentedPackChapters.Add(PresentedChapterKey(step.Chapter)) && !replay)
        {
            PlayBoundCutscene(s, step.Pack.Def, step.Chapter.Def.OpenCutscene, step.Chapter.Id);
        }

        if (!replay || !StepDoneOrSkipped(s, step))
        {
            PlayBoundCutscene(s, step.Pack.Def, step.Def.OnStart?.Cutscene, step.Id);
        }

        AskSkipBeforeFirstStep(s, step); // #2499: "Want me to show you the basics?" — once, before the first lesson's line
        SpeakStepStart(s, step);
        _repo.SavePlayer(s.State);
        return false;
    }

    /// <summary>Arms a react step: it listens, says nothing yet.</summary>
    private void ArmStep(PlayerSession s, CompiledStep step)
    {
        if (!s.State.StepProgress.ContainsKey(step.ProgressKey))
        {
            s.State.StepProgress[step.ProgressKey] = 0;
            _stepListenersDirty = true;
            _repo.SavePlayer(s.State);
        }
    }

    /// <summary>Stamps a step done (a replay: done again in this replay) — the legacy stage of a tutorial step is written
    /// next to it for older builds — removes its counter and, unless <paramref name="silent"/>, speaks its done line.
    /// The chapter's earlier read steps that are still waiting for "continue" complete with it. A step done for the first
    /// time inside a replay (#2471: a running chapter restarted from the Journal, an open extra finished in a replay) is
    /// stamped done as well — the replay key alone would vanish with the replay.</summary>
    private void CompleteStep(PlayerSession s, CompiledStep step, bool silent)
    {
        var m = MilestonesOf(s);
        bool replay = ChapterReplaying(s, step.Chapter);
        bool first = !StepDoneOrSkipped(s, step);
        m.Add(replay ? StoryPackKeys.StepReplay(step.Pack.Id, step.Id) : StoryPackKeys.StepDone(step.Pack.Id, step.Id));
        if (replay && first)
        {
            m.Add(StoryPackKeys.StepDone(step.Pack.Id, step.Id));
        }

        if ((!replay || first) && step.Pack.IsTutorial && !string.IsNullOrEmpty(step.Def.LegacyKey))
        {
            m.Add(step.Def.LegacyKey); // double-write (synthesis §5.3): older builds keep their onboarding state
        }

        s.State.StepProgress.Remove(step.ProgressKey);
        s.StepEscalation.Remove(step.ProgressKey);
        _stepListenersDirty = true;
        MarkJournalDirty(s);

        if (step.IsDo)
        {
            foreach (var read in step.Chapter.Steps.TakeWhile(st => st != step).Where(st => st.IsRead && StepActive(s, st)))
            {
                CompleteStep(s, read, silent: true);
            }
        }

        if (!silent)
        {
            s.PresentedPackChapters.Add(PresentedChapterKey(step.Chapter)); // the player did it: the chapter was shown
            if (!replay || first)
            {
                PlayBoundCutscene(s, step.Pack.Def, step.Def.OnComplete?.Cutscene, step.Id); // #2467: before the lines
            }

            if (step.IsReact)
            {
                SpeakStepStart(s, step); // a react step's explanation is shown when its situation happens
            }

            SendPackLine(s, step.Pack, StepDoneKey(s, step),
                legacyKey: step.LegacyStageId is { } stage ? StoryLegacy.DoneLineOf(stage) : null);
        }

        _repo.SavePlayer(s.State);
    }

    /// <summary>Stamps a step skipped (a replay: through in this replay) with the legacy double-write, removes its counter.</summary>
    private void MarkStepSkipped(PlayerSession s, CompiledStep step)
    {
        var m = MilestonesOf(s);
        bool replay = ChapterReplaying(s, step.Chapter);
        bool first = !StepDoneOrSkipped(s, step);
        m.Add(replay ? StoryPackKeys.StepReplay(step.Pack.Id, step.Id) : StoryPackKeys.StepSkipped(step.Pack.Id, step.Id));
        if (replay && first)
        {
            m.Add(StoryPackKeys.StepSkipped(step.Pack.Id, step.Id)); // #2471: see CompleteStep
        }

        if ((!replay || first) && step.Pack.IsTutorial && !string.IsNullOrEmpty(step.Def.LegacyKey))
        {
            m.Add(step.Def.LegacyKey);
        }

        if (s.State.StepProgress.Remove(step.ProgressKey))
        {
            _stepListenersDirty = true;
        }

        s.StepEscalation.Remove(step.ProgressKey);
        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
    }

    /// <summary>Completes (or skips) a chapter. A replay ends: its replay keys go, the chapter's done key stayed. Open
    /// extras (CORE/OPT do steps) and armed react steps keep listening; waiting read steps complete. A chapter completed by
    /// play (not skipped, not <paramref name="silent"/> evidence) that finishes the pack's mandatory part graduates the
    /// player (#2469, <see cref="Graduate"/>) — at the end of a replay only when it is the last mandatory chapter (a full
    /// restart), so replaying one early chapter does not. A chapter that was not through before its replay (#2471: a running
    /// chapter restarted from the Journal) completes for real at the end of it.</summary>
    private void CompleteChapter(PlayerSession s, CompiledChapter ch, bool skipped, bool silent = false)
    {
        var m = MilestonesOf(s);
        string pack = ch.Pack.Id;
        bool replay = ChapterReplaying(s, ch);
        bool first = !ChapterDoneOrSkipped(s, ch);
        if (replay)
        {
            m.Remove(StoryPackKeys.ChapterReplay(pack, ch.Id));
            foreach (var step in ch.Steps)
            {
                m.Remove(StoryPackKeys.StepReplay(pack, step.Id));
            }
        }

        if (!replay || first)
        {
            m.Add(skipped ? StoryPackKeys.ChapterSkipped(pack, ch.Id) : StoryPackKeys.ChapterDone(pack, ch.Id));
        }
        else if (!skipped && !silent && m.Remove(StoryPackKeys.ChapterSkipped(pack, ch.Id)))
        {
            // #2498: a skipped chapter replayed to its end by play ("Start basic training" after a skip) is learnt now — done,
            // not skipped, so the Journal reads it as finished and its badge can be earned. A skip of the replay keeps it skipped.
            m.Add(StoryPackKeys.ChapterDone(pack, ch.Id));
        }

        MarkJournalDirty(s);

        m.Remove(StoryPackKeys.ChapterActive(pack, ch.Id));
        foreach (var step in ch.Steps)
        {
            if (step.IsRead && StepActive(s, step))
            {
                CompleteStep(s, step, silent: true);
            }
            else if (step.IsAlternative && StepActive(s, step))
            {
                s.State.StepProgress.Remove(step.ProgressKey); // a stand-in has no business once the chapter is through
                s.StepEscalation.Remove(step.ProgressKey);
            }
        }

        _stepListenersDirty = true;
        _repo.SavePlayer(s.State);

        // #2467: the closer only for a chapter the player was shown — not when skipped, replayed or completed silently.
        if (!skipped && (!replay || first) && s.PresentedPackChapters.Contains(PresentedChapterKey(ch)))
        {
            PlayBoundCutscene(s, ch.Pack.Def, ch.Def.CloseCutscene, ch.Id);
        }

        if (ch.Pack.IsStoryMissions)
        {
            OnStoryMissionClosed(s, ch, skipped); // #2476: a finished mission counts for the chapter bonus
        }

        if (!skipped && !silent && !ch.Optional
            && (!replay || first || ch == ch.Pack.Chapters.LastOrDefault(c => !c.Optional))
            && ch.Pack.Chapters.Where(c => !c.Optional).All(c => ChapterFinished(s, c)))
        {
            Graduate(s, ch.Pack);
        }
    }

    /// <summary>The graduation (#2469, synthesis §3.3 "Absolvent"): the pack's graduation cutscene (#2473, once per player —
    /// a full restart does not show it again), the closing line (a client with <c>packs1</c> —
    /// an older client of a tutorial hears the old <c>vega.done</c> when the pack is stamped done), its achievement
    /// counter, and its suggested deep dives marked offered (the Journal lists them; never more than three, never one that
    /// is done, declined, running or not yet available). No item, no knowledge (maintainer decision: no tutorial rewards).</summary>
    private void Graduate(PlayerSession s, CompiledPack pack)
    {
        var graduation = pack.Def.Graduation ?? new StoryGraduation();
        PlayBoundCutscene(s, pack.Def, graduation.Cutscene, pack.Id); // #2473: the card before the line it shows (once per player)
        if (s.SupportsPacks && !string.IsNullOrEmpty(graduation.LineKey))
        {
            SendPackLine(s, pack, graduation.LineKey);
        }

        if (!string.IsNullOrEmpty(graduation.Counter))
        {
            Advance(s, graduation.Counter);
        }

        SuggestCourses(s, pack);
        MarkJournalDirty(s); // #2471: the badge and the suggested chapters
        _repo.SavePlayer(s.State);
    }

    /// <summary>Marks the graduation's suggested courses (<c>graduation.suggest</c>, at most
    /// <see cref="StoryGraduation.MaxSuggestions"/>) offered — silently: the Journal lists them with Start / Later and its
    /// badge, VEGA says nothing. Never one that is done, skipped, declined, running, not admitted (<c>requires</c>), ruled out
    /// by the world or through by evidence. Run by the graduation and, since #2498, by every skip of the mandatory part
    /// (<paramref name="skip"/>: "Skip all — I know the game", the silent skip of a "Short courses only" world): the flight
    /// courses c3 and c4 have no moment of their own, so a skipper was never offered them. A skip leaves a suggested course
    /// that has its own moment (<c>offerWhen</c>, e.g. the workshop course c1 at the tool-tier wall) to that moment — VEGA's
    /// offer there tells a player who skipped the training more than a silent mark.</summary>
    private void SuggestCourses(PlayerSession s, CompiledPack pack, bool skip = false)
    {
        var graduation = pack.Def.Graduation ?? new StoryGraduation();
        var m = MilestonesOf(s);
        bool marked = false;
        foreach (string id in graduation.Suggest.Take(StoryGraduation.MaxSuggestions))
        {
            if (pack.ChaptersById.TryGetValue(id ?? string.Empty, out var suggested) && suggested.Optional
                && !(skip && suggested.OfferWhen is not null)
                && !ChapterDoneOrSkipped(s, suggested)
                && !m.Contains(StoryPackKeys.ChapterDeclined(pack.Id, suggested.Id))
                && !m.Contains(StoryPackKeys.ChapterActive(pack.Id, suggested.Id))
                && EvaluateStoryCondition(suggested.Requires, s)
                && ChapterVisible(s, suggested) // #2482: never a course the world rules out
                && !ChapterThroughByEvidence(s, suggested) // #2481: never suggest a course there is nothing left of
                && m.Add(StoryPackKeys.ChapterOffered(pack.Id, suggested.Id)))
            {
                // #2479: the suggestions are this moment's offer — the next deep dive VEGA offers by itself (a data cache in
                // reach …) waits the usual gap, so the graduation's cards and lines are not followed by an offer line at once.
                s.LastChapterOfferAt = s.StoryPackPlaySeconds;
                marked = true;
            }
        }

        if (marked)
        {
            MarkJournalDirty(s); // #2471: Start / Later and the badge
        }
    }

    /// <summary>Stamps the pack done once every non-optional chapter is through. A tutorial writes all nine legacy stage
    /// keys (an older build then sees a finished onboarding) and an older client hears the old send-off — unless
    /// <paramref name="legacySendOff"/> is off (the silent skip of a "Short courses only" world, #2498: no training, no
    /// send-off).</summary>
    private bool CheckPackDone(PlayerSession s, CompiledPack pack, bool legacySendOff = true)
    {
        if (PackDone(s, pack) || !pack.Chapters.Where(c => !c.Optional).All(c => ChapterFinished(s, c)))
        {
            return false;
        }

        var m = MilestonesOf(s);
        m.Add(StoryPackKeys.Done(pack.Id));
        MarkJournalDirty(s); // #2471
        if (pack.IsTutorial)
        {
            WriteLegacyGraduation(s);
            if (!s.SupportsPacks && legacySendOff)
            {
                SendVegaLine(s, StoryLegacy.DoneLine, 0);
            }
        }

        _repo.SavePlayer(s.State);
        return true;
    }

    private static void WriteLegacyGraduation(PlayerSession s)
    {
        var m = MilestonesOf(s);
        m.Add(StoryLegacy.IntroMilestone);
        foreach (string stage in StoryLegacy.StageIds)
        {
            m.Add(StoryLegacy.StagePrefix + stage);
        }
    }

    /// <summary>The one-time migration of a pack (<c>story:&lt;pack&gt;:migrated:v1</c>, synthesis §5.3): a tutorial step whose
    /// legacy key the player already holds counts as done; the official tutorial then reads the rest of the old onboarding
    /// state (<see cref="MigrateOfficialTutorial"/>, #2469). Runs before the pack starts, so the legacy intro key it reads
    /// is the player's own, not the one the start writes.</summary>
    private void MigratePackIfNeeded(PlayerSession s, CompiledPack pack)
    {
        var m = MilestonesOf(s);
        string marker = StoryPackKeys.Migrated(pack.Id);
        if (m.Contains(marker))
        {
            return;
        }

        if (pack.IsTutorial)
        {
            foreach (var step in pack.StepsById.Values)
            {
                if (!string.IsNullOrEmpty(step.Def.LegacyKey) && m.Contains(step.Def.LegacyKey))
                {
                    m.Add(StoryPackKeys.StepDone(pack.Id, step.Id));
                }
            }

            if (string.Equals(pack.Id, OfficialTutorialPackId, StringComparison.Ordinal))
            {
                MigrateOfficialTutorial(s, pack);
            }
        }

        m.Add(marker);
        _repo.SavePlayer(s.State);
    }

    // ---------------------------------------------------------------------------------------------
    // Events.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The runtime's handle on the event bus.</summary>
    private sealed class StepEventListener : IGameEventListener
    {
        private readonly GameServer _server;

        public StepEventListener(GameServer server) => _server = server;

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => _server.OnStepGameEvent(actor, key, arg, amount);
    }

    private StepEventListener? _stepListener;
    private readonly HashSet<string> _stepListenerKeys = new(StringComparer.Ordinal);
    private bool _stepListenersDirty;

    /// <summary>Registers the runtime for exactly the event keys of the steps that are active for some joined player, and
    /// drops the rest — so with no active step the bus stays at its one-compare fast path.</summary>
    private void SyncStepListeners()
    {
        if (!_stepListenersDirty)
        {
            return;
        }

        _stepListenersDirty = false;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s.Spectating)
            {
                continue;
            }

            foreach (string key in s.State.StepProgress.Keys)
            {
                if (_stepByProgressKey.TryGetValue(key, out var step) && step.Event.Length > 0)
                {
                    wanted.Add(step.Event);
                }
            }
        }

        _stepListener ??= new StepEventListener(this);
        foreach (string stale in _stepListenerKeys.Where(k => !wanted.Contains(k)).ToList())
        {
            RemoveGameEventListener(stale, _stepListener);
            _stepListenerKeys.Remove(stale);
        }

        foreach (string key in wanted)
        {
            if (_stepListenerKeys.Add(key))
            {
                AddGameEventListener(key, _stepListener);
            }
        }
    }

    /// <summary>The event keys the runtime listens to right now (test seam).</summary>
    internal IReadOnlyCollection<string> StepListenerKeysForTest => _stepListenerKeys;

    /// <summary>One game event for the actor's active steps: counts, completes, advances.</summary>
    private void OnStepGameEvent(PlayerSession actor, string key, string arg, int amount)
    {
        var progress = actor.State.StepProgress;
        if (progress.Count == 0)
        {
            return;
        }

        List<CompiledStep>? hits = null;
        foreach (string progressKey in progress.Keys)
        {
            if (_stepByProgressKey.TryGetValue(progressKey, out var step)
                && step.Event.Length > 0
                && GameEventKeys.Matches(step.Event, key)
                && (step.Args.Length == 0 || Array.IndexOf(step.Args, arg) >= 0)
                && PackRunsFor(actor, step.Pack)
                && StepRunsForClient(actor, step))
            {
                (hits ??= new List<CompiledStep>()).Add(step);
            }
        }

        if (hits is null)
        {
            return;
        }

        bool completed = false;
        bool chipMoved = false;
        foreach (var step in hits)
        {
            if (!progress.TryGetValue(step.ProgressKey, out int count))
            {
                continue; // completed by an earlier hit of this event (a read step closed by its do step)
            }

            count += amount;
            if (count >= step.Required)
            {
                bool wasChip = ChipStepOf(actor, step.Pack) == step;
                CompleteStep(actor, step, silent: !(wasChip || step.IsReact));
                completed = true;
            }
            else
            {
                progress[step.ProgressKey] = count;
                if (actor.StepEscalation.TryGetValue(step.ProgressKey, out var esc))
                {
                    esc.Since = actor.StoryPackPlaySeconds; // progress resets the help clock
                }

                chipMoved = true;
            }
        }

        if (completed)
        {
            foreach (var pack in hits.Select(h => h.Pack).Distinct())
            {
                AdvancePack(actor, pack);
            }

            SyncStepListeners();
        }

        if (completed || chipMoved)
        {
            SendVegaObjective(actor); // the counter or the next step reaches the chip at once
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The chip.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The chip looks at non-optional chapters first, then optional ones.</summary>
    private static readonly bool[] ChipChapterPasses = { false, true };

    /// <summary>A pack objective for the chip.</summary>
    private readonly record struct PackObjective(string PackId, string Key, int Progress, int Target, string Source, string StepId = "");

    /// <summary>The step of a pack that owns the chip: the first active blocking do step of the first running chapter
    /// (non-optional chapters in order first, then optional ones), or null.</summary>
    private CompiledStep? ChipStepOf(PlayerSession s, CompiledPack pack, bool? optional = null)
    {
        if (!PackRunsFor(s, pack))
        {
            return null;
        }

        foreach (bool wantOptional in ChipChapterPasses)
        {
            if (optional is { } only && only != wantOptional)
            {
                continue;
            }

            foreach (var ch in pack.Chapters)
            {
                if (ch.Optional != wantOptional || !ChapterRuns(s, ch))
                {
                    continue;
                }

                foreach (var step in ch.Steps)
                {
                    if (step.IsDo && StepActive(s, step) && !StepComplete(s, step) && StepBlocks(s, step) && StepRunsForClient(s, step))
                    {
                        return step;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The chip objective of a pack for this player (null when it has none the client can show): a legacy client
    /// sees only a tutorial step's old <c>vega.obj.&lt;stage&gt;</c> key.</summary>
    private PackObjective? PackObjectiveOf(PlayerSession s, CompiledPack pack, bool? optional = null)
    {
        if (ChipStepOf(s, pack, optional) is not { } step)
        {
            return null;
        }

        string key;
        if (!s.SupportsPacks)
        {
            if (!pack.IsTutorial || step.LegacyStageId is null)
            {
                return null; // an older client cannot localize a pack chip — d3 §5.6.4: other steps clear it
            }

            key = StoryLegacy.ChipKey(step.LegacyStageId);
        }
        else
        {
            key = StepChipKey(s, step);
            if (key.Length == 0)
            {
                return null;
            }
        }

        string source = step.IsMission ? ObjectiveSources.Mission : pack.IsTutorial ? ObjectiveSources.Tutorial : ObjectiveSources.Story;
        s.State.StepProgress.TryGetValue(step.ProgressKey, out int count);
        return new PackObjective(pack.Id, key, count, step.Required, source, step.Id);
    }

    /// <summary>Rank 3 of the chip order: a tutorial pack's mandatory part ("basic training").</summary>
    private PackObjective? BasicTrainingObjective(PlayerSession s)
    {
        foreach (var pack in PlayerPacks)
        {
            if (pack.IsTutorial && PackObjectiveOf(s, pack, optional: false) is { } o)
            {
                return o;
            }
        }

        return null;
    }

    /// <summary>Rank 6: a tutorial deep-dive step, or any pack's mission-bridge step.</summary>
    private PackObjective? DeepDiveObjective(PlayerSession s)
    {
        foreach (var pack in PlayerPacks)
        {
            if (pack.IsTutorial && PackObjectiveOf(s, pack, optional: true) is { } o)
            {
                return o;
            }
        }

        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsTutorial && PackObjectiveOf(s, pack) is { Source: ObjectiveSources.Mission } m)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>Whether a tutorial pack's mandatory part still runs for this player (the Settings button says "Skip"). An
    /// older client is through once its nine legacy lessons are (#2487, <see cref="MandatoryChapterThrough"/>).</summary>
    private bool TutorialPackRunning(PlayerSession s)
        => PlayerPacks.Any(p => p.IsTutorial && PackRunsFor(s, p) && !MandatoryChaptersThrough(s, p));

    // ---------------------------------------------------------------------------------------------
    // The tick: offers and help escalation.
    // ---------------------------------------------------------------------------------------------

    /// <summary>1 Hz per player (from <c>TickShipAi</c>, so the clock stands still while the world is held): advances the
    /// packs, offers optional chapters and escalates help on the chip steps.</summary>
    private void TickStoryPacksFor(PlayerSession s)
    {
        if (PlayerPacks.Count == 0)
        {
            return;
        }

        s.StoryPackPlaySeconds += 1.0;
        AdvancePlayer(s, offers: true);
        foreach (var pack in PlayerPacks)
        {
            if (ChipStepOf(s, pack, optional: false) is { } mandatory)
            {
                EscalateStep(s, mandatory);
            }

            if (ChipStepOf(s, pack, optional: true) is { } deepDive)
            {
                EscalateStep(s, deepDive);
            }
        }

        SyncStepListeners();
    }

    /// <summary>Offers each optional chapter once, when its <c>offerWhen</c> holds — never while the pack's mandatory
    /// chapters still run (no offers during basic training), never to an older client for a tutorial (its deep dives do not
    /// run), at most one offer per <see cref="ChapterOfferGapSeconds"/>.</summary>
    private void OfferChapters(PlayerSession s, CompiledPack pack)
    {
        if (DueOffer(s, pack) is not { } ch)
        {
            return;
        }

        MilestonesOf(s).Add(StoryPackKeys.ChapterOffered(pack.Id, ch.Id));
        s.LastChapterOfferAt = s.StoryPackPlaySeconds;
        MarkJournalDirty(s); // #2471: Start / Later in the Journal
        MarkTipsToldByOffer(s, ch); // #2459: the tip of this moment is the offer — it does not follow it
        string title = string.IsNullOrEmpty(ch.Def.TitleKey) ? ch.Id : LocalizerFor(s.Locale).Get(ch.Def.TitleKey);
        if (s.SupportsChoices && s.SupportsPacks)
        {
            SendPackLine(s, pack, ChapterOfferChoiceLine, title, choices: OfferChoices(ch)); // #2499: [Later] [Start now] in the panel
        }
        else
        {
            SendPackLine(s, pack, ChapterOfferLine, title);
        }

        _repo.SavePlayer(s.State);
    }

    /// <summary>The optional chapter of this pack an offer pass would offer the player now (the first in pack order whose
    /// moment holds — one offer at a time), or null: the pack must run, its mandatory chapters be through, the last offer
    /// be <see cref="ChapterOfferGapSeconds"/> ago, and the chapter not done, offered, put off or running, admitted
    /// (<c>requires</c>), visible, at its moment (<c>offerWhen</c>) and not through by evidence.</summary>
    private CompiledChapter? DueOffer(PlayerSession s, CompiledPack pack)
    {
        if (!PackRunsFor(s, pack) || (pack.IsTutorial && !s.SupportsPacks)
            || s.StoryPackPlaySeconds - s.LastChapterOfferAt < ChapterOfferGapSeconds
            || !pack.Chapters.Where(c => !c.Optional).All(c => ChapterFinished(s, c)))
        {
            return null;
        }

        var m = MilestonesOf(s);
        foreach (var ch in pack.Chapters)
        {
            if (!ch.Optional || ch.OfferWhen is null
                || ChapterDoneOrSkipped(s, ch)
                || m.Contains(StoryPackKeys.ChapterOffered(pack.Id, ch.Id))
                || m.Contains(StoryPackKeys.ChapterDeclined(pack.Id, ch.Id))
                || m.Contains(StoryPackKeys.ChapterActive(pack.Id, ch.Id))
                || !EvaluateStoryCondition(ch.Requires, s)
                || !ChapterVisible(s, ch) // #2482: a course the world rules out (combat off, a singleplayer world)
                || !EvaluateStoryCondition(ch.OfferWhen, s)
                || ChapterThroughByEvidence(s, ch)) // #2481: nothing left to teach (e.g. the whole Crystal workshop done)
            {
                continue;
            }

            return ch;
        }

        return null;
    }

    /// <summary>#2490: play seconds before a marker probe that found nothing tries again on the same body — at most one probe
    /// per this many seconds per chip step (a new location tries at once).</summary>
    internal const double MarkerRetrySeconds = 30.0;

    /// <summary>The help escalation of one chip step on active play time (synthesis §3.1): after <c>hintAfter</c> the hint
    /// line (an open CORE step of a non-optional chapter then stops blocking), after <c>markerAfter</c> a marker ping for
    /// this session only on a "where" step, after <c>offerSkipAfter</c> the skip offer on a MUST step. Each fires once
    /// per session; progress restarts the clock. A marker whose provider found nothing (the site is on another world, no
    /// ore in reach yet) is not spent (#2490): it tries again when the player's location changes, or after
    /// <see cref="MarkerRetrySeconds"/> on the same spot.</summary>
    private void EscalateStep(PlayerSession s, CompiledStep step)
    {
        if (!s.StepEscalation.TryGetValue(step.ProgressKey, out var esc))
        {
            s.StepEscalation[step.ProgressKey] = esc = new StepEscalationState { Since = s.StoryPackPlaySeconds };
        }

        double open = s.StoryPackPlaySeconds - esc.Since;
        var pack = step.Pack.Def.Escalate ?? new StoryEscalation();
        int hintAfter = step.Def.Escalate?.HintAfter ?? pack.HintAfter ?? StoryEscalation.DefaultHintAfter;
        int markerAfter = step.Def.Escalate?.MarkerAfter ?? pack.MarkerAfter ?? StoryEscalation.DefaultMarkerAfter;
        int offerAfter = step.Def.Escalate?.OfferSkipAfter ?? pack.OfferSkipAfter ?? StoryEscalation.DefaultOfferSkipAfter;

        if (!esc.HintSent && hintAfter > 0 && open >= hintAfter)
        {
            esc.HintSent = true;
            SendPackLine(s, step.Pack, StepHintKey(s, step));
            if (!step.Chapter.Optional && step.Tier == StoryStepTiers.Core)
            {
                esc.Deferred = true; // an open extra from now on: it still counts, the chapter goes on
                AdvancePack(s, step.Pack);
            }
        }

        string reveal = step.Def.OnStart?.Reveal ?? string.Empty;
        if (!esc.MarkerSent && markerAfter > 0 && open >= markerAfter && reveal.Length > 0 && MarkerProbeDue(s, esc))
        {
            if (MarkRevealTarget(s, step, reveal))
            {
                esc.MarkerSent = true;
            }
            else
            {
                esc.MarkerMissAt = MarkerLocationKey(s); // #2490: not spent — retried elsewhere or a little later
                esc.MarkerMissTime = s.StoryPackPlaySeconds;
            }
        }

        if (!esc.OfferSent && offerAfter > 0 && open >= offerAfter && step.Tier == StoryStepTiers.Must
            && (step.Def.Skippable ?? true) && (step.Chapter.Def.Skippable ?? true))
        {
            esc.OfferSent = true;
            SendPackLine(s, step.Pack, StepOfferSkipLine);
        }
    }

    /// <summary>#2490: whether a marker probe may run now — never tried, a new location since the last miss, or
    /// <see cref="MarkerRetrySeconds"/> of play on the same spot.</summary>
    private bool MarkerProbeDue(PlayerSession s, StepEscalationState esc)
        => esc.MarkerMissAt is null
           || !string.Equals(esc.MarkerMissAt, MarkerLocationKey(s), StringComparison.Ordinal)
           || s.StoryPackPlaySeconds - esc.MarkerMissTime >= MarkerRetrySeconds;

    /// <summary>#2490: where the player is for the marker retry — the body, and whether they are in flight above it or on a
    /// station (a launch or a landing on the same body is a new place for the providers).</summary>
    private string MarkerLocationKey(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        string where = InSpace(pid) ? "space" : InStation(pid) ? "station" : "ground";
        return where + "|" + (s.CurrentLocationId ?? string.Empty);
    }

    /// <summary>The help marker of a "where" step: the nearest station becomes the client's space waypoint
    /// (<see cref="StoryRevealProviders.NearestStation"/>, #2471 — in flight or still landed, so the dock lesson finally
    /// points somewhere); every other provider pings a spot on the ground for this session only. Nothing found ⇒ no marker
    /// and no line. Returns whether a marker went up.</summary>
    private bool MarkRevealTarget(PlayerSession s, CompiledStep step, string reveal)
    {
        if (reveal == StoryRevealProviders.NearestStation)
        {
            if (NearestStationFor(s) is not { } station)
            {
                return false;
            }

            SendPackLine(s, step.Pack, StoryNarratorLines.StepMarkedStation, station.Name, waypointId: station.Id);
            return true;
        }

        if (RevealTarget(s, reveal) is not { } target)
        {
            return false;
        }

        RaisePrivatePing(s, target);
        SendPackLine(s, step.Pack, StepMarkedLine);
        return true;
    }

    /// <summary>The station the dock lesson's marker points at (#2471): in flight the station contact of the player's own
    /// orbit nearest to their ship; landed (or between instances) the first station of the system as seen from the body
    /// they are on — the same list the step's <c>station_in_system</c> feasibility reads. Null when there is none.</summary>
    private (string Id, string Name)? NearestStationFor(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        if (_playerInstance.TryGetValue(pid, out var instanceId) && _spaceInstances.TryGetValue(instanceId, out var instance))
        {
            var at = PilotPositionIn(instance, pid);
            CombatEntity? best = null;
            double bestSq = double.MaxValue;
            foreach (var e in instance.Entities)
            {
                if (e.Kind != CombatEntityKind.SpaceStation)
                {
                    continue;
                }

                double dx = e.Position.X - at.X, dy = e.Position.Y - at.Y, dz = e.Position.Z - at.Z;
                double d = (dx * dx) + (dy * dy) + (dz * dz);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = e;
                }
            }

            if (best is not null)
            {
                return (best.Id, string.IsNullOrEmpty(best.Name) ? best.Id : best.Name);
            }
        }

        if (string.IsNullOrEmpty(s.CurrentLocationId))
        {
            return null;
        }

        var station = StationContactsForCurrentSystem(s.CurrentLocationId).FirstOrDefault();
        return station is null ? null : (station.Id, station.Name);
    }

    /// <summary>Test seam: the station id the dock lesson's marker would point at for this player, or null.</summary>
    internal string? NearestStationForTest(PlayerSession s) => NearestStationFor(s)?.Id;

    /// <summary>Where a marker provider points for this player, or null when nothing is in reach (then no ping — the
    /// hint line already said where to look). Reads the player's own world, which is the active cursor in the tick.</summary>
    private Vector3f? RevealTarget(PlayerSession s, string reveal)
    {
        if (string.IsNullOrEmpty(s.CurrentLocationId) || InSpace(s.State.PlayerId) || s.CurrentLocationId != _world.LocationId)
        {
            return null;
        }

        var pos = s.State.Position;
        if (reveal.StartsWith(StoryRevealProviders.UniqueSite + ":", StringComparison.Ordinal))
        {
            // #2476: the site's own spot while the player is on its world ("read the observatory's last log").
            string kind = reveal.Substring(StoryRevealProviders.UniqueSite.Length + 1);
            return UniqueSiteTarget(kind) is { } site && site == s.CurrentLocationId ? UniqueSiteGroundSpot(kind, site) : null;
        }

        if (reveal == StoryRevealProviders.NearestFoodPlant)
        {
            return NearestBlockOf(s, FoodPlantBlocks) is { } plant ? new Vector3f(plant.X + 0.5f, plant.Y + 0.5f, plant.Z + 0.5f) : null; // #2469
        }

        if (reveal == StoryRevealProviders.NearestSettlement)
        {
            Vector3f? best = null;
            double bestSq = double.MaxValue;
            foreach (var settlement in _worlds.Find(s.CurrentLocationId)?.Settlements ?? new List<SettlementInstance>())
            {
                var centre = new Vector3f((settlement.Min.X + settlement.Max.X) * 0.5f, settlement.GroundY + 1, (settlement.Min.Z + settlement.Max.Z) * 0.5f);
                double d = WrapDistSq(centre, pos);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = centre;
                }
            }

            return best;
        }

        if (!reveal.StartsWith(StoryRevealProviders.NearestOre + ":", StringComparison.Ordinal)
            || _content.GetBlock(reveal.Substring(StoryRevealProviders.NearestOre.Length + 1)) is not { } block)
        {
            return null;
        }

        var origin = WorldConstants.CanonicalBlock(new Vector3i(
            (int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z)), _world.Circumference);
        int bestDist = int.MaxValue;
        Vector3i? found = null;
        int r = RevealProbeRadius;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    int d = (dx * dx) + (dy * dy) + (dz * dz);
                    if (d >= bestDist)
                    {
                        continue;
                    }

                    var cell = WorldConstants.CanonicalBlock(new Vector3i(origin.X + dx, origin.Y + dy, origin.Z + dz), _world.Circumference);
                    if (_world.GetBlockIfLoaded(cell) == block.NumericId)
                    {
                        bestDist = d;
                        found = cell;
                    }
                }
            }
        }

        return found is { } c ? new Vector3f(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Join.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The join hook (after VEGA's own join): migrates, starts and advances the packs (new steps speak their start
    /// lines) and re-speaks the step that was already on the chip before the join (a rejoin used to say only
    /// <c>vega.resume</c>).</summary>
    private void StoryPacksOnJoin(PlayerSession s)
    {
        if (PlayerPacks.Count == 0 || s.Spectating)
        {
            return;
        }

        // A counter of a step its (installed) pack no longer has — renamed or removed by an update — goes; counters of a
        // pack that is not installed stay, in case it comes back.
        foreach (string key in s.State.StepProgress.Keys.ToList())
        {
            int colon = key.IndexOf(':');
            string owner = colon > 0 ? key.Substring(0, colon) : string.Empty;
            if (colon > 0 && (TryGetPlayerPack(owner, out _) || _storyMissionPacks.ContainsKey(owner)) && !_stepByProgressKey.ContainsKey(key))
            {
                s.State.StepProgress.Remove(key);
            }
        }

        var before = new HashSet<string>(s.State.StepProgress.Keys, StringComparer.Ordinal);
        foreach (string key in before)
        {
            if (_stepByProgressKey.TryGetValue(key, out var running))
            {
                s.PresentedPackChapters.Add(PresentedChapterKey(running.Chapter)); // #2467: a chapter under way was shown
            }
        }

        _stepListenersDirty = true;
        AdvancePlayer(s);
        foreach (var pack in PlayerPacks)
        {
            if (ChipStepOf(s, pack) is { } chip && before.Contains(chip.ProgressKey))
            {
                SpeakStepStart(s, chip);
            }
        }

        s.StoryPacksJoined = true;
    }
}
