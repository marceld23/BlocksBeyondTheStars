// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3347 / #3349 / #3357 — the C# validator knows what a side pack may bring besides chapters: lore gates, readable texts,
/// star systems, a milestone namespace, dialog replies with conditions, melodies and <c>milestone:</c> consequences, the
/// <c>milestone:</c> condition, the new marker providers and the <c>system:&lt;id&gt;</c> chart anchor. The test pack
/// <c>side_extras</c> (every new field at once) is clean in C# — and in the Python twin, which the strict read mirrors.
/// </summary>
public sealed class StoryPackExtrasValidationTests
{
    private static readonly JsonSerializerOptions Strict = StoryPackValidationTests.Strict;

    private static StoryDefinition Pack(Action<StoryDefinition>? edit = null)
    {
        var def = StoryPackFixtures.Load(StoryPackFixtures.SideExtras);
        edit?.Invoke(def);
        return def;
    }

    private static List<StoryPackIssue> Issues(StoryDefinition def)
    {
        var installed = new Dictionary<string, StoryDefinition>(StoryPackValidationTests.Shipped.Value.Stories, StringComparer.OrdinalIgnoreCase)
        {
            [def.Id] = def,
        };
        return StoryPackValidator.Validate(def, StoryPackValidationTests.Context(installed, StoryPackFixtures.Dir(StoryPackFixtures.SideExtras)));
    }

    private static void AssertError(StoryDefinition def, string fragment)
    {
        var errors = Issues(def).Where(i => i.Severity == StoryPackIssueSeverity.Error).ToList();
        Assert.True(errors.Any(e => e.Message.Contains(fragment, StringComparison.Ordinal)),
            $"expected an error containing '{fragment}', got:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void TheSideExtrasPack_ReadsStrictly_AndIsClean()
    {
        Assert.Null(Record.Exception(() => JsonSerializer.Deserialize<StoryDefinition>(StoryPackFixtures.RawJson(StoryPackFixtures.SideExtras), Strict)));
        var issues = Issues(Pack());
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    [Fact]
    public void AVisibleIf_ReadsAsOneStringOrAList()
    {
        var one = JsonSerializer.Deserialize<DialogChoice>("{\"visibleIf\": \"milestone:snd:echo:kamo\"}", ContentLoader.JsonOptions)!;
        var two = JsonSerializer.Deserialize<DialogChoice>("{\"visibleIf\": [\"milestone:a:b\", \"world:hunger\"]}", ContentLoader.JsonOptions)!;
        Assert.Equal(new[] { "milestone:snd:echo:kamo" }, one.VisibleIf);
        Assert.Equal(new[] { "milestone:a:b", "world:hunger" }, two.VisibleIf);
        Assert.Empty(JsonSerializer.Deserialize<DialogChoice>("{}", ContentLoader.JsonOptions)!.VisibleIf);
    }

    [Fact]
    public void AMilestoneConsequence_OutsideThePacksNamespace_IsAnError()
        => AssertError(Pack(d => d.Dialogs[0].Nodes[0].Choices[0].Consequence = "milestone:snd:echo:kamo"), "own milestone namespace");

    [Fact]
    public void AMelody_IsLettersOnly()
        => AssertError(Pack(d => d.Dialogs[0].Nodes[0].Choices[0].Melody = "OR-NE5"), "a melody is letters");

    [Fact]
    public void AChoiceCondition_MustParse()
        => AssertError(Pack(d => d.Dialogs[0].Nodes[0].Choices[1].VisibleIf = new List<string> { "world:no_such_thing" }), "unknown world predicate");

    [Fact]
    public void AReservedMilestonePrefix_IsAnError()
        => AssertError(Pack(d => d.MilestonePrefix = "story"), "own milestone namespace");

    [Fact]
    public void ATextWithoutAKey_OrUsedTwice_IsAnError()
    {
        AssertError(Pack(d => d.Texts[0].TextKey = string.Empty), "titleKey and a textKey");
        AssertError(Pack(d => d.Texts[1].Id = d.Texts[0].Id), "used twice");
        AssertError(Pack(d => d.Texts[0].UnlockedBy = "milestone:"), "milestone key");
    }

    [Theory]
    [InlineData("sys3")]
    [InlineData("guardian_finale")]
    [InlineData("bad id")]
    public void AProceduralOrGuardianSystemId_IsAnError(string id) => AssertError(Pack(d => d.Systems[0].Id = id), "system id");

    [Fact]
    public void ASystemNeedsANameAPlanetTypeAndAKnownPlacement()
    {
        AssertError(Pack(d => d.Systems[0].Name = string.Empty), "a nameKey or a name");
        AssertError(Pack(d => d.Systems[0].PlanetType = string.Empty), "planet type of its world");
        AssertError(Pack(d => d.Systems[0].Placement = "beside_home"), "unknown placement");
    }

    [Fact]
    public void AWorldStory_MayNotDeclareSystems()
        => AssertError(Pack(d => { d.Scope = StoryRegistry.ScopeWorld; d.Kind = StoryRegistry.KindStory; }), "only a player pack's systems");

    [Fact]
    public void AStorySystemMarker_AndASystemAnchor_NameADeclaredSystem()
    {
        AssertError(Pack(d => d.Chapters[1].Steps[0].OnStart.Reveal = "story_system:nowhere"), "declares the system 'nowhere'");
        Assert.True(StoryRevealProviders.IsKnown("pack_site:choir_tower"));
        Assert.True(StoryRevealProviders.IsKnown(StoryRevealProviders.NearestMonument));
        Assert.False(StoryRevealProviders.IsKnown("pack_site:"));
        Assert.False(StoryRevealProviders.IsKnown("story_system:"));

        var chart = new CutsceneDefinition
        {
            Id = "sx.chart",
            Tier = CutsceneTiers.Scene,
            Shots = { new CutsceneShot { Duration = 4f, Camera = new CutsceneCamera { Type = CutsceneCameras.Chart, Anchor = "system:sx_home" } } },
        };
        Assert.DoesNotContain(Issues(Pack(d => d.Cutscenes.Add(chart))), i => i.Path.Contains("camera.anchor", StringComparison.Ordinal));
        chart.Shots[0].Camera.Anchor = "system:nowhere";
        AssertError(Pack(d => d.Cutscenes.Add(chart)), "declares the system 'nowhere'");
        chart.Shots[0].Camera = new CutsceneCamera { Type = CutsceneCameras.Orbit, Anchor = "system:sx_home" };
        AssertError(Pack(d => d.Cutscenes.Add(chart)), "needs a chart camera");
        Assert.False(CutsceneAnchors.IsKnown(CutsceneAnchors.System)); // never bare
        Assert.Equal("ornemis", CutsceneAnchors.SystemIdOf("system:ornemis"));
    }

    [Fact]
    public void ALoreGate_FitsThePacksScope()
    {
        AssertError(Pack(d => d.LoreSites[0].MinChapter = 9), "out of range");
        AssertError(Pack(d => d.LoreSites[1].Key = d.LoreSites[0].Key), "used twice");
        var warnings = Issues(Pack(d => d.LoreSites[0].MinKnowledge = 2)).Where(i => i.Severity == StoryPackIssueSeverity.Warning);
        Assert.Contains(warnings, w => w.Message.Contains("minChapter", StringComparison.Ordinal));
    }

    [Fact]
    public void ACharactersPlanetType_MustExist()
        => AssertError(Pack(d => d.Characters[0].PlanetTypes.Add("no_such_world")), "planet type 'no_such_world'");

    [Fact]
    public void TheShippedBlueprints_HiddenUntilParses()
    {
        foreach (var bp in StoryPackValidationTests.Shipped.Value.Blueprints.Values.Where(b => !string.IsNullOrWhiteSpace(b.HiddenUntil)))
        {
            Assert.True(StoryConditionParser.TryParse(bp.HiddenUntil, out _, out string error), $"{bp.Key}: {error}");
        }

        string raw = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "blueprints.json"));
        Assert.NotNull(JsonSerializer.Deserialize<List<BlueprintDefinition>>(raw, ContentLoader.JsonOptions));
    }
}
