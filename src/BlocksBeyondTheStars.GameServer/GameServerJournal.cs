// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Journal (#2471, synthesis §5.8): what the client's Journal tab and its objective tracker read. Two messages, both
/// only for clients that announce <c>packs1</c>:
/// <list type="bullet">
/// <item><see cref="StoryPackStates"/> (tag 300) — every story the player can follow: the player packs (the tutorial, side
/// stories) with each chapter's state (done / skipped / running / replay / offered / declined / available / upcoming /
/// locked with what it needs), the active do steps ("step 4 of 8", open extras, the chip's step), the switches (started,
/// opted out, startable, blocked by the world rule, tracked) and the badge; plus one short entry for the world story (its
/// arc stays on <see cref="StoryStateMessage"/>).</item>
/// <item><see cref="ObjectiveList"/> (tag 299) — up to three objectives in the chip order (<see cref="ChipCandidates"/>), one
/// per story, the first being the chip: the live progress of the Journal and the optional "Show all objectives" lines.</item>
/// </list>
/// Both go out on the join, at once after a player's own Journal action, on request (the Journal opened) — and otherwise
/// coalesced: anything that changes a story marks the session dirty (<see cref="MarkJournalDirty"/>; every VEGA line and chip
/// update does), and <see cref="TickJournal"/> sends what differs from the last send at most once per
/// <see cref="JournalPushGapSeconds"/>. A mere progress tick never re-sends the states (their counters are left out of the
/// comparison) — the objective list carries live progress.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Shortest gap between two coalesced Journal pushes to one player (seconds of server uptime).</summary>
    internal const double JournalPushGapSeconds = 1.0;

    /// <summary>From this many missed story beats on, a started player's join catch-up speaks ONE line pointing at the
    /// Journal instead of one line per beat (#2471, B09).</summary>
    internal const int CatchUpCollapseThreshold = 2;

    // ---------------------------------------------------------------------------------------------
    // Coalescing.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Something the Journal shows changed for this player; the next <see cref="TickJournal"/> sends it.</summary>
    private static void MarkJournalDirty(PlayerSession s) => s.JournalDirty = true;

    /// <summary>Per server tick (wherever the players are — in space too): pushes the Journal of every dirty session whose
    /// last push is at least <see cref="JournalPushGapSeconds"/> old. Cheap while nothing changed (one flag per session).</summary>
    private void TickJournal()
    {
        foreach (var s in _sessions.Values)
        {
            if (s.JournalDirty && _uptime - s.JournalPushedAt >= JournalPushGapSeconds)
            {
                PushJournal(s, force: false);
            }
        }
    }

    /// <summary>Sends the Journal's two messages to a <c>packs1</c> client where they differ from what it has —
    /// <paramref name="force"/> sends both (the join, a request). Observers and older clients get nothing.</summary>
    private void PushJournal(PlayerSession s, bool force)
    {
        s.JournalDirty = false;
        if (!s.Joined || s.Spectating || !s.SupportsPacks)
        {
            return;
        }

        s.JournalPushedAt = _uptime;
        var states = BuildStoryPackStates(s);
        byte[] statesKey = StatesComparisonBytes(states);
        if (force || s.JournalStatesSent is null || !statesKey.AsSpan().SequenceEqual(s.JournalStatesSent))
        {
            s.JournalStatesSent = statesKey;
            Send(s, states);
        }

        var objectives = BuildObjectiveList(s);
        byte[] objectivesKey = NetCodec.Encode(objectives);
        if (force || s.JournalObjectivesSent is null || !objectivesKey.AsSpan().SequenceEqual(s.JournalObjectivesSent))
        {
            s.JournalObjectivesSent = objectivesKey;
            Send(s, objectives);
        }
    }

    /// <summary>The encoded states with every step counter zeroed — the comparison key that keeps a progress tick from
    /// re-sending them.</summary>
    private static byte[] StatesComparisonBytes(StoryPackStates states)
    {
        var steps = states.Packs.SelectMany(p => p.Steps).ToList();
        var saved = steps.Select(st => st.Progress).ToList();
        foreach (var st in steps)
        {
            st.Progress = 0;
        }

        // #2476: the story missions' step counters likewise.
        var missionSteps = states.Packs.SelectMany(p => p.Missions).SelectMany(m => m.Steps).ToList();
        var savedMission = missionSteps.Select(st => st.Progress).ToList();
        foreach (var st in missionSteps)
        {
            st.Progress = 0;
        }

        byte[] bytes = NetCodec.Encode(states);
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].Progress = saved[i];
        }

        for (int i = 0; i < missionSteps.Count; i++)
        {
            missionSteps[i].Progress = savedMission[i];
        }

        return bytes;
    }

    // ---------------------------------------------------------------------------------------------
    // StoryPackStates.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The Journal's stories for one player, in Journal order: tutorial packs, the world story, the other packs.</summary>
    private StoryPackStates BuildStoryPackStates(PlayerSession s)
    {
        var packs = new List<NetStoryPack>();
        foreach (var pack in PlayerPacks.Where(p => p.IsTutorial))
        {
            packs.Add(NetPackOf(s, pack));
        }

        if (WorldStoryEntry(s) is { } world)
        {
            packs.Add(world);
        }

        foreach (var pack in PlayerPacks.Where(p => !p.IsTutorial))
        {
            packs.Add(NetPackOf(s, pack));
        }

        return new StoryPackStates
        {
            Packs = packs.ToArray(),
            TrackedPackId = s.State.TrackedStoryId ?? string.Empty,
            TutorialPolicy = Rules.TutorialPolicy.ToString(),
        };
    }

    /// <summary>The world story's short entry: its names, the player's track (started, startable), the finale won as done and
    /// as the badge, the pin, and (#2475) the current chapter's leads as its tasks. No chapters or steps — the arc is on
    /// <see cref="StoryStateMessage"/>.</summary>
    private NetStoryPack? WorldStoryEntry(PlayerSession s)
    {
        if (!StoryActive || _story is null)
        {
            return null;
        }

        bool started = StoryTrackStarted(s);
        bool won = started && _storyState.GuardianDefeated;
        return new NetStoryPack
        {
            Id = _story.Id,
            Kind = string.IsNullOrEmpty(_story.Kind) ? StoryRegistry.KindStory : _story.Kind,
            Scope = StoryRegistry.ScopeWorld,
            NameKey = _story.NameKey ?? string.Empty,
            ShortNameKey = _story.ShortNameKey ?? string.Empty,
            DescriptionKey = _story.DescriptionKey ?? string.Empty,
            Enabled = true,
            Started = started,
            Done = won,
            Tracked = string.Equals(s.State.TrackedStoryId, _story.Id, StringComparison.OrdinalIgnoreCase),
            Startable = !started && ((_story.Start?.EarlyOptIn ?? false) || StoryTrackStartReason(s) is not null),
            Badge = won,
            Leads = JournalLeads(s), // #2475: the current chapter's leads as the VEGA page's tasks
            Missions = JournalMissions(s), // #2476: the current chapter's story missions
        };
    }

    /// <summary>One player pack as the Journal shows it.</summary>
    private NetStoryPack NetPackOf(PlayerSession s, CompiledPack pack)
    {
        var def = pack.Def;
        var m = MilestonesOf(s);
        bool started = PackStarted(s, pack);
        bool runs = PackRunsFor(s, pack);
        var mandatory = pack.Chapters.Where(c => !c.Optional).ToList();
        bool done = PackDone(s, pack);
        bool badge = done && (mandatory.Count > 0
            ? mandatory.All(c => m.Contains(StoryPackKeys.ChapterDone(pack.Id, c.Id)))
            : pack.Chapters.All(c => m.Contains(StoryPackKeys.ChapterDone(pack.Id, c.Id))));
        return new NetStoryPack
        {
            Id = pack.Id,
            Kind = def.Kind ?? string.Empty,
            Scope = StoryRegistry.ScopePlayer,
            NameKey = def.NameKey ?? string.Empty,
            ShortNameKey = def.ShortNameKey ?? string.Empty,
            DescriptionKey = def.DescriptionKey ?? string.Empty,
            Enabled = StoryPackEnabledFor(s, pack.Id),
            Started = started,
            Done = done,
            OptedOut = PackOptedOut(s, pack),
            Tracked = string.Equals(s.State.TrackedStoryId, pack.Id, StringComparison.OrdinalIgnoreCase),
            Startable = !started && CanStartPackOnRequest(s, pack),
            Blocked = pack.IsTutorial && Rules.TutorialPolicy == TutorialPolicy.Off,
            MandatoryRunning = runs && mandatory.Count > 0 && !mandatory.All(c => ChapterFinished(s, c)),
            Badge = badge,
            Chapters = pack.Chapters.Where(c => JournalListsChapter(s, c)).Select(c => NetChapterOf(s, c, runs)).ToArray(),
            Steps = runs ? ActiveStepsOf(s, pack).ToArray() : Array.Empty<NetStoryStep>(),
        };
    }

    /// <summary>#2482: whether the Journal lists a chapter — always, unless the world rules it out (<c>visibleIf</c>) and the player
    /// has nothing of it: one they run, replay, finished or skipped stays listed.</summary>
    private bool JournalListsChapter(PlayerSession s, CompiledChapter ch)
        => ChapterVisible(s, ch) || ChapterDoneOrSkipped(s, ch) || ChapterReplaying(s, ch)
           || MilestonesOf(s).Contains(StoryPackKeys.ChapterActive(ch.Pack.Id, ch.Id));

    /// <summary>A chapter's Journal state (<see cref="StoryChapterStates"/>).</summary>
    private NetStoryChapter NetChapterOf(PlayerSession s, CompiledChapter ch, bool packRuns)
    {
        var m = MilestonesOf(s);
        string state;
        if (ChapterReplaying(s, ch))
        {
            state = StoryChapterStates.Replay;
        }
        else if (m.Contains(StoryPackKeys.ChapterDone(ch.Pack.Id, ch.Id)))
        {
            state = StoryChapterStates.Done;
        }
        else if (m.Contains(StoryPackKeys.ChapterSkipped(ch.Pack.Id, ch.Id)))
        {
            state = StoryChapterStates.Skipped;
        }
        else if (packRuns && ChapterRuns(s, ch))
        {
            state = StoryChapterStates.Active;
        }
        else if (!ch.Optional)
        {
            state = StoryChapterStates.Upcoming;
        }
        else if (m.Contains(StoryPackKeys.ChapterDeclined(ch.Pack.Id, ch.Id)))
        {
            state = StoryChapterStates.Declined;
        }
        else if (!EvaluateStoryCondition(ch.Requires, s))
        {
            state = StoryChapterStates.Locked;
        }
        else if (m.Contains(StoryPackKeys.ChapterOffered(ch.Pack.Id, ch.Id)))
        {
            state = StoryChapterStates.Offered;
        }
        else
        {
            state = StoryChapterStates.Available;
        }

        return new NetStoryChapter
        {
            Id = ch.Id,
            TitleKey = ch.Def.TitleKey ?? string.Empty,
            SummaryKey = ch.Def.SummaryKey ?? string.Empty,
            State = state,
            Optional = ch.Optional,
            Skippable = ch.Def.Skippable ?? true,
            RequiresKeys = state == StoryChapterStates.Locked ? RequirementKeys(s, ch.Requires) : Array.Empty<string>(),
            NothingLeft = state != StoryChapterStates.Locked && ChapterSpentFor(s, ch), // #2500: no Replay / Start, "nothing left"
        };
    }

    /// <summary>Locale keys naming what a condition still needs for this player — the title of a chapter not yet done, the
    /// name of a blueprint not yet owned. Atoms under a <c>not:</c> and the ones nobody can name (steps, world facts) are left
    /// out; the Journal then just says "locked".</summary>
    private string[] RequirementKeys(PlayerSession s, StoryCondition? condition)
    {
        var keys = new List<string>();
        Collect(condition);
        return keys.Distinct(StringComparer.Ordinal).ToArray();

        void Collect(StoryCondition? c)
        {
            if (c is null || c.Kind == StoryConditionKind.Not)
            {
                return;
            }

            if (c.Kind is StoryConditionKind.All or StoryConditionKind.Any)
            {
                foreach (var child in c.Children)
                {
                    Collect(child);
                }

                return;
            }

            if (EvaluateStoryCondition(c, s))
            {
                return;
            }

            switch (c.Kind)
            {
                case StoryConditionKind.Chapter:
                    if (ChapterTitleKey(c.PackId, c.Id) is { Length: > 0 } title)
                    {
                        keys.Add(title);
                    }

                    break;
                case StoryConditionKind.Blueprint:
                    if (_content.Blueprints.TryGetValue(c.Id, out var blueprint) && !string.IsNullOrEmpty(blueprint.NameKey))
                    {
                        keys.Add(blueprint.NameKey);
                    }

                    break;
            }
        }
    }

    /// <summary>The title key of a chapter of a player pack or of the world story, or null.</summary>
    private string? ChapterTitleKey(string packId, string chapterId)
    {
        if (TryGetPlayerPack(packId, out var pack) && pack.ChaptersById.TryGetValue(chapterId, out var ch))
        {
            return ch.Def.TitleKey;
        }

        return _story is not null && string.Equals(_story.Id, packId, StringComparison.OrdinalIgnoreCase)
            ? _story.Chapters.FirstOrDefault(c => c.Id == chapterId)?.TitleKey
            : null;
    }

    /// <summary>The active do steps of a pack's running chapters (read and react steps are not tasks): the chapter's own chip
    /// text, "step i of n" among its tasks (a stand-in counts at its primary's place), open extras, the chip's step.</summary>
    private IEnumerable<NetStoryStep> ActiveStepsOf(PlayerSession s, CompiledPack pack)
    {
        var chip = ChipStepOf(s, pack);
        foreach (var ch in pack.Chapters)
        {
            if (!ChapterRuns(s, ch))
            {
                continue;
            }

            var tasks = ch.Steps.Where(st => st.IsDo && !st.IsAlternative).ToList();
            foreach (var step in ch.Steps)
            {
                if (!step.IsDo || !StepActive(s, step) || StepComplete(s, step) || !StepRunsForClient(s, step))
                {
                    continue;
                }

                var place = step.IsAlternative ? ch.Steps.FirstOrDefault(st => st.Alternative == step) ?? step : step;
                s.State.StepProgress.TryGetValue(step.ProgressKey, out int progress);
                yield return new NetStoryStep
                {
                    Id = step.Id,
                    ChapterId = ch.Id,
                    ChipKey = StepChipKey(s, step),
                    StartKey = StepStartKey(s, step),
                    MoreGuide = step.Def.Explain?.MoreGuide ?? string.Empty,
                    Tier = step.Tier,
                    Progress = progress,
                    Target = step.Required,
                    Index = tasks.IndexOf(place) + 1,
                    Count = tasks.Count,
                    Skippable = step.Def.Skippable ?? true,
                    Extra = !StepBlocks(s, step),
                    OnChip = step == chip,
                };
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // ObjectiveList.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The player's objectives in the chip order (§5.3): the first row of each story, at most
    /// <see cref="ObjectiveList.MaxItems"/>, the chip's first.</summary>
    private ObjectiveList BuildObjectiveList(PlayerSession s)
    {
        var items = new List<NetObjective>();
        var stories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pinned = s.State.TrackedStoryId ?? string.Empty;
        foreach (var chip in ChipCandidates(s, tutorialActive: false))
        {
            if (chip.Key.Length == 0 || !stories.Add(chip.PackId))
            {
                continue;
            }

            items.Add(new NetObjective
            {
                Source = chip.Source,
                PackId = chip.PackId,
                StepId = chip.StepId,
                Key = chip.Key,
                Arg = chip.Arg,
                Progress = chip.Progress,
                Target = chip.Target,
                Pinned = pinned.Length > 0 && string.Equals(chip.PackId, pinned, StringComparison.OrdinalIgnoreCase),
            });
            if (items.Count >= ObjectiveList.MaxItems)
            {
                break;
            }
        }

        return new ObjectiveList { Items = items.ToArray() };
    }

    // ---------------------------------------------------------------------------------------------
    // The world story's notes (non-beat narrator lines).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The world story's narrator lines that are not beats which this player has heard (#2471): the Guardian-system
    /// reveal, the insights, the resolution line — read from their once keys, so the Journal's notes survive a rejoin.</summary>
    private string[] StoryNoteKeys(PlayerSession s)
    {
        if (_story is null)
        {
            return Array.Empty<string>();
        }

        var m = s.State.Milestones;
        var keys = new List<string>();
        foreach (var insight in _story.Insights)
        {
            string once = string.IsNullOrEmpty(insight.OnceKey) ? "story:insight:" + insight.Event : insight.OnceKey;
            if (!string.IsNullOrEmpty(insight.TextKey) && m.Contains(once))
            {
                keys.Add(insight.TextKey);
            }
        }

        if (m.Contains(GuardianRevealHeardKey))
        {
            keys.Add(GuardianRevealedLine);
        }

        if (_storyState.GuardianDefeated && m.Contains(ResolutionWatchedKey))
        {
            keys.Add(FinaleResolvedLine);
        }

        return keys.ToArray();
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: the Journal's stories for a player as they would be sent now.</summary>
    internal StoryPackStates StoryPackStatesForTest(PlayerSession s) => BuildStoryPackStates(s);

    /// <summary>Test seam: the objective list for a player as it would be sent now.</summary>
    internal ObjectiveList ObjectiveListForTest(PlayerSession s) => BuildObjectiveList(s);

    /// <summary>Test seam: the coalesced Journal pass of one server tick.</summary>
    internal void TickJournalForTest() => TickJournal();

    /// <summary>Test seam: whether the player's Journal waits for a push.</summary>
    internal bool JournalDirtyForTest(PlayerSession s) => s.JournalDirty;
}
