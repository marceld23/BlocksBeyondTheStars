// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2473 — the basic training's cards on the client, planned from the shipped data pack: the part card reads "Basic training ·
/// Part n of 6" with the part's title, the welcome "Part 1 of 6 · First steps"; the cards' titles run on their own (they never
/// wait for "continue" while the player walks on); the first launch lies over the flight without blacking it out; the first
/// landing circles the ship with the world's name; the graduation shows the achievement and VEGA's closing line; the "Watch
/// again" names and the dev palette's argument; and the client queue's cross-pack chain from VEGA's prologue to the welcome.
/// </summary>
public sealed class TutorialCardsClientTests
{
    private const string T = "tutorial";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static StoryDefinition Pack => Content.Value.Stories[T];

    private static CutsceneTextSource Text(GameLocale locale = GameLocale.English)
    {
        var loc = Content.Value.CreateLocalizer(locale);
        return new CutsceneTextSource(loc.Get, loc.Has, a => a);
    }

    /// <summary>Plans a card the way the director does, with the argument the server sends for it.</summary>
    private static CutscenePlan Plan(string id, bool waitForReader = true, string? arg = null, GameLocale locale = GameLocale.English)
    {
        var def = Pack.FindCutscene(id)!;
        return CutscenePlan.Build(def, T, id, arg ?? CutsceneArgs.For(Pack, def, id), Text(locale), waitForReader);
    }

    [Fact]
    public void ThePartCard_ReadsPartNOfSix_WithThePartsTitle_InBothLanguages_AndRunsOnItsOwn()
    {
        var plan = Plan("tut.strip@b3");
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.False(plan.TakesCamera || plan.HoldWorld || plan.LockInput || plan.OpensFromBlack);
        var title = Assert.Single(plan.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Basic training · Part 3 of 6", "Mining and carrying"), (title.Kicker, title.Text));
        Assert.False(title.PageGated); // a title never waits for "continue"

        var de = Assert.Single(Plan("tut.strip@b5", locale: GameLocale.German).Shots.SelectMany(s => s.Captions));
        Assert.Equal("Grundausbildung · Teil 5 von 6", de.Kicker);
    }

    [Fact]
    public void TheWelcome_IntroducesTheTraining_ThenPartOne()
    {
        var plan = Plan("tut.welcome");
        var captions = plan.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(("Six short parts", "Basic training"), (captions[0].Kicker, captions[0].Text));
        Assert.Equal(("Part 1 of 6", "First steps"), (captions[1].Kicker, captions[1].Text));
        Assert.All(captions, c => Assert.False(c.PageGated));
        Assert.True(plan.TimedLength <= 11f);
    }

    [Fact]
    public void ThePartBanner_IsABannerWithThePartsTitle()
    {
        var plan = Plan("tut.done@b1");
        Assert.Equal(CutsceneTiers.Banner, plan.Tier);
        var caption = Assert.Single(plan.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Part complete!", "First steps"), (caption.Kicker, caption.Text));
    }

    [Fact]
    public void TheFirstLaunch_LiesOverTheFlight_WithoutBlackingItOut_AndNeverWaits()
    {
        var plan = Plan("tut.first_launch");
        Assert.Equal(CutsceneTiers.Scene, plan.Tier); // the full letterbox of a scene…
        Assert.False(plan.TakesCamera);                // …over the flight's own camera
        Assert.False(plan.OpensFromBlack);
        Assert.False(plan.HoldWorld || plan.LockInput);
        Assert.All(plan.Shots.SelectMany(s => s.Captions), c => Assert.False(c.PageGated));
        Assert.Equal("Lift-off!", plan.Shots[0].Captions[0].Text);
        Assert.True(plan.TimedLength <= 8f);
    }

    [Fact]
    public void TheFirstLanding_CirclesTheShip_WithTheWorldsName_AndHoldsASingleplayerWorld()
    {
        var plan = Plan("tut.first_landing", arg: "Kepler-7");
        Assert.True(plan.TakesCamera && plan.HoldWorld && plan.LockInput && plan.OpensFromBlack);
        Assert.Equal(CutsceneCameras.Orbit, plan.Shots[0].Camera);
        Assert.Equal(CutsceneAnchors.Ship, plan.Shots[0].Anchor);
        Assert.Equal(("Touchdown on a new world", "Kepler-7"), (plan.Shots[0].Captions[0].Kicker, plan.Shots[0].Captions[0].Text));
        Assert.True(plan.Shots[1].Captions[0].PageGated); // the world is held: the line may wait for the reader
    }

    [Fact]
    public void TheGraduation_ShowsTheAchievement_ThenVegasClosingLine_InsteadOfThePanel()
    {
        var plan = Plan("tut.graduation");
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.Equal(("★ Achievement", "Graduate"), (plan.Shots[0].Captions[0].Kicker, plan.Shots[0].Captions[0].Text));
        var line = plan.Shots[1].Captions[0];
        Assert.Equal(CutsceneCaptionStyles.Speaker, line.Style);
        Assert.Equal("story.tutorial.graduated", line.Key);
        Assert.Contains("story.tutorial.graduated", plan.ReplaceLineKeys); // the panel does not say it a second time
        Assert.DoesNotContain(plan.Shots.SelectMany(s => s.Captions), c => c.Text.StartsWith('[') || c.Kicker.StartsWith('['));
    }

    [Fact]
    public void TheWatchAgainNames_ReadLikeTheCards()
    {
        var text = Text();
        string Label(string id) => CutsceneCatalog.Label(Pack, Pack.FindCutscene(id)!, id, text);
        Assert.Equal("Six short parts — Basic training", Label("tut.welcome"));
        Assert.Equal("Basic training · Part 4 of 6 — Crafting and building", Label("tut.strip@b4"));
        Assert.Equal("First launch — Lift-off!", Label("tut.first_launch"));
        Assert.Equal("Touchdown on a new world", Label("tut.first_landing")); // the world's name is known only when it plays
        Assert.Equal("★ Achievement — Graduate", Label("tut.graduation"));
    }

    [Fact]
    public void ALocalPlay_TakesTheServersArgument_WithTheWorldFromTheStarMap()
    {
        var map = new StarMapData
        {
            ActiveLocationId = "sys1-p2",
            Systems = new[]
            {
                new NetStarSystem
                {
                    Id = "sys1", Name = "Sol", Bodies = new[] { new NetBody { Id = "sys1-p1", Name = "Rocky" }, new NetBody { Id = "sys1-p2", Name = "Kepler-7" } },
                },
            },
        };
        Assert.Equal("Kepler-7", CutsceneCatalog.PlayArg(Pack, Pack.FindCutscene("tut.first_landing")!, "tut.first_landing", map));
        Assert.Equal(CutsceneArgs.For(Pack, Pack.FindCutscene("tut.strip")!, "tut.strip@b2"),
            CutsceneCatalog.PlayArg(Pack, Pack.FindCutscene("tut.strip")!, "tut.strip@b2", map));
        Assert.Equal(string.Empty, CutsceneCatalog.PlayArg(Pack, Pack.FindCutscene("tut.first_landing")!, "tut.first_landing", null));
    }

    [Fact]
    public void VegasPrologue_LeadsStraightIntoTheWelcome_ButAPartCardAfterItWaits()
    {
        var vega = Content.Value.Stories[StoryRegistry.DefaultStoryId].FindCutscene(StoryCutsceneIds.Prologue)!;
        var queue = new CutsceneQueue();
        var prologue = new QueuedCutscene(StoryRegistry.DefaultStoryId, StoryCutsceneIds.Prologue, vega.Tier) { ChainNext = vega.Options.ChainNext.ToArray() };
        var welcome = Pack.FindCutscene("tut.welcome")!;
        var welcomeItem = new QueuedCutscene(T, "tut.welcome", welcome.Tier) { ChainNext = welcome.Options.ChainNext.ToArray() };
        var strip = new QueuedCutscene(T, "tut.strip@b2", Pack.FindCutscene("tut.strip")!.Tier);
        Assert.True(queue.Enqueue(prologue));
        Assert.True(queue.Enqueue(welcomeItem));

        queue.Start(prologue);
        queue.Finish(100f);
        Assert.True(queue.MayGoNow(welcomeItem, 100.5f)); // a cross-pack chain: no 90 s gap after the cold open
        queue.Start(welcomeItem);
        queue.Finish(110f);

        Assert.True(queue.Enqueue(strip));
        Assert.False(queue.MayGoNow(strip, 120f)); // the next part's card keeps the spacing
        Assert.True(queue.MayGoNow(strip, 110f + CutsceneQueue.CooldownSeconds));
    }
}
