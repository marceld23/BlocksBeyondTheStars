// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P1 — the story pack format + loader: the `vega_protocol` pack loads from `data/stories/`, every pack-owned
/// line (beats, finale, insights, fragments, memories, arguments) resolves bilingual, and the data pack matches
/// the built-in fallback (no drift).
/// </summary>
public class StoryPackLoadTests
{
    private static GameContent Load() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Loads_the_vega_protocol_pack_from_data()
    {
        var content = Load();
        Assert.True(content.Stories.ContainsKey("vega_protocol"));

        var def = content.Stories["vega_protocol"];
        Assert.Equal("vega_protocol", def.Id);
        Assert.Equal(13, def.Beats.Count);        // B0..B12
        Assert.Equal(0, def.Beats[0].Threshold);
        for (int i = 1; i < def.Beats.Count; i++)
        {
            Assert.Equal(i, def.Beats[i].Index);
            Assert.True(def.Beats[i].Threshold >= def.Beats[i - 1].Threshold);
        }
    }

    [Fact]
    public void Data_pack_matches_the_builtin_fallback_no_drift()
    {
        var data = Load().Stories["vega_protocol"];
        var builtin = StoryRegistry.Default;

        Assert.Equal(builtin.Beats.Count, data.Beats.Count);
        Assert.Equal(builtin.FragmentWeight, data.FragmentWeight);
        Assert.Equal(builtin.KillWeight, data.KillWeight);
        Assert.Equal(builtin.MilestoneWeight, data.MilestoneWeight);
        Assert.Equal(builtin.KillContributionCap, data.KillContributionCap);
        Assert.Equal(builtin.FinaleRevealTextKey, data.FinaleRevealTextKey);
        Assert.Equal(builtin.FinaleResolvedTextKey, data.FinaleResolvedTextKey);
        Assert.Equal(builtin.FinaleSystemNameKey, data.FinaleSystemNameKey);
        Assert.Equal(builtin.InsightUnlockBeatCount, data.InsightUnlockBeatCount);
        Assert.Equal(builtin.CompanionWardTextKey, data.CompanionWardTextKey);
        Assert.Equal(builtin.ShapeAnomalyTextKey, data.ShapeAnomalyTextKey);
        for (int i = 0; i < builtin.Beats.Count; i++)
        {
            Assert.Equal(builtin.Beats[i].Threshold, data.Beats[i].Threshold);
            Assert.Equal(builtin.Beats[i].TextKey, data.Beats[i].TextKey);
        }
    }

    [Fact]
    public void Every_pack_owned_story_text_resolves_in_both_languages()
    {
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);
        var def = content.Stories["vega_protocol"];

        Assert.Equal("The VEGA Protocol", en.Get(def.NameKey));
        Assert.Equal("Das VEGA-Protokoll", de.Get(def.NameKey));

        // The engine is story-agnostic only when every story-owned line travels with the pack: finale, insights,
        // beats, fragments (+ their lore category label), memories, flavour lines and the finale argument tree.
        var keys = new List<string>
        {
            def.NameKey,
            def.FinaleRevealTextKey,
            def.FinaleResolvedTextKey,
            def.FinaleSystemNameKey,
            def.CompanionWardTextKey,
            def.ShapeAnomalyTextKey,
        };
        keys.AddRange(def.Beats.Select(x => x.TextKey));
        keys.AddRange(def.Fragments.SelectMany(x => new[] { x.TextKey, "lore.cat." + x.Category }));
        keys.AddRange(def.Memories.Select(x => x.TextKey));
        keys.AddRange(def.FlavourLines.Select(x => x.TextKey));
        foreach (var node in def.CoreArguments)
        {
            keys.Add(node.PromptKey);
            keys.AddRange(node.Choices.SelectMany(x => new[] { x.TextKey, x.ResponseKey }));
        }

        // Compare against the localizer's exact missing-key marker: story prose may legitimately start with a
        // bracket (e.g. "[archive corrupted] …"). DE falls back to EN inside the localizer, so strict per-language
        // completeness is enforced by tools/merge_story.py; this guards the runtime surface.
        foreach (var key in keys)
        {
            Assert.NotEqual($"[{key}]", en.Get(key));
            Assert.NotEqual($"[{key}]", de.Get(key));
        }
    }

    [Fact]
    public void Pack_owned_finale_and_insight_keys_load_from_data()
    {
        var def = Load().Stories["vega_protocol"];
        Assert.Equal("story.vega.guardian_revealed", def.FinaleRevealTextKey);
        Assert.Equal("story.vega.finale_resolved", def.FinaleResolvedTextKey);
        Assert.Equal("story.vega.guardian_system", def.FinaleSystemNameKey);
        Assert.Equal(6, def.InsightUnlockBeatCount);
        Assert.Equal("story.vega.insight.companion_ward", def.CompanionWardTextKey);
        Assert.Equal("story.vega.insight.shape_anomaly", def.ShapeAnomalyTextKey);
    }

    [Fact]
    public void DefaultStory_resolves_to_vega_protocol()
    {
        Assert.Equal("vega_protocol", Load().DefaultStory.Id);
    }
}
