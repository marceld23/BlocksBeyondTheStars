// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2467 — step 8 of the story packs: the cutscene data model (a pack's <c>cutscenes.json</c>), its loader and defaults, the
/// validator's cutscene rules (tiers, cameras, anchors, caption styles, grades, budgets, chains, chapters, sounds), the
/// official packs' id vocabulary (synthesis §5.3), the ledger key helpers and the two new wire messages (tags 295/296).
/// The server ledger itself is <see cref="CutsceneLedgerTests"/>.
/// </summary>
public sealed class CutsceneContentTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    // ---------------- Loader and defaults ----------------

    [Fact]
    public void TheVegaPrologue_LoadsFromTheCutscenesJson()
    {
        var vega = Shipped.Value.Stories[StoryRegistry.DefaultStoryId];
        var prologue = vega.FindCutscene(StoryCutsceneIds.Prologue);
        Assert.NotNull(prologue);
        Assert.Equal(CutsceneTiers.Scene, prologue!.Tier);
        Assert.Equal("music_mystery_signal", prologue.Music);
        // #2470: B0 and the call to adventure are VEGA's pages of the prologue now (the scene shows them itself).
        Assert.Equal(new[] { "vega.prologue.1", "vega.prologue.2", "vega.prologue.3", "story.vega.beat00", "story.vega.call" }, prologue.ReplaceLineKeys);
        Assert.Equal(new[] { CutsceneCameras.PlayerView, CutsceneCameras.Orbit, CutsceneCameras.Static, CutsceneCameras.PlayerView, CutsceneCameras.PlayerView, CutsceneCameras.PlayerView },
            prologue.Shots.Select(s => s.Camera.Type));
        var title = Assert.Single(prologue.Shots[0].Captions);
        Assert.Equal(CutsceneCaptionStyles.TitleCard, title.Style);
        Assert.Equal("ui.story.prologue", title.KickerKey);
        Assert.Equal("story.vega.ch.awakening", title.Key);
        Assert.Equal("vega.prologue.3", Assert.Single(prologue.Shots[3].Captions).Key);
        Assert.Equal(CutsceneCaptionStyles.Speaker, Assert.Single(prologue.Shots[4].Captions).Style);
        Assert.Equal("story.vega.call", Assert.Single(prologue.Shots[5].Captions).Key);
        Assert.True(prologue.TakesCamera);
        Assert.True(prologue.EffectiveHoldWorld);
        Assert.True(prologue.EffectiveLockInput);
        Assert.True(prologue.EffectiveHideHud);
        Assert.True(prologue.Options.Skippable);
    }

    [Fact]
    public void TheVegaPrologue_ReadsInEnglishAndGerman()
    {
        var prologue = Shipped.Value.Stories[StoryRegistry.DefaultStoryId].FindCutscene(StoryCutsceneIds.Prologue)!;
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = Shipped.Value.CreateLocalizer(locale);
            foreach (var caption in prologue.Shots.SelectMany(s => s.Captions))
            {
                Assert.True(loc.Has(caption.Key), $"{caption.Key} missing in {locale}");
                Assert.True(caption.KickerKey.Length == 0 || loc.Has(caption.KickerKey), $"{caption.KickerKey} missing in {locale}");
            }
        }

        Assert.Equal("Prologue", Shipped.Value.CreateLocalizer(GameLocale.English).Get("ui.story.prologue"));
        Assert.Equal("Prolog", Shipped.Value.CreateLocalizer(GameLocale.German).Get("ui.story.prologue"));
    }

    [Fact]
    public void TheFixtureCutscenes_LoadWithTheirDefaults()
    {
        var pack = StoryPackFixtures.Load(StoryPackFixtures.Cutscenes);
        Assert.Equal(7, pack.Cutscenes.Count);

        var welcome = pack.FindCutscene("welcome")!;
        Assert.Equal(CutsceneTiers.Card, welcome.Tier);
        Assert.False(welcome.Template);
        Assert.Equal(new[] { "strip" }, welcome.Options.ChainNext);
        Assert.True(welcome.Options.Skippable);
        Assert.True(welcome.Options.Replayable);
        Assert.False(welcome.Options.CooldownExempt);
        Assert.Null(welcome.Options.HoldWorld);
        Assert.False(welcome.EffectiveHoldWorld); // a card never holds
        Assert.False(welcome.EffectiveLockInput);
        Assert.False(welcome.EffectiveHideHud);   // a card only dims the HUD
        Assert.Equal(CutsceneGrades.None, welcome.Shots[0].Grade);

        Assert.True(pack.FindCutscene("strip")!.Template);
        Assert.Same(pack.FindCutscene("strip"), pack.FindCutscene("strip@two")); // an instance finds its template
        Assert.Equal(CutsceneTiers.Banner, pack.FindCutscene("done")!.Tier);

        var big = pack.FindCutscene("big_moment")!;
        Assert.True(big.EffectiveHoldWorld);
        Assert.True(big.EffectiveLockInput); // derived: a T3 that takes the camera
        Assert.True(big.EffectiveHideHud);
        Assert.Equal(1.5f, big.Options.MinDisplay);
        Assert.Equal(CutsceneGrades.Memory, big.Shots[0].Grade);
        Assert.Equal("ai_blip", Assert.Single(big.Shots[0].Sfx).Cue);
        Assert.Equal("vega", big.Shots[0].Captions[0].Speaker);

        Assert.True(pack.FindCutscene("now")!.Options.CooldownExempt);
        Assert.False(pack.FindCutscene("anchored")!.Options.Replayable);
        Assert.Equal(CutsceneAnchors.Custom, pack.FindCutscene("anchored")!.Shots[0].Camera.Anchor);
        Assert.Null(pack.FindCutscene("nowhere"));
        Assert.Null(pack.FindCutscene(null));
    }

    [Fact]
    public void ACaption_DefaultsToASubtitle_AndAShot_ToThePlayerView()
    {
        var parsed = ContentLoader.ParseCutscenes("[{\"id\":\"x\",\"shots\":[{\"duration\":2,\"captions\":[{\"key\":\"k\"}]}]}]");
        var shot = Assert.Single(Assert.Single(parsed).Shots);
        Assert.Equal(CutsceneCameras.PlayerView, shot.Camera.Type);
        Assert.Equal(string.Empty, shot.Camera.Anchor);
        Assert.Equal(CutsceneCaptionStyles.Subtitle, shot.Captions[0].Style);
        Assert.Equal(CutsceneTiers.Card, parsed[0].Tier);
    }

    [Fact]
    public void TheCutscenesJson_IsReadInTheObjectAndInTheArrayForm()
    {
        const string Scene = "{\"id\":\"x\",\"tier\":1,\"shots\":[{\"duration\":2}]}";
        Assert.Equal("x", Assert.Single(ContentLoader.ParseCutscenes("{ // comment\n \"cutscenes\": [" + Scene + "], }")).Id);
        Assert.Equal("x", Assert.Single(ContentLoader.ParseCutscenes("[" + Scene + "]")).Id);
        Assert.Empty(ContentLoader.ParseCutscenes("{}"));
        Assert.Empty(ContentLoader.LoadPackCutscenes(StoryPackFixtures.Dir(StoryPackFixtures.Runtime))); // no file, no scenes
    }

    // ---------------- The official packs' ids (synthesis §5.3) ----------------

    [Fact]
    public void TheOfficialPacks_UseOnlyTheBindingIds()
    {
        foreach (var pack in Shipped.Value.Stories.Values)
        {
            Assert.DoesNotContain(pack.Cutscenes, c => StoryCutsceneIds.IsBuiltIn(c.Id));
            IReadOnlyList<string>? allowed = pack.Id switch
            {
                StoryRegistry.DefaultStoryId => StoryCutsceneIds.Vega,
                "tutorial" => StoryCutsceneIds.Tutorial,
                _ => null,
            };
            if (allowed is not null)
            {
                var stray = pack.Cutscenes.Select(c => c.Id).Where(id => !allowed.Contains(id)).ToList();
                Assert.True(stray.Count == 0, $"{pack.Id} defines cutscene ids outside synthesis §5.3: {string.Join(", ", stray)}");
            }
        }

        Assert.Contains(StoryCutsceneIds.Prologue, StoryCutsceneIds.Vega);
        Assert.Contains("ch7.open", StoryCutsceneIds.Vega);
        Assert.Contains("tut.welcome", StoryCutsceneIds.Tutorial);
        Assert.DoesNotContain("tut.open", StoryCutsceneIds.Tutorial); // dropped by §5.3
    }

    [Fact]
    public void TheBuiltInVegaFallback_CarriesNoCutscenes()
        => Assert.Empty(StoryRegistry.Default.Cutscenes); // the narrator lines play; only the data pack stages them

    // ---------------- Validator rules (one defect each) ----------------

    private static StoryDefinition Probe(Action<CutsceneDefinition>? edit = null, Action<StoryDefinition>? editPack = null)
    {
        var cut = new CutsceneDefinition
        {
            Id = "scene",
            Tier = CutsceneTiers.Scene,
            Chapter = "c1",
            Music = "music_mystery_signal",
            Shots =
            {
                new CutsceneShot
                {
                    Camera = new CutsceneCamera { Type = CutsceneCameras.Orbit, Anchor = CutsceneAnchors.Ship },
                    Duration = 8,
                    Grade = CutsceneGrades.Flashback,
                    Captions = { new CutsceneCaption { Key = "vega.prologue.1", Style = CutsceneCaptionStyles.Subtitle, At = 0.5f, Hold = 6 } },
                    Sfx = { new CutsceneSfx { Cue = "ai_blip", At = 0.2f } },
                },
            },
            Options = new CutsceneOptions { HoldWorld = true },
        };
        edit?.Invoke(cut);
        var def = new StoryDefinition
        {
            Id = "probe",
            Kind = StoryRegistry.KindSide,
            Scope = StoryRegistry.ScopePlayer,
            NameKey = "story.vega_protocol.name",
            ShortNameKey = "ui.story.prologue",
            Chapters =
            {
                new StoryChapter
                {
                    Id = "c1",
                    TitleKey = "story.vega_protocol.name",
                    OpenCutscene = "scene",
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
            Cutscenes = { cut },
        };
        editPack?.Invoke(def);
        return def;
    }

    private static List<StoryPackIssue> Issues(StoryDefinition def, StoryPackValidationContext? context = null)
        => StoryPackValidator.Validate(def, context ?? StoryPackValidationTests.Context());

    private static void AssertError(StoryDefinition def, string fragment, StoryPackValidationContext? context = null)
    {
        var errors = Issues(def, context).Where(i => i.Severity == StoryPackIssueSeverity.Error).ToList();
        Assert.True(errors.Any(e => e.Message.Contains(fragment, StringComparison.Ordinal)),
            $"expected an error containing '{fragment}', got:\n{string.Join("\n", errors)}");
    }

    private static void AssertWarning(StoryDefinition def, string fragment)
    {
        var issues = Issues(def);
        Assert.DoesNotContain(issues, i => i.Severity == StoryPackIssueSeverity.Error);
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Warning && i.Message.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void TheProbeScene_IsClean()
    {
        var issues = Issues(Probe());
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void AnUnknownTier_IsAnError(int tier) => AssertError(Probe(c => c.Tier = tier), "unknown tier");

    [Fact]
    public void ASceneWithoutShots_IsAnError() => AssertError(Probe(c => c.Shots.Clear()), "at least one shot");

    [Fact]
    public void AnUnknownCamera_IsAnError() => AssertError(Probe(c => c.Shots[0].Camera.Type = "steadicam"), "unknown camera");

    /// <summary>#2477: the moves and the synthetic stages are known cameras — a staged scene may use them, a card may not
    /// (they take the camera); a diorama shows the sleeping core or VEGA's memory chart and nothing else.</summary>
    [Theory]
    [InlineData(CutsceneCameras.Dolly, CutsceneAnchors.Ship)]
    [InlineData(CutsceneCameras.Pan, CutsceneAnchors.Player)]
    [InlineData(CutsceneCameras.Flyover, "")]
    [InlineData(CutsceneCameras.SpaceVista, CutsceneAnchors.Planet)]
    [InlineData(CutsceneCameras.SpaceVista, "")]
    [InlineData(CutsceneCameras.Diorama, CutsceneAnchors.Core)]
    [InlineData(CutsceneCameras.Diorama, CutsceneAnchors.StarMap)]
    public void TheNewCameras_AreAcceptedInAStagedScene_AndRefusedOnACard(string camera, string anchor)
    {
        var issues = Issues(Probe(c => c.Shots[0].Camera = new CutsceneCamera { Type = camera, Anchor = anchor }));
        Assert.True(issues.Count == 0, string.Join("\n", issues));
        Assert.True(CutsceneCameras.TakesCamera(camera));
        AssertError(Probe(c =>
        {
            c.Tier = CutsceneTiers.Card;
            c.Options.HoldWorld = null;
            c.Shots[0].Camera = new CutsceneCamera { Type = camera, Anchor = anchor };
        }), "never takes the camera");
    }

    [Theory]
    [InlineData("")]
    [InlineData(CutsceneAnchors.Ship)]
    [InlineData(CutsceneAnchors.HealTank)]
    public void ADiorama_ShowsTheCoreOrTheMemoryChart_Only(string anchor)
        => AssertError(Probe(c => c.Shots[0].Camera = new CutsceneCamera { Type = CutsceneCameras.Diorama, Anchor = anchor }), "a diorama shows");

    [Fact]
    public void OnlyTheSpaceVistaAndTheDiorama_AreSyntheticStages()
    {
        Assert.Equal(new[] { CutsceneCameras.SpaceVista, CutsceneCameras.Diorama }, CutsceneCameras.All.Where(CutsceneCameras.IsSynthetic));
        Assert.Equal(12, CutsceneCameras.All.Count);
    }

    [Fact]
    public void AnUnknownAnchor_IsAnError() => AssertError(Probe(c => c.Shots[0].Camera.Anchor = "station:medbay"), "unknown anchor");

    [Fact]
    public void AnUnknownCaptionStyle_IsAnError() => AssertError(Probe(c => c.Shots[0].Captions[0].Style = "narration"), "unknown caption style");

    [Fact]
    public void AnUnknownGrade_IsAnError() => AssertError(Probe(c => c.Shots[0].Grade = "warm"), "unknown grade");

    [Theory]
    [InlineData(CutsceneTiers.Banner)]
    [InlineData(CutsceneTiers.Card)]
    public void ABannerOrCard_NeverTakesTheCamera(int tier)
        => AssertError(Probe(c => { c.Tier = tier; c.Options.HoldWorld = null; }), "never takes the camera");

    [Fact]
    public void HoldingTheWorld_NeedsACameraTakeover()
    {
        AssertError(Probe(c => { c.Tier = CutsceneTiers.Card; c.Shots[0].Camera.Type = CutsceneCameras.PlayerView; }), "may hold the world");
        AssertError(Probe(c => c.Shots[0].Camera.Type = CutsceneCameras.Overlay), "may hold the world"); // never over a flight
    }

    [Fact]
    public void FreezingTheControls_NeedsACameraTakeover()
        => AssertError(Probe(c =>
        {
            c.Tier = CutsceneTiers.Card;
            c.Shots[0].Camera.Type = CutsceneCameras.PlayerView;
            c.Options.HoldWorld = null;
            c.Options.LockInput = true;
        }), "may freeze the controls");

    [Theory]
    [InlineData(0f)]
    [InlineData(121f)]
    public void AShotLength_OutOfRange_IsAnError(float duration) => AssertError(Probe(c => c.Shots[0].Duration = duration), "a shot lasts");

    [Fact]
    public void ACaptionOrCue_OutsideItsShot_IsAnError()
    {
        AssertError(Probe(c => c.Shots[0].Captions[0].At = 8f), "lies outside");
        AssertError(Probe(c => c.Shots[0].Captions[0].Hold = -1f), "must not be negative");
        AssertError(Probe(c => c.Shots[0].Sfx[0].At = 9f), "lies outside");
    }

    [Fact]
    public void ACaptionKeyMissingInGerman_IsAnError()
    {
        var context = StoryPackValidationTests.Context();
        var english = context.English!;
        context.German = k => k == "vega.prologue.1" ? null : english(k);
        AssertError(Probe(), "missing in German", context);
    }

    [Fact]
    public void ACaptionKeyMissingInEnglish_IsAnError()
        => AssertError(Probe(c => c.Shots[0].Captions[0].Key = "story.probe.nowhere"), "missing in English");

    [Fact]
    public void TheCaptionBudget_FollowsTheStyle()
    {
        AssertError(Probe(c => c.Shots[0].Captions[0].Key = "story.vega.beat13"), "budget 220"); // the epilogue as a subtitle
        Assert.DoesNotContain(Issues(Probe(c =>
        {
            c.Shots[0].Captions[0].Key = "story.vega.beat13";
            c.Shots[0].Captions[0].Style = CutsceneCaptionStyles.ReadingFrame;
        })), i => i.Severity == StoryPackIssueSeverity.Error);
        AssertError(Probe(c => c.Shots[0].Captions[0].Style = CutsceneCaptionStyles.TitleCard), "budget 60");
        AssertError(Probe(c => c.ReplaceLineKeys.Add("story.probe.nowhere")), "missing in English");
    }

    [Theory]
    [InlineData("__mine", "built in")]
    [InlineData("a b", "not key-safe")]
    [InlineData("", "not key-safe")]
    public void ABadId_IsAnError(string id, string fragment)
        => AssertError(Probe(c => c.Id = id, d => d.Chapters[0].OpenCutscene = string.Empty), fragment);

    [Fact]
    public void ARepeatedId_IsAnError()
        => AssertError(Probe(editPack: d => d.Cutscenes.Add(new CutsceneDefinition { Id = "scene", Shots = { new CutsceneShot { Duration = 2 } } })), "used twice");

    [Fact]
    public void AnUnknownChapter_IsAnError() => AssertError(Probe(c => c.Chapter = "c9"), "has no chapter 'c9'");

    [Fact]
    public void AMissingTrackOrSound_IsAnError()
    {
        AssertError(Probe(c => c.Music = "music_nowhere"), "does not exist in client/Music");
        AssertError(Probe(c => c.Shots[0].Sfx[0].Cue = "nowhere_blip"), "does not exist in client/Assets/Resources/audio");
    }

    [Fact]
    public void AKickerOrSpeaker_OutsideItsStyle_IsAWarning()
    {
        AssertWarning(Probe(c => c.Shots[0].Captions[0].KickerKey = "ui.story.prologue"), "only a title_card");
        AssertWarning(Probe(c => c.Shots[0].Captions[0].Speaker = "vega"), "only a speaker caption");
    }

    /// <summary>#2478: the finale's stages are a closed set; the pack's speakers are well-formed and a caption names a known
    /// one; the players anchor labels the star chart only; a reading frame shows its kicker as its heading.</summary>
    [Fact]
    public void FinaleStages_Speakers_AndThePlayersAnchor_AreValidated()
    {
        Assert.Empty(Issues(Probe(editPack: d => d.Finale.Cutscenes[StoryFinaleStages.Breach] = "scene")));
        AssertError(Probe(editPack: d => d.Finale.Cutscenes["epilogue"] = "scene"), "unknown finale stage 'epilogue'");

        var core = new StoryNarrator { Id = "core", NameKey = "ui.finale.duel_title", Color = "#FF5A4A", LineStart = "anomaly_hum" };
        Assert.Empty(Issues(Probe(c => c.Shots[0].Captions[0].Style = CutsceneCaptionStyles.Speaker, d => d.Speakers.Add(core))));
        AssertError(Probe(editPack: d => d.Speakers.Add(new StoryNarrator { Id = "vega", NameKey = "ui.finale.duel_title" })), "used twice");
        AssertError(Probe(editPack: d => d.Speakers.Add(new StoryNarrator { Id = "core" })), "a speaker needs a name");
        AssertError(Probe(editPack: d => d.Speakers.Add(new StoryNarrator { Id = "core", NameKey = "ui.finale.duel_title", Color = "red" })),
            "is not a #RRGGBB colour");
        AssertError(Probe(editPack: d => d.Speakers.Add(new StoryNarrator { Id = "core", NameKey = "ui.finale.duel_title", LineStart = "no_such_cue" })),
            "sound 'no_such_cue' does not exist");
        AssertWarning(Probe(c =>
        {
            c.Shots[0].Captions[0].Style = CutsceneCaptionStyles.Speaker;
            c.Shots[0].Captions[0].Speaker = "yara";
        }), "neither the pack's narrator nor one of its speakers");

        AssertError(Probe(c => c.Shots[0].Camera = new CutsceneCamera { Type = CutsceneCameras.Orbit, Anchor = CutsceneAnchors.Players }),
            "the players anchor labels the star chart");
        Assert.Empty(Issues(Probe(c => c.Shots[0].Camera = new CutsceneCamera { Type = CutsceneCameras.Chart, Anchor = CutsceneAnchors.Players })));

        Assert.Empty(Issues(Probe(c =>
        {
            c.Shots[0].Captions[0].Style = CutsceneCaptionStyles.ReadingFrame;
            c.Shots[0].Captions[0].KickerKey = "ui.story.prologue";
        })));
        Assert.True(StoryPackValidator.IsHexColor("#5fd8FF"));
        Assert.False(StoryPackValidator.IsHexColor("#5FD8F"));
    }

    [Fact]
    public void ChainReferences_Resolve()
    {
        Assert.Empty(Issues(Probe(c => c.Options.ChainNext.Add(StoryCutsceneIds.Recap))));
        AssertError(Probe(c => c.Options.ChainNext.Add("nowhere")), "is not defined in the pack");
        AssertError(Probe(c => c.Options.ChainNext.Add("scene")), "chain to itself");
        AssertError(Probe(c => c.Options.ChainNext.Add(string.Empty)), "empty chain reference");
        Assert.Empty(Issues(Probe(c => c.Options.ChainNext.Add("vega_protocol/ch1.open")))); // #2474 added VEGA's chapter scenes
        Assert.Empty(Issues(Probe(c => c.Options.ChainNext.Add("vega_protocol/fin.arrival")))); // #2478 added the finale's scenes
        AssertError(Probe(c => c.Options.ChainNext.Add("vega_protocol/fin.nowhere")), "has no cutscene 'fin.nowhere'");
        Assert.Empty(Issues(Probe(c => c.Options.ChainNext.Add("vega_protocol/prologue"))));
        // #2469: the tutorial pack is installed, so a chain to one of its scenes must resolve — #2473 added its cards.
        Assert.Empty(Issues(Probe(c => c.Options.ChainNext.Add("tutorial/tut.welcome"))));
        AssertError(Probe(c => c.Options.ChainNext.Add("tutorial/tut.first_dock")), "has no cutscene 'tut.first_dock'"); // step 18
        AssertWarning(Probe(c => c.Options.ChainNext.Add("side_story/intro")), "not installed");
    }

    [Fact]
    public void AReturnOnSkipLine_MustBeOneTheSceneReplaces()
    {
        // #2474: a line a skip gives back to the panel must be one the scene takes out of it.
        Assert.Empty(Issues(Probe(c =>
        {
            c.ReplaceLineKeys.Add("vega.prologue.1");
            c.Options.ReturnOnSkip.Add("vega.prologue.1");
        })));
        AssertError(Probe(c => c.Options.ReturnOnSkip.Add("vega.prologue.2")), "is not one of the scene's replaceLineKeys");
        AssertError(Probe(c => c.Options.ReturnOnSkip.Add(string.Empty)), "is not one of the scene's replaceLineKeys");
    }

    // ---------------- Ledger keys and ids ----------------

    [Fact]
    public void TheLedgerKeys_FollowSynthesis56_AndParseBack()
    {
        Assert.Equal("story:vega_protocol:cut:prologue", StoryPackKeys.CutsceneSeen("vega_protocol", "prologue"));
        Assert.Equal("story:vega_protocol:cut:prologue:sent", StoryPackKeys.CutsceneSent("vega_protocol", "prologue"));
        Assert.True(StoryPackKeys.TryParseCutsceneSent("story:tutorial:cut:tut.strip@b3:sent", out string pack, out string id));
        Assert.Equal("tutorial", pack);
        Assert.Equal("tut.strip@b3", id);
        Assert.False(StoryPackKeys.TryParseCutsceneSent("story:vega_protocol:cut:prologue", out _, out _)); // seen, not sent
        Assert.False(StoryPackKeys.TryParseCutsceneSent("story:vega_protocol:beat:3", out _, out _));
        Assert.False(StoryPackKeys.TryParseCutsceneSent("story::cut:x:sent", out _, out _));
        Assert.False(StoryPackKeys.TryParseCutsceneSent("story:p:cut::sent", out _, out _));
        Assert.False(StoryPackKeys.TryParseCutsceneSent(null, out _, out _));
    }

    [Fact]
    public void InstanceIds_SplitIntoTemplateAndOwner()
    {
        Assert.Equal("tut.strip@b3", StoryCutsceneIds.Instance("tut.strip", "b3"));
        Assert.Equal("tut.strip", StoryCutsceneIds.DefinitionOf("tut.strip@b3"));
        Assert.Equal("b3", StoryCutsceneIds.OwnerOf("tut.strip@b3"));
        Assert.Equal("prologue", StoryCutsceneIds.DefinitionOf("prologue"));
        Assert.Equal(string.Empty, StoryCutsceneIds.OwnerOf("prologue"));
        Assert.True(StoryCutsceneIds.IsBuiltIn(StoryCutsceneIds.Recap));
        Assert.False(StoryCutsceneIds.IsBuiltIn("prologue"));
        Assert.True(CutsceneCameras.TakesCamera(CutsceneCameras.Orbit));
        Assert.False(CutsceneCameras.TakesCamera(CutsceneCameras.Overlay));
        Assert.False(CutsceneCameras.TakesCamera(CutsceneCameras.PlayerView));
    }

    // ---------------- Wire (tags 295/296, additive) ----------------

    [Fact]
    public void ThePlayAndTheReceipt_RoundTrip()
    {
        var play = Assert.IsType<CutscenePlay>(NetCodec.Decode(NetCodec.Encode(new CutscenePlay
        {
            PackId = "vega_protocol",
            CutsceneId = "tut.strip@b3",
            Arg = "a" + LineText.ArgSeparator + "b",
            HasAnchor = true,
            AnchorX = 1.5f,
            AnchorY = -2f,
            AnchorZ = 300.25f,
            Replay = true,
        })));
        Assert.Equal("vega_protocol", play.PackId);
        Assert.Equal("tut.strip@b3", play.CutsceneId);
        Assert.Equal(new[] { "a", "b" }, LineText.SplitArgs(play.Arg));
        Assert.True(play.HasAnchor);
        Assert.Equal(1.5f, play.AnchorX);
        Assert.Equal(-2f, play.AnchorY);
        Assert.Equal(300.25f, play.AnchorZ);
        Assert.True(play.Replay);

        var receipt = Assert.IsType<CutsceneSeenIntent>(NetCodec.Decode(NetCodec.Encode(new CutsceneSeenIntent
        {
            PackId = "tutorial",
            Ids = new[] { "tut.welcome", "tut.strip@b1" },
            Skipped = true,
            Unsupported = true,
        })));
        Assert.Equal("tutorial", receipt.PackId);
        Assert.Equal(new[] { "tut.welcome", "tut.strip@b1" }, receipt.Ids);
        Assert.True(receipt.Skipped);
        Assert.True(receipt.Unsupported);

        var replay = Assert.IsType<StoryPackIntent>(NetCodec.Decode(NetCodec.Encode(new StoryPackIntent
        {
            PackId = "vega_protocol",
            Action = StoryPackActions.ReplayCutscene,
            CutsceneId = "prologue",
        })));
        Assert.Equal("prologue", replay.CutsceneId);
        Assert.Equal(string.Empty, new StoryPackIntent().CutsceneId);
        Assert.False(new CutscenePlay().HasAnchor);
        Assert.Empty(new CutsceneSeenIntent().Ids);
    }
}
