// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>One <see cref="Validate"/> call: the pack and its context, the issues in the order they are found, the locale
    /// keys collected for the final English/German check, the pack's scope flags, the id sets the sections share — and the
    /// small checks every section uses (a condition, a key, a voice, a cutscene binding).</summary>
    private sealed class Run
    {
        public Run(StoryDefinition def, StoryPackValidationContext context)
        {
            Def = def;
            Context = context;
        }

        public StoryDefinition Def { get; }

        public StoryPackValidationContext Context { get; }

        /// <summary>Every issue so far, in the order found — the result of <see cref="Validate"/>.</summary>
        public List<StoryPackIssue> Issues { get; } = new();

        /// <summary>Every locale key the sections name, in order — checked in English and German only at the end.</summary>
        public List<(string Path, string Key, int MaxEn, int MaxDe)> Keys { get; } = new();

        /// <summary>Whether the pack is a tutorial (set with the pack's identity, before every other section).</summary>
        public bool Tutorial { get; set; }

        /// <summary>Whether the pack is player-scoped (set with the pack's identity, before every other section).</summary>
        public bool PlayerPack { get; set; }

        /// <summary>The chapter ids seen so far (a cutscene's <c>chapter</c> must name one).</summary>
        public HashSet<string> ChapterIds { get; } = new(StringComparer.Ordinal);

        /// <summary>The step ids seen so far — chapters' and missions' steps share one id space.</summary>
        public HashSet<string> StepIds { get; } = new(StringComparer.Ordinal);

        /// <summary>The legacy keys seen so far (tutorial only).</summary>
        public HashSet<string> LegacyKeys { get; } = new(StringComparer.Ordinal);

        /// <summary>The story-mission ids seen so far.</summary>
        public HashSet<string> MissionIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Every chapter id of the pack (a mission id may be none of them); filled when the chapters are checked.</summary>
        public HashSet<string> AllChapterIds { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Reports an error at <paramref name="path"/>.</summary>
        public void Error(string path, string message) => Issues.Add(new StoryPackIssue(StoryPackIssueSeverity.Error, path, message));

        /// <summary>Reports a warning at <paramref name="path"/>.</summary>
        public void Warn(string path, string message) => Issues.Add(new StoryPackIssue(StoryPackIssueSeverity.Warning, path, message));

        /// <summary>A condition parses and every pack, chapter, step, blueprint and lore text it names exists.</summary>
        public void CheckCondition(string path, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Error(path, "an empty condition");
                return;
            }

            if (!StoryConditionParser.TryParse(text!, out var condition, out string error, Context.WorldPredicates))
            {
                Error(path, error);
                return;
            }

            foreach (var atom in condition!.Atoms())
            {
                CheckAtom(path, atom);
            }
        }

        private void CheckAtom(string path, StoryCondition atom)
        {
            switch (atom.Kind)
            {
                case StoryConditionKind.Blueprint:
                    if (Context.BlueprintExists is { } bp && !bp(atom.Id))
                    {
                        Error(path, $"blueprint '{atom.Id}' does not exist");
                    }

                    break;
                case StoryConditionKind.Step:
                case StoryConditionKind.Chapter:
                case StoryConditionKind.Pack:
                    var target = string.Equals(atom.PackId, Def.Id, StringComparison.OrdinalIgnoreCase)
                        ? Def
                        : Context.Packs is { } packs && packs.TryGetValue(atom.PackId, out var other) ? other : null;
                    if (target is null)
                    {
                        if (Context.Packs is not null)
                        {
                            Error(path, $"pack '{atom.PackId}' is not installed");
                        }

                        break;
                    }

                    // #2476: a world story's missions count as chapters (they share the chapter keys) and their steps as steps.
                    if (atom.Kind == StoryConditionKind.Step
                        && !target.Chapters.SelectMany(c => c.Steps.Concat(c.Missions.Where(m => m is not null).SelectMany(m => m.Steps))).Any(s => s.Id == atom.Id))
                    {
                        Error(path, $"pack '{atom.PackId}' has no step '{atom.Id}'");
                    }
                    else if (atom.Kind == StoryConditionKind.Chapter
                             && !target.Chapters.Any(c => c.Id == atom.Id || c.Missions.Any(m => m is not null && m.Id == atom.Id)))
                    {
                        Error(path, $"pack '{atom.PackId}' has no chapter '{atom.Id}'");
                    }

                    break;
                case StoryConditionKind.Lore:
                    // #2476: a lore text of any installed pack (lore_sites.json), else of this pack.
                    var loreExists = Context.LoreExists
                        ?? (Def.LoreSites.Count > 0 ? key => Def.LoreSites.Any(l => l.Key == key) : (Func<string, bool>?)null);
                    if (loreExists is not null && !loreExists(atom.Id))
                    {
                        Error(path, $"lore text '{atom.Id}' does not exist in any lore_sites.json");
                    }

                    break;
            }
        }

        /// <summary><see cref="CheckCondition"/> for every entry of a condition list (null = none).</summary>
        public void CheckConditions(string path, List<string>? list)
        {
            if (list is null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CheckCondition($"{path}[{i}]", list[i]);
            }
        }

        /// <summary>Collects a locale key (with its English/German budget, 0 = none) for the final key check; an empty key is
        /// skipped.</summary>
        public void Key(string path, string? key, int maxEn = 0, int maxDe = 0)
        {
            if (!string.IsNullOrEmpty(key))
            {
                Keys.Add((path, key!, maxEn, maxDe));
            }
        }

        // #2495: the voices the client plays — every chatter clip a narrator or a speaker names (`voice`, played while its
        // speaker box types) and the narrator's line-start cue exist in client/Assets/Resources/audio.
        public void CheckVoice(string path, StoryNarrator? voice)
        {
            foreach (var (clip, ci) in (voice?.Voice ?? new List<string>()).Select((c, i) => (c, i)))
            {
                if (string.IsNullOrEmpty(clip))
                {
                    Error($"{path}.voice[{ci}]", "a voice clip needs a name");
                }
                else if (Context.SoundExists is { } voiceSound && !voiceSound(clip))
                {
                    Error($"{path}.voice[{ci}]", $"sound '{clip}' does not exist in client/Assets/Resources/audio");
                }
            }
        }

        private bool OwnCutscene(string id) => Def.FindCutscene(id) is not null || (Context.CutsceneExists?.Invoke(Def.Id, id) ?? false);

        /// <summary>A cutscene binding names a cutscene of the pack — never a built-in one or an instance.</summary>
        public void Cutscene(string path, string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (StoryCutsceneIds.IsBuiltIn(id))
            {
                Error(path, $"'{id}' is built in — the server plays it, a pack never binds it");
            }
            else if (id!.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0 || !OwnCutscene(id))
            {
                Error(path, $"cutscene '{id}' is not defined in the pack's cutscenes.json");
            }
        }
    }
}
