// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>The pack's id (key-safe, not reserved), kind and scope — and the scope flags every later section reads: a
    /// tutorial is player-scoped, a player pack carries no beats, a world pack has beats and can be the world story.</summary>
    private static void ValidateIdentity(Run run)
    {
        if (!IsKeySafeId(run.Def.Id))
        {
            run.Error("id", $"'{run.Def.Id}' may only use letters, digits, '_' and '-' (it becomes part of milestone and locale keys)");
        }

        if (StoryRegistry.IsReservedPackId(run.Def.Id))
        {
            run.Error("id", $"'{run.Def.Id}' is a reserved pack id ({string.Join(", ", StoryRegistry.ReservedPackIds)})");
        }

        string kind = run.Def.Kind ?? string.Empty;
        if (!new[] { StoryRegistry.KindStory, StoryRegistry.KindTutorial, StoryRegistry.KindSide }.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            run.Error("kind", $"unknown kind '{kind}' (story, tutorial, side)");
        }

        string scope = run.Def.Scope ?? string.Empty;
        if (scope.Length > 0 && !string.Equals(scope, StoryRegistry.ScopeWorld, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scope, StoryRegistry.ScopePlayer, StringComparison.OrdinalIgnoreCase))
        {
            run.Error("scope", $"unknown scope '{scope}' (world, player)");
        }

        run.Tutorial = string.Equals(kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase);
        if (run.Tutorial && string.Equals(scope, StoryRegistry.ScopeWorld, StringComparison.OrdinalIgnoreCase))
        {
            run.Error("scope", "a tutorial is always player-scoped");
        }

        run.PlayerPack = string.Equals(StoryRegistry.EffectiveScope(run.Def), StoryRegistry.ScopePlayer, StringComparison.Ordinal);
        if (run.PlayerPack && run.Def.Beats.Count > 0)
        {
            run.Error("beats", "a player pack carries no beats — it can never be the world story");
        }

        if (!run.PlayerPack && run.Def.Beats.Count == 0)
        {
            run.Error("beats", "a world pack needs beats to be the world story");
        }

        if (!run.PlayerPack && !StoryRegistry.CanBeWorldStory(run.Def))
        {
            run.Error("scope", "this world pack could never be chosen as the world story");
        }
    }

    /// <summary>The beats (the old merge_story.py rules): index = position, thresholds monotonic, a text, no tutorial reward.</summary>
    private static void ValidateBeats(Run run)
    {
        for (int i = 0; i < run.Def.Beats.Count; i++)
        {
            var beat = run.Def.Beats[i];
            if (beat.Index != i)
            {
                run.Error($"beats[{i}]", $"index {beat.Index} is out of order (expected {i})");
            }

            if (i > 0 && beat.Threshold < run.Def.Beats[i - 1].Threshold)
            {
                run.Error($"beats[{i}]", $"threshold {beat.Threshold} is below the previous {run.Def.Beats[i - 1].Threshold}");
            }

            if (string.IsNullOrEmpty(beat.TextKey))
            {
                run.Error($"beats[{i}]", "the beat has no textKey");
            }

            if (run.Tutorial && beat.KnowledgeReward > 0)
            {
                run.Error($"beats[{i}]", "a tutorial pays no rewards (knowledgeReward)");
            }
        }
    }

    /// <summary>The arc's pacing extras (#2475): the lead bonus, the peaceful-world normalisation, the memory triggers.</summary>
    private static void ValidatePacing(Run run)
    {
        if (run.Def.LeadBonusMilestones < 0 || run.Def.LeadBonusMilestones > 10)
        {
            run.Error("leadBonusMilestones", $"{run.Def.LeadBonusMilestones} is out of range (0–10 milestones per chapter)");
        }

        foreach (var (trigger, ti) in (run.Def.MemoryTriggers ?? new List<string>()).Select((t, i) => (t, i)))
        {
            if (!StoryMemoryTriggers.All.Contains(trigger ?? string.Empty, StringComparer.Ordinal))
            {
                run.Error($"memoryTriggers[{ti}]", $"unknown memory trigger '{trigger}' ({string.Join(", ", StoryMemoryTriggers.All)})");
            }
        }

        if (run.PlayerPack && (run.Def.LeadBonusMilestones > 0 || run.Def.NormaliseUnavailableDrivers || (run.Def.MemoryTriggers?.Count ?? 0) > 0))
        {
            run.Warn("leadBonusMilestones", "leadBonusMilestones, normaliseUnavailableDrivers and memoryTriggers are read for world packs only");
        }
    }

    /// <summary>The pack-level conditions: the start condition, the gates and the hints the basic training holds back.</summary>
    private static void ValidateStartAndGates(Run run)
    {
        run.CheckConditions("start.after", run.Def.Start?.After);
        foreach (var gate in run.Def.Gates ?? new Dictionary<string, string>())
        {
            run.CheckCondition($"gates.{gate.Key}", gate.Value);
            if (!StoryGateNames.All.Contains(gate.Key))
            {
                run.Warn($"gates.{gate.Key}", $"unknown gate (known: {string.Join(", ", StoryGateNames.All)})");
            }

            if (!run.Tutorial)
            {
                run.Warn($"gates.{gate.Key}", "only a tutorial pack's gates are read");
            }
        }

        // #2549: the hints the basic training holds back — hint id → the condition that releases it.
        foreach (var hold in run.Def.Gates?.HoldHints ?? new Dictionary<string, string>())
        {
            string hp = $"gates.{StoryGates.HoldHintsField}.{hold.Key}";
            if (string.IsNullOrWhiteSpace(hold.Key) || hold.Key.Any(char.IsWhiteSpace))
            {
                run.Error(hp, "a hint id (the <id> of vega:hint:<id>) without spaces");
            }

            run.CheckCondition(hp, hold.Value);
            if (!run.Tutorial)
            {
                run.Warn(hp, "only a tutorial pack holds hints back");
            }
        }
    }

    /// <summary>The graduation (#2469): its line, its counter, at most three suggested optional chapters, its cutscene.</summary>
    private static void ValidateGraduation(Run run)
    {
        var graduation = run.Def.Graduation ?? new StoryGraduation();
        bool graduates = !string.IsNullOrEmpty(graduation.LineKey) || !string.IsNullOrEmpty(graduation.Counter) || graduation.Suggest.Count > 0;
        if (graduates && !run.PlayerPack)
        {
            run.Warn("graduation", "only a player pack graduates anybody");
        }

        run.Key("graduation.lineKey", graduation.LineKey, run.Tutorial ? BasicLineMaxEn : LineMaxEn, run.Tutorial ? BasicLineMaxDe : LineMaxDe);
        if (!string.IsNullOrEmpty(graduation.Counter)
            && (graduation.Counter.IndexOf(':') <= 0 || !IsKeySafeId(graduation.Counter.Replace(':', '_'))))
        {
            run.Error("graduation.counter", $"'{graduation.Counter}' is not an achievement counter ('<verb>:<thing>')");
        }

        if (graduation.Suggest.Count > StoryGraduation.MaxSuggestions)
        {
            run.Error("graduation.suggest", $"at most {StoryGraduation.MaxSuggestions} suggested chapters ({graduation.Suggest.Count} named)");
        }

        for (int gi = 0; gi < graduation.Suggest.Count; gi++)
        {
            string suggested = graduation.Suggest[gi] ?? string.Empty;
            if (!run.Def.Chapters.Any(c => c.Optional && c.Id == suggested))
            {
                run.Error($"graduation.suggest[{gi}]", $"'{suggested}' is not an optional chapter of this pack");
            }
        }

        run.Cutscene("graduation.cutscene", graduation.Cutscene); // #2473
    }

    /// <summary>A tutorial pays no rewards (the raw document: fields the model does not know count too). Only the pack's own
    /// document counts — a bridged mission ({type: Mission}) keeps the rewards it has in data/missions.json (#2481).</summary>
    private static void ValidateTutorialRewards(Run run, string? rawJson)
    {
        if (run.Tutorial && !string.IsNullOrEmpty(rawJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson!, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                FindRewards(doc.RootElement, "$", run.Error);
            }
            catch (JsonException ex)
            {
                run.Error("$", "story.json is not valid JSON: " + ex.Message);
            }
        }
    }

    /// <summary>Reports every non-empty field named like a reward, and every <c>gift:</c> consequence, in a tutorial's
    /// raw document.</summary>
    private static void FindRewards(JsonElement element, string path, Action<string, string> error)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string childPath = path + "." + property.Name;
                    if (property.Name.IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0 && !IsEmptyValue(property.Value))
                    {
                        error(childPath, "a tutorial pays no rewards");
                    }

                    FindRewards(property.Value, childPath, error);
                }

                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FindRewards(item, $"{path}[{i++}]", error);
                }

                break;
            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                if (path.EndsWith(".consequence", StringComparison.Ordinal) && value.StartsWith("gift:", StringComparison.Ordinal))
                {
                    error(path, "a tutorial pays no rewards (gift consequence)");
                }

                break;
        }
    }

    /// <summary>Whether a reward-named field holds nothing at all: null, false, "", [] or {}. A number is never empty —
    /// <c>"knowledgeReward": 0</c> is still a reward field in a tutorial (#2927).</summary>
    private static bool IsEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false,
    };
}
