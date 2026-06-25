// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The installed story packs. The engine is story-agnostic; packs are added here (and later loaded from
/// <c>data/stories/&lt;id&gt;/</c>). The first pack is <b>"The VEGA Protocol"</b> — the SPS grundstory whose
/// canon + beat agenda live in <c>docs/developer/LORE_STRUCTURE.md</c>. The beat <see cref="StoryBeat.TextKey"/>s are the
/// B0–B12 arc from that doc; their bilingual text is authored later (workstream W-A). Thresholds are tunable
/// (plan §9 numeric tuning) but must stay monotonic.
/// </summary>
public static class StoryRegistry
{
    /// <summary>The default/active pack id for a fresh world.</summary>
    public const string DefaultStoryId = "vega_protocol";

    /// <summary>The reserved id that disables the story entirely (pure sandbox).</summary>
    public const string NoneStoryId = "none";

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
        Beats = new List<StoryBeat>
        {
            Beat(0,  "Systems online",        0,   0),
            Beat(1,  "A familiar signature",  6,   3),
            Beat(2,  "The Service",           14,  3),
            Beat(3,  "Not scattered — erased", 24, 3),
            Beat(4,  "Ours, once",            36,  3),
            Beat(5,  "The Guardian",          50,  3),
            Beat(6,  "The verdict",           66,  3),
            Beat(7,  "Her stand",             84,  3),
            Beat(8,  "The thought-arcs",      104, 3),
            Beat(9,  "What you are",          126, 3),  // the clone reveal
            Beat(10, "Many minds",            150, 3),
            Beat(11, "It still sleeps",       176, 3),  // locates the dormant Guardian system
            Beat(12, "The choice",            204, 3),  // finale opens
        },
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
