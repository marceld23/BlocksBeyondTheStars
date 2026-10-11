// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2468 — the pure half of the cutscene director: <see cref="CutscenePlan"/> (shots, resolved captions, pages and
/// timings from a definition and the play's argument; "Story text: wait for me" vs timed; title cards always timed; the
/// recap card; the downgrade to a card), <see cref="CaptionPager"/> and <see cref="CutscenePlayback"/> (the clock: shots,
/// page-gated holds, the reader's continue, the typewriter, skip rules, sound cues).
/// </summary>
public sealed class CutscenePlanTests
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        ["t.title"] = "The Title",
        ["t.kicker"] = "Chapter: {0}",
        ["ch.one"] = "Awakening",
        ["ch.two"] = "A familiar signature",
        ["t.short"] = "A short line.",
        ["t.key"] = "Press {key:Interact} to begin.",
        ["t.long"] = "The first sentence of a long caption runs on for a while so that it fills most of a page. "
                     + "A second sentence follows and adds even more words to the same caption here. "
                     + "And a third one closes the thought, so the text needs more than one page to show.",
        ["ui.story.so_far"] = "The story so far",
        ["ui.story.so_far_hint"] = "The full texts are in the Story tab.",
        ["pack.name"] = "The Test Pack",
    };

    private static CutsceneTextSource Text()
        => new(k => Table.TryGetValue(k, out var v) ? v : "[" + k + "]", Table.ContainsKey, a => a == "Interact" ? "E" : a);

    private static CutsceneDefinition Scene(int tier, params CutsceneShot[] shots)
    {
        var def = new CutsceneDefinition { Id = "scene", Tier = tier };
        def.Shots.AddRange(shots);
        return def;
    }

    private static CutsceneShot Shot(float duration, string camera = CutsceneCameras.PlayerView, params CutsceneCaption[] captions)
    {
        var shot = new CutsceneShot { Duration = duration, Camera = new CutsceneCamera { Type = camera } };
        shot.Captions.AddRange(captions);
        return shot;
    }

    private static CutsceneCaption Caption(string key, string style = CutsceneCaptionStyles.Subtitle, float at = 0f, float hold = 0f, string kicker = "")
        => new() { Key = key, Style = style, At = at, Hold = hold, KickerKey = kicker };

    // ---------------- The plan ----------------

    [Fact]
    public void TitleCards_AreAlwaysTimed_WhileSubtitlesWaitForTheReader()
    {
        var def = Scene(CutsceneTiers.Card, Shot(6f, CutsceneCameras.PlayerView,
            Caption("t.title", CutsceneCaptionStyles.TitleCard, kicker: "t.kicker"),
            Caption("t.short", at: 3f)));
        var plan = CutscenePlan.Build(def, "pack", "scene", "ch.one", Text(), waitForReader: true);

        var captions = plan.Shots[0].Captions;
        Assert.False(captions[0].PageGated);
        Assert.True(captions[1].PageGated);
        Assert.Equal("Chapter: Awakening", captions[0].Kicker); // the argument was a key: localized
        Assert.Equal("The Title", captions[0].Text);
    }

    [Fact]
    public void WithoutWaitForMe_EveryCaptionIsTimed_AndStretchedToItsReadingFloor()
    {
        var def = Scene(CutsceneTiers.Card, Shot(2f, CutsceneCameras.PlayerView, Caption("t.key")));
        var plan = CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: false);

        var caption = plan.Shots[0].Captions[0];
        Assert.False(caption.PageGated);
        float floor = CutscenePlan.ReadingSeconds("Press E to begin.");
        Assert.Equal(floor, caption.End, 3);
        Assert.Equal(floor, plan.Shots[0].Duration, 3); // the shot grows with its caption
        Assert.Equal(2f, plan.Shots[0].AuthoredDuration);
    }

    [Theory]
    [InlineData(0, 3f)]
    [InlineData(24, 3.2f)]
    [InlineData(60, 6.2f)]
    [InlineData(400, 10f)]
    public void ReadingSeconds_FollowsTheFloorFormula(int chars, float expected)
        => Assert.Equal(expected, CutscenePlan.ReadingSeconds(new string('x', chars)), 3);

    [Fact]
    public void TheArgument_IsLocalizedWhenItIsAKey_AndKeptOtherwise()
    {
        var text = Text();
        Assert.Equal("Awakening", CutscenePlan.ResolveArg("ch.one", text));
        Assert.Equal("Zeta Prime", CutscenePlan.ResolveArg("Zeta Prime", text));
        Assert.Equal("Awakening" + LineText.ArgSeparator + "Body 7", CutscenePlan.ResolveArg("ch.one" + LineText.ArgSeparator + "Body 7", text));
        Assert.Equal(string.Empty, CutscenePlan.ResolveArg(null, text));
    }

    [Fact]
    public void KeyTokens_ResolveThroughTheOneResolver()
    {
        var def = Scene(CutsceneTiers.Card, Shot(5f, CutsceneCameras.PlayerView, Caption("t.key", CutsceneCaptionStyles.Speaker)));
        var plan = CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true);
        Assert.Equal("Press E to begin.", plan.Shots[0].Captions[0].Text);
    }

    [Fact]
    public void LongCaptions_ArePagedAtSentenceEnds_AndLoseNoText()
    {
        var def = Scene(CutsceneTiers.Scene, Shot(8f, CutsceneCameras.Orbit, Caption("t.long")));
        var plan = CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true);

        var pages = plan.Shots[0].Captions[0].Pages;
        Assert.True(pages.Count >= 2);
        Assert.All(pages, p => Assert.True(p.Length <= CutscenePlan.SubtitlePageChars, p));
        Assert.All(pages.Take(pages.Count - 1), p => Assert.EndsWith(".", p));
        Assert.Equal(Table["t.long"].Replace(" ", string.Empty), string.Concat(pages).Replace(" ", string.Empty));
    }

    [Fact]
    public void CaptionPager_CutsTextWithoutSpaces_AtTheLimit()
    {
        string cjk = string.Concat(Enumerable.Repeat("这是一段很长的中文说明文字", 30)); // no spaces, no full stops
        var pages = CaptionPager.Pages(cjk, 100);
        Assert.All(pages, p => Assert.True(p.Length <= 100));
        Assert.Equal(cjk, string.Concat(pages));

        string withStops = string.Concat(Enumerable.Repeat("这是一句话。", 40));
        var stopped = CaptionPager.Pages(withStops, 100);
        Assert.All(stopped.Take(stopped.Count - 1), p => Assert.EndsWith("。", p));
        Assert.Empty(CaptionPager.Pages("   ", 100));
    }

    [Fact]
    public void ABanner_NeverWaitsForTheReader()
    {
        var def = Scene(CutsceneTiers.Banner, Shot(3f, CutsceneCameras.PlayerView, Caption("t.short")));
        var plan = CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true);
        Assert.False(plan.Shots[0].Captions[0].PageGated);
        Assert.False(plan.HoldWorld);
        Assert.False(plan.LockInput);
    }

    [Fact]
    public void AStagedScene_DerivesHoldAndLock_AndAsCardDropsThem()
    {
        var def = Scene(CutsceneTiers.Scene, Shot(5f, CutsceneCameras.Orbit, Caption("t.short")), Shot(4f, CutsceneCameras.PlayerView, Caption("t.short")));
        def.Music = "music_mystery_signal";
        def.ReplaceLineKeys.Add("vega.prologue.1");
        var plan = CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true);
        Assert.True(plan.TakesCamera);
        Assert.True(plan.HoldWorld);
        Assert.True(plan.LockInput);
        Assert.True(plan.HideHud);
        Assert.Equal("music_mystery_signal", plan.Music);
        Assert.Equal(new[] { "vega.prologue.1" }, plan.ReplaceLineKeys);

        var card = plan.AsCard();
        Assert.Equal(CutsceneTiers.Card, card.Tier);
        Assert.False(card.TakesCamera);
        Assert.False(card.HoldWorld || card.LockInput || card.HideHud);
        Assert.Equal(plan.Shots.Count, card.Shots.Count);
        Assert.Equal(plan.Shots[0].Captions[0].Text, card.Shots[0].Captions[0].Text);
        Assert.Same(card, card.AsCard());
    }

    [Fact]
    public void TheRecapCard_ListsTheChapterTitles_Ticked_WithoutBeatText()
    {
        var plan = CutscenePlan.Recap("pack", "ch.one" + LineText.ArgSeparator + "ch.two", "pack.name", Text(), waitForReader: true);
        Assert.True(plan.IsRecap);
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.Equal(StoryCutsceneIds.Recap, plan.CutsceneId);
        var caption = Assert.Single(plan.Shots[0].Captions);
        Assert.Equal(CutsceneCaptionStyles.ReadingFrame, caption.Style);
        Assert.Equal("The story so far · The Test Pack", caption.Kicker);
        Assert.Contains("✓ Awakening", caption.Text);
        Assert.Contains("✓ A familiar signature", caption.Text);
        Assert.Contains("The full texts are in the Story tab.", caption.Text);
        Assert.True(caption.PageGated);
        Assert.Equal(new[] { "✓ Awakening", "✓ A familiar signature" }, CutscenePlan.RecapLines("ch.one" + LineText.ArgSeparator + "ch.two", Text()));
    }

    [Fact]
    public void TheVegaPrologue_PlansFromTheDataPack()
    {
        string dir = Path.Combine(ClientTestPaths.DataDir(), "stories", "vega_protocol");
        var file = ContentLoader.ParseCutscenes(File.ReadAllText(Path.Combine(dir, "cutscenes.json")));
        var def = Assert.Single(file, c => c.Id == StoryCutsceneIds.Prologue);
        var table = ReadTable(Path.Combine(ClientTestPaths.DataDir(), "locales", "en.json"));
        foreach (var kv in ReadTable(Path.Combine(dir, "locales", "en.json")))
        {
            table[kv.Key] = kv.Value;
        }

        var text = new CutsceneTextSource(k => table.TryGetValue(k, out var v) ? v : "[" + k + "]", table.ContainsKey);
        var plan = CutscenePlan.Build(def, StoryRegistry.DefaultStoryId, StoryCutsceneIds.Prologue, null, text, waitForReader: true);

        Assert.Equal(CutsceneTiers.Scene, plan.Tier);
        Assert.True(plan.TakesCamera && plan.HoldWorld && plan.LockInput);
        Assert.Equal(new[] { CutsceneCameras.PlayerView, CutsceneCameras.Orbit, CutsceneCameras.Static, CutsceneCameras.PlayerView },
            plan.Shots.Select(s => s.Camera));
        var title = plan.Shots[0].Captions[0];
        Assert.False(title.PageGated); // the title card runs on its own
        Assert.Equal(table["ui.story.prologue"], title.Kicker);
        Assert.Equal(table["story.vega_protocol.name"], title.Text);
        Assert.All(plan.Shots.Skip(1), s => Assert.True(s.Captions[0].PageGated));
        Assert.Equal("ai_blip", plan.Shots[3].Sfx.Single().Cue);
        Assert.DoesNotContain(plan.Shots.SelectMany(s => s.Captions), c => c.Text.StartsWith('[')); // every key resolved
    }

    private static Dictionary<string, string> ReadTable(string path)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path),
               new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
           ?? new Dictionary<string, string>();

    // ---------------- The playback ----------------

    [Fact]
    public void APageGatedCaption_HoldsTheShot_UntilTheReaderHasTurnedTheLastPage()
    {
        var def = Scene(CutsceneTiers.Scene, Shot(4f, CutsceneCameras.Orbit, Caption("t.long")));
        var play = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true));
        int pages = play.Plan.Shots[0].Captions[0].Pages.Count;

        play.Tick(30f);
        Assert.False(play.Done);
        Assert.True(play.Waiting);
        Assert.Equal(4f, play.ShotTime, 3); // the shot holds at the caption's end
        var view = Assert.Single(play.Visible());
        Assert.Equal(0, view.Page);
        Assert.True(view.WaitsForReader);

        for (int p = 1; p < pages; p++)
        {
            Assert.True(play.Continue());
            Assert.Equal(p, Assert.Single(play.Visible()).Page);
        }

        Assert.True(play.Continue()); // the last page closes the caption
        Assert.Empty(play.Visible());
        play.Tick(0.1f);
        Assert.True(play.Done);
        Assert.False(play.Continue());
    }

    [Fact]
    public void TimedCaptions_PageThroughOnTheirOwn()
    {
        var def = Scene(CutsceneTiers.Scene, Shot(4f, CutsceneCameras.Orbit, Caption("t.long")));
        var play = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: false));
        var caption = play.Plan.Shots[0].Captions[0];
        Assert.True(caption.Pages.Count >= 2);

        play.Tick(0.5f);
        Assert.Equal(0, Assert.Single(play.Visible()).Page);
        play.Tick(caption.PageSeconds[0]);
        Assert.Equal(1, Assert.Single(play.Visible()).Page);
        Assert.False(play.Continue()); // nothing waits for the reader
        play.Tick(caption.End);
        Assert.True(play.Done);
    }

    [Fact]
    public void ATypingSpeakerBox_CompletesOnTheFirstContinue()
    {
        var def = Scene(CutsceneTiers.Card, Shot(6f, CutsceneCameras.PlayerView, Caption("t.short", CutsceneCaptionStyles.Speaker)));
        var play = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true));

        play.Tick(0.1f);
        var typing = Assert.Single(play.Visible());
        Assert.True(typing.VisibleChars < typing.Text.Length);
        Assert.False(typing.WaitsForReader);
        Assert.Equal(typing.Text.Substring(0, typing.VisibleChars), typing.Shown);

        Assert.True(play.Continue()); // completes the typing, keeps the page
        var typed = Assert.Single(play.Visible());
        Assert.Equal(typed.Text.Length, typed.VisibleChars);
        Assert.True(typed.WaitsForReader);

        Assert.True(play.Continue()); // turns the (only) page
        Assert.Empty(play.Visible());
    }

    [Fact]
    public void Skip_IsAcceptedAfterTheMinimumDisplay_AndAtOnceOnAReplay()
    {
        var def = Scene(CutsceneTiers.Scene, Shot(5f, CutsceneCameras.Orbit, Caption("t.short")));
        def.Options.MinDisplay = 1.5f;
        var play = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true));
        Assert.False(play.CanSkip);
        play.Tick(1.6f);
        Assert.True(play.CanSkip);

        def.Options.Skippable = false;
        var locked = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true));
        locked.Tick(10f);
        Assert.False(locked.CanSkip);
        var replay = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true, replay: true));
        Assert.True(replay.CanSkip); // a replay is skippable at once, whatever the scene says
    }

    [Fact]
    public void SoundCues_FireOnce_AndShotChangesAreReportedOncePerShot()
    {
        var first = Shot(2f);
        first.Sfx.Add(new CutsceneSfx { Cue = "ai_blip", At = 0f });
        first.Sfx.Add(new CutsceneSfx { Cue = "chime", At = 1.5f });
        var def = Scene(CutsceneTiers.Card, first, Shot(2f));
        var play = new CutscenePlayback(CutscenePlan.Build(def, "pack", "scene", null, Text(), waitForReader: true));

        Assert.True(play.TakeShotChanged());
        Assert.False(play.TakeShotChanged());
        play.Tick(1f);
        Assert.Equal(new[] { "ai_blip" }, play.TakeDueSfx());
        play.Tick(0.6f);
        Assert.Equal(new[] { "chime" }, play.TakeDueSfx());
        play.Tick(0.5f);
        Assert.Empty(play.TakeDueSfx());
        Assert.Equal(1, play.ShotIndex);
        Assert.True(play.TakeShotChanged());
        play.Tick(5f);
        Assert.True(play.Done);
    }
}
