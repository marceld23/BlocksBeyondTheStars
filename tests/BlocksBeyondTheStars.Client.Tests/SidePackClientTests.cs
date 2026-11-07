// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3347 / #3350 / #3357 on the client, Unity-free: the world-creation switches for the side stories (listed, on by default,
/// the festival stories left to the Festivals option, only the switched-off ones sent — and the server reads them back), a
/// sung dialog reply (its note symbols and its notes in the Sounder voice), the research tree's rule for hidden blueprints,
/// and the chart's <c>system:&lt;id&gt;</c> anchor.
/// </summary>
public sealed class SidePackClientTests
{
    private static StoryDefinition Side(string id, int priority = 0, string? after = null)
    {
        var def = new StoryDefinition
        {
            Id = id,
            Kind = StoryRegistry.KindSide,
            Scope = StoryRegistry.ScopePlayer,
            Priority = priority,
            NameKey = "story." + id + ".name",
        };
        def.Chapters.Add(new StoryChapter { Id = "c1" });
        if (after is not null)
        {
            def.Start.After.Add(after);
        }

        return def;
    }

    [Fact]
    public void TheSideStories_AreListed_ByPriority_WithoutFestivalsTutorialsOrWorldStories()
    {
        var stories = new[]
        {
            Side("sounders", 20),
            Side("side_pilot", 10),
            Side("festival_lights", 5, "world:festival_lights"),
            new StoryDefinition { Id = "tutorial", Kind = StoryRegistry.KindTutorial, Scope = StoryRegistry.ScopePlayer },
            new StoryDefinition { Id = "vega_protocol", Kind = StoryRegistry.KindStory, Beats = { new StoryBeat() } },
        };
        Assert.Equal(new[] { "sounders", "side_pilot" }, WorldStoryOptions.SideStoryChoices(stories));
        Assert.True(WorldStoryOptions.IsFestivalStory(stories[2]));
        Assert.Equal(WorldStoryOptions.MaxSideStories,
            WorldStoryOptions.SideStoryChoices(Enumerable.Range(0, 9).Select(i => Side("s" + i))).Count);
    }

    [Fact]
    public void ASwitchedOffSideStory_IsTheOnlyThingSent_AndTheServerReadsItBack()
    {
        var opt = new WorldStoryOptions();
        Assert.True(opt.SideStoryOn("sounders"));
        Assert.Equal(string.Empty, opt.ToArgs()); // every side story on: nothing to send

        opt.SetSideStory("sounders", on: false);
        opt.SetSideStory("side_pilot", on: false);
        opt.SetSideStory("side_pilot", on: true);
        Assert.False(opt.SideStoryOn("Sounders"));
        Assert.Equal(" --disable-stories sounders", opt.ToArgs());

        var copy = new WorldStoryOptions();
        copy.CopyFrom(opt);
        Assert.False(copy.SideStoryOn("sounders"));

        var config = new ServerConfig();
        config.ApplyCommandLine(opt.ToArgs().Trim().Split(' '));
        Assert.Equal(new[] { "sounders" }, config.Rules.DisabledStoryPacks);
    }

    [Fact]
    public void ASungReply_ShowsItsNotes_AndPlaysThemInTheSounderVoice()
    {
        var names = DialogMelody.ToneNames("C D E F G A H C'");
        Assert.Equal("H", names[6]);
        Assert.Equal(SounderAlphabet.ToneNames, DialogMelody.ToneNames("broken"));

        // O = row F (3), column E (2); R = row G (4), column D (1).
        Assert.Equal(new[] { 3, 2, SounderAlphabet.Rest, 4, 1 }, DialogMelody.Notes("or"));
        Assert.Equal("♪ F E · G D ♪", DialogMelody.Symbols("OR", SounderAlphabet.ToneNames));
        Assert.Equal("Sing back  ♪ F E · G D ♪", DialogMelody.ChoiceLabel("Sing back", "OR", SounderAlphabet.ToneNames));
        Assert.Equal("Goodbye", DialogMelody.ChoiceLabel("Goodbye", string.Empty, SounderAlphabet.ToneNames));
        Assert.Equal("note_4_3", DialogMelody.NoteId(3));
        Assert.Null(DialogMelody.NoteId(SounderAlphabet.Rest));
        Assert.Equal("OR", DialogMelody.MelodyOf(new[] { "OR", string.Empty }, 0));
        Assert.Equal(string.Empty, DialogMelody.MelodyOf(System.Array.Empty<string>(), 1)); // an older server sends none
    }

    [Fact]
    public void AHiddenBlueprint_IsListedOnlyOnceTheServerShowsItOrItIsOwned()
    {
        var plain = new BlueprintDefinition { Key = "glider" };
        var hidden = new BlueprintDefinition { Key = "sounder_resonance", HiddenUntil = "milestone:snd:disc:song_disc_builders" };
        var none = new HashSet<string>();
        Assert.True(HiddenBlueprints.IsShown(plain, none, null));
        Assert.False(HiddenBlueprints.IsShown(hidden, none, null));
        Assert.True(HiddenBlueprints.IsShown(hidden, none, new HashSet<string> { "sounder_resonance" }));
        Assert.True(HiddenBlueprints.IsShown(hidden, new HashSet<string> { "sounder_resonance" }, null));
    }

    [Fact]
    public void ASystemAnchor_RingsAPackSystem_OnlyWhenItIsOnTheStarMap()
    {
        var map = new StarMapData { Systems = new[] { new NetStarSystem { Id = "sys0" }, new NetStarSystem { Id = "ornemis" } } };
        Assert.Equal("ornemis", PackSystemChart.TargetOf("system:ornemis", map));
        Assert.Null(PackSystemChart.TargetOf("system:ornemis", new StarMapData()));
        Assert.Null(PackSystemChart.TargetOf(CutsceneAnchors.StarMap, map));
        Assert.True(CutsceneRules.NeedsObject("system:ornemis")); // not on the map: the scene plays as a card
        Assert.False(CutsceneRules.NeedsObject(CutsceneAnchors.StarMap));
    }
}
