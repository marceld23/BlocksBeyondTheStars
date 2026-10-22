// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

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

    /// <summary>Whether a ship module key exists (a <c>module:</c> lead, #2475); null = skip.</summary>
    public Func<string, bool>? ModuleExists { get; set; }

    /// <summary>Whether a lore text key exists in some installed pack's <c>lore_sites.json</c> (a <c>lore:</c> condition, #2476);
    /// null = only the validated pack's own lore texts are looked at (none: skip).</summary>
    public Func<string, bool>? LoreExists { get; set; }

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
/// ids, the graduation (#2469: its line, its counter, at most three suggested optional chapters), a world chapter's story
/// missions (#2476: ids that are no chapter's, a title, feasibility, an alternative of the same chapter, do steps with a chip
/// text without a slot, lead bridges naming a lead a player can follow), <c>lore:</c> conditions naming a lore text that
/// exists, a tutorial pays no rewards (its own fields: a mission it bridges onto keeps the rewards it has in
/// <c>data/missions.json</c>, #2481 — the Crystal workshop course), every locale key exists in English and
/// German, every cutscene binding resolves in the pack's <c>cutscenes.json</c> and every cutscene follows the tier, camera,
/// caption and budget rules (#2467, <see cref="ValidateCutscenes"/>), the short name fits (≤ 10 characters), and the text budget
/// (basic-training lines ≤ 120 EN / 145 DE, other step lines ≤ 200 / 240, chip ≤ 45 / 53; a <c>{key:…}</c> token counts
/// 3 characters, a <c>{0}</c> slot 10). Beats keep the old rules (index = position, thresholds monotonic). It never changes
/// the pack.
/// </summary>
public static partial class StoryPackValidator
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
        var run = new Run(def, context);

        // ---- Identity, kind, scope ----
        if (string.IsNullOrEmpty(def.Id))
        {
            run.Error("id", "the pack has no id");
            return run.Issues;
        }

        ValidateIdentity(run);
        // ---- Beats (the old merge_story.py rules) ----
        ValidateBeats(run);
        // ---- The arc's pacing extras (#2475): the lead bonus, the peaceful-world normalisation, the memory triggers ----
        ValidatePacing(run);
        // ---- Conditions ----
        ValidateStartAndGates(run);
        // ---- Keys and texts ----
        RegisterPackTexts(run);
        // ---- Cutscene bindings ----
        ValidateCutsceneBindings(run);
        // ---- Chapters and steps ----
        ValidateChapters(run);
        // ---- Graduation (#2469) ----
        ValidateGraduation(run);
        // ---- Cutscene definitions (cutscenes.json, #2467) ----
        ValidateCutscenes(run);
        // ---- Every key in English and German, within its budget ----
        ValidateKeyTexts(run);
        // ---- A tutorial pays no rewards (the raw document: fields the model does not know count too). Only the pack's own
        // document counts — a bridged mission ({type: Mission}) keeps the rewards it has in data/missions.json (#2481). ----
        ValidateTutorialRewards(run, rawJson);
        return run.Issues;
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

    /// <summary>#2478: whether a colour reads as <c>#RRGGBB</c> (a speaker's accent colour).</summary>
    public static bool IsHexColor(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            if (!(c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
