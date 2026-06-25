// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P1 — the story pack format + loader: the `vega_protocol` pack loads from `data/stories/`, its beat text
/// resolves bilingual, and the data pack matches the built-in fallback (no drift).
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
        for (int i = 0; i < builtin.Beats.Count; i++)
        {
            Assert.Equal(builtin.Beats[i].Threshold, data.Beats[i].Threshold);
            Assert.Equal(builtin.Beats[i].TextKey, data.Beats[i].TextKey);
        }
    }

    [Fact]
    public void Beat_text_resolves_in_both_languages()
    {
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);
        var def = content.Stories["vega_protocol"];

        Assert.Equal("The VEGA Protocol", en.Get(def.NameKey));
        Assert.Equal("Das VEGA-Protokoll", de.Get(def.NameKey));

        foreach (var beat in def.Beats)
        {
            Assert.False(en.Get(beat.TextKey).StartsWith("["), $"EN missing {beat.TextKey}");
            Assert.False(de.Get(beat.TextKey).StartsWith("["), $"DE missing {beat.TextKey}");
        }
    }

    [Fact]
    public void DefaultStory_resolves_to_vega_protocol()
    {
        Assert.Equal("vega_protocol", Load().DefaultStory.Id);
    }
}
