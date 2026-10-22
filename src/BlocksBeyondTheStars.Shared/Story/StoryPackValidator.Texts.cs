// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>The pack's own keys and texts, in document order: its name and description, the narrator, the voices and
    /// speakers, the epilogue, the built-in lead texts, a name at all, the short name, the beats, fragments, memories, core
    /// arguments, flavour lines, lore sites, NPC threads, dialogs (no tutorial gift) and insights. The keys are only
    /// collected here — <see cref="ValidateKeyTexts"/> checks them at the end.</summary>
    private static void RegisterPackTexts(Run run)
    {
        run.Key("nameKey", run.Def.NameKey);
        run.Key("descriptionKey", run.Def.DescriptionKey);
        run.Key("narrator.nameKey", run.Def.Narrator?.NameKey);

        ValidateVoicesAndSpeakers(run);

        run.Key("epilogueTitleKey", run.Def.EpilogueTitleKey);
        run.Key("epilogueTextKey", run.Def.EpilogueTextKey);
        if (!run.PlayerPack && run.Def.Chapters.Any(c => c.Leads.Count > 0))
        {
            // #2475: the lead texts the runtime picks on its own — the fallbacks, "explore" for both rule sets, the rules-aware
            // variants.
            foreach (string builtIn in new[]
            {
                StoryLeads.FragmentOnKey, StoryLeads.FragmentHereKey, StoryLeads.ObjPrefix + StoryLeadTypes.SettlementHelp,
                StoryLeads.ExploreKey, StoryLeads.ExplorePeacefulKey, StoryLeads.FinaleJumpKey,
            })
            {
                run.Key("chapters.leads", builtIn, ChipMaxEn, ChipMaxDe);
            }
        }

        if (string.IsNullOrEmpty(run.Def.NameKey))
        {
            run.Error("nameKey", "the pack has no nameKey");
        }

        if (!string.IsNullOrEmpty(run.Def.ShortNameKey))
        {
            run.Key("shortNameKey", run.Def.ShortNameKey, ShortNameMax, ShortNameMax);
        }
        else if (run.Def.Chapters.Count > 0 && run.PlayerPack)
        {
            run.Warn("shortNameKey", "a player pack without a short name shows \"Objective:\" on the chip");
        }

        for (int i = 0; i < run.Def.Beats.Count; i++)
        {
            run.Key($"beats[{i}].textKey", run.Def.Beats[i].TextKey);
        }

        foreach (var (f, i) in run.Def.Fragments.Select((f, i) => (f, i)))
        {
            run.Key($"fragments[{i}].textKey", f.TextKey);
        }

        foreach (var (m, i) in run.Def.Memories.Select((m, i) => (m, i)))
        {
            run.Key($"memories[{i}].textKey", m.TextKey);
        }

        foreach (var (a, i) in run.Def.CoreArguments.Select((a, i) => (a, i)))
        {
            run.Key($"coreArguments[{i}].promptKey", a.PromptKey);
            foreach (var (c, j) in a.Choices.Select((c, j) => (c, j)))
            {
                run.Key($"coreArguments[{i}].choices[{j}].textKey", c.TextKey);
                run.Key($"coreArguments[{i}].choices[{j}].responseKey", c.ResponseKey);
            }
        }

        foreach (var (l, i) in run.Def.FlavourLines.Select((l, i) => (l, i)))
        {
            run.Key($"flavourLines[{i}].textKey", l.TextKey);
        }

        foreach (var (l, i) in run.Def.LoreSites.Select((l, i) => (l, i)))
        {
            run.Key($"loreSites[{i}].textKey", l.TextKey);
        }

        foreach (var (t, i) in run.Def.NpcThreads.Select((t, i) => (t, i)))
        {
            run.Key($"npcThreads[{i}].textKey", t.TextKey);
        }

        foreach (var (d, i) in run.Def.Dialogs.Select((d, i) => (d, i)))
        {
            foreach (var (n, j) in d.Nodes.Select((n, j) => (n, j)))
            {
                run.Key($"dialogs[{i}].nodes[{j}].promptKey", n.PromptKey);
                foreach (var (c, k) in n.Choices.Select((c, k) => (c, k)))
                {
                    run.Key($"dialogs[{i}].nodes[{j}].choices[{k}].textKey", c.TextKey);
                    run.Key($"dialogs[{i}].nodes[{j}].choices[{k}].responseKey", c.ResponseKey);
                    if (run.Tutorial && (c.Consequence ?? string.Empty).StartsWith("gift:", StringComparison.Ordinal))
                    {
                        run.Error($"dialogs[{i}].nodes[{j}].choices[{k}]", "a tutorial pays no rewards (gift consequence)");
                    }
                }
            }
        }

        foreach (var (insight, i) in run.Def.Insights.Select((x, i) => (x, i)))
        {
            if (string.IsNullOrEmpty(insight.Event))
            {
                run.Error($"insights[{i}]", "an insight needs an event");
            }

            run.Key($"insights[{i}].textKey", insight.TextKey);
        }
    }

    /// <summary>The narrator's voice and line-start cue, and the pack's other voices (#2478).</summary>
    private static void ValidateVoicesAndSpeakers(Run run)
    {
        run.CheckVoice("narrator", run.Def.Narrator);
        if (!string.IsNullOrEmpty(run.Def.Narrator?.LineStart) && run.Context.SoundExists is { } narratorSound && !narratorSound(run.Def.Narrator!.LineStart))
        {
            run.Error("narrator.lineStart", $"sound '{run.Def.Narrator.LineStart}' does not exist in client/Assets/Resources/audio");
        }

        // #2478: the pack's other voices (a cutscene caption's `speaker`, the finale's duel dialogue) — ids unique next to the
        // narrator's, a name in both languages, a colour the client can read, a line-start cue that exists.
        var speakerIds = new HashSet<string>(StringComparer.Ordinal) { run.Def.Narrator?.Id ?? StoryNarrator.DefaultId };
        foreach (var (speaker, si) in (run.Def.Speakers ?? new List<StoryNarrator>()).Select((s, i) => (s, i)))
        {
            string sp = $"speakers[{si}]";
            if (speaker is null || !IsKeySafeId(speaker.Id) || !speakerIds.Add(speaker.Id))
            {
                run.Error(sp, $"speaker id '{speaker?.Id}' is empty, not key-safe or used twice (the narrator's id included)");
                continue;
            }

            if (string.IsNullOrEmpty(speaker.NameKey))
            {
                run.Error($"{sp}.nameKey", "a speaker needs a name (a locale key)");
            }

            run.Key($"{sp}.nameKey", speaker.NameKey);
            if (!string.IsNullOrEmpty(speaker.Color) && !IsHexColor(speaker.Color))
            {
                run.Error($"{sp}.color", $"'{speaker.Color}' is not a #RRGGBB colour");
            }

            if (!string.IsNullOrEmpty(speaker.LineStart) && run.Context.SoundExists is { } speakerSound && !speakerSound(speaker.LineStart))
            {
                run.Error($"{sp}.lineStart", $"sound '{speaker.LineStart}' does not exist in client/Assets/Resources/audio");
            }

            run.CheckVoice(sp, speaker);
        }
    }

    /// <summary>Every collected key exists in English and German, within its budget.</summary>
    private static void ValidateKeyTexts(Run run)
    {
        if (run.Context.English is { } en && run.Context.German is { } de)
        {
            foreach (var (path, key, maxEn, maxDe) in run.Keys)
            {
                string? textEn = en(key);
                string? textDe = de(key);
                if (textEn is null)
                {
                    run.Error(path, $"key '{key}' is missing in English");
                }

                if (textDe is null)
                {
                    run.Error(path, $"key '{key}' is missing in German");
                }

                if (maxEn > 0 && textEn is not null && ShownLength(textEn) > maxEn)
                {
                    run.Error(path, $"'{key}' is {ShownLength(textEn)} characters in English (budget {maxEn})");
                }

                if (maxDe > 0 && textDe is not null && ShownLength(textDe) > maxDe)
                {
                    run.Error(path, $"'{key}' is {ShownLength(textDe)} characters in German (budget {maxDe})");
                }
            }
        }
    }
}
