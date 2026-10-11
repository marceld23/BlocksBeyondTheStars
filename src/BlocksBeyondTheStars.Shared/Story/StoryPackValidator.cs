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

    /// <summary>Whether a cutscene id exists in ANOTHER installed pack (<c>(packId, cutsceneId)</c>, for <c>chainNext</c>
    /// references and for a pack validated from its <c>story.json</c> alone); null = only the pack's own
    /// <see cref="StoryDefinition.Cutscenes"/> and <see cref="Packs"/> are looked at.</summary>
    public Func<string, string, bool>? CutsceneExists { get; set; }

    /// <summary>Whether a music track exists (a file in <c>client/Music</c>); null = skip.</summary>
    public Func<string, bool>? MusicExists { get; set; }

    /// <summary>Whether a sound cue exists (a clip in <c>client/Assets/Resources/audio</c>); null = skip.</summary>
    public Func<string, bool>? SoundExists { get; set; }
}

/// <summary>
/// The story-pack validator (#2466, synthesis §5.9) — the C# twin of <c>tools/validate_story_pack.py</c>, used by
/// <c>StoryPackValidationTests</c> for every pack under <c>data/stories/</c>. It checks: id rules (not reserved, key-safe),
/// <c>kind</c>/<c>scope</c> (a tutorial is player-scoped, a player pack carries no beats so it can never be the world story,
/// a world pack has beats), every condition parses and names existing packs/chapters/steps/blueprints, mission-bridge
/// targets exist, event keys are published (warning otherwise), <c>ui:</c> only in a player pack's step objectives (and one a
/// client reports — a warning otherwise, #2469), marker providers, legacy keys (tutorial only), alternatives, empty hint
/// ids, the graduation (#2469: its line, its counter, at most three suggested optional chapters), a tutorial pays no
/// rewards, every locale key exists in English and
/// German, every cutscene binding resolves in the pack's <c>cutscenes.json</c> and every cutscene follows the tier, camera,
/// caption and budget rules (#2467, <see cref="ValidateCutscenes"/>), the short name fits (≤ 10 characters), and the text budget
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

    /// <summary>Cutscene caption budget of a subtitle or speaker page (one VEGA panel page; the director pages it further),
    /// English.</summary>
    public const int CaptionLineMaxEn = 220;

    /// <summary>Cutscene caption budget of a subtitle or speaker page, German.</summary>
    public const int CaptionLineMaxDe = 264;

    /// <summary>Cutscene title-card budget (the title line, and the kicker), English.</summary>
    public const int TitleCardMaxEn = 60;

    /// <summary>Cutscene title-card budget, German.</summary>
    public const int TitleCardMaxDe = 72;

    /// <summary>Cutscene reading-frame budget (the recap, the epilogue), English.</summary>
    public const int ReadingFrameMaxEn = 600;

    /// <summary>Cutscene reading-frame budget, German.</summary>
    public const int ReadingFrameMaxDe = 720;

    /// <summary>Longest shot in seconds.</summary>
    public const float ShotMaxSeconds = 120f;

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
        bool OwnCutscene(string id) => def.FindCutscene(id) is not null || (context.CutsceneExists?.Invoke(def.Id, id) ?? false);

        void Cutscene(string path, string? id)
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
                        Error($"{sp}.onStart.reveal", $"unknown marker provider '{reveal}' ({StoryRevealProviders.NearestOre}:<block>, {StoryRevealProviders.NearestSettlement}, {StoryRevealProviders.NearestFoodPlant})");
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
                foreach (var (hint, hi) in (step.HintRefs ?? new List<string>()).Select((h, i) => (h, i)))
                {
                    if (string.IsNullOrWhiteSpace(hint))
                    {
                        Error($"{sp}.hintRefs[{hi}]", "an empty hint id");
                    }
                }

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

        // ---- Graduation (#2469) ----
        var graduation = def.Graduation ?? new StoryGraduation();
        bool graduates = !string.IsNullOrEmpty(graduation.LineKey) || !string.IsNullOrEmpty(graduation.Counter) || graduation.Suggest.Count > 0;
        if (graduates && !playerPack)
        {
            Warn("graduation", "only a player pack graduates anybody");
        }

        Key("graduation.lineKey", graduation.LineKey, tutorial ? BasicLineMaxEn : LineMaxEn, tutorial ? BasicLineMaxDe : LineMaxDe);
        if (!string.IsNullOrEmpty(graduation.Counter)
            && (graduation.Counter.IndexOf(':') <= 0 || !IsKeySafeId(graduation.Counter.Replace(':', '_'))))
        {
            Error("graduation.counter", $"'{graduation.Counter}' is not an achievement counter ('<verb>:<thing>')");
        }

        if (graduation.Suggest.Count > StoryGraduation.MaxSuggestions)
        {
            Error("graduation.suggest", $"at most {StoryGraduation.MaxSuggestions} suggested chapters ({graduation.Suggest.Count} named)");
        }

        for (int gi = 0; gi < graduation.Suggest.Count; gi++)
        {
            string suggested = graduation.Suggest[gi] ?? string.Empty;
            if (!def.Chapters.Any(c => c.Optional && c.Id == suggested))
            {
                Error($"graduation.suggest[{gi}]", $"'{suggested}' is not an optional chapter of this pack");
            }
        }

        // ---- Cutscene definitions (cutscenes.json, #2467) ----
        ValidateCutscenes(def, context, chapterIds, Key, Error, Warn);

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

    /// <summary>The cutscene rules (synthesis §6.2/§6.3): unique key-safe ids (no built-in <c>__</c> id), a known tier, at
    /// least one shot, known camera types, anchors, caption styles and grades, positive shot lengths with captions and cues
    /// inside them, every caption key in English and German within its style's budget, the tier rules (a T1/T2 never
    /// takes the camera; only a T3/T4 that takes it may hold the world or freeze the controls), <c>chainNext</c> references
    /// that resolve, a chapter that exists, and (when the context can tell) existing music and sound cues.</summary>
    private static void ValidateCutscenes(StoryDefinition def, StoryPackValidationContext context, HashSet<string> chapterIds,
        Action<string, string?, int, int> key, Action<string, string> error, Action<string, string> warn)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int ci = 0; ci < def.Cutscenes.Count; ci++)
        {
            var cut = def.Cutscenes[ci];
            string cp = $"cutscenes[{ci}]";
            if (string.IsNullOrEmpty(cut.Id) || !IsKeySafeId(cut.Id, allowDot: true) || !ids.Add(cut.Id))
            {
                error(cp, $"cutscene id '{cut.Id}' is empty, not key-safe or used twice");
            }
            else if (StoryCutsceneIds.IsBuiltIn(cut.Id))
            {
                error(cp, $"'{cut.Id}': ids starting with '{StoryCutsceneIds.BuiltInPrefix}' are built in");
            }

            if (!CutsceneTiers.IsValid(cut.Tier))
            {
                error($"{cp}.tier", $"unknown tier {cut.Tier} (1 banner, 2 card, 3 scene, 4 set piece)");
            }

            if (!string.IsNullOrEmpty(cut.Chapter) && !chapterIds.Contains(cut.Chapter))
            {
                error($"{cp}.chapter", $"the pack has no chapter '{cut.Chapter}'");
            }

            foreach (var (line, i) in (cut.ReplaceLineKeys ?? new List<string>()).Select((l, i) => (l, i)))
            {
                key($"{cp}.replaceLineKeys[{i}]", line, 0, 0);
            }

            if (!string.IsNullOrEmpty(cut.Music) && context.MusicExists is { } musicExists && !musicExists(cut.Music))
            {
                error($"{cp}.music", $"music track '{cut.Music}' does not exist in client/Music");
            }

            var shots = cut.Shots ?? new List<CutsceneShot>();
            if (shots.Count == 0)
            {
                error($"{cp}.shots", "a cutscene needs at least one shot");
            }

            for (int si = 0; si < shots.Count; si++)
            {
                var shot = shots[si];
                string sp = $"{cp}.shots[{si}]";
                string camera = shot.Camera?.Type ?? string.Empty;
                string anchor = shot.Camera?.Anchor ?? string.Empty;
                if (!CutsceneCameras.All.Contains(camera))
                {
                    error($"{sp}.camera.type", $"unknown camera '{camera}' ({string.Join(", ", CutsceneCameras.All)})");
                }
                else if (cut.Tier < CutsceneTiers.Scene && CutsceneCameras.TakesCamera(camera))
                {
                    error($"{sp}.camera.type", $"a T{cut.Tier} {(cut.Tier == CutsceneTiers.Banner ? "banner" : "card")} never takes the camera (player_view or overlay)");
                }

                if (anchor.Length > 0 && !CutsceneAnchors.All.Contains(anchor))
                {
                    error($"{sp}.camera.anchor", $"unknown anchor '{anchor}' ({string.Join(", ", CutsceneAnchors.All)})");
                }

                if (!(shot.Duration > 0f) || shot.Duration > ShotMaxSeconds)
                {
                    error($"{sp}.duration", $"a shot lasts more than 0 and at most {ShotMaxSeconds:0} seconds (found {shot.Duration})");
                }

                if (!CutsceneGrades.All.Contains(shot.Grade ?? string.Empty))
                {
                    error($"{sp}.grade", $"unknown grade '{shot.Grade}' ({string.Join(", ", CutsceneGrades.All)})");
                }

                foreach (var (sfx, xi) in (shot.Sfx ?? new List<CutsceneSfx>()).Select((x, i) => (x, i)))
                {
                    string xp = $"{sp}.sfx[{xi}]";
                    if (string.IsNullOrEmpty(sfx.Cue))
                    {
                        error(xp, "a sound cue needs a clip name");
                    }
                    else if (context.SoundExists is { } soundExists && !soundExists(sfx.Cue))
                    {
                        error(xp, $"sound '{sfx.Cue}' does not exist in client/Assets/Resources/audio");
                    }

                    if (sfx.At < 0f || (shot.Duration > 0f && sfx.At >= shot.Duration))
                    {
                        error($"{xp}.at", $"the cue at {sfx.At} s lies outside the {shot.Duration} s shot");
                    }
                }

                foreach (var (caption, ki) in (shot.Captions ?? new List<CutsceneCaption>()).Select((c, i) => (c, i)))
                {
                    string kp = $"{sp}.captions[{ki}]";
                    string style = caption.Style ?? string.Empty;
                    if (!CutsceneCaptionStyles.All.Contains(style))
                    {
                        error($"{kp}.style", $"unknown caption style '{style}' ({string.Join(", ", CutsceneCaptionStyles.All)})");
                    }

                    if (string.IsNullOrEmpty(caption.Key))
                    {
                        error(kp, "a caption needs a key");
                    }

                    var (maxEn, maxDe) = style switch
                    {
                        CutsceneCaptionStyles.TitleCard => (TitleCardMaxEn, TitleCardMaxDe),
                        CutsceneCaptionStyles.ReadingFrame => (ReadingFrameMaxEn, ReadingFrameMaxDe),
                        CutsceneCaptionStyles.Credits => (0, 0),
                        _ => (CaptionLineMaxEn, CaptionLineMaxDe),
                    };
                    key($"{kp}.key", caption.Key, maxEn, maxDe);
                    if (!string.IsNullOrEmpty(caption.KickerKey))
                    {
                        if (style != CutsceneCaptionStyles.TitleCard)
                        {
                            warn($"{kp}.kickerKey", "only a title_card shows a kicker");
                        }

                        key($"{kp}.kickerKey", caption.KickerKey, TitleCardMaxEn, TitleCardMaxDe);
                    }

                    if (!string.IsNullOrEmpty(caption.Speaker) && style != CutsceneCaptionStyles.Speaker)
                    {
                        warn($"{kp}.speaker", "only a speaker caption names a speaker");
                    }

                    if (caption.At < 0f || (shot.Duration > 0f && caption.At >= shot.Duration))
                    {
                        error($"{kp}.at", $"the caption at {caption.At} s lies outside the {shot.Duration} s shot");
                    }

                    if (caption.Hold < 0f)
                    {
                        error($"{kp}.hold", "hold must not be negative (0 = until the shot ends)");
                    }
                }
            }

            // ---- Options: the tier rules of synthesis §5.3 "Stufen" ----
            var options = cut.Options ?? new CutsceneOptions();
            bool takeover = cut.Tier >= CutsceneTiers.Scene && cut.TakesCamera;
            if (options.HoldWorld == true && !takeover)
            {
                error($"{cp}.options.holdWorld", "only a T3/T4 scene that takes the camera may hold the world");
            }

            if (options.LockInput == true && !takeover)
            {
                error($"{cp}.options.lockInput", "only a T3/T4 scene that takes the camera may freeze the controls");
            }

            if (options.MinDisplay < 0f)
            {
                error($"{cp}.options.minDisplay", "minDisplay must not be negative");
            }

            foreach (var (next, ni) in (options.ChainNext ?? new List<string>()).Select((n, i) => (n, i)))
            {
                CheckChainReference(def, context, cut, $"{cp}.options.chainNext[{ni}]", next, error, warn);
            }
        }
    }

    /// <summary>A <c>chainNext</c> entry: <c>__recap</c>, an id of the same pack, or <c>&lt;pack&gt;/&lt;id&gt;</c> — an
    /// error when it names nothing, a warning when it names a pack that is not installed (packs are optional).</summary>
    private static void CheckChainReference(StoryDefinition def, StoryPackValidationContext context, CutsceneDefinition from,
        string path, string? reference, Action<string, string> error, Action<string, string> warn)
    {
        if (string.IsNullOrEmpty(reference))
        {
            error(path, "an empty chain reference");
            return;
        }

        if (reference == StoryCutsceneIds.Recap)
        {
            return;
        }

        int slash = reference!.IndexOf(StoryCutsceneIds.PackSeparator);
        if (slash < 0)
        {
            if (reference == from.Id)
            {
                error(path, "a cutscene cannot chain to itself");
            }
            else if (def.FindCutscene(reference) is null || reference.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0)
            {
                error(path, $"cutscene '{reference}' is not defined in the pack");
            }

            return;
        }

        string pack = reference.Substring(0, slash);
        string id = reference.Substring(slash + 1);
        if (string.Equals(pack, def.Id, StringComparison.OrdinalIgnoreCase))
        {
            CheckChainReference(def, context, from, path, id, error, warn);
            return;
        }

        StoryDefinition? other = context.Packs is { } packs && packs.TryGetValue(pack, out var p) ? p : null;
        if (other is null && context.Packs is not null)
        {
            warn(path, $"pack '{pack}' is not installed — the chain applies only when it is");
            return;
        }

        if (id == StoryCutsceneIds.Recap)
        {
            return; // that pack's recap card
        }

        bool exists = (other?.FindCutscene(id) is not null) || (context.CutsceneExists?.Invoke(pack, id) ?? false);
        if (!exists && (other is not null || context.CutsceneExists is not null))
        {
            error(path, $"pack '{pack}' has no cutscene '{id}'");
        }
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
            else if (!GameEventKeys.IsKnownUiEventId(eventKey.Substring(GameEventKeys.UiPrefix.Length)))
            {
                warn(path, $"'{eventKey}' is no flag a client reports ({string.Join(", ", GameEventKeys.UiEvents)})");
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
