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
using BlocksBeyondTheStars.Shared.Definitions;
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
/// missing English/German keys, an over-long short name and the text budget, cutscene bindings that must resolve (#2467;
/// the cutscene definitions' own rules are in <see cref="CutsceneContentTests"/>).
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

    internal static StoryPackValidationContext Context(IReadOnlyDictionary<string, StoryDefinition>? packs = null, params string[] extraPackDirs)
    {
        var content = Shipped.Value;
        var en = Table("en", extraPackDirs);
        var de = Table("de", extraPackDirs);
        var installed = packs ?? content.Stories;
        string music = Path.Combine(TestPaths.RepoRoot(), "client", "Music");
        string audio = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        return new StoryPackValidationContext
        {
            Packs = installed,
            MissionExists = id => content.Missions.ContainsKey(id),
            BlueprintExists = key => content.GetBlueprint(key) is not null,
            BlockExists = key => content.GetBlock(key) is not null,
            ModuleExists = key => content.GetShipModule(key) is not null, // #2475: module: leads
            LoreExists = key => installed.Values.Any(p => p.LoreSites.Any(l => l.Key == key)), // #2476: lore: conditions
            English = k => en.TryGetValue(k, out var v) ? v : null,
            German = k => de.TryGetValue(k, out var v) ? v : null,
            // #2467: the installed packs' cutscenes (a pack validated from its story.json alone finds its own here), and
            // the client's streamed music and sound clips.
            CutsceneExists = (pack, id) => installed.TryGetValue(pack, out var def) && def.FindCutscene(id) is not null,
            MusicExists = track => File.Exists(Path.Combine(music, track + ".mp3")),
            SoundExists = cue => Directory.GetFiles(audio, cue + ".*").Any(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)),
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
        def.Cutscenes.AddRange(ContentLoader.LoadPackCutscenes(Path.Combine(StoriesDir, folder))); // #2467: as the loader does
        var issues = StoryPackValidator.Validate(def, Context(), raw);
        Assert.True(Errors(issues).Count == 0, string.Join("\n", Errors(issues)));
    }

    [Theory]
    [MemberData(nameof(DataPacks))]
    public void EveryDataPack_UsesOnlyKnownFields(string folder)
    {
        // A misspelt field ("requries") would silently fall back to its default — the strict read names it.
        string raw = File.ReadAllText(Path.Combine(StoriesDir, folder, "story.json"));
        var ex = Record.Exception(() => JsonSerializer.Deserialize<StoryDefinition>(raw, Strict));
        Assert.Null(ex);

        // #2467: the same for the pack's cutscenes.json.
        string cutscenes = Path.Combine(StoriesDir, folder, "cutscenes.json");
        if (File.Exists(cutscenes))
        {
            Assert.Null(Record.Exception(() => JsonSerializer.Deserialize<StoryCutsceneFile>(File.ReadAllText(cutscenes), Strict)));
        }
    }

    /// <summary>The content loader's options, refusing unknown fields.</summary>
    internal static readonly JsonSerializerOptions Strict = new(ContentLoader.JsonOptions) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    [Fact]
    public void PackIds_AreUnique_AcrossTheDataFolders()
    {
        var ids = PackDirs().Select(d => JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(Path.Combine(d, "story.json")), ContentLoader.JsonOptions)!.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(StoryPackFixtures.Runtime)]
    [InlineData(StoryPackFixtures.Tutorial)]
    [InlineData(StoryPackFixtures.Cutscenes)]
    [InlineData(StoryPackFixtures.SidePilot)] // #2483: the pilot side story — no warning either
    public void TheTestPacks_PassTheValidator_AndUseOnlyKnownFields(string id)
    {
        string raw = StoryPackFixtures.RawJson(id);
        var def = JsonSerializer.Deserialize<StoryDefinition>(raw, Strict)!;
        string cutscenes = Path.Combine(StoryPackFixtures.Dir(id), "cutscenes.json");
        if (File.Exists(cutscenes))
        {
            def.Cutscenes.AddRange(JsonSerializer.Deserialize<StoryCutsceneFile>(File.ReadAllText(cutscenes), Strict)!.Cutscenes);
        }

        var packs = new Dictionary<string, StoryDefinition>(Shipped.Value.Stories, StringComparer.OrdinalIgnoreCase)
        {
            [StoryPackFixtures.Runtime] = StoryPackFixtures.Load(StoryPackFixtures.Runtime),
            [StoryPackFixtures.Tutorial] = StoryPackFixtures.Load(StoryPackFixtures.Tutorial),
            [StoryPackFixtures.Cutscenes] = StoryPackFixtures.Load(StoryPackFixtures.Cutscenes),
            [StoryPackFixtures.SidePilot] = StoryPackFixtures.Load(StoryPackFixtures.SidePilot),
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
    public void AUiFlag_NoClientReports_IsAWarning()
    {
        // #2469: the client reports only GameEventKeys.UiEvents — a step waiting for another flag would wait forever.
        var issues = StoryPackValidator.Validate(Side(d => Step(d).Objective.Event = "ui:inventory_sorted"), Context());
        Assert.Empty(Errors(issues));
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Warning && i.Message.Contains("no flag a client reports", StringComparison.Ordinal));
        Assert.DoesNotContain(StoryPackValidator.Validate(Side(d => Step(d).Objective.Event = GameEventKeys.UiCodexOpen), Context()),
            i => i.Message.Contains("no flag a client reports", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGraduation_NeedsItsLine_ACounter_AndAtMostThreeOptionalChapters()
    {
        // #2469: a fine graduation — a line in both languages, a counter, an optional chapter to suggest.
        StoryDefinition Graduating(Action<StoryGraduation> edit) => Side(d =>
        {
            d.Chapters.Add(new StoryChapter
            {
                Id = "c2",
                TitleKey = "story.vega_protocol.name",
                Optional = true,
                Steps = { new StoryStep { Id = "c2.1", Objective = new StoryStepObjective { Event = GameEventKeys.CraftAny }, Explain = new StoryStepExplain { ChipKey = "vega.obj.craft" } } },
            });
            d.Graduation = new StoryGraduation { LineKey = "vega.skip", Counter = "probe:graduated", Suggest = { "c2" } };
            edit(d.Graduation);
        });

        Assert.Empty(Errors(StoryPackValidator.Validate(Graduating(_ => { }), Context())));
        AssertError(Graduating(g => g.LineKey = "story.probe.nowhere"), "missing in English");
        AssertError(Graduating(g => g.Counter = "graduated"), "is not an achievement counter");
        AssertError(Graduating(g => g.Suggest.Add("c1")), "is not an optional chapter");
        AssertError(Graduating(g => g.Suggest.AddRange(new[] { "c2", "c2", "c2" })), "at most 3 suggested chapters");
    }

    [Fact]
    public void AnEmptyHintRef_IsAnError() => AssertError(Side(d => Step(d).HintRefs.Add(" ")), "an empty hint id");

    [Fact]
    public void TheFoodPlantMarker_IsAKnownProvider()
        => Assert.Empty(Errors(StoryPackValidator.Validate(Side(d => Step(d).OnStart.Reveal = StoryRevealProviders.NearestFoodPlant), Context())));

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
    public void ATutorialsRewardField_HoldingZero_IsStillARewardField()
    {
        // #2927: a number is never an empty value — only null, false, "", [] and {} are.
        var def = Side(d => d.Kind = StoryRegistry.KindTutorial);
        string raw = "{\"id\":\"probe\",\"kind\":\"tutorial\",\"chapters\":[{\"id\":\"c1\",\"steps\":[{\"id\":\"c1.1\",\"knowledgeReward\":0}]}]}";
        var errors = Errors(StoryPackValidator.Validate(def, Context(), raw));
        Assert.Contains(errors, e => e.Path == "$.chapters[0].steps[0].knowledgeReward" && e.Message == "a tutorial pays no rewards");
    }

    [Fact]
    public void APlayerPacksLead_WithABadType_IsStillToldLeadsAreForWorldPacksOnly()
    {
        // #2927: the type check came first and skipped the warning — a player pack ignores its leads whatever their type.
        string[] lead = Report(Side(d => d.Chapters[0].Leads.Add(new StoryLead { Type = "teleport" })))
            .Where(line => line.Contains(": chapters[0].leads[0]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, lead.Length);
        Assert.Equal("Warning: chapters[0].leads[0]: leads are read for world packs only", lead[0]);
        Assert.StartsWith("Error: chapters[0].leads[0].type: unknown lead type 'teleport'", lead[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ATutorialMissionBridge_LeavesTheBridgedMissionItsRewards()
    {
        // #2481: the rule covers the pack's own document. The Crystal workshop course (C8) bridges onto VEGA's radio jobs, which
        // keep paying their items and knowledge — the course pays nothing itself.
        var job = Shipped.Value.Missions["crystal_workshop_1"];
        Assert.NotEmpty(job.Rewards);
        Assert.True(job.KnowledgeReward > 0);
        var def = Side(d =>
        {
            d.Kind = StoryRegistry.KindTutorial;
            Step(d).Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Mission, Target = job.Id };
        });
        string raw = "{\"id\":\"probe\",\"kind\":\"tutorial\",\"chapters\":[{\"id\":\"c1\",\"steps\":[{\"id\":\"c1.1\",\"objective\":{\"type\":\"Mission\",\"target\":\"crystal_workshop_1\"}}]}]}";
        Assert.DoesNotContain(Errors(StoryPackValidator.Validate(def, Context(), raw)), e => e.Message.Contains("rewards", StringComparison.Ordinal));

        // The shipped tutorial's bridges: every one names a job that pays, and the pack itself still passes.
        var tutorial = Shipped.Value.Stories["tutorial"];
        var bridged = tutorial.Chapters.SelectMany(c => c.Steps).Where(s => s.Objective.IsMission).Select(s => s.Objective.Target).ToList();
        Assert.Equal(new[] { "crystal_workshop_1", "crystal_workshop_2", "crystal_workshop_3", "crystal_workshop_4", "crystal_workshop_5", "crystal_workshop_6" }, bridged);
        Assert.All(bridged, id => Assert.True(Shipped.Value.Missions[id].Rewards.Count > 0 && Shipped.Value.Missions[id].KnowledgeReward > 0, id));
    }

    [Fact]
    public void VisibleIf_ParsesItsConditions_AndOnlyAnOptionalChapterMayBeLeftOut()
    {
        // #2482: a course the world's rules rule out is left out — an optional chapter; a mandatory one would block the pack.
        var optional = Side(d =>
        {
            d.Chapters[0].Optional = true;
            d.Chapters[0].VisibleIf.Add("world:multiplayer");
        });
        Assert.Empty(Errors(StoryPackValidator.Validate(optional, Context())));

        var mandatory = Side(d => d.Chapters[0].VisibleIf.Add("world:creatures"));
        Assert.Contains(Errors(StoryPackValidator.Validate(mandatory, Context())), e => e.Path.EndsWith(".visibleIf", StringComparison.Ordinal));

        var unknown = Side(d =>
        {
            d.Chapters[0].Optional = true;
            d.Chapters[0].VisibleIf.Add("world:no_such_rule");
        });
        Assert.Contains(Errors(StoryPackValidator.Validate(unknown, Context())), e => e.Path.Contains(".visibleIf", StringComparison.Ordinal));
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
    public void CutsceneBindings_MustResolve()
    {
        // #2467: the binding check is on — a binding names a cutscene of the pack (its own cutscenes.json, or what the
        // context knows of it when the pack is validated from story.json alone).
        var def = Side(d => { d.IntroCutscene = "welcome"; d.Chapters[0].OpenCutscene = "open"; });
        var errors = Errors(StoryPackValidator.Validate(def, Context()));
        Assert.Equal(2, errors.Count(e => e.Message.Contains("is not defined in the pack's cutscenes.json", StringComparison.Ordinal)));

        def.Cutscenes.Add(new CutsceneDefinition
        {
            Id = "welcome",
            Shots = { new CutsceneShot { Duration = 4 } },
        });
        errors = Errors(StoryPackValidator.Validate(def, Context()));
        Assert.Single(errors);
        Assert.Contains("'open'", errors[0].Message, StringComparison.Ordinal);

        var context = Context();
        context.CutsceneExists = (_, id) => id == "open";
        Assert.Empty(Errors(StoryPackValidator.Validate(def, context)));
    }

    [Theory]
    [InlineData("__recap", "built in")]
    [InlineData("strip@b3", "is not defined")]
    public void ABindingToTheRecapOrToAnInstance_IsAnError(string binding, string fragment)
        => AssertError(Side(d => Step(d).OnComplete.Cutscene = binding), fragment);

    [Fact]
    public void ShownLength_CountsATokenAsThree_AndASlotAsTen()
    {
        Assert.Equal(5, StoryPackValidator.ShownLength("Hello"));
        Assert.Equal("Press ".Length + 3, StoryPackValidator.ShownLength("Press {key:Interact}"));
        Assert.Equal(10 + " lies here".Length, StoryPackValidator.ShownLength("{0} lies here"));
    }

    // ---------------- The issue order is stable (B8 refactor guard) ----------------
    //
    // The validator is one orchestrator calling one section method per rule group; these three broken packs touch every
    // section, so they pin which issues come out and in which order (the "missing in English/German" and budget errors are
    // collected along the way and only reported at the end). The expected lines embed a little live content on purpose:
    // the legacy stage list, the finale stages, the marker providers, the gate parser's message and the shown length of
    // "The VEGA Protocol" — a change there updates these lines in its own commit, never together with validator code.

    private static string[] Report(StoryDefinition def, string? raw = null)
        => StoryPackValidator.Validate(def, Context(), raw).Select(i => i.ToString()).ToArray();

    [Fact]
    public void ABrokenPlayerPack_ReportsItsIssuesInDocumentOrder()
    {
        var def = Side(d =>
        {
            d.Beats.Add(new StoryBeat { Index = 1, TextKey = string.Empty });
            d.LeadBonusMilestones = 40;
            d.Gates[StoryGateNames.Banter] = "ui:menu_open";
            d.Gates.HoldHints["look around"] = "step:probe:c1.1";
            d.Speakers.Add(new StoryNarrator { Id = "core", NameKey = "ui.finale.duel_title", Color = "red" });
            d.ShortNameKey = "story.vega_protocol.name";
            d.IntroCutscene = "welcome";
            d.Chapters[0].OpenCutscene = "open";
            d.Chapters[0].VisibleIf.Add("world:creatures");
            d.Chapters[0].Leads.Add(new StoryLead { Type = "research:no_such_blueprint" });
            Step(d).Requires.Add("blueprint:no_such_blueprint");
            Step(d).LegacyKey = "vega:stage:mine";
            Step(d).Alternative = "c9.9";
            Step(d).OnStart.Reveal = "nearest_whatever";
            Step(d).HintRefs.Add(" ");
            Step(d).Explain.StartKey = "story.probe.nowhere";
            d.Graduation = new StoryGraduation { Counter = "graduated", Suggest = { "c1" } };
            d.Cutscenes.Add(new CutsceneDefinition { Id = "welcome", Tier = 9 });
        });
        string[] expected =
        {
            "Error: beats: a player pack carries no beats — it can never be the world story",
            "Error: beats[0]: index 1 is out of order (expected 0)",
            "Error: beats[0]: the beat has no textKey",
            "Error: leadBonusMilestones: 40 is out of range (0–10 milestones per chapter)",
            "Warning: leadBonusMilestones: leadBonusMilestones, normaliseUnavailableDrivers and memoryTriggers are read for world packs only",
            "Error: gates.banter: Story condition \"ui:menu_open\": 'ui:' events are client claims and never a condition (at position 0).",
            "Warning: gates.banter: only a tutorial pack's gates are read",
            "Error: gates.holdHints.look around: a hint id (the <id> of vega:hint:<id>) without spaces",
            "Warning: gates.holdHints.look around: only a tutorial pack holds hints back",
            "Error: speakers[0].color: 'red' is not a #RRGGBB colour",
            "Error: chapters[0].visibleIf: only an optional chapter can be left out (#2482) — a mandatory one would block the pack",
            "Error: chapters[0].openCutscene: cutscene 'open' is not defined in the pack's cutscenes.json",
            "Warning: chapters[0].leads[0]: leads are read for world packs only",
            "Error: chapters[0].leads[0].type: blueprint 'no_such_blueprint' does not exist",
            "Error: chapters[0].steps[0].legacyKey: only a tutorial pack may name legacy keys",
            "Error: chapters[0].steps[0].requires[0]: blueprint 'no_such_blueprint' does not exist",
            "Error: chapters[0].steps[0].alternative: 'c9.9' is not another step of chapter 'c1'",
            "Error: chapters[0].steps[0].onStart.reveal: unknown marker provider 'nearest_whatever' (nearest_ore:<block>, nearest_settlement, nearest_food_plant, nearest_station, unique_site:<site>)",
            "Error: chapters[0].steps[0].hintRefs[0]: an empty hint id",
            "Error: graduation.counter: 'graduated' is not an achievement counter ('<verb>:<thing>')",
            "Error: graduation.suggest[0]: 'c1' is not an optional chapter of this pack",
            "Error: cutscenes[0].tier: unknown tier 9 (1 banner, 2 card, 3 scene, 4 set piece)",
            "Error: cutscenes[0].shots: a cutscene needs at least one shot",
            "Error: shortNameKey: 'story.vega_protocol.name' is 17 characters in English (budget 10)",
            "Error: shortNameKey: 'story.vega_protocol.name' is 18 characters in German (budget 10)",
            "Error: chapters[0].steps[0].explain.startKey: key 'story.probe.nowhere' is missing in English",
            "Error: chapters[0].steps[0].explain.startKey: key 'story.probe.nowhere' is missing in German",
        };
        Assert.Equal(expected, Report(def));
    }

    [Fact]
    public void ABrokenWorldPack_ReportsItsIssuesInDocumentOrder()
    {
        var def = Side(d =>
        {
            d.Kind = StoryRegistry.KindStory;
            d.Scope = StoryRegistry.ScopeWorld;
            d.Beats.Add(new StoryBeat { Index = 0, TextKey = "story.vega.beat00" });
            d.LeadBonusMilestones = 40;
            d.Chapters[0].FromBeat = 5;
            d.Chapters[0].Leads.Add(null!);
            d.Chapters[0].Missions.Add(new StoryMission { Id = "c1", TitleKey = string.Empty });
            Step(d).Objective.Event = "ui:menu_open";
            d.Finale.Cutscenes["epilogue"] = "scene";
            d.Graduation = new StoryGraduation { Counter = "probe:graduated" };
        });
        string[] expected =
        {
            "Error: leadBonusMilestones: 40 is out of range (0–10 milestones per chapter)",
            "Error: finale.cutscenes.epilogue: unknown finale stage 'epilogue' (arrival, descent, breach, duel, standdown, resolution)",
            "Error: finale.cutscenes.epilogue: cutscene 'scene' is not defined in the pack's cutscenes.json",
            "Error: chapters[0].fromBeat: beat 5 does not exist (0–0, or -1 for none)",
            "Error: chapters[0].leads[0]: an empty lead",
            "Error: chapters[0].steps[0].objective.event: 'ui:' teaching flags exist only in a player pack's step objectives",
            "Error: chapters[0].missions[0]: mission id 'c1' is empty, not key-safe, used twice or a chapter's id",
            "Error: chapters[0].missions[0]: the mission has no titleKey",
            "Error: chapters[0].missions[0].steps: a mission needs at least one step",
            "Warning: graduation: only a player pack graduates anybody",
        };
        Assert.Equal(expected, Report(def));
    }

    [Fact]
    public void ABrokenTutorial_ReportsItsIssuesInDocumentOrder()
    {
        var def = Side(d =>
        {
            d.Kind = StoryRegistry.KindTutorial;
            d.Beats.Add(new StoryBeat { Index = 0, TextKey = "story.vega.beat00", KnowledgeReward = 5 });
            d.Gates[StoryGateNames.Banter] = "ui:menu_open";
            d.Dialogs.Add(new DialogDefinition
            {
                Nodes =
                {
                    new DialogNode
                    {
                        PromptKey = "vega.obj.mine",
                        Choices = { new DialogChoice { TextKey = "vega.obj.mine", ResponseKey = "vega.obj.mine", Consequence = "gift:iron_plate" } },
                    },
                },
            });
            Step(d).LegacyKey = "vega:stage:swim";
            Step(d).Explain.StartKey = "story.probe.nowhere";
        });
        string raw = "{\"id\":\"probe\",\"kind\":\"tutorial\",\"chapters\":[{\"id\":\"c1\",\"steps\":[{\"id\":\"c1.1\",\"rewards\":[{\"item\":\"iron_plate\",\"count\":5}]}]}],"
            + "\"dialogs\":[{\"nodes\":[{\"choices\":[{\"consequence\":\"gift:iron_plate\"}]}]}]}";
        string[] expected =
        {
            "Error: beats: a player pack carries no beats — it can never be the world story",
            "Error: beats[0]: a tutorial pays no rewards (knowledgeReward)",
            "Error: gates.banter: Story condition \"ui:menu_open\": 'ui:' events are client claims and never a condition (at position 0).",
            "Warning: shortNameKey: a player pack without a short name shows \"Objective:\" on the chip",
            "Error: dialogs[0].nodes[0].choices[0]: a tutorial pays no rewards (gift consequence)",
            "Error: chapters[0].steps[0].legacyKey: 'vega:stage:swim' is not one of the legacy stages (vega:stage:mine|craft|eat|scan|unlock|launch|dock|trade|land)",
            "Error: chapters[0].steps[0].explain.startKey: key 'story.probe.nowhere' is missing in English",
            "Error: chapters[0].steps[0].explain.startKey: key 'story.probe.nowhere' is missing in German",
            "Error: $.chapters[0].steps[0].rewards: a tutorial pays no rewards",
            "Error: $.dialogs[0].nodes[0].choices[0].consequence: a tutorial pays no rewards (gift consequence)",
        };
        Assert.Equal(expected, Report(def, raw));
    }
}
