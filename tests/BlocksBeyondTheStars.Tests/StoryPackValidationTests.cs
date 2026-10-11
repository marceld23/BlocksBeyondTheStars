// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2466 — the story-pack validator (synthesis §5.9; the C# twin of <c>tools/validate_story_pack.py</c>): every pack under
/// <c>data/stories/</c> and both test packs pass, and each rule catches what it is for — reserved and repeated ids, a
/// tutorial that is not player-scoped, a player pack with beats (it could never be the world story), conditions that do
/// not parse or name nothing, <c>ui:</c> outside a player pack's step objectives, mission bridges to missing missions,
/// legacy keys outside the tutorial, alternatives from another chapter, unknown marker providers, tutorial rewards,
/// missing English/German keys, an over-long short name and the text budget, cutscene bindings (once cutscenes exist).
/// </summary>
public sealed class StoryPackValidationTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static string StoriesDir => Path.Combine(TestPaths.DataDir(), "stories");

    private static IEnumerable<string> PackDirs()
        => Directory.GetDirectories(StoriesDir).Where(d => File.Exists(Path.Combine(d, "story.json"))).OrderBy(d => d, StringComparer.Ordinal);

    public static IEnumerable<object[]> DataPacks() => PackDirs().Select(d => new object[] { Path.GetFileName(d) });

    /// <summary>The base tables plus every data pack's tables (and the given extra pack folders), per language — the
    /// English and German lookups must be separate (a localizer falls back to English and would hide a missing German key).</summary>
    private static Dictionary<string, string> Table(string code, params string[] extraPackDirs)
    {
        var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")));
        foreach (string dir in PackDirs().Concat(extraPackDirs))
        {
            string file = Path.Combine(dir, "locales", code + ".json");
            if (File.Exists(file))
            {
                foreach (var kv in ContentLoader.ParseLocaleTable(File.ReadAllText(file)))
                {
                    table[kv.Key] = kv.Value;
                }
            }
        }

        return table;
    }

    private static StoryPackValidationContext Context(IReadOnlyDictionary<string, StoryDefinition>? packs = null, params string[] extraPackDirs)
    {
        var content = Shipped.Value;
        var en = Table("en", extraPackDirs);
        var de = Table("de", extraPackDirs);
        return new StoryPackValidationContext
        {
            Packs = packs ?? content.Stories,
            MissionExists = id => content.Missions.ContainsKey(id),
            BlueprintExists = key => content.GetBlueprint(key) is not null,
            BlockExists = key => content.GetBlock(key) is not null,
            English = k => en.TryGetValue(k, out var v) ? v : null,
            German = k => de.TryGetValue(k, out var v) ? v : null,
        };
    }

    private static List<StoryPackIssue> Errors(IEnumerable<StoryPackIssue> issues)
        => issues.Where(i => i.Severity == StoryPackIssueSeverity.Error).ToList();

    // ---------------- The shipped packs and the test packs pass ----------------

    [Theory]
    [MemberData(nameof(DataPacks))]
    public void EveryDataPack_PassesTheValidator(string folder)
    {
        string raw = File.ReadAllText(Path.Combine(StoriesDir, folder, "story.json"));
        var def = JsonSerializer.Deserialize<StoryDefinition>(raw, ContentLoader.JsonOptions)!;
        var issues = StoryPackValidator.Validate(def, Context(), raw);
        Assert.True(Errors(issues).Count == 0, string.Join("\n", Errors(issues)));
    }

    [Theory]
    [MemberData(nameof(DataPacks))]
    public void EveryDataPack_UsesOnlyKnownFields(string folder)
    {
        // A misspelt field ("requries") would silently fall back to its default — the strict read names it.
        var strict = new JsonSerializerOptions(ContentLoader.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        string raw = File.ReadAllText(Path.Combine(StoriesDir, folder, "story.json"));
        var ex = Record.Exception(() => JsonSerializer.Deserialize<StoryDefinition>(raw, strict));
        Assert.Null(ex);
    }

    [Fact]
    public void PackIds_AreUnique_AcrossTheDataFolders()
    {
        var ids = PackDirs().Select(d => JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(Path.Combine(d, "story.json")), ContentLoader.JsonOptions)!.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(StoryPackFixtures.Runtime)]
    [InlineData(StoryPackFixtures.Tutorial)]
    public void TheTestPacks_PassTheValidator_AndUseOnlyKnownFields(string id)
    {
        string raw = StoryPackFixtures.RawJson(id);
        var strict = new JsonSerializerOptions(ContentLoader.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var def = JsonSerializer.Deserialize<StoryDefinition>(raw, strict)!;
        var packs = new Dictionary<string, StoryDefinition>(Shipped.Value.Stories, StringComparer.OrdinalIgnoreCase)
        {
            [StoryPackFixtures.Runtime] = StoryPackFixtures.Load(StoryPackFixtures.Runtime),
            [StoryPackFixtures.Tutorial] = StoryPackFixtures.Load(StoryPackFixtures.Tutorial),
        };
        var issues = StoryPackValidator.Validate(def, Context(packs, StoryPackFixtures.Dir(id)), raw);
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    [Fact]
    public void TheBuiltInVegaPack_PassesTheValidator()
    {
        var issues = StoryPackValidator.Validate(StoryRegistry.Default, Context());
        Assert.True(Errors(issues).Count == 0, string.Join("\n", Errors(issues)));
    }

    [Fact]
    public void APlayerPack_IsNeverTheWorldStory()
    {
        var content = StoryPackFixtures.ContentWith(StoryPackFixtures.Runtime, StoryPackFixtures.Tutorial);
        foreach (string id in new[] { StoryPackFixtures.Runtime, StoryPackFixtures.Tutorial })
        {
            Assert.False(StoryRegistry.CanBeWorldStory(content.Stories[id]));
            Assert.False(content.TryGetWorldStory(id, out var fallback));
            Assert.Equal(StoryRegistry.DefaultStoryId, fallback.Id);
        }
    }

    // ---------------- Each rule catches its defect ----------------

    private static StoryDefinition Side(Action<StoryDefinition>? edit = null)
    {
        var def = new StoryDefinition
        {
            Id = "probe",
            Kind = StoryRegistry.KindSide,
            Scope = StoryRegistry.ScopePlayer,
            NameKey = "story.vega_protocol.name",
            Chapters =
            {
                new StoryChapter
                {
                    Id = "c1",
                    TitleKey = "story.vega_protocol.name",
                    Steps =
                    {
                        new StoryStep
                        {
                            Id = "c1.1",
                            Objective = new StoryStepObjective { Event = GameEventKeys.MineAny },
                            Explain = new StoryStepExplain { ChipKey = "vega.obj.mine" },
                        },
                    },
                },
            },
        };
        edit?.Invoke(def);
        return def;
    }

    private static StoryStep Step(StoryDefinition def, int i = 0) => def.Chapters[0].Steps[i];

    private static void AssertError(StoryDefinition def, string fragment, string? raw = null, StoryPackValidationContext? context = null)
    {
        var errors = Errors(StoryPackValidator.Validate(def, context ?? Context(), raw));
        Assert.True(errors.Any(e => e.Message.Contains(fragment, StringComparison.Ordinal)),
            $"expected an error containing '{fragment}', got:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void TheProbePack_IsClean()
    {
        var issues = StoryPackValidator.Validate(Side(), Context());
        Assert.True(Errors(issues).Count == 0, string.Join("\n", issues));
    }

    [Theory]
    [InlineData("mem")]
    [InlineData("vega")]
    [InlineData("none")]
    public void AReservedId_IsAnError(string id) => AssertError(Side(d => d.Id = id), "reserved pack id");

    [Fact]
    public void AnIdThatIsNotKeySafe_IsAnError() => AssertError(Side(d => d.Id = "my:pack"), "may only use letters");

    [Fact]
    public void ATutorialMarkedWorld_IsAnError()
        => AssertError(Side(d => { d.Kind = StoryRegistry.KindTutorial; d.Scope = StoryRegistry.ScopeWorld; }), "always player-scoped");

    [Fact]
    public void APlayerPackWithBeats_IsAnError()
        => AssertError(Side(d => d.Beats.Add(new StoryBeat { Index = 0, TextKey = "story.vega.beat00" })), "never be the world story");

    [Fact]
    public void AWorldPackWithoutBeats_IsAnError()
        => AssertError(Side(d => { d.Scope = StoryRegistry.ScopeWorld; d.Kind = StoryRegistry.KindStory; }), "needs beats");

    [Theory]
    [InlineData("step:probe")]
    [InlineData("ui:menu_open")]
    [InlineData("world:no_such_predicate")]
    [InlineData("rule:hunger=off")]
    public void AConditionThatDoesNotParse_IsAnError(string condition)
        => AssertError(Side(d => Step(d).Requires.Add(condition)), "Story condition");

    [Theory]
    [InlineData("step:probe:nope", "has no step")]
    [InlineData("chapter:probe:nope", "has no chapter")]
    [InlineData("pack:elsewhere:started", "is not installed")]
    [InlineData("blueprint:no_such_blueprint", "does not exist")]
    public void AConditionThatNamesNothing_IsAnError(string condition, string fragment)
        => AssertError(Side(d => d.Chapters[0].OfferWhen.Add(condition)), fragment);

    [Fact]
    public void AGateThatDoesNotParse_IsAnError()
        => AssertError(Side(d => { d.Kind = StoryRegistry.KindTutorial; d.Gates[StoryGateNames.Banter] = "ui:menu_open"; }), "never a condition");

    [Fact]
    public void AUiEvent_InAWorldPacksStep_IsAnError()
        => AssertError(Side(d =>
        {
            d.Scope = StoryRegistry.ScopeWorld;
            d.Kind = StoryRegistry.KindStory;
            d.Beats.Add(new StoryBeat { Index = 0, TextKey = "story.vega.beat00" });
            Step(d).Objective.Event = "ui:menu_open";
        }), "only in a player pack's step objectives");

    [Fact]
    public void AUiEvent_InAPlayerPacksStep_IsFine()
    {
        var issues = StoryPackValidator.Validate(Side(d => Step(d).Objective.Event = "ui:menu_open"), Context());
        Assert.Empty(Errors(issues));
    }

    [Fact]
    public void AnUnpublishedEvent_IsAWarning()
    {
        var issues = StoryPackValidator.Validate(Side(d => Step(d).Objective.Event = "equip:any"), Context());
        Assert.Empty(Errors(issues));
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Warning && i.Message.Contains("not raised", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissionBridge_ToAMissingMission_IsAnError()
        => AssertError(Side(d => Step(d).Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Mission, Target = "no_such_mission" }), "does not exist in data/missions.json");

    [Fact]
    public void AMissionBridge_ToAnExistingMission_IsFine()
        => Assert.Empty(Errors(StoryPackValidator.Validate(Side(d => Step(d).Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Mission, Target = "crystal_workshop_1" }), Context())));

    [Fact]
    public void ALegacyKey_OutsideATutorial_IsAnError() => AssertError(Side(d => Step(d).LegacyKey = "vega:stage:mine"), "only a tutorial pack");

    [Fact]
    public void AnUnknownLegacyStage_IsAnError()
        => AssertError(Side(d => { d.Kind = StoryRegistry.KindTutorial; Step(d).LegacyKey = "vega:stage:swim"; }), "not one of the legacy stages");

    [Fact]
    public void AnAlternative_FromNowhere_IsAnError() => AssertError(Side(d => Step(d).Alternative = "c9.9"), "is not another step");

    [Theory]
    [InlineData("nearest_whatever", "unknown marker provider")]
    [InlineData("nearest_ore:no_such_block", "does not exist")]
    public void ABadMarkerProvider_IsAnError(string reveal, string fragment) => AssertError(Side(d => Step(d).OnStart.Reveal = reveal), fragment);

    [Fact]
    public void ARepeatedStepId_IsAnError()
        => AssertError(Side(d => d.Chapters[0].Steps.Add(new StoryStep { Id = "c1.1", Objective = new StoryStepObjective { Event = GameEventKeys.CraftAny } })), "used twice");

    [Fact]
    public void ADoStepWithoutAnEvent_IsAnError() => AssertError(Side(d => Step(d).Objective.Event = string.Empty), "needs an event");

    [Fact]
    public void ATutorial_PaysNoRewards()
    {
        var def = Side(d => d.Kind = StoryRegistry.KindTutorial);
        string raw = "{\"id\":\"probe\",\"kind\":\"tutorial\",\"chapters\":[{\"id\":\"c1\",\"steps\":[{\"id\":\"c1.1\",\"rewards\":[{\"item\":\"iron_plate\",\"count\":5}]}]}]}";
        AssertError(def, "pays no rewards", raw);
        Assert.Empty(Errors(StoryPackValidator.Validate(def, Context(), "{\"id\":\"probe\",\"kind\":\"tutorial\",\"rewards\":[]}")));
    }

    [Fact]
    public void AKeyMissingInGerman_IsAnError()
    {
        var context = Context();
        var english = context.English!;
        context.German = k => k == "vega.obj.mine" ? null : english(k);
        AssertError(Side(), "missing in German", context: context);
    }

    [Fact]
    public void AKeyMissingInEnglish_IsAnError() => AssertError(Side(d => Step(d).Explain.StartKey = "story.probe.nowhere"), "missing in English");

    [Fact]
    public void AShortNameOverTenCharacters_IsAnError()
        => AssertError(Side(d => d.ShortNameKey = "story.vega_protocol.name"), "budget 10"); // "The VEGA Protocol"

    [Fact]
    public void AChipOverBudget_IsAnError()
        => AssertError(Side(d => Step(d).Explain.ChipKey = "vega.done"), "budget 45");

    [Fact]
    public void ABasicTrainingLineOverBudget_IsAnError()
        => AssertError(Side(d => { d.Kind = StoryRegistry.KindTutorial; Step(d).Explain.StartKey = "vega.done"; }), "budget 120");

    [Fact]
    public void ADeepDiveLine_HasTheLargerBudget()
    {
        var def = Side(d => { d.Kind = StoryRegistry.KindTutorial; d.Chapters[0].Optional = true; Step(d).Explain.StartKey = "vega.s.mine.start"; });
        Assert.DoesNotContain(Errors(StoryPackValidator.Validate(def, Context())), e => e.Message.Contains("budget", StringComparison.Ordinal));
    }

    [Fact]
    public void CutsceneBindings_AreCheckedOnceCutscenesExist()
    {
        var def = Side(d => { d.IntroCutscene = "welcome"; d.Chapters[0].OpenCutscene = "open"; });
        Assert.Empty(Errors(StoryPackValidator.Validate(def, Context()))); // no cutscene model yet: skipped

        var context = Context();
        context.CutsceneExists = (_, id) => id == "welcome";
        var errors = Errors(StoryPackValidator.Validate(def, context));
        Assert.Single(errors);
        Assert.Contains("'open'", errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShownLength_CountsATokenAsThree_AndASlotAsTen()
    {
        Assert.Equal(5, StoryPackValidator.ShownLength("Hello"));
        Assert.Equal("Press ".Length + 3, StoryPackValidator.ShownLength("Press {key:Interact}"));
        Assert.Equal(10 + " lies here".Length, StoryPackValidator.ShownLength("{0} lies here"));
    }
}
