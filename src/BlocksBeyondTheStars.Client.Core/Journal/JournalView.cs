// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One entry of the Journal's left list (#2471): a story, VEGA's notes or the player's own notes.</summary>
    public sealed class JournalCategory
    {
        public JournalCategory(string id, string label, bool badge, bool attention)
        {
            Id = id;
            Label = label;
            Badge = badge;
            Attention = attention;
        }

        /// <summary><c>pack:&lt;id&gt;</c>, <see cref="JournalCategories.TipsId"/> or <see cref="JournalCategories.NotesId"/>.</summary>
        public string Id { get; }

        /// <summary>The localized name (the story's name, "VEGA's notes", "Notes").</summary>
        public string Label { get; }

        /// <summary>The story's badge is earned (finished by playing).</summary>
        public bool Badge { get; }

        /// <summary>Something waits for the player there (an offered chapter, a story that may be started).</summary>
        public bool Attention { get; }
    }

    /// <summary>A button of a Journal page (#2471): a <see cref="StoryPackActions"/> word with its target, or the Codex.</summary>
    public sealed class JournalAction
    {
        public JournalAction(string labelKey, string action, string chapterId = "", string stepId = "", string codex = "")
        {
            LabelKey = labelKey;
            Action = action;
            ChapterId = chapterId;
            StepId = stepId;
            Codex = codex;
        }

        /// <summary>The button's locale key.</summary>
        public string LabelKey { get; }

        /// <summary>The <see cref="StoryPackActions"/> word to send — empty for <see cref="Codex"/>.</summary>
        public string Action { get; }

        public string ChapterId { get; }

        public string StepId { get; }

        /// <summary>"More": the Codex chapter or guide article to open (no intent is sent).</summary>
        public string Codex { get; }

        /// <summary>Whether this button opens the Codex instead of sending an intent.</summary>
        public bool OpensCodex => Codex.Length > 0;
    }

    /// <summary>
    /// The Journal's left list (#2471, synthesis §5.8): one entry per story the player follows — the stories of
    /// <see cref="StoryPackStates"/> that are started, startable or switched off (a tutorial the world keeps off and the
    /// player never began is left out) and the world story — then VEGA's notes (tips, lessons and narrator lines that are
    /// not beats) and the player's own notes. Before a server sends the stories (an older one never does) the world story
    /// comes from <see cref="StoryStateMessage"/>. Player-facing word: "stories", never "pack". Pure, unit-tested headless.
    /// </summary>
    public static class JournalCategories
    {
        /// <summary>VEGA's notes: tips, lessons, narrator lines that are not beats.</summary>
        public const string TipsId = "tips";

        /// <summary>The player's own notes (#1844).</summary>
        public const string NotesId = "notes";

        /// <summary>Prefix of a story's category.</summary>
        public const string PackPrefix = "pack:";

        public static string PackCategory(string packId) => PackPrefix + (packId ?? string.Empty);

        /// <summary>The story id of a <c>pack:</c> category.</summary>
        public static bool TryPackId(string? category, out string packId)
        {
            if (category != null && category.StartsWith(PackPrefix, StringComparison.Ordinal) && category.Length > PackPrefix.Length)
            {
                packId = category.Substring(PackPrefix.Length);
                return true;
            }

            packId = string.Empty;
            return false;
        }

        /// <summary>Whether a story belongs in the left list: the world story; a player pack that started, may be started or
        /// was switched off by the player. A tutorial the world keeps off and the player never began is not listed.</summary>
        public static bool Listed(NetStoryPack pack)
        {
            if (pack == null)
            {
                return false;
            }

            if (string.Equals(pack.Scope, StoryRegistry.ScopeWorld, StringComparison.Ordinal))
            {
                return true;
            }

            return pack.Started || pack.OptedOut || (pack.Startable && !pack.Blocked);
        }

        /// <summary>The story of a category, or null.</summary>
        public static NetStoryPack? Find(StoryPackStates? states, string? category)
        {
            if (states == null || !TryPackId(category, out string id))
            {
                return null;
            }

            return states.Packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Builds the left list. <paramref name="worldStoryId"/> / <paramref name="worldStoryNameKey"/>: the world
        /// story as the story state names it (used only while no <see cref="StoryPackStates"/> arrived);
        /// <paramref name="worldStoryActive"/> false = the "none" sandbox.</summary>
        public static List<JournalCategory> Build(StoryPackStates? states, string? worldStoryId, string? worldStoryNameKey,
            bool worldStoryActive, Func<string, string> localize, Func<string, bool>? has = null)
        {
            var list = new List<JournalCategory>();
            if (states != null)
            {
                foreach (var pack in states.Packs)
                {
                    if (!Listed(pack))
                    {
                        continue;
                    }

                    bool attention = !pack.Started && pack.Startable && !pack.Blocked
                                     || pack.Chapters.Any(c => c.State == StoryChapterStates.Offered);
                    list.Add(new JournalCategory(PackCategory(pack.Id), Name(pack.NameKey, pack.Id, localize, has), pack.Badge, attention));
                }
            }
            else if (worldStoryActive && !string.IsNullOrEmpty(worldStoryId))
            {
                list.Add(new JournalCategory(PackCategory(worldStoryId!), Name(worldStoryNameKey, worldStoryId!, localize, has), false, false));
            }

            list.Add(new JournalCategory(TipsId, localize("ui.journal.cat_tips"), false, false));
            list.Add(new JournalCategory(NotesId, localize("ui.notes.category"), false, false));
            return list;
        }

        /// <summary>The category the Journal opens on: the pinned story, else the story on the chip, else the first story,
        /// else VEGA's notes.</summary>
        public static string Default(IReadOnlyList<JournalCategory> categories, StoryPackStates? states, ObjectiveList? objectives)
        {
            bool Has(string id) => categories.Any(c => c.Id == id);
            if (states != null && !string.IsNullOrEmpty(states.TrackedPackId) && Has(PackCategory(states.TrackedPackId)))
            {
                return PackCategory(states.TrackedPackId);
            }

            if (objectives != null && objectives.Items.Length > 0 && Has(PackCategory(objectives.Items[0].PackId)))
            {
                return PackCategory(objectives.Items[0].PackId);
            }

            return categories.FirstOrDefault(c => c.Id.StartsWith(PackPrefix, StringComparison.Ordinal))?.Id ?? TipsId;
        }

        private static string Name(string? key, string id, Func<string, string> localize, Func<string, bool>? has)
            => !string.IsNullOrEmpty(key) && (has == null || has(key!)) ? localize(key!) : id;
    }

    /// <summary>
    /// A player story's Journal page (#2471, synthesis §5.8 "Tutorial-Seite", d1 journal behaviour): the status line, the
    /// chapter rows with their state and buttons (Start / Later for offers, Replay per finished chapter), the running
    /// chapter's tasks ("Step 4 of 8: Dock at a station", live progress from <see cref="ObjectiveList"/>) with Skip step and
    /// "More" (the Codex), the running chapter's Skip / Start over, and the story's switches (Start, Skip all, Restart from the
    /// beginning, Switch off / on). Pure, unit-tested headless; the Unity Journal only draws what this returns.
    /// </summary>
    public static class JournalPackPage
    {
        /// <summary>Whether this is a tutorial (its mandatory chapters read "Part n — title").</summary>
        public static bool IsTutorial(NetStoryPack pack)
            => pack != null && string.Equals(pack.Kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase);

        /// <summary>A chapter's row label: a tutorial's mandatory chapter as "Part n — title" (n counts the mandatory chapters),
        /// every other chapter by its title.</summary>
        public static string ChapterLabel(NetStoryPack pack, NetStoryChapter chapter, Func<string, string> localize, Func<string, bool>? has = null)
        {
            string title = !string.IsNullOrEmpty(chapter.TitleKey) && (has == null || has(chapter.TitleKey)) ? localize(chapter.TitleKey) : chapter.Id;
            if (!IsTutorial(pack) || chapter.Optional)
            {
                return title;
            }

            int part = 0;
            foreach (var c in pack.Chapters)
            {
                if (!c.Optional)
                {
                    part++;
                }

                if (ReferenceEquals(c, chapter))
                {
                    break;
                }
            }

            return Format(localize("ui.journal.part_row"), part.ToString(System.Globalization.CultureInfo.InvariantCulture), title);
        }

        /// <summary>The state mark in front of a chapter row.</summary>
        public static string Icon(string? state) => state switch
        {
            StoryChapterStates.Done => "✔",
            StoryChapterStates.Skipped => "»",
            StoryChapterStates.Active => "●",
            StoryChapterStates.Replay => "↻",
            StoryChapterStates.Offered => "★",
            _ => "○",
        };

        /// <summary>The state word after a chapter row — for a locked chapter with what it still needs.</summary>
        public static string StateText(NetStoryChapter chapter, Func<string, string> localize, Func<string, bool>? has = null)
        {
            string state = string.IsNullOrEmpty(chapter.State) ? StoryChapterStates.Upcoming : chapter.State;
            string word = localize("ui.journal.state." + state);
            if (state != StoryChapterStates.Locked || chapter.RequiresKeys.Length == 0)
            {
                return word;
            }

            var needs = chapter.RequiresKeys.Where(k => has == null || has(k)).Select(localize).ToList();
            return needs.Count == 0 ? word : word + " — " + Format(localize("ui.journal.needs"), string.Join(", ", needs));
        }

        /// <summary>The buttons of a chapter row: Start / Later for an offer, Start for an available or put-off chapter, Replay
        /// for a finished one (not while the story is switched off).</summary>
        public static List<JournalAction> ChapterActions(NetStoryPack pack, NetStoryChapter chapter)
        {
            var actions = new List<JournalAction>();
            if (!pack.Started || !pack.Enabled)
            {
                return actions;
            }

            switch (chapter.State)
            {
                case StoryChapterStates.Offered:
                    actions.Add(new JournalAction("ui.journal.start", StoryPackActions.StartChapter, chapter.Id));
                    actions.Add(new JournalAction("ui.journal.later", StoryPackActions.DeclineChapter, chapter.Id));
                    break;
                case StoryChapterStates.Available:
                case StoryChapterStates.Declined:
                    actions.Add(new JournalAction("ui.journal.start", StoryPackActions.StartChapter, chapter.Id));
                    break;
                case StoryChapterStates.Done:
                case StoryChapterStates.Skipped:
                    actions.Add(new JournalAction("ui.journal.replay", StoryPackActions.ReplayChapter, chapter.Id));
                    break;
            }

            return actions;
        }

        /// <summary>The buttons under a running chapter: Skip (when it may be skipped) and Start over.</summary>
        public static List<JournalAction> RunningChapterActions(NetStoryChapter chapter)
        {
            var actions = new List<JournalAction>();
            if (chapter.State != StoryChapterStates.Active && chapter.State != StoryChapterStates.Replay)
            {
                return actions;
            }

            if (chapter.Skippable)
            {
                actions.Add(new JournalAction("ui.journal.skip_chapter", StoryPackActions.SkipChapter, chapter.Id));
            }

            actions.Add(new JournalAction("ui.journal.restart_chapter", StoryPackActions.RestartChapter, chapter.Id));
            return actions;
        }

        /// <summary>The running tasks of a chapter, the chip's first, open extras last.</summary>
        public static List<NetStoryStep> StepsOf(NetStoryPack pack, string chapterId)
            => pack.Steps.Where(s => s.ChapterId == chapterId)
                .OrderBy(s => s.Extra ? 1 : 0).ThenBy(s => s.OnChip ? 0 : 1).ThenBy(s => s.Index)
                .ToList();

        /// <summary>The live progress of a task: the objective list's counter when the task is on it, else the states'.</summary>
        public static int LiveProgress(NetStoryPack pack, NetStoryStep step, ObjectiveList? live)
        {
            if (live != null)
            {
                foreach (var row in live.Items)
                {
                    if (string.Equals(row.PackId, pack.Id, StringComparison.OrdinalIgnoreCase) && row.StepId == step.Id)
                    {
                        return row.Progress;
                    }
                }
            }

            return step.Progress;
        }

        /// <summary>A task's line: "Step 4 of 8: Dock at a station  (2/3)" — an open extra as "Extra: …". The text resolves
        /// <c>{key:…}</c> tokens through <paramref name="compose"/> (the client's one resolver).</summary>
        public static string StepLine(NetStoryPack pack, NetStoryStep step, ObjectiveList? live, Func<string, string> localize,
            Func<string, string>? compose = null)
        {
            string text = string.IsNullOrEmpty(step.ChipKey) ? step.Id : localize(step.ChipKey);
            if (compose != null)
            {
                text = compose(text);
            }

            string counter = ObjectiveTracker.Counter(LiveProgress(pack, step, live), step.Target);
            string line = step.Extra || step.Index <= 0 || step.Count <= 0
                ? Format(localize("ui.journal.extra_row"), text)
                : Format(localize("ui.journal.step_row"), step.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    step.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
            return counter.Length > 0 ? line + "  " + counter : line;
        }

        /// <summary>The buttons of a task: Skip step (when it may be skipped and is not an open extra) and More (the Codex).</summary>
        public static List<JournalAction> StepActions(NetStoryStep step)
        {
            var actions = new List<JournalAction>();
            if (step.Skippable && !step.Extra)
            {
                actions.Add(new JournalAction("ui.journal.skip_step", StoryPackActions.SkipStep, step.ChapterId, step.Id));
            }

            if (!string.IsNullOrEmpty(step.MoreGuide))
            {
                actions.Add(new JournalAction("ui.journal.more", string.Empty, step.ChapterId, step.Id, step.MoreGuide));
            }

            return actions;
        }

        /// <summary>The Codex chapters the "More" button can open directly; any other <c>moreGuide</c> (a guide article id such
        /// as <c>welcome</c>) opens the Guide chapter, where the articles are.</summary>
        public static readonly IReadOnlyList<string> CodexChapters = new[]
        {
            "guide", "blocks", "items", "tech", "ships", "modules", "planets", "discoveries", "substances", "compounds", "materials", "lore",
        };

        /// <summary>The Codex chapter a task's "More" opens.</summary>
        public static string CodexChapter(string? moreGuide)
            => !string.IsNullOrEmpty(moreGuide) && CodexChapters.Contains(moreGuide!) ? moreGuide! : "guide";

        /// <summary>The story's own switches: Track / Tracked, Start (not started), Skip all (the mandatory part still runs),
        /// Restart from the beginning (started, the world allows it), Switch off / Switch on.</summary>
        public static List<JournalAction> PackActions(NetStoryPack pack)
        {
            var actions = new List<JournalAction>();
            if (pack.Started && pack.Enabled)
            {
                actions.Add(pack.Tracked
                    ? new JournalAction("ui.journal.tracked", StoryPackActions.Untrack)
                    : new JournalAction("ui.journal.track", StoryPackActions.Track));
            }

            if (pack.Blocked)
            {
                return actions; // the world keeps it off — no switch of the player's helps
            }

            if (!pack.Started && pack.Startable)
            {
                actions.Add(new JournalAction("ui.journal.start", StoryPackActions.StartPack));
            }

            if (pack.OptedOut)
            {
                actions.Add(new JournalAction("ui.journal.on", StoryPackActions.OptIn));
                return actions;
            }

            if (pack.MandatoryRunning)
            {
                actions.Add(new JournalAction("ui.journal.skip_pack", StoryPackActions.SkipPack));
            }

            if (pack.Started)
            {
                actions.Add(new JournalAction("ui.journal.restart_pack", StoryPackActions.RestartPack));
                actions.Add(new JournalAction("ui.journal.off", StoryPackActions.OptOut));
            }

            return actions;
        }

        /// <summary>The status line under the story's name: switched off by the world or the player, not started, finished.</summary>
        public static string Status(NetStoryPack pack, Func<string, string> localize)
        {
            if (pack.Blocked && !pack.Started)
            {
                return localize("ui.journal.status_blocked");
            }

            if (pack.OptedOut || (pack.Started && !pack.Enabled))
            {
                return pack.Blocked ? localize("ui.journal.status_blocked") : localize("ui.journal.status_off");
            }

            if (!pack.Started)
            {
                return localize("ui.journal.status_not_started");
            }

            return pack.Done ? localize(pack.Badge ? "ui.journal.status_badge" : "ui.journal.status_done") : string.Empty;
        }

        /// <summary>#2475: the world story's chapter leads the VEGA page lists as tasks — the chapter's own leads in order, then the
        /// stand-in the chip shows (a fallback or "explore"). Empty for a player pack and from an older server.</summary>
        public static List<NetStoryLead> LeadsOf(NetStoryPack? pack)
            => pack?.Leads == null
                ? new List<NetStoryLead>()
                : pack.Leads.OrderBy(l => l.StandIn ? 1 : 0).ThenBy(l => l.Index).ToList();

        /// <summary>#2475: a lead's row on the VEGA page — "✔ Visit the Sealed Observatory on Kepler-3 b" (followed), "● …" (on
        /// the chip now), "○ …" (open). The text is the lead's chip text with its {0}; a key this client does not know shows the
        /// lead word rather than a raw key.</summary>
        public static string LeadLine(NetStoryLead lead, Func<string, string> localize, Func<string, bool>? has = null)
        {
            string text = !string.IsNullOrEmpty(lead.Key) && (has == null || has(lead.Key))
                ? localize(lead.Key)
                : lead.Type;
            if (!string.IsNullOrEmpty(lead.Arg))
            {
                text = Format(text, lead.Arg);
            }

            string icon = lead.Done ? Icon(StoryChapterStates.Done) : lead.Current ? Icon(StoryChapterStates.Active) : Icon(null);
            return icon + "  " + text;
        }

        /// <summary>#2476: the world story's missions the VEGA page lists under "Missions in this chapter" — running ones first, then
        /// done ones, each in the server's order. Empty for a player pack and from an older server.</summary>
        public static List<NetStoryMission> MissionsOf(NetStoryPack? pack)
            => pack?.Missions == null
                ? new List<NetStoryMission>()
                : pack.Missions.Select((m, i) => (m, i))
                    .OrderBy(x => x.m.State == StoryChapterStates.Active ? 0 : 1).ThenBy(x => x.i)
                    .Select(x => x.m).ToList();

        /// <summary>#2476: a mission's title row — "● The Sealed Observatory" (running) or "✔ …" (done); an unknown key shows the
        /// mission id rather than a raw key.</summary>
        public static string MissionTitle(NetStoryMission mission, Func<string, string> localize, Func<string, bool>? has = null)
        {
            string title = !string.IsNullOrEmpty(mission.TitleKey) && (has == null || has(mission.TitleKey))
                ? localize(mission.TitleKey)
                : mission.Id;
            return Icon(mission.State == StoryChapterStates.Done ? StoryChapterStates.Done : StoryChapterStates.Active) + "  " + title;
        }

        /// <summary>#2476: one step row of a mission — "✔ Visit the Sealed Observatory", "● Read the observatory's last log (1/2)"
        /// (running, with a counter only above 1), "○ …" (ahead). The text is the step's chip text with its {0}; a key this client
        /// does not know shows nothing rather than a raw key.</summary>
        public static string MissionStepLine(NetStoryMissionStep step, Func<string, string> localize, Func<string, bool>? has = null)
        {
            string text = !string.IsNullOrEmpty(step.Key) && (has == null || has(step.Key)) ? localize(step.Key) : string.Empty;
            if (!string.IsNullOrEmpty(step.Arg))
            {
                text = Format(text, step.Arg);
            }

            if (step.State == StoryChapterStates.Active && step.Target > 1)
            {
                text += " " + ObjectiveTracker.Counter(step.Progress, step.Target);
            }

            string? state = step.State == StoryChapterStates.Done || step.State == StoryChapterStates.Active ? step.State : null;
            return Icon(state) + "  " + text;
        }

        /// <summary>#2476: the buttons under a running mission — "Skip mission" (the world story's id with the mission's id).</summary>
        public static List<JournalAction> MissionActions(NetStoryMission mission)
        {
            var actions = new List<JournalAction>();
            if (mission.State == StoryChapterStates.Active && mission.Skippable)
            {
                actions.Add(new JournalAction("ui.journal.skip_mission", StoryPackActions.SkipChapter, mission.Id));
            }

            return actions;
        }

        internal static string Format(string template, params object[] args)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template ?? string.Empty, args);
            }
            catch (FormatException)
            {
                return template ?? string.Empty; // a stray brace in a translation must never blank the row
            }
        }
    }

    /// <summary>
    /// The objective tracker's text rules (#2471, synthesis §5.3 "Chip-Text", §5.8 "Chip"): the story's short name replaces
    /// the generic "Objective:" ("Training: Dock at a station", "VEGA: A signal on Kepler-3 b"); the counter shows only for a
    /// target above 1; the optional "Show all objectives" lines are the objective list's other rows (never while the chat is
    /// open, and a story row hides with the VEGA hints like the chip). Pure, unit-tested headless.
    /// </summary>
    public static class ObjectiveTracker
    {
        /// <summary>The counter after an objective — "(2/5)" — or empty for a target of 1 or less.</summary>
        public static string Counter(int progress, int target)
            => target > 1
                ? "(" + Math.Min(Math.Max(progress, 0), target).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
                  + target.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")"
                : string.Empty;

        /// <summary>The short-name key of a story: from the server's story list, else from the client's own copy of the story,
        /// else empty (the label then says "Objective").</summary>
        public static string ShortNameKey(string? packId, StoryPackStates? states, IEnumerable<StoryDefinition>? packs)
        {
            if (string.IsNullOrEmpty(packId))
            {
                return string.Empty;
            }

            var fromServer = states?.Packs.FirstOrDefault(p => string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase));
            if (fromServer != null && !string.IsNullOrEmpty(fromServer.ShortNameKey))
            {
                return fromServer.ShortNameKey;
            }

            var local = packs?.FirstOrDefault(p => string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase));
            return local?.ShortNameKey ?? string.Empty;
        }

        /// <summary>The label before the objective: the story's short name when this client can say it, else "Objective".</summary>
        public static string Prefix(string? shortNameKey, Func<string, string> localize, Func<string, bool>? has = null)
            => !string.IsNullOrEmpty(shortNameKey) && (has == null || has(shortNameKey!))
                ? localize(shortNameKey!)
                : localize("ui.vega.objective");

        /// <summary>The chip line without its counter: "Prefix: text".</summary>
        public static string ChipBody(string prefix, string objectiveText) => (prefix ?? string.Empty) + ": " + (objectiveText ?? string.Empty);

        /// <summary>Whether an objective row hides behind the VEGA hints switch (only the story's coaching does).</summary>
        public static bool Muted(NetObjective row, bool hintsOn)
            => ObjectiveSourceRules.HiddenByHintsMute(ObjectiveSourceRules.Effective(row.Source, row.Key), hintsOn);

        /// <summary>The extra lines under the chip with "Show all objectives" on: the list's rows after the chip's (rows the hints
        /// mute hides are skipped), none while the setting is off or the chat is open.</summary>
        public static List<NetObjective> ExtraRows(ObjectiveList? list, bool showAll, bool chatOpen, bool hintsOn)
        {
            var rows = new List<NetObjective>();
            if (!showAll || chatOpen || list == null)
            {
                return rows;
            }

            for (int i = 1; i < list.Items.Length && i < ObjectiveList.MaxItems; i++)
            {
                if (!Muted(list.Items[i], hintsOn))
                {
                    rows.Add(list.Items[i]);
                }
            }

            return rows;
        }

        /// <summary>The space waypoint a VEGA line suggests (<see cref="ShipAiLine.SpaceWaypointId"/>, the dock lesson's station)
        /// — taken only when the player has none of their own (neither a target nor a free point).</summary>
        public static string? WaypointToAdopt(string? currentId, bool hasFreePoint, string? suggested)
            => !string.IsNullOrEmpty(suggested) && string.IsNullOrEmpty(currentId) && !hasFreePoint ? suggested : null;
    }

    /// <summary>The Settings' "Tutorial: on / off" switch (#2471, synthesis §5.8 "Einstellungen") — it replaced the old
    /// "Skip / Restart tutorial" button. Pure, unit-tested headless.</summary>
    public static class TutorialSwitch
    {
        /// <summary>What the switch shows.</summary>
        public enum State
        {
            /// <summary>No story list from the server (an older one): the old skip / restart button stays.</summary>
            Unknown,

            /// <summary>The tutorial runs for the player (or will start by itself).</summary>
            On,

            /// <summary>The player switched it off — or it never started on an opt-in world.</summary>
            Off,

            /// <summary>The world keeps it off: shown, not switchable.</summary>
            Blocked,

            /// <summary>The world has no tutorial at all: the row is left out.</summary>
            None,
        }

        /// <summary>The first tutorial of the server's story list, or null.</summary>
        public static NetStoryPack? TutorialOf(StoryPackStates? states)
            => states?.Packs.FirstOrDefault(JournalPackPage.IsTutorial);

        /// <summary>The switch's state for the server's story list.</summary>
        public static State Of(StoryPackStates? states)
        {
            if (states == null)
            {
                return State.Unknown;
            }

            var tutorial = TutorialOf(states);
            if (tutorial == null)
            {
                return State.None;
            }

            if (tutorial.Blocked)
            {
                return State.Blocked;
            }

            return tutorial.OptedOut || !tutorial.Enabled ? State.Off : State.On;
        }

        /// <summary>The intent the switch sends: on → <c>optOut</c>, off → <c>optIn</c> (the server starts a tutorial that would
        /// not start by itself), blocked / none / unknown → none.</summary>
        public static string ActionFor(State state) => state switch
        {
            State.On => StoryPackActions.OptOut,
            State.Off => StoryPackActions.OptIn,
            _ => string.Empty,
        };
    }
}
