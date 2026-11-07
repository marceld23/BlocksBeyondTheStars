// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Story;

public static partial class StoryPackValidator
{
    /// <summary>
    /// What a pack brings besides chapters (#3347, #3357): its lore texts' gates (a world story's by <c>minKnowledge</c>, a
    /// player pack's by <c>minChapter</c>), its readable texts (ids, keys, the unlock condition), its star systems (ids that no
    /// procedural or Guardian system can have and no other pack uses, a name, a planet type that exists, a known placement,
    /// the visibility condition; a player pack's only), its milestone namespace, its characters' planet types, and its
    /// dialogs' replies (the <c>visibleIf</c> conditions, a melody of note-alphabet letters, a <c>milestone:</c> consequence
    /// inside the pack's namespace).
    /// </summary>
    private static void ValidatePackExtras(Run run)
    {
        ValidateLoreGates(run);
        ValidateTexts(run);
        ValidateSystems(run);
        if (!string.IsNullOrEmpty(run.Def.MilestonePrefix) && !StoryPackMilestones.IsValidPrefix(run.Def.MilestonePrefix))
        {
            run.Error("milestonePrefix", $"'{run.Def.MilestonePrefix}' is not a pack's own milestone namespace (key-safe, no ':', not one the game writes: {string.Join(", ", StoryPackMilestones.ReservedPrefixes)})");
        }

        foreach (var (ch, i) in run.Def.Characters.Select((c, i) => (c, i)))
        {
            foreach (var (type, j) in (ch.PlanetTypes ?? new List<string>()).Select((t, j) => (t, j)))
            {
                if (run.Context.PlanetExists is { } planet && !planet(type))
                {
                    run.Error($"characters[{i}].planetTypes[{j}]", $"planet type '{type}' does not exist");
                }
            }
        }

        foreach (var (d, i) in run.Def.Dialogs.Select((d, i) => (d, i)))
        {
            foreach (var (n, j) in d.Nodes.Select((n, j) => (n, j)))
            {
                foreach (var (c, k) in n.Choices.Select((c, k) => (c, k)))
                {
                    ValidateDialogChoice(run, c, $"dialogs[{i}].nodes[{j}].choices[{k}]");
                }
            }
        }
    }

    /// <summary>A world story's lore texts are gated by the world's knowledge level, a player pack's by the player's chapter.</summary>
    private static void ValidateLoreGates(Run run)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (l, i) in run.Def.LoreSites.Select((l, i) => (l, i)))
        {
            string lp = $"loreSites[{i}]";
            if (!string.IsNullOrEmpty(l.Key) && !keys.Add(l.Key))
            {
                run.Error(lp, $"lore key '{l.Key}' is used twice (it is the per-player 'lore:<key>' milestone)");
            }

            if (l.MinChapter < 0 || l.MinChapter > run.Def.Chapters.Count)
            {
                run.Error($"{lp}.minChapter", $"{l.MinChapter} is out of range (0 = at once, up to the pack's {run.Def.Chapters.Count} chapters)");
            }

            if (run.PlayerPack && l.MinKnowledge > 0)
            {
                run.Warn($"{lp}.minKnowledge", "a player pack's lore follows the player's chapter (minChapter), not the world's knowledge");
            }
            else if (!run.PlayerPack && l.MinChapter > 0)
            {
                run.Warn($"{lp}.minChapter", "a world story's lore follows the world's knowledge (minKnowledge), not a chapter");
            }
        }
    }

    /// <summary>The pack's readable texts (#3347): key-safe unique ids, a title and a text, a condition that parses.</summary>
    private static void ValidateTexts(Run run)
    {
        run.Key("textsTitleKey", run.Def.TextsTitleKey);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (t, i) in run.Def.Texts.Select((t, i) => (t, i)))
        {
            string tp = $"texts[{i}]";
            if (t is null || !IsKeySafeId(t.Id, allowDot: true) || !ids.Add(t.Id))
            {
                run.Error(tp, $"text id '{t?.Id}' is empty, not key-safe or used twice");
                continue;
            }

            if (string.IsNullOrEmpty(t.TitleKey) || string.IsNullOrEmpty(t.TextKey))
            {
                run.Error(tp, "a text needs a titleKey and a textKey");
            }

            run.Key($"{tp}.titleKey", t.TitleKey);
            run.Key($"{tp}.textKey", t.TextKey);
            if (!string.IsNullOrWhiteSpace(t.UnlockedBy))
            {
                run.CheckCondition($"{tp}.unlockedBy", t.UnlockedBy);
            }
        }

        if (run.Def.Texts.Count > 0 && !run.PlayerPack)
        {
            run.Warn("texts", "only a player pack's texts are shown (on its Journal page)");
        }
    }

    /// <summary>The pack's star systems (#3357).</summary>
    private static void ValidateSystems(Run run)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (s, i) in run.Def.Systems.Select((s, i) => (s, i)))
        {
            string sp = $"systems[{i}]";
            if (s is null || !IsKeySafeId(s.Id) || s.Id.StartsWith("sys", StringComparison.Ordinal)
                || string.Equals(s.Id, "guardian_finale", StringComparison.Ordinal) || !ids.Add(s.Id))
            {
                run.Error(sp, $"system id '{s?.Id}' is empty, not key-safe, used twice, procedural ('sys…') or the Guardian's");
                continue;
            }

            if (OtherPackDeclaresSystem(run, s.Id) is { } other)
            {
                run.Error($"{sp}.id", $"pack '{other}' declares a system '{s.Id}' too");
            }

            if (string.IsNullOrEmpty(s.NameKey) && string.IsNullOrEmpty(s.Name))
            {
                run.Error(sp, "a system needs a nameKey or a name");
            }

            run.Key($"{sp}.nameKey", s.NameKey);
            if (string.IsNullOrEmpty(s.PlanetType))
            {
                run.Error($"{sp}.planetType", "a system needs the planet type of its world");
            }
            else if (run.Context.PlanetExists is { } planet && !planet(s.PlanetType))
            {
                run.Warn($"{sp}.planetType", $"planet type '{s.PlanetType}' does not exist (the world falls back to a rocky one)");
            }

            if (!StoryPackSystemPlacements.All.Contains(s.Placement ?? string.Empty))
            {
                run.Error($"{sp}.placement", $"unknown placement '{s.Placement}' ({string.Join(", ", StoryPackSystemPlacements.All)})");
            }

            run.CheckConditions($"{sp}.visibleIf", s.VisibleIf);
        }

        if (run.Def.Systems.Count > 0 && !run.PlayerPack)
        {
            run.Error("systems", "only a player pack's systems are revealed (per player) — a world story has its finale system");
        }
    }

    /// <summary>The id of another installed pack that declares the system, or null.</summary>
    private static string? OtherPackDeclaresSystem(Run run, string systemId)
        => run.Context.Packs?.Values
            .FirstOrDefault(p => p is not null && !string.Equals(p.Id, run.Def.Id, StringComparison.OrdinalIgnoreCase)
                                 && (p.Systems ?? new List<StoryPackSystem>()).Any(s => s?.Id == systemId))?.Id;

    /// <summary>Whether this pack or an installed one declares the system (null when no pack list is known and the pack has none).</summary>
    private static bool? SystemDeclared(Run run, string systemId)
    {
        if (run.Def.Systems.Any(s => s?.Id == systemId))
        {
            return true;
        }

        return run.Context.Packs is null ? null : OtherPackDeclaresSystem(run, systemId) is not null;
    }

    /// <summary>#3357: a <c>system:&lt;id&gt;</c> anchor is shown on a chart, and its system is a pack's.</summary>
    private static void ValidateSystemAnchor(Run run, string sp, string camera, string systemId)
    {
        if (camera != CutsceneCameras.Chart)
        {
            run.Error($"{sp}.camera.anchor", "a system anchor is a star on the chart — it needs a chart camera");
        }

        if (SystemDeclared(run, systemId) == false)
        {
            run.Error($"{sp}.camera.anchor", $"no installed pack declares the system '{systemId}' (systems[])");
        }
    }

    /// <summary>#3357: a <c>story_system:&lt;id&gt;</c> marker names a pack's system.</summary>
    private static void CheckStorySystemReveal(Run run, string path, string reveal)
    {
        if (StoryRevealProviders.ArgumentOf(reveal, StoryRevealProviders.StorySystem) is { } systemId && SystemDeclared(run, systemId) == false)
        {
            run.Error(path, $"no installed pack declares the system '{systemId}' (systems[])");
        }
    }

    /// <summary>One reply of a pack dialog (#3347): its conditions, its melody, a <c>milestone:</c> consequence.</summary>
    private static void ValidateDialogChoice(Run run, DialogChoice c, string cp)
    {
        run.CheckConditions($"{cp}.visibleIf", c.VisibleIf);
        string melody = c.Melody ?? string.Empty;
        if (melody.Any(ch => ch != ' ' && !(ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))))
        {
            run.Error($"{cp}.melody", $"'{melody}' — a melody is letters A–Z (and spaces) of the note alphabet");
        }

        string consequence = c.Consequence ?? string.Empty;
        if (consequence.StartsWith(StoryPackMilestones.Consequence + ":", StringComparison.Ordinal))
        {
            string key = consequence.Substring(StoryPackMilestones.Consequence.Length + 1);
            if (!StoryPackMilestones.MaySet(run.Def, key))
            {
                run.Error($"{cp}.consequence", $"'{key}' is not in the pack's own milestone namespace ('{run.Def.Id}:…'"
                                                + (string.IsNullOrEmpty(run.Def.MilestonePrefix) ? string.Empty : $" or '{run.Def.MilestonePrefix}:…'") + ")");
            }
        }
    }
}
