// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2459 — gap closure, one teaching voice for the client-only lessons (synthesis G12): VEGA's lessons the client speaks
/// itself (<c>VegaPanel.SayLocal</c> — the planet card, target lock, wormhole, climbing, the bio lab's tabs, the flight chart
/// and the ship scanner) stay quiet while a running deep-dive course has the step that teaches them active or still ahead,
/// and count as given then. The course steps name them in their <c>hintRefs</c> (data), the client reads its copy of the pack
/// and the server's Journal view (<see cref="StoryPackStates"/>); an offered course, a passed step, an older server or a pack
/// this client lacks cover nothing.
/// </summary>
public sealed class StoryGapClosureClientTests
{
    private const string T = "tutorial";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static IEnumerable<StoryDefinition> Packs => Content.Value.Stories.Values;

    /// <summary>The Journal view of the tutorial with one chapter in <paramref name="state"/> and these active steps.</summary>
    private static StoryPackStates States(string chapter, string state, params string[] activeSteps) => new()
    {
        Packs = new[]
        {
            new NetStoryPack
            {
                Id = T,
                Kind = StoryRegistry.KindTutorial,
                Scope = StoryRegistry.ScopePlayer,
                Enabled = true,
                Started = true,
                Done = true,
                Chapters = new[] { new NetStoryChapter { Id = chapter, State = state, Optional = true } },
                Steps = activeSteps.Select(s => new NetStoryStep { Id = s, ChapterId = chapter }).ToArray(),
            },
        },
    };

    private static bool Covered(string lesson, StoryPackStates? states) => VegaText.LessonCoveredByStoryStep("vega.hint." + lesson, states, Packs);

    [Theory]
    [InlineData("planet_card", "c3", "c3.2")]
    [InlineData("ship_scanner", "c3", "c3.2")]
    [InlineData("ship_scan_hold", "c3", "c3.2")]
    [InlineData("chart_waypoint", "c3", "c3.1")]
    [InlineData("target_lock", "c3", "c3.6")]
    [InlineData("wormhole", "c5", "c5.9")]
    [InlineData("climb", "c13", "c13.1")]
    [InlineData("bio_lab_tabs", "c10", "c10.5")]
    public void EachClientLesson_IsNamedByTheCourseStepThatTeachesIt(string lesson, string chapter, string step)
    {
        var pack = Content.Value.Stories[T];
        var def = pack.Chapters.Single(c => c.Id == chapter).Steps.Single(s => s.Id == step);
        Assert.Contains(lesson, def.HintRefs);
    }

    [Fact]
    public void EveryLessonTheClientSpeaks_IsTaughtByTheTrainingOrACourse()
    {
        // Every SayLocal("vega.hint.<id>") in the Unity scripts: the basic training covers it (E11), or a course step names it.
        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        var lessons = Directory.GetFiles(scripts, "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"SayLocal\(""vega\.hint\.(?<id>[a-z0-9_]+)""\)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5))
                .Select(m => m.Groups["id"].Value))
            .Distinct()
            .ToList();
        Assert.Contains("climb", lessons);
        var named = Content.Value.Stories[T].Chapters.SelectMany(c => c.Steps).SelectMany(s => s.HintRefs).ToHashSet();
        foreach (string lesson in lessons)
        {
            Assert.True(VegaText.LessonCoveredByTutorial("vega.hint." + lesson) || named.Contains(lesson),
                $"The client lesson vega.hint.{lesson} is taught by no tutorial step — name it in the hintRefs of the step that teaches it.");
        }
    }

    [Fact]
    public void ARunningCourse_CoversItsLessons_WhileTheStepIsActiveOrAhead()
    {
        // The pilot course at its first step: the chart (active), the planet card and the target lock (ahead).
        var pilot = States("c3", StoryChapterStates.Active, "c3.1");
        Assert.True(Covered("chart_waypoint", pilot));
        Assert.True(Covered("planet_card", pilot));
        Assert.True(Covered("target_lock", pilot));
        Assert.False(Covered("climb", pilot)); // another course's lesson

        // Past the chart step: the chart lesson is no longer the course's to give; the card still is.
        var later = States("c3", StoryChapterStates.Active, "c3.3");
        Assert.False(Covered("chart_waypoint", later));
        Assert.False(Covered("planet_card", later));
        Assert.True(Covered("target_lock", later));

        // A replay counts like a run; with no active step known the whole chapter does.
        Assert.True(Covered("climb", States("c13", StoryChapterStates.Replay, "c13.1")));
        Assert.True(Covered("climb", States("c13", StoryChapterStates.Active)));
        Assert.False(Covered("climb", States("c13", StoryChapterStates.Active, "c13.2")));
    }

    [Fact]
    public void AnOfferedOrFinishedCourse_AnOlderServer_OrAMissingPack_CoverNothing()
    {
        Assert.False(Covered("climb", States("c13", StoryChapterStates.Offered)));
        Assert.False(Covered("climb", States("c13", StoryChapterStates.Declined)));
        Assert.False(Covered("climb", States("c13", StoryChapterStates.Done)));
        Assert.False(Covered("climb", null)); // an older server sends no Journal view
        Assert.False(VegaText.LessonCoveredByStoryStep("vega.hint.climb", States("c13", StoryChapterStates.Active, "c13.1"), Array.Empty<StoryDefinition>()));
        Assert.False(VegaText.LessonCoveredByStoryStep("ui.hud.use", States("c13", StoryChapterStates.Active, "c13.1"), Packs)); // no VEGA hint
        Assert.False(VegaText.LessonCoveredByStoryStep(null, States("c13", StoryChapterStates.Active, "c13.1"), Packs));
    }

    [Fact]
    public void ThePanel_AsksTheCourses_BeforeItSpeaksALesson()
    {
        string panel = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "VegaPanel.cs"));
        int say = panel.IndexOf("public bool SayLocal(string lineKey)", StringComparison.Ordinal);
        int covered = panel.IndexOf("VegaText.LessonCoveredByStoryStep(lineKey, Game.StoryPackStates", say, StringComparison.Ordinal);
        int muted = panel.IndexOf("Settings is { VegaHints: false }", say, StringComparison.Ordinal);
        Assert.True(say >= 0 && covered > say && muted > covered, "SayLocal must check the running courses before the hints mute");

        // Every lesson key the panel may speak exists in English and German.
        using var en = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", "en.json")));
        using var de = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", "de.json")));
        foreach (string lesson in new[] { "planet_card", "target_lock", "wormhole", "climb", "bio_lab_tabs", "chart_waypoint" })
        {
            Assert.True(en.RootElement.TryGetProperty("vega.hint." + lesson, out _), lesson);
            Assert.True(de.RootElement.TryGetProperty("vega.hint." + lesson, out _), lesson);
        }
    }
}
