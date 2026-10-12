// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
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
        Assert.Equal(builtin.EpilogueTitle, data.EpilogueTitle);     // #1124
        Assert.Equal(builtin.EpilogueTitleKey, data.EpilogueTitleKey); // #2461
        Assert.Equal(builtin.EpilogueTextKey, data.EpilogueTextKey); // #1124
        Assert.Equal(builtin.FragmentWeight, data.FragmentWeight);
        Assert.Equal(builtin.KillWeight, data.KillWeight);
        Assert.Equal(builtin.MilestoneWeight, data.MilestoneWeight);
        Assert.Equal(builtin.KillContributionCap, data.KillContributionCap);
        for (int i = 0; i < builtin.Beats.Count; i++)
        {
            Assert.Equal(builtin.Beats[i].Threshold, data.Beats[i].Threshold);
            Assert.Equal(builtin.Beats[i].TextKey, data.Beats[i].TextKey);
            // #767 set the per-beat knowledge to 1; the data pack wins at runtime, so it must not drift
            // back up (it silently had, until PR 3 caught it).
            Assert.Equal(builtin.Beats[i].KnowledgeReward, data.Beats[i].KnowledgeReward);
            Assert.Equal(builtin.Beats[i].Cutscene, data.Beats[i].Cutscene); // #2466
        }

        // #2466: every behaviour-relevant manifest field of the pack runtime.
        Assert.Equal(builtin.Kind, data.Kind);
        Assert.Equal(StoryRegistry.EffectiveScope(builtin), StoryRegistry.EffectiveScope(data));
        Assert.Equal(builtin.Priority, data.Priority);
        Assert.Equal(builtin.ShortNameKey, data.ShortNameKey);
        Assert.Equal(builtin.DescriptionKey, data.DescriptionKey);
        Assert.Equal(builtin.Narrator.Id, data.Narrator.Id);
        Assert.Equal(builtin.Narrator.NameKey, data.Narrator.NameKey);
        Assert.Equal(builtin.DefaultEnabled, data.DefaultEnabled);
        Assert.Equal(builtin.IntroCutscene, data.IntroCutscene);
        Assert.Equal(builtin.Gates, data.Gates);
        Assert.Equal(builtin.Escalate.HintAfter, data.Escalate.HintAfter);
        Assert.Equal(builtin.Escalate.MarkerAfter, data.Escalate.MarkerAfter);
        Assert.Equal(builtin.Escalate.OfferSkipAfter, data.Escalate.OfferSkipAfter);
        Assert.Equal(builtin.Start.After, data.Start.After);
        Assert.Equal(builtin.Start.EarlyOptIn, data.Start.EarlyOptIn);
        Assert.Equal(builtin.Chapters.Count, data.Chapters.Count);
        Assert.Equal(builtin.Insights.Count, data.Insights.Count);
        // #2470: VEGA as its own pack — the chapters (reached by the arc), the insights, the narrator.
        Assert.Equal(builtin.Chapters.Select(c => (c.Id, c.TitleKey, c.FromBeat, c.FromEvent, c.OpenCutscene, c.CloseCutscene)),
            data.Chapters.Select(c => (c.Id, c.TitleKey, c.FromBeat, c.FromEvent, c.OpenCutscene, c.CloseCutscene)));
        Assert.Equal(builtin.Insights.Select(i => (i.Event, i.MinBeat, i.TextKey, i.OnceKey)),
            data.Insights.Select(i => (i.Event, i.MinBeat, i.TextKey, i.OnceKey)));
        Assert.Equal(builtin.Narrator.Icon, data.Narrator.Icon);
        Assert.Equal(builtin.Narrator.Color, data.Narrator.Color);
        Assert.Equal(builtin.Narrator.Voice, data.Narrator.Voice);
        Assert.Equal(builtin.Narrator.LineStart, data.Narrator.LineStart);
        Assert.Equal(builtin.Scope, data.Scope);
        Assert.Equal(builtin.Finale.Type, data.Finale.Type);
        Assert.Equal(builtin.Finale.Cutscenes, data.Finale.Cutscenes);
        Assert.Equal(builtin.Graduation.LineKey, data.Graduation.LineKey); // #2469
        Assert.Equal(builtin.Graduation.Counter, data.Graduation.Counter);
        Assert.Equal(builtin.Graduation.Suggest, data.Graduation.Suggest);
    }

    /// <summary>#2466: a pack written before the manifest existed loads with every new field at today's behaviour.</summary>
    [Fact]
    public void ManifestFields_DefaultToTodaysBehaviour()
    {
        var def = System.Text.Json.JsonSerializer.Deserialize<StoryDefinition>("{\"id\":\"old\",\"nameKey\":\"x\"}", ContentLoader.JsonOptions)!;
        Assert.Equal(StoryRegistry.KindStory, def.Kind);
        Assert.Equal(string.Empty, def.Scope);
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(def)); // no beats
        Assert.Equal(0, def.Priority);
        Assert.Equal(StoryNarrator.DefaultId, def.Narrator.Id);
        Assert.True(def.DefaultEnabled);
        Assert.Empty(def.Chapters);
        Assert.Empty(def.Gates);
        Assert.Empty(def.Start.After);
        Assert.False(def.Start.EarlyOptIn);
        Assert.Null(def.Escalate.HintAfter);
        Assert.Equal(string.Empty, def.IntroCutscene);
    }

    /// <summary>#2466: the manifest of a player pack loads into the model — chapters, steps, objectives, variants,
    /// escalation overrides and gates (the runtime's test pack).</summary>
    [Fact]
    public void APackManifest_LoadsIntoTheModel()
    {
        var def = StoryPackFixtures.Load(StoryPackFixtures.Runtime);
        Assert.Equal(StoryRegistry.KindSide, def.Kind);
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(def));
        Assert.Equal("story.runtime_test.short", def.ShortNameKey);
        Assert.Equal(5, def.Escalate.HintAfter);
        Assert.Equal(3, def.Chapters.Count);
        var a1 = def.Chapters[0].Steps[0];
        Assert.Equal("a.1", a1.Id);
        Assert.Equal(StoryStepKinds.Do, a1.Kind);
        Assert.Equal(StoryStepTiers.Must, a1.Tier);
        Assert.Equal(GameEventKeys.MineAny, a1.Objective.Event);
        Assert.Equal(3, a1.Objective.Required);
        Assert.Equal("nearest_ore:stone", a1.OnStart.Reveal);
        Assert.Equal("world:creative", Assert.Single(a1.Variants).When);
        Assert.Equal(3, def.Chapters[0].Steps[3].Escalate!.HintAfter);
        Assert.Null(def.Chapters[0].Steps[3].Escalate!.MarkerAfter);
        Assert.True(def.Chapters[2].Optional);
        Assert.True(def.Chapters[2].Steps[0].Objective.IsMission);
        Assert.Equal("b.2", def.Chapters[1].Steps[0].Alternative);

        var tutorial = StoryPackFixtures.Load(StoryPackFixtures.Tutorial);
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(tutorial));
        Assert.Equal("vega:stage:mine", tutorial.Chapters[0].Steps[0].LegacyKey);
        Assert.True(tutorial.Gates.ContainsKey(StoryGateNames.Banter));
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
