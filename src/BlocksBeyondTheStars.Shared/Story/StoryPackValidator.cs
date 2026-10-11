// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>How bad a <see cref="StoryPackIssue"/> is.</summary>
public enum StoryPackIssueSeverity
{
    /// <summary>The pack is broken or breaks a rule — tests fail on it.</summary>
    Error,

    /// <summary>Worth a look (an event key not raised yet, a gate a non-tutorial pack cannot use).</summary>
    Warning,
}

/// <summary>One finding of the <see cref="StoryPackValidator"/>: where in the pack (<c>chapters[2].steps[0]</c>) and what.</summary>
public sealed class StoryPackIssue
{
    public StoryPackIssue(StoryPackIssueSeverity severity, string path, string message)
    {
        Severity = severity;
        Path = path;
        Message = message;
    }

    public StoryPackIssueSeverity Severity { get; }

    public string Path { get; }

    public string Message { get; }

    public override string ToString() => $"{Severity}: {Path}: {Message}";
}

/// <summary>What the validator checks a pack against — the content around it. Every lookup is optional: a missing one
/// skips its check (a tool without the game's content can still check the pack's own structure).</summary>
public sealed class StoryPackValidationContext
{
    /// <summary>The known <c>world:</c> predicates (default: <see cref="StoryWorldPredicates.All"/>).</summary>
    public IReadOnlyCollection<string> WorldPredicates { get; set; } = StoryWorldPredicates.All;

    /// <summary>Every installed pack by id (for <c>step:</c>/<c>chapter:</c>/<c>pack:</c> references); null = skip.</summary>
    public IReadOnlyDictionary<string, StoryDefinition>? Packs { get; set; }

    /// <summary>Whether a mission id exists in <c>data/missions.json</c>; null = skip.</summary>
    public Func<string, bool>? MissionExists { get; set; }

    /// <summary>Whether a blueprint key exists; null = skip.</summary>
    public Func<string, bool>? BlueprintExists { get; set; }

    /// <summary>Whether a block key exists; null = skip.</summary>
    public Func<string, bool>? BlockExists { get; set; }

    /// <summary>The English text of a key, or null when it is missing; null = skip the key checks.</summary>
    public Func<string, string?>? English { get; set; }

    /// <summary>The German text of a key, or null when it is missing; null = skip the key checks.</summary>
    public Func<string, string?>? German { get; set; }

    /// <summary>Whether a cutscene id exists in a pack (<c>(packId, cutsceneId)</c>); null = cutscenes are not modelled yet
    /// (until step 8) and the binding check is skipped.</summary>
    public Func<string, string, bool>? CutsceneExists { get; set; }
}

/// <summary>
/// The story-pack validator (#2466, synthesis §5.9) — the C# twin of <c>tools/validate_story_pack.py</c>, used by
/// <c>StoryPackValidationTests</c> for every pack under <c>data/stories/</c>. It checks: id rules (not reserved, key-safe),
/// <c>kind</c>/<c>scope</c> (a tutorial is player-scoped, a player pack carries no beats so it can never be the world story,
/// a world pack has beats), every condition parses and names existing packs/chapters/steps/blueprints, mission-bridge
/// targets exist, event keys are published (warning otherwise), <c>ui:</c> only in a player pack's step objectives, marker
/// providers, legacy keys (tutorial only), alternatives, a tutorial pays no rewards, every locale key exists in English and
/// German, cutscene bindings resolve (once cutscenes exist), the short name fits (≤ 10 characters), and the text budget
/// (basic-training lines ≤ 120 EN / 145 DE, other step lines ≤ 200 / 240, chip ≤ 45 / 53; a <c>{key:…}</c> token counts
/// 3 characters, a <c>{0}</c> slot 10). Beats keep the old rules (index = position, thresholds monotonic). It never changes
/// the pack.
/// </summary>
public static class StoryPackValidator
{
    /// <summary>Longest short name (EN and DE).</summary>
    public const int ShortNameMax = 10;

    /// <summary>Basic-training line budget (a tutorial's non-optional chapters), English.</summary>
    public const int BasicLineMaxEn = 120;

    /// <summary>Basic-training line budget, German.</summary>
    public const int BasicLineMaxDe = 145;

    /// <summary>Other step line budget (one VEGA panel page), English.</summary>
    public const int LineMaxEn = 200;

    /// <summary>Other step line budget, German.</summary>
    public const int LineMaxDe = 240;

    /// <summary>Chip text budget, English.</summary>
    public const int ChipMaxEn = 45;

    /// <summary>Chip text budget, German.</summary>
    public const int ChipMaxDe = 53;

    /// <summary>What a <c>{key:…}</c> token counts.</summary>
    public const int TokenWidth = 3;

    /// <summary>What a <c>{0}</c> slot counts.</summary>
    public const int SlotWidth = 10;

    /// <summary>Validates one pack. <paramref name="rawJson"/> (the pack's <c>story.json</c>) enables the reward check on the
    /// raw document (fields the model does not know are invisible otherwise).</summary>
    public static List<StoryPackIssue> Validate(StoryDefinition def, StoryPackValidationContext? context = null, string? rawJson = null)
    {
        context ??= new StoryPackValidationContext();
        var issues = new List<StoryPackIssue>();
        void Error(string path, string message) => issues.Add(new StoryPackIssue(StoryPackIssueSeverity.Error, path, message));
        void Warn(string path, string message) => issues.Add(new StoryPackIssue(StoryPackIssueSeverity.Warning, path, message));

        // ---- Identity, kind, scope ----
        if (string.IsNullOrEmpty(def.Id))
        {
            Error("id", "the pack has no id");
            return issues;
        }

        if (!IsKeySafeId(def.Id))
        {
            Error("id", $"'{def.Id}' may only use letters, digits, '_' and '-' (it becomes part of milestone and locale keys)");
        }

        if (StoryRegistry.IsReservedPackId(def.Id))
        {
            Error("id", $"'{def.Id}' is a reserved pack id ({string.Join(", ", StoryRegistry.ReservedPackIds)})");
        }

        string kind = def.Kind ?? string.Empty;
        if (!new[] { StoryRegistry.KindStory, StoryRegistry.KindTutorial, StoryRegistry.KindSide }.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            Error("kind", $"unknown kind '{kind}' (story, tutorial, side)");
        }

        string scope = def.Scope ?? string.Empty;
        if (scope.Length > 0 && !string.Equals(scope, StoryRegistry.ScopeWorld, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scope, StoryRegistry.ScopePlayer, StringComparison.OrdinalIgnoreCase))
        {
            Error("scope", $"unknown scope '{scope}' (world, player)");
        }

        bool tutorial = string.Equals(kind, StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase);
        if (tutorial && string.Equals(scope, StoryRegistry.ScopeWorld, StringComparison.OrdinalIgnoreCase))
        {
            Error("scope", "a tutorial is always player-scoped");
        }

        bool playerPack = string.Equals(StoryRegistry.EffectiveScope(def), StoryRegistry.ScopePlayer, StringComparison.Ordinal);
        if (playerPack && def.Beats.Count > 0)
        {
            Error("beats", "a player pack carries no beats — it can never be the world story");
        }

        if (!playerPack && def.Beats.Count == 0)
        {
            Error("beats", "a world pack needs beats to be the world story");
        }

        if (!playerPack && !StoryRegistry.CanBeWorldStory(def))
        {
            Error("scope", "this world pack could never be chosen as the world story");
        }

        // ---- Beats (the old merge_story.py rules) ----
        for (int i = 0; i < def.Beats.Count; i++)
        {
            var beat = def.Beats[i];
            if (beat.Index != i)
            {
                Error($"beats[{i}]", $"index {beat.Index} is out of order (expected {i})");
            }

            if (i > 0 && beat.Threshold < def.Beats[i - 1].Threshold)
            {
                Error($"beats[{i}]", $"threshold {beat.Threshold} is below the previous {def.Beats[i - 1].Threshold}");
            }

            if (string.IsNullOrEmpty(beat.TextKey))
            {
                Error($"beats[{i}]", "the beat has no textKey");
            }

            if (tutorial && beat.KnowledgeReward > 0)
            {
                Error($"beats[{i}]", "a tutorial pays no rewards (knowledgeReward)");
            }
        }

        // ---- Conditions ----
        void CheckCondition(string path, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Error(path, "an empty condition");
                return;
            }

            if (!StoryConditionParser.TryParse(text!, out var condition, out string error, context.WorldPredicates))
            {
                Error(path, error);
                return;
            }

            foreach (var atom in condition!.Atoms())
            {
                CheckAtom(path, atom);
            }
        }

        void CheckAtom(string path, StoryCondition atom)
        {
            switch (atom.Kind)
            {
                case StoryConditionKind.Blueprint:
                    if (context.BlueprintExists is { } bp && !bp(atom.Id))
                    {
                        Error(path, $"blueprint '{atom.Id}' does not exist");
                    }

                    break;
                case StoryConditionKind.Step:
                case StoryConditionKind.Chapter:
                case StoryConditionKind.Pack:
                    var target = string.Equals(atom.PackId, def.Id, StringComparison.OrdinalIgnoreCase)
                        ? def
                        : context.Packs is { } packs && packs.TryGetValue(atom.PackId, out var other) ? other : null;
                    if (target is null)
                    {
                        if (context.Packs is not null)
                        {
                            Error(path, $"pack '{atom.PackId}' is not installed");
                        }

                        break;
                    }

                    if (atom.Kind == StoryConditionKind.Step && !target.Chapters.SelectMany(c => c.Steps).Any(s => s.Id == atom.Id))
                    {
                        Error(path, $"pack '{atom.PackId}' has no step '{atom.Id}'");
                    }
                    else if (atom.Kind == StoryConditionKind.Chapter && !target.Chapters.Any(c => c.Id == atom.Id))
                    {
                        Error(path, $"pack '{atom.PackId}' has no chapter '{atom.Id}'");
                    }

                    break;
            }
        }

        void CheckConditions(string path, List<string>? list)
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

        CheckConditions("start.after", def.Start?.After);
        foreach (var gate in def.Gates ?? new Dictionary<string, string>())
        {
            CheckCondition($"gates.{gate.Key}", gate.Value);
            if (!StoryGateNames.All.Contains(gate.Key))
            {
                Warn($"gates.{gate.Key}", $"unknown gate (known: {string.Join(", ", StoryGateNames.All)})");
            }

            if (!tutorial)
            {
                Warn($"gates.{gate.Key}", "only a tutorial pack's gates are read");
            }
        }

        // ---- Keys and texts ----
        var keys = new List<(string Path, string Key, int MaxEn, int MaxDe)>();
        void Key(string path, string? key, int maxEn = 0, int maxDe = 0)
        {
            if (!string.IsNullOrEmpty(key))
            {
                keys.Add((path, key!, maxEn, maxDe));
            }
        }

        Key("nameKey", def.NameKey);
        Key("descriptionKey", def.DescriptionKey);
        Key("narrator.nameKey", def.Narrator?.NameKey);
        Key("epilogueTitleKey", def.EpilogueTitleKey);
        Key("epilogueTextKey", def.EpilogueTextKey);
        if (string.IsNullOrEmpty(def.NameKey))
        {
            Error("nameKey", "the pack has no nameKey");
        }

        if (!string.IsNullOrEmpty(def.ShortNameKey))
        {
            Key("shortNameKey", def.ShortNameKey, ShortNameMax, ShortNameMax);
        }
        else if (def.Chapters.Count > 0 && playerPack)
        {
            Warn("shortNameKey", "a player pack without a short name shows \"Objective:\" on the chip");
        }

        for (int i = 0; i < def.Beats.Count; i++)
        {
            Key($"beats[{i}].textKey", def.Beats[i].TextKey);
        }

        foreach (var (f, i) in def.Fragments.Select((f, i) => (f, i)))
        {
            Key($"fragments[{i}].textKey", f.TextKey);
        }

        foreach (var (m, i) in def.Memories.Select((m, i) => (m, i)))
        {
            Key($"memories[{i}].textKey", m.TextKey);
        }

        foreach (var (a, i) in def.CoreArguments.Select((a, i) => (a, i)))
        {
            Key($"coreArguments[{i}].promptKey", a.PromptKey);
            foreach (var (c, j) in a.Choices.Select((c, j) => (c, j)))
            {
                Key($"coreArguments[{i}].choices[{j}].textKey", c.TextKey);
                Key($"coreArguments[{i}].choices[{j}].responseKey", c.ResponseKey);
            }
        }

        foreach (var (l, i) in def.FlavourLines.Select((l, i) => (l, i)))
        {
            Key($"flavourLines[{i}].textKey", l.TextKey);
        }

        foreach (var (l, i) in def.LoreSites.Select((l, i) => (l, i)))
        {
            Key($"loreSites[{i}].textKey", l.TextKey);
        }

        foreach (var (t, i) in def.NpcThreads.Select((t, i) => (t, i)))
        {
            Key($"npcThreads[{i}].textKey", t.TextKey);
        }

        foreach (var (d, i) in def.Dialogs.Select((d, i) => (d, i)))
        {
            foreach (var (n, j) in d.Nodes.Select((n, j) => (n, j)))
            {
                Key($"dialogs[{i}].nodes[{j}].promptKey", n.PromptKey);
                foreach (var (c, k) in n.Choices.Select((c, k) => (c, k)))
                {
                    Key($"dialogs[{i}].nodes[{j}].choices[{k}].textKey", c.TextKey);
                    Key($"dialogs[{i}].nodes[{j}].choices[{k}].responseKey", c.ResponseKey);
                    if (tutorial && (c.Consequence ?? string.Empty).StartsWith("gift:", StringComparison.Ordinal))
                    {
                        Error($"dialogs[{i}].nodes[{j}].choices[{k}]", "a tutorial pays no rewards (gift consequence)");
                    }
                }
            }
        }

        foreach (var (insight, i) in def.Insights.Select((x, i) => (x, i)))
        {
            if (string.IsNullOrEmpty(insight.Event))
            {
                Error($"insights[{i}]", "an insight needs an event");
            }

            Key($"insights[{i}].textKey", insight.TextKey);
        }

        // ---- Cutscene bindings ----
        void Cutscene(string path, string? id)
        {
            if (!string.IsNullOrEmpty(id) && context.CutsceneExists is { } exists && !exists(def.Id, id!))
            {
                Error(path, $"cutscene '{id}' is not defined in the pack's cutscenes.json");
            }
        }

        Cutscene("introCutscene", def.IntroCutscene);
        foreach (var (beat, i) in def.Beats.Select((b, i) => (b, i)))
        {
            Cutscene($"beats[{i}].cutscene", beat.Cutscene);
        }

        foreach (var binding in def.Finale?.Cutscenes ?? new Dictionary<string, string>())
        {
            Cutscene($"finale.cutscenes.{binding.Key}", binding.Value);
        }

        // ---- Chapters and steps ----
        var chapterIds = new HashSet<string>(StringComparer.Ordinal);
        var stepIds = new HashSet<string>(StringComparer.Ordinal);
        var legacyKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int ci = 0; ci < def.Chapters.Count; ci++)
        {
            var ch = def.Chapters[ci];
            string cp = $"chapters[{ci}]";
            if (string.IsNullOrEmpty(ch.Id) || !IsKeySafeId(ch.Id, allowDot: true) || !chapterIds.Add(ch.Id))
            {
                Error(cp, $"chapter id '{ch.Id}' is empty, not key-safe or used twice");
            }

            if (string.IsNullOrEmpty(ch.TitleKey))
            {
                Error(cp, "the chapter has no titleKey");
            }

            Key($"{cp}.titleKey", ch.TitleKey);
            Key($"{cp}.summaryKey", ch.SummaryKey);
            CheckConditions($"{cp}.requires", ch.Requires);
            CheckConditions($"{cp}.offerWhen", ch.OfferWhen);
            CheckConditions($"{cp}.autoCompleteIf", ch.AutoCompleteIf);
            if (ch.OfferWhen.Count > 0 && !ch.Optional)
            {
                Warn($"{cp}.offerWhen", "only an optional chapter is offered");
            }

            Cutscene($"{cp}.openCutscene", ch.OpenCutscene);
            Cutscene($"{cp}.closeCutscene", ch.CloseCutscene);
            foreach (var (lead, li) in ch.Leads.Select((l, i) => (l, i)))
            {
                Key($"{cp}.leads[{li}].objKey", lead.ObjKey, ChipMaxEn, ChipMaxDe);
            }

            if (playerPack && ch.Steps.Count == 0)
            {
                Warn(cp, "a chapter of a player pack without steps completes at once");
            }

            bool basic = tutorial && !ch.Optional;
            int lineEn = basic ? BasicLineMaxEn : LineMaxEn;
            int lineDe = basic ? BasicLineMaxDe : LineMaxDe;
            for (int si = 0; si < ch.Steps.Count; si++)
            {
                var step = ch.Steps[si];
                string sp = $"{cp}.steps[{si}]";
                if (string.IsNullOrEmpty(step.Id) || !IsKeySafeId(step.Id, allowDot: true) || !stepIds.Add(step.Id))
                {
                    Error(sp, $"step id '{step.Id}' is empty, not key-safe or used twice in the pack");
                }

                string stepKind = (step.Kind ?? string.Empty).ToLowerInvariant();
                if (!StoryStepKinds.All.Contains(stepKind))
                {
                    Error($"{sp}.kind", $"unknown kind '{step.Kind}' (do, read, react)");
                }

                if (!StoryStepTiers.All.Contains((step.Tier ?? string.Empty).ToLowerInvariant()))
                {
                    Error($"{sp}.tier", $"unknown tier '{step.Tier}' (must, core, opt)");
                }

                var objective = step.Objective ?? new StoryStepObjective();
                if (objective.IsMission)
                {
                    if (string.IsNullOrEmpty(objective.Target))
                    {
                        Error($"{sp}.objective", "a mission bridge needs a target mission");
                    }
                    else if (context.MissionExists is { } missionExists && !missionExists(objective.Target))
                    {
                        Error($"{sp}.objective", $"mission '{objective.Target}' does not exist in data/missions.json");
                    }
                }
                else if (!string.IsNullOrEmpty(objective.Type))
                {
                    Error($"{sp}.objective.type", $"unknown objective type '{objective.Type}' (Mission, or none for an event count)");
                }
                else if (string.IsNullOrEmpty(objective.Event))
                {
                    if (stepKind != StoryStepKinds.Read)
                    {
                        Error($"{sp}.objective", "a do or react step needs an event or a mission bridge");
                    }
                }
                else
                {
                    CheckEvent($"{sp}.objective.event", objective.Event, playerPack, Error, Warn);
                }

                if (objective.Required < 1)
                {
                    Error($"{sp}.objective.required", "required must be at least 1");
                }

                if (!string.IsNullOrEmpty(step.LegacyKey))
                {
                    if (!tutorial)
                    {
                        Error($"{sp}.legacyKey", "only a tutorial pack may name legacy keys");
                    }
                    else if (StoryLegacy.StageIdOf(step.LegacyKey) is null)
                    {
                        Error($"{sp}.legacyKey", $"'{step.LegacyKey}' is not one of the legacy stages ({StoryLegacy.StagePrefix}{string.Join("|", StoryLegacy.StageIds)})");
                    }
                    else if (!legacyKeys.Add(step.LegacyKey))
                    {
                        Error($"{sp}.legacyKey", $"'{step.LegacyKey}' is used by two steps");
                    }
                }

                CheckConditions($"{sp}.requires", step.Requires);
                CheckConditions($"{sp}.feasibleIf", step.FeasibleIf);
                CheckConditions($"{sp}.alreadyDoneIf", step.AlreadyDoneIf);
                if (!string.IsNullOrEmpty(step.Alternative)
                    && (step.Alternative == step.Id || !ch.Steps.Any(s => s.Id == step.Alternative)))
                {
                    Error($"{sp}.alternative", $"'{step.Alternative}' is not another step of chapter '{ch.Id}'");
                }

                string reveal = step.OnStart?.Reveal ?? string.Empty;
                if (reveal.Length > 0)
                {
                    if (!StoryRevealProviders.IsKnown(reveal))
                    {
                        Error($"{sp}.onStart.reveal", $"unknown marker provider '{reveal}' ({StoryRevealProviders.NearestOre}:<block>, {StoryRevealProviders.NearestSettlement})");
                    }
                    else if (reveal.StartsWith(StoryRevealProviders.NearestOre + ":", StringComparison.Ordinal)
                             && context.BlockExists is { } blockExists
                             && !blockExists(reveal.Substring(StoryRevealProviders.NearestOre.Length + 1)))
                    {
                        Error($"{sp}.onStart.reveal", $"block '{reveal.Substring(StoryRevealProviders.NearestOre.Length + 1)}' does not exist");
                    }
                }

                Cutscene($"{sp}.onStart.cutscene", step.OnStart?.Cutscene);
                Cutscene($"{sp}.onComplete.cutscene", step.OnComplete?.Cutscene);

                var explain = step.Explain ?? new StoryStepExplain();
                Key($"{sp}.explain.startKey", explain.StartKey, lineEn, lineDe);
                Key($"{sp}.explain.hintKey", explain.HintKey, lineEn, lineDe);
                Key($"{sp}.explain.doneKey", explain.DoneKey, lineEn, lineDe);
                Key($"{sp}.explain.chipKey", explain.ChipKey, ChipMaxEn, ChipMaxDe);
                if (stepKind == StoryStepKinds.Do && string.IsNullOrEmpty(explain.ChipKey) && step.Variants.All(v => string.IsNullOrEmpty(v.ChipKey)))
                {
                    Warn($"{sp}.explain.chipKey", "a do step without a chip text leaves the chip to the next source");
                }

                foreach (var (variant, vi) in step.Variants.Select((v, i) => (v, i)))
                {
                    string vp = $"{sp}.variants[{vi}]";
                    CheckCondition($"{vp}.when", variant.When);
                    Key($"{vp}.startKey", variant.StartKey, lineEn, lineDe);
                    Key($"{vp}.hintKey", variant.HintKey, lineEn, lineDe);
                    Key($"{vp}.doneKey", variant.DoneKey, lineEn, lineDe);
                    Key($"{vp}.chipKey", variant.ChipKey, ChipMaxEn, ChipMaxDe);
                }
            }
        }

        // ---- Every key in English and German, within its budget ----
        if (context.English is { } en && context.German is { } de)
        {
            foreach (var (path, key, maxEn, maxDe) in keys)
            {
                string? textEn = en(key);
                string? textDe = de(key);
                if (textEn is null)
                {
                    Error(path, $"key '{key}' is missing in English");
                }

                if (textDe is null)
                {
                    Error(path, $"key '{key}' is missing in German");
                }

                if (maxEn > 0 && textEn is not null && ShownLength(textEn) > maxEn)
                {
                    Error(path, $"'{key}' is {ShownLength(textEn)} characters in English (budget {maxEn})");
                }

                if (maxDe > 0 && textDe is not null && ShownLength(textDe) > maxDe)
                {
                    Error(path, $"'{key}' is {ShownLength(textDe)} characters in German (budget {maxDe})");
                }
            }
        }

        // ---- A tutorial pays no rewards (the raw document: fields the model does not know count too) ----
        if (tutorial && !string.IsNullOrEmpty(rawJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson!, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                FindRewards(doc.RootElement, "$", Error);
            }
            catch (JsonException ex)
            {
                Error("$", "story.json is not valid JSON: " + ex.Message);
            }
        }

        return issues;
    }

    /// <summary>The characters a line shows: a <c>{key:…}</c> token as <see cref="TokenWidth"/>, a <c>{0}</c> slot as
    /// <see cref="SlotWidth"/>.</summary>
    public static int ShownLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int length = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int end = text.IndexOf('}', i);
                if (end > i)
                {
                    string inner = text.Substring(i + 1, end - i - 1);
                    if (inner.StartsWith("key:", StringComparison.Ordinal))
                    {
                        length += TokenWidth;
                        i = end;
                        continue;
                    }

                    if (inner.Length == 1 && char.IsDigit(inner[0]))
                    {
                        length += SlotWidth;
                        i = end;
                        continue;
                    }
                }
            }

            length++;
        }

        return length;
    }

    /// <summary>Whether an id is safe inside milestone and locale keys: letters, digits, <c>_</c>, <c>-</c> (and <c>.</c> for
    /// chapter/step ids, which carry their chapter: <c>b3.1</c>).</summary>
    public static bool IsKeySafeId(string id, bool allowDot = false)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        foreach (char c in id)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' || (allowDot && c == '.');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckEvent(string path, string eventKey, bool playerPack, Action<string, string> error, Action<string, string> warn)
    {
        if (GameEventKeys.IsUiEvent(eventKey))
        {
            if (!playerPack)
            {
                error(path, "'ui:' teaching flags exist only in a player pack's step objectives");
            }
            else if (eventKey.Length == GameEventKeys.UiPrefix.Length || !IsKeySafeId(eventKey.Substring(GameEventKeys.UiPrefix.Length)))
            {
                error(path, $"'{eventKey}' is not a valid teaching flag (ui:<id>)");
            }

            return;
        }

        int star = eventKey.IndexOf('*');
        if (star >= 0 && star != eventKey.Length - 1)
        {
            error(path, $"'{eventKey}': a wildcard may only end the event ('craft:*')");
            return;
        }

        bool known = star >= 0
            ? GameEventKeys.All.Any(k => GameEventKeys.Matches(eventKey, k))
            : GameEventKeys.All.Contains(eventKey);
        if (!known)
        {
            warn(path, $"event '{eventKey}' is not raised by the server yet (GameEventKeys)");
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

    private static bool IsEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined => true,
        JsonValueKind.Number => value.TryGetDouble(out double d) && d == 0,
        JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false,
    };
}
