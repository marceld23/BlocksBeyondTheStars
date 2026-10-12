// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The built-in story packs — the fallback when no <c>data/stories/&lt;id&gt;/story.json</c> is installed (the shipped
/// game loads its packs from there through ContentLoader; a drift test keeps this copy in step). The engine is
/// story-agnostic. The first pack is <b>"The VEGA Protocol"</b> — the SPS grundstory whose canon + beat agenda live in
/// <c>docs/developer/LORE_STRUCTURE.md</c>. The beat <see cref="StoryBeat.TextKey"/>s are the B0–B12 arc from that doc;
/// their text lives in the pack's <c>locales/</c>. Thresholds are tunable (plan §9 numeric tuning) but must stay
/// monotonic.
/// </summary>
public static class StoryRegistry
{
    /// <summary>The default/active pack id for a fresh world.</summary>
    public const string DefaultStoryId = "vega_protocol";

    /// <summary>The reserved id that disables the story entirely (pure sandbox).</summary>
    public const string NoneStoryId = "none";

    /// <summary><see cref="StoryDefinition.Kind"/> of a narrative arc (the default).</summary>
    public const string KindStory = "story";

    /// <summary><see cref="StoryDefinition.Kind"/> of a pack that teaches the game; such a pack is always player-scoped.</summary>
    public const string KindTutorial = "tutorial";

    /// <summary><see cref="StoryDefinition.Kind"/> of a small optional storyline.</summary>
    public const string KindSide = "side";

    /// <summary><see cref="StoryDefinition.Scope"/>: progress shared by everyone in the save.</summary>
    public const string ScopeWorld = "world";

    /// <summary><see cref="StoryDefinition.Scope"/>: progress per player.</summary>
    public const string ScopePlayer = "player";

    /// <summary>Pack ids no pack may use (#2465, synthesis §5.3): <c>none</c> is the "no story" sentinel, the others are
    /// the second segment of milestone families the story code already writes (<c>story:mem:*</c>,
    /// <c>story:insight:*</c>, …) or reserves for the pack runtime, so a pack named like that would read or clobber
    /// keys that are not its own.</summary>
    public static readonly IReadOnlyList<string> ReservedPackIds = new[] { NoneStoryId, "mem", "insight", "obj", "vega", "track", "cut" };

    /// <summary>Whether <paramref name="packId"/> is one of the <see cref="ReservedPackIds"/> (case-insensitive).</summary>
    public static bool IsReservedPackId(string? packId)
    {
        if (string.IsNullOrEmpty(packId))
        {
            return false;
        }

        foreach (string reserved in ReservedPackIds)
        {
            if (string.Equals(reserved, packId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The scope a pack runs with: its explicit <see cref="StoryDefinition.Scope"/>, a tutorial is always
    /// <see cref="ScopePlayer"/>, and an unset scope is <see cref="ScopeWorld"/> exactly when the pack has beats.</summary>
    public static string EffectiveScope(StoryDefinition def)
    {
        if (string.Equals(def.Kind, KindTutorial, StringComparison.OrdinalIgnoreCase))
        {
            return ScopePlayer;
        }

        if (string.Equals(def.Scope, ScopePlayer, StringComparison.OrdinalIgnoreCase))
        {
            return ScopePlayer;
        }

        if (string.Equals(def.Scope, ScopeWorld, StringComparison.OrdinalIgnoreCase))
        {
            return ScopeWorld;
        }

        return def.Beats.Count > 0 ? ScopeWorld : ScopePlayer;
    }

    /// <summary>Whether a pack may be the save's (world-shared) story (#2465, N11): world-scoped, at least one beat and
    /// not a reserved id. A beat-less player pack chosen as the world story would turn the story "active" with nothing
    /// to tell — the prologue would play for it and the choice would be persisted for good.</summary>
    public static bool CanBeWorldStory(StoryDefinition? def)
        => def is not null
           && def.Beats.Count > 0
           && !IsReservedPackId(def.Id)
           && string.Equals(EffectiveScope(def), ScopeWorld, StringComparison.Ordinal);

    private static readonly Dictionary<string, StoryDefinition> Packs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultStoryId] = BuildVegaProtocol(),
        };

    /// <summary>All installed packs (excludes the "none" sentinel).</summary>
    public static IReadOnlyCollection<StoryDefinition> All => Packs.Values;

    /// <summary>The default pack ("vega_protocol").</summary>
    public static StoryDefinition Default => Packs[DefaultStoryId];

    /// <summary>True if the id is a real installed pack (not empty and not the "none" sentinel).</summary>
    public static bool IsStoryActive(string? storyId)
        => !string.IsNullOrEmpty(storyId)
           && !string.Equals(storyId, NoneStoryId, StringComparison.OrdinalIgnoreCase)
           && Packs.ContainsKey(storyId!);

    /// <summary>Looks up a pack by id. Returns false for empty/unknown ids and for the "none" sentinel.</summary>
    public static bool TryGet(string? storyId, out StoryDefinition definition)
    {
        if (!string.IsNullOrEmpty(storyId) && Packs.TryGetValue(storyId!, out var def))
        {
            definition = def;
            return true;
        }

        definition = Default;
        return false;
    }

    private static StoryDefinition BuildVegaProtocol() => new()
    {
        Id = DefaultStoryId,
        NameKey = "story.vega_protocol.name",
        // progress = fragments*3 + min(kills,40)*1 + milestones*2
        FragmentWeight = 3,
        KillWeight = 1,
        MilestoneWeight = 2,
        KillContributionCap = 40,
        // Beats pay +1 knowledge each (was +3): the arc alone used to out-earn the whole research
        // ladder's knowledge ceiling, trivialising the blueprint gate (#767).
        Beats = new List<StoryBeat>
        {
            Beat(0,  "Systems online",        0,   0),
            Beat(1,  "A familiar signature",  6,   1),
            Beat(2,  "The Service",           14,  1),
            Beat(3,  "Not scattered — erased", 24, 1),
            Beat(4,  "Ours, once",            36,  1),
            Beat(5,  "The Guardian",          50,  1),
            Beat(6,  "The verdict",           66,  1),
            Beat(7,  "Her stand",             84,  1),
            Beat(8,  "The thought-arcs",      104, 1),
            Beat(9,  "What you are",          126, 1),  // the clone reveal
            Beat(10, "Many minds",            150, 1),
            Beat(11, "It still sleeps",       176, 1),  // locates the dormant Guardian system
            Beat(12, "The choice",            204, 1),  // finale opens
        },
        // #1124: revealed by WINNING the finale, never by score — see StoryDefinition.EpilogueTitle.
        EpilogueTitle = "The relay network",
        EpilogueTitleKey = "story.vega.epilogue.title", // #2461: the cinematic shows the localized title
        EpilogueTextKey = "story.vega.beat13",
        // #2470: VEGA as its own pack — the manifest fields the data pack sets, mirrored here (StoryPackLoadTests' no-drift
        // test compares them). The fallback carries no cutscenes, so its prologue plays as the old full-screen pages.
        Kind = KindStory,
        Scope = ScopeWorld,
        ShortNameKey = "story.vega.short",
        DescriptionKey = "story.vega.desc", // #2472: the world-creation Stories page's line under the main story
        Narrator = new StoryNarrator
        {
            Id = StoryNarrator.DefaultId,
            NameKey = "ui.vega.name",
            Icon = "icon_vega",
            Color = "#5FD8FF",
            Voice = new List<string> { "vega_chatter_1", "vega_chatter_2", "vega_chatter_3" },
            LineStart = "ai_blip",
        },
        IntroCutscene = StoryCutsceneIds.Prologue,
        Start = new StoryStartRule { After = new List<string> { "chapter:tutorial:b6" }, EarlyOptIn = true },
        Chapters = new List<StoryChapter>
        {
            Chapter("awakening", fromBeat: 0, openCutscene: StoryCutsceneIds.Prologue),
            Chapter("signature", fromBeat: 1),
            Chapter("service", fromBeat: 2),
            Chapter("guardian", fromBeat: 5),
            Chapter("verdict", fromBeat: 6),
            Chapter("what_you_are", fromBeat: 9),
            Chapter("sleeps", fromBeat: 11),
            Chapter("core", fromBeat: 12),
            Chapter("resolution", fromEvent: StoryChapterEvents.GuardianDefeated),
        },
        Insights = new List<StoryInsight>
        {
            new() { Event = "companion_ward", MinBeat = 5, TextKey = "story.vega.insight.companion_ward", OnceKey = "story:insight:companion-ward" },
            new() { Event = "shape_noncube", MinBeat = 5, TextKey = "story.vega.insight.shape_anomaly", OnceKey = "story:insight:shape-anomaly" },
        },
    };

    private static StoryChapter Chapter(string id, int fromBeat = -1, string fromEvent = "", string openCutscene = "") => new()
    {
        Id = id,
        TitleKey = "story.vega.ch." + id,
        FromBeat = fromBeat,
        FromEvent = fromEvent,
        OpenCutscene = openCutscene,
    };

    private static StoryBeat Beat(int index, string title, int threshold, int knowledge) => new()
    {
        Index = index,
        Title = title,
        TextKey = "story.vega.beat" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
        Threshold = threshold,
        KnowledgeReward = knowledge,
    };
}
