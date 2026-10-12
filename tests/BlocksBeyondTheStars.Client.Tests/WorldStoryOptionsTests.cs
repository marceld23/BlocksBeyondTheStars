// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2472 — world creation's "Stories" page (<see cref="WorldStoryOptions"/>): the main-story list (installed world
/// stories only, the default first, "None" last, capped), the summary row ("VEGA · Tutorial Auto ›" in English and German
/// from the real tables), the server arguments (non-defaults only, and they parse back into the rules the server bakes),
/// the fallbacks for a pack this client cannot name and the server words the in-game world rules read. #2498 added the fourth
/// tutorial rule "Short courses only" as the page's second row.
/// </summary>
public sealed class WorldStoryOptionsTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static StoryDefinition Pack(string id, string kind = StoryRegistry.KindStory, string scope = "", int beats = 1, int priority = 0)
    {
        var def = new StoryDefinition
        {
            Id = id,
            Kind = kind,
            Scope = scope,
            Priority = priority,
            NameKey = "story." + id + ".name",
            ShortNameKey = "story." + id + ".short",
        };
        for (int i = 0; i < beats; i++)
        {
            def.Beats.Add(new StoryBeat { Index = i, Threshold = i * 10, TextKey = "story." + id + ".beat" + i });
        }

        return def;
    }

    private static (Func<string, string> L, Func<string, bool> Has) Real(GameLocale locale)
    {
        var loc = Content.Value.CreateLocalizer(locale);
        return (loc.Get, loc.Has);
    }

    [Fact]
    public void AnUntouchedPage_EmitsNoArguments_AndReadsVegaWithTheTutorialOnAuto()
    {
        var opt = new WorldStoryOptions();
        Assert.Equal(string.Empty, opt.ToArgs()); // an untouched page creates exactly the world it created before
        Assert.True(opt.IsDefaultStory);
        Assert.False(opt.IsNone);

        var (en, enHas) = Real(GameLocale.English);
        Assert.Equal("VEGA · Tutorial Auto ›", opt.Summary(Content.Value.Stories, en, enHas));
        var (de, deHas) = Real(GameLocale.German);
        Assert.Equal("VEGA · Tutorial Auto ›", opt.Summary(Content.Value.Stories, de, deHas));
    }

    [Fact]
    public void TheSummary_NamesNone_ThePaceWhenNotNormal_AndTheTutorialRule()
    {
        var (en, enHas) = Real(GameLocale.English);
        var (de, deHas) = Real(GameLocale.German);
        var stories = Content.Value.Stories;

        var opt = new WorldStoryOptions { Density = 2, Tutorial = 2 };
        Assert.Equal("VEGA (Fast) · Tutorial On request ›", opt.Summary(stories, en, enHas));
        Assert.Equal("VEGA (Schnell) · Tutorial Auf Wunsch ›", opt.Summary(stories, de, deHas));

        opt = new WorldStoryOptions { StoryId = StoryRegistry.NoneStoryId, Density = 0, Tutorial = 3 };
        Assert.Equal("None · Tutorial Off ›", opt.Summary(stories, en, enHas)); // no pace without a story
        Assert.Equal("Keine · Tutorial Aus ›", opt.Summary(stories, de, deHas));

        opt = new WorldStoryOptions { Tutorial = 1 }; // #2498: the second row
        Assert.Equal("VEGA · Tutorial Short courses only ›", opt.Summary(stories, en, enHas));
        Assert.Equal("VEGA · Tutorial Nur Kurzkurse ›", opt.Summary(stories, de, deHas));
    }

    /// <summary>#2498: the page lists four tutorial rules, from the most guidance to none — Auto, Short courses only, On request,
    /// Off — each named and explained in English and German; the order is the page's (the enum appended Courses as 3).</summary>
    [Fact]
    public void TheTutorialRows_AreAutoShortCoursesOnRequestOff_WithTheirTexts()
    {
        Assert.Equal(new[] { "Auto", "Courses", "OptIn", "Off" }, WorldStoryOptions.TutorialSteps);
        Assert.Equal(new[] { "auto", "courses", "optin", "off" }, WorldStoryOptions.TutorialCliWords);
        Assert.Equal(3, (int)TutorialPolicy.Courses); // appended: a save's 0/1/2 keep their meaning

        var (en, enHas) = Real(GameLocale.English);
        var (de, deHas) = Real(GameLocale.German);
        Assert.Equal(new[] { "Auto", "Short courses only", "On request", "Off" }, WorldStoryOptions.TutorialNameKeys.Select(en).ToArray());
        Assert.Equal(new[] { "Auto", "Nur Kurzkurse", "Auf Wunsch", "Aus" }, WorldStoryOptions.TutorialNameKeys.Select(de).ToArray());
        foreach (string key in WorldStoryOptions.TutorialHintKeys)
        {
            Assert.True(enHas(key) && deHas(key), key);
            Assert.True(en(key).Length <= 90 && de(key).Length <= 90, $"{key}: one short line under the row");
        }

        Assert.Contains("short courses", en("ui.worldopt.tutorial_courses_hint"), StringComparison.Ordinal);
        Assert.Contains("Kurzkurse", de("ui.worldopt.tutorial_courses_hint"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none", 1, 0, " --story none")]
    [InlineData("vega_protocol", 0, 0, " --story-density Sparse")]
    [InlineData("", 2, 3, " --story-density Dense --tutorial off")]
    [InlineData("vega_protocol", 1, 2, " --tutorial optin")]
    [InlineData("vega_protocol", 1, 1, " --tutorial courses")] // #2498
    [InlineData("side_arc", 1, 0, " --story side_arc")]
    [InlineData("VEGA_PROTOCOL", 1, 0, "")]
    public void TheArguments_AreTheNonDefaultsOnly(string storyId, int density, int tutorial, string expected)
    {
        var opt = new WorldStoryOptions { StoryId = storyId, Density = density, Tutorial = tutorial };
        Assert.Equal(expected, opt.ToArgs());
    }

    [Theory]
    [InlineData("none", 0, 3, TutorialPolicy.Off)]
    [InlineData("vega_protocol", 2, 2, TutorialPolicy.OptIn)]
    [InlineData("vega_protocol", 1, 1, TutorialPolicy.Courses)] // #2498
    [InlineData("side_arc", 1, 0, TutorialPolicy.Auto)]
    public void TheArguments_ParseBackIntoTheRulesTheServerBakes(string storyId, int density, int tutorial, TutorialPolicy policy)
    {
        var opt = new WorldStoryOptions { StoryId = storyId, Density = density, Tutorial = tutorial };
        var config = new ServerConfig();
        config.ApplyCommandLine(opt.ToArgs().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(storyId == StoryRegistry.DefaultStoryId ? string.Empty : storyId, config.Rules.StoryId);
        Assert.Equal((StoryDensity)density, config.Rules.StoryDensity);
        Assert.Equal(policy, config.Rules.TutorialPolicy);
    }

    [Fact]
    public void TheStepTables_FollowTheServerEnums()
    {
        Assert.Equal(Enum.GetNames<StoryDensity>(), WorldStoryOptions.DensitySteps);
        // #2498: every tutorial rule once, in the page's order (not the enum's) — each step is a server word, and its CLI word
        // parses to the same rule.
        Assert.Equal(Enum.GetNames<TutorialPolicy>().OrderBy(n => n), WorldStoryOptions.TutorialSteps.OrderBy(n => n));
        Assert.Equal(WorldStoryOptions.TutorialSteps.Length, WorldStoryOptions.TutorialCliWords.Length);
        for (int i = 0; i < WorldStoryOptions.TutorialCliWords.Length; i++)
        {
            Assert.True(ServerConfig.TryParseTutorialPolicy(WorldStoryOptions.TutorialCliWords[i], out var p));
            Assert.Equal(Enum.Parse<TutorialPolicy>(WorldStoryOptions.TutorialSteps[i]), p);
            Assert.True(ServerConfig.TryParseTutorialPolicy(WorldStoryOptions.TutorialSteps[i], out var wire)); // the in-game row's word
            Assert.Equal(p, wire);
        }

        Assert.Equal((int)StoryDensity.Normal, WorldStoryOptions.NormalDensity);
        Assert.Equal((int)TutorialPolicy.Auto, WorldStoryOptions.AutoTutorial);
        Assert.Equal(WorldStoryOptions.TutorialSteps.Length, WorldStoryOptions.TutorialNameKeys.Length);
        Assert.Equal(WorldStoryOptions.TutorialSteps.Length, WorldStoryOptions.TutorialHintKeys.Length);
        Assert.Equal(WorldStoryOptions.DensitySteps.Length, WorldStoryOptions.DensityNameKeys.Length);
    }

    [Fact]
    public void EveryKeyThePageNames_HasAnEnglishAndAGermanText()
    {
        var keys = new List<string>
        {
            "ui.worldopt.stories", "ui.worldopt.stories_summary", "ui.worldopt.stories_paced", "ui.worldopt.stories_note",
            "ui.worldopt.main_story", "ui.worldopt.story_none", "ui.worldopt.story_none_short", "ui.worldopt.story_none_hint",
            "ui.worldopt.story_default", "ui.worldopt.story_density", "ui.worldopt.tutorial", "story.vega.desc",
        };
        keys.AddRange(WorldStoryOptions.TutorialNameKeys);
        keys.AddRange(WorldStoryOptions.TutorialHintKeys);
        keys.AddRange(WorldStoryOptions.DensityNameKeys);

        foreach (var code in new[] { "en", "de" })
        {
            var table = LocaleKeys(code);
            Assert.All(keys, k => Assert.True(table.Contains(k), $"{code}: {k} is missing"));
        }
    }

    /// <summary>Every key of one language: the core table plus each story pack's table (no English fallback).</summary>
    private static HashSet<string> LocaleKeys(string code)
    {
        var files = new List<string> { Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json") };
        files.AddRange(Directory.GetDirectories(Path.Combine(ClientTestPaths.DataDir(), "stories"))
            .Select(d => Path.Combine(d, "locales", code + ".json"))
            .Where(File.Exists));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file),
                new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                keys.Add(p.Name);
            }
        }

        return keys;
    }

    [Fact]
    public void TheMainStoryList_HoldsInstalledWorldStoriesOnly_DefaultFirst_NoneLast()
    {
        var packs = new[]
        {
            Pack("tutorial", StoryRegistry.KindTutorial, beats: 0, priority: 100), // teaches — never a world story
            Pack("side_quest", StoryRegistry.KindSide, StoryRegistry.ScopePlayer),  // player-scoped
            Pack("empty_arc", beats: 0),                                             // beat-less (N11)
            Pack("zeta_arc"),
            Pack("alpha_arc"),
            Pack("big_arc", priority: 5),
            Pack(StoryRegistry.DefaultStoryId),
        };

        var choices = WorldStoryOptions.MainStoryChoices(packs);
        Assert.Equal(new[] { StoryRegistry.DefaultStoryId, "big_arc", "alpha_arc", "zeta_arc", StoryRegistry.NoneStoryId }, choices);
    }

    [Fact]
    public void TheMainStoryList_FromTheShippedData_IsVegaAndNone()
    {
        Assert.Equal(new[] { StoryRegistry.DefaultStoryId, StoryRegistry.NoneStoryId },
            WorldStoryOptions.MainStoryChoices(Content.Value.Stories.Values));
        Assert.Equal(new[] { StoryRegistry.DefaultStoryId, StoryRegistry.NoneStoryId }, WorldStoryOptions.MainStoryChoices(null));
    }

    [Fact]
    public void TheMainStoryList_IsCapped_ButKeepsTheDefaultAndNone()
    {
        var packs = Enumerable.Range(0, 20).Select(i => Pack($"arc_{i:00}")).ToList();
        var choices = WorldStoryOptions.MainStoryChoices(packs);

        Assert.Equal(WorldStoryOptions.MaxChoices, choices.Count);
        Assert.Equal(StoryRegistry.DefaultStoryId, choices[0]);
        Assert.Equal(StoryRegistry.NoneStoryId, choices[^1]);
    }

    [Fact]
    public void Normalize_DropsAStoryThePageDoesNotList_AndClampsTheSteps()
    {
        var choices = new[] { StoryRegistry.DefaultStoryId, "side_arc", StoryRegistry.NoneStoryId };
        var opt = new WorldStoryOptions { StoryId = "removed_arc", Density = 9, Tutorial = -3 };
        opt.Normalize(choices);
        Assert.Equal(StoryRegistry.DefaultStoryId, opt.StoryId);
        Assert.Equal(2, opt.Density);
        Assert.Equal(0, opt.Tutorial);

        opt.StoryId = "SIDE_ARC";
        opt.Normalize(choices);
        Assert.Equal("SIDE_ARC", opt.StoryId);
        Assert.True(opt.IsChosen("side_arc"));
        Assert.False(opt.IsChosen(StoryRegistry.DefaultStoryId));

        opt.StoryId = string.Empty; // empty = the default story
        opt.Normalize(choices);
        Assert.True(opt.IsChosen(StoryRegistry.DefaultStoryId));
    }

    [Fact]
    public void Names_FallBackForAPackThisClientCannotName()
    {
        var stories = new Dictionary<string, StoryDefinition> { ["side_arc"] = Pack("side_arc") };
        var table = new Dictionary<string, string>
        {
            ["ui.worldopt.story_none"] = "None (sandbox)",
            ["ui.worldopt.story_none_short"] = "None",
            ["ui.worldopt.story_default"] = "The VEGA Protocol",
            ["ui.worldopt.story_none_hint"] = "No story.",
        };
        string L(string k) => table.TryGetValue(k, out var v) ? v : k;
        bool Has(string k) => table.ContainsKey(k);

        Assert.Equal("side_arc", WorldStoryOptions.StoryName("side_arc", stories, L, Has));       // no text → the id
        Assert.Equal("side_arc", WorldStoryOptions.StoryShortName("side_arc", stories, L, Has));
        Assert.Equal("The VEGA Protocol", WorldStoryOptions.StoryName(StoryRegistry.DefaultStoryId, null, L, Has)); // built-in name
        Assert.Equal("None (sandbox)", WorldStoryOptions.StoryName(StoryRegistry.NoneStoryId, stories, L, Has));
        Assert.Equal(string.Empty, WorldStoryOptions.StoryDescription("side_arc", stories, L, Has));
        Assert.Equal("No story.", WorldStoryOptions.StoryDescription(StoryRegistry.NoneStoryId, stories, L, Has));

        table["story.side_arc.name"] = "The Side Arc";
        table["story.side_arc.short"] = "Side";
        Assert.Equal("The Side Arc", WorldStoryOptions.StoryName("side_arc", stories, L, Has));
        Assert.Equal("Side", WorldStoryOptions.StoryShortName("side_arc", stories, L, Has));
    }

    [Fact]
    public void TheShippedStories_HaveTheirNamesAndDescriptionsOnThePage()
    {
        var (en, has) = Real(GameLocale.English);
        var stories = Content.Value.Stories;
        Assert.Equal("The VEGA Protocol", WorldStoryOptions.StoryName(StoryRegistry.DefaultStoryId, stories, en, has));
        Assert.NotEqual(string.Empty, WorldStoryOptions.StoryDescription(StoryRegistry.DefaultStoryId, stories, en, has));

        var tutorial = WorldStoryOptions.TutorialPack(stories.Values);
        Assert.NotNull(tutorial);
        Assert.Equal("tutorial", tutorial!.Id);
        Assert.True(has(tutorial.DescriptionKey));
        Assert.Null(WorldStoryOptions.TutorialPack(null));
    }

    [Fact]
    public void ABrokenTranslation_NeverThrows()
    {
        var opt = new WorldStoryOptions { Density = 0 };
        string L(string k) => k switch
        {
            "ui.worldopt.stories_summary" => "{0} · {3} ›", // a translator's stray placeholder
            "ui.worldopt.stories_paced" => "{0} ({1})",
            _ => k,
        };
        string summary = opt.Summary(null, L, _ => false);
        Assert.Contains("ui.worldopt.tutorial_auto", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Auto", 0)]
    [InlineData("Courses", 1)] // #2498
    [InlineData("optin", 2)]
    [InlineData("OFF", 3)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    [InlineData("On", -1)]
    public void TheInGameRows_ReadTheServersTutorialWord(string? word, int expected)
        => Assert.Equal(expected, WorldStoryOptions.TutorialIndex(word));

    [Theory]
    [InlineData("Sparse", 0)]
    [InlineData("normal", 1)]
    [InlineData("Dense", 2)]
    [InlineData("", -1)] // an older server: the row is hidden
    public void TheInGameRows_ReadTheServersPaceWord(string word, int expected)
        => Assert.Equal(expected, WorldStoryOptions.DensityIndex(word));

    [Fact]
    public void CopyFrom_TakesEveryChoice()
    {
        var a = new WorldStoryOptions { StoryId = StoryRegistry.NoneStoryId, Density = 0, Tutorial = 2 };
        var b = new WorldStoryOptions();
        b.CopyFrom(a);
        Assert.Equal(a.ToArgs(), b.ToArgs());
        Assert.True(b.IsNone);
    }

    [Fact]
    public void AnIdThatCannotRideTheCommandLine_IsNeverSent()
    {
        Assert.Equal(string.Empty, new WorldStoryOptions { StoryId = "bad id\" --admins x" }.ToArgs());
        Assert.Equal(string.Empty, new WorldStoryOptions { StoryId = "mem" }.ToArgs()); // a reserved id
    }
}
