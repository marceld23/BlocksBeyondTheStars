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
/// #2474 — VEGA's chapter scenes and beat cards on the client, planned from the shipped data pack: the chapter cards read
/// "Chapter n" over the chapter's title in both languages and show VEGA's beat in her speaker box (page-gated, free input);
/// the beat cards "VEGA remembers" over the beat's title; "What You Are" at the player's real heal tank — framed up close
/// inside the medbay, and played as a card when this world has no own heal tank; chapter 7's set piece holds a singleplayer
/// world only while it has the camera and never in multiplayer, and gives the jump-generator line back to the panel when the
/// player skips before it showed; the "Watch again" names; the client queue's chain from chapter 1's card to B1's.
/// </summary>
public sealed class VegaChapterScenesClientTests
{
    private const string V = StoryRegistry.DefaultStoryId;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static StoryDefinition Pack => Content.Value.Stories[V];

    private static CutsceneTextSource Text(GameLocale locale = GameLocale.English)
    {
        var loc = Content.Value.CreateLocalizer(locale);
        return new CutsceneTextSource(loc.Get, loc.Has, a => a);
    }

    /// <summary>Plans a scene the way the director does, with the argument the server sends for it.</summary>
    private static CutscenePlan Plan(string id, bool waitForReader = true, GameLocale locale = GameLocale.English)
    {
        var def = Pack.FindCutscene(id)!;
        return CutscenePlan.Build(def, V, id, CutsceneArgs.For(Pack, def, id), Text(locale), waitForReader);
    }

    private static string Localized(string key, GameLocale locale = GameLocale.English) => Content.Value.CreateLocalizer(locale).Get(key);

    [Fact]
    public void AChapterCard_ReadsChapterN_OverItsTitle_ThenVegaSaysTheBeat_WithTheInputFree()
    {
        var plan = Plan("ch4.open");
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.False(plan.TakesCamera || plan.HoldWorld || plan.LockInput || plan.OpensFromBlack);
        var captions = plan.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(("Chapter 4", "The Verdict"), (captions[0].Kicker, captions[0].Text));
        Assert.False(captions[0].PageGated); // the title runs on its own
        Assert.Equal(CutsceneCaptionStyles.Speaker, captions[1].Style);
        Assert.Equal(Localized("story.vega.beat06"), captions[1].Text);
        Assert.True(captions[1].PageGated); // VEGA's words wait for the reader, like her panel
        Assert.Contains("story.vega.beat06", plan.ReplaceLineKeys); // …and the panel does not say them again

        var de = Plan("ch6.open", locale: GameLocale.German).Shots.SelectMany(s => s.Captions).First();
        Assert.Equal(("Kapitel 6", "Er schläft noch"), (de.Kicker, de.Text));
    }

    [Fact]
    public void ChapterOne_IsATitleCardOnly_AndB1sCard_ShowsTheBeatAlone()
    {
        var ch1 = Assert.Single(Plan("ch1.open").Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Chapter 1", "A Familiar Signature"), (ch1.Kicker, ch1.Text));

        var b01 = Plan("b01");
        Assert.Equal(CutsceneTiers.Card, b01.Tier);
        var speaker = Assert.Single(b01.Shots.SelectMany(s => s.Captions));
        Assert.Equal(CutsceneCaptionStyles.Speaker, speaker.Style);
        Assert.Equal(new[] { "story.vega.beat01" }, b01.ReplaceLineKeys);
        Assert.Equal(CutsceneGrades.Memory, b01.Shots[0].Grade);
    }

    [Fact]
    public void ABeatCard_ReadsVegaRemembers_OverTheBeatsTitle_ThenTheBeat_InBothLanguages()
    {
        var captions = Plan("b07").Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(("VEGA remembers", "Her stand"), (captions[0].Kicker, captions[0].Text));
        Assert.True(captions[0].End < 5f); // the title clears while VEGA speaks
        Assert.Equal(Localized("story.vega.beat07"), captions[1].Text);

        var de = Plan("b10", locale: GameLocale.German).Shots.SelectMany(s => s.Captions).First();
        Assert.Equal(("VEGA erinnert sich", "Viele Leben"), (de.Kicker, de.Text));
    }

    // ---------------- "What You Are" at the real heal tank ----------------

    private static NetShipStation Station(string type, float x, float y, float z) => new() { Type = type, X = x, Y = y, Z = z };

    [Fact]
    public void TheHealTank_IsTheOwnShipsMedbay_NearEnoughOnThisWorld()
    {
        var stations = new[] { Station("cockpit", 10.5f, 64f, 10.5f), Station("medbay", 12.5f, 64f, 14.5f) };
        Assert.True(CutsceneRules.TryFindHealTank(stations, 20f, 64f, 20f, 4096, out float x, out float y, out float z));
        Assert.Equal((12.5f, 64f, 14.5f), (x, y, z));

        Assert.False(CutsceneRules.TryFindHealTank(stations, 200f, 64f, 200f, 4096, out _, out _, out _)); // the ship is far away
        Assert.False(CutsceneRules.TryFindHealTank(new[] { Station("cockpit", 10f, 64f, 10f) }, 10f, 64f, 10f, 4096, out _, out _, out _));
        Assert.False(CutsceneRules.TryFindHealTank(null, 0f, 0f, 0f, 4096, out _, out _, out _));
        // Across the world's east–west seam the tank is near.
        Assert.True(CutsceneRules.TryFindHealTank(new[] { Station("medbay", 4094.5f, 64f, 10.5f) }, 2f, 64f, 10f, 4096, out _, out _, out _));
    }

    [Fact]
    public void WhatYouAre_PlaysAtTheHealTank_OrAsACardWhenThisWorldHasNone()
    {
        var plan = Plan("ch5.open");
        Assert.Equal(CutsceneTiers.Scene, plan.Tier);
        Assert.True(plan.TakesCamera);
        Assert.Contains(plan.Shots, s => s.Anchor == CutsceneAnchors.HealTank && s.Camera == CutsceneCameras.Static);

        Assert.False(CutsceneRules.NeedsCardFallback(plan, _ => true)); // the tank is here: the staged scene
        Assert.True(CutsceneRules.NeedsCardFallback(plan, _ => false)); // no tank: the downgrade path
        var card = plan.AsCard();
        Assert.Equal(CutsceneTiers.Card, card.Tier);
        Assert.False(card.TakesCamera || card.HoldWorld || card.LockInput);
        Assert.Contains(card.Shots.SelectMany(s => s.Captions), c => c.Key == "story.vega.beat09"); // the words stay
        Assert.Equal(plan.ReplaceLineKeys, card.ReplaceLineKeys);

        // Only an object anchor forces the card; the sky, the chart and a card never do.
        Assert.False(CutsceneRules.NeedsCardFallback(Plan("ch3.open"), _ => false));
        Assert.False(CutsceneRules.NeedsCardFallback(Plan("ch7.open"), _ => false));
        Assert.False(CutsceneRules.NeedsCardFallback(Plan("ch4.open"), _ => false));
    }

    /// <summary>A parked ship's cabin as the director's samplers read it: the hull is no part of the voxel world, so the
    /// terrain path reads everything as air; the camera-spot sampler reads the hull (walls, floor, roof, the heal tank) as
    /// solid. The cabin's air is x 0–6, y 0–3, z 0–6 above a floor at y −1; the tank stands at (3, 0, 3).</summary>
    private static (CinematicStageScan.ClearSampler Path, CinematicStageScan.ClearSampler Spot) Cabin(int width = 7)
    {
        bool Hull(int x, int y, int z)
        {
            bool inBox = x >= -1 && x <= width && y >= -1 && y <= 4 && z >= -1 && z <= width;
            bool air = x >= 0 && x < width && y >= 0 && y <= 3 && z >= 0 && z < width;
            return (inBox && !air) || (x == 3 && y == 0 && z == 3);
        }

        return ((x, y, z) => true, (x, y, z) => !Hull(x, y, z));
    }

    [Fact]
    public void TheHealTankShot_FramesTheTankUpClose_InsideTheCabin()
    {
        var (path, spot) = Cabin();
        var staged = CutsceneStaging.Stage(CutsceneCameras.Static, path, spot, 3.5f, 0f, 3.5f, 1f, 0f, skyOpen: false, chartAvailable: false,
            closeUp: CutsceneStaging.WantsCloseUp(CutsceneAnchors.HealTank));
        Assert.Equal(StagedCameraKind.Static, staged.Kind);
        Assert.True(staged.CloseUp);
        Assert.False(staged.FellBack);
        double rad = staged.StartAngle * Math.PI / 180.0;
        float cx = staged.CenterX + (float)Math.Sin(rad) * staged.StartRadius;
        float cz = staged.CenterZ + (float)Math.Cos(rad) * staged.StartRadius;
        float cy = staged.CenterY + staged.StartHeight;
        Assert.InRange(cx, 0f, 7f); // the camera stands in the cabin, not outside the hull
        Assert.InRange(cz, 0f, 7f);
        Assert.InRange(cy, 0f, 4f);
        Assert.True(staged.Radius < staged.StartRadius); // the push-in

        // The room's line of sight: the tank's own cells pass, a wall does not.
        var line = CutsceneStaging.ObjectLine(spot, 3.5f, 0f, 3.5f);
        Assert.True(line(3, 0, 3));
        Assert.True(line(3, 1, 3));
        Assert.False(line(-1, 1, 3));

        // Without the close-up a static shot stands 9 blocks off — outside this cabin (the ship seen from outside).
        var far = CutsceneStaging.Stage(CutsceneCameras.Static, path, spot, 3.5f, 0f, 3.5f, 1f, 0f, false, false);
        Assert.False(far.CloseUp);
    }

    [Fact]
    public void ACabinTooSmallForTheCamera_FallsBackToTheUsualChain()
    {
        var (path, spot) = Cabin(width: 1); // a closet
        Assert.Null(CutsceneStaging.TryCloseUp(spot, 0.5f, 0f, 0.5f, 0f));
        var staged = CutsceneStaging.Stage(CutsceneCameras.Static, path, spot, 0.5f, 0f, 0.5f, 1f, 0f, false, false, closeUp: true);
        Assert.False(staged.CloseUp);
    }

    // ---------------- Chapter 7: the hold, the skip ----------------

    [Fact]
    public void ChapterSeven_HoldsASingleplayerWorld_OnlyWhileItHasTheCamera_AndNeverInMultiplayer()
    {
        var plan = Plan("ch7.open");
        Assert.Equal(CutsceneTiers.SetPiece, plan.Tier);
        Assert.True(plan.HoldWorld);
        Assert.True(CutsceneRules.HoldsWorld(plan, cameraTaken: true, multiplayer: false));
        Assert.False(CutsceneRules.HoldsWorld(plan, cameraTaken: false, multiplayer: false)); // the title shot on the player's view
        Assert.False(CutsceneRules.HoldsWorld(plan, cameraTaken: true, multiplayer: true));
        Assert.True(CutsceneRules.FreezesControls(plan, capped: false, cameraTaken: false));
        Assert.False(CutsceneRules.FreezesControls(plan, capped: true, cameraTaken: true)); // MP: after the 30 s cap, a card

        // A card never holds or freezes, even "with the camera" (it has none).
        var card = Plan("b03");
        Assert.False(CutsceneRules.HoldsWorld(card, cameraTaken: true, multiplayer: false));
        Assert.False(CutsceneRules.FreezesControls(card, capped: false, cameraTaken: true));
    }

    [Fact]
    public void SkippingChapterSeven_BeforeTheRevealShowed_GivesTheJumpGeneratorLineBack_AfterwardsNot()
    {
        var play = new CutscenePlayback(Plan("ch7.open"));
        Assert.Equal(new[] { "story.vega.guardian_revealed" }, play.KeysToReturnOnSkip()); // skipped at once: the line comes back

        // Shot 1: B12 types out, the reader continues; shot 2: the reveal over the chart types out.
        play.Tick(6f);
        Assert.Contains("story.vega.beat12", play.ShownKeys);
        play.Tick(6f);
        Assert.True(play.Waiting);
        Assert.True(play.Continue());
        play.Tick(0.1f);
        Assert.Equal(1, play.ShotIndex);
        Assert.Equal(new[] { "story.vega.guardian_revealed" }, play.KeysToReturnOnSkip()); // not on screen yet
        play.Tick(8f);
        Assert.Contains("story.vega.guardian_revealed", play.ShownKeys);
        Assert.Empty(play.KeysToReturnOnSkip()); // read it: a skip now keeps it in the log only

        // A scene without returnOnSkip never gives anything back.
        Assert.Empty(new CutscenePlayback(Plan("ch5.open")).KeysToReturnOnSkip());
        Assert.Equal(Plan("ch7.open").ReturnOnSkip, Plan("ch7.open").AsCard().ReturnOnSkip); // the downgrade keeps it
    }

    // ---------------- Names and chains ----------------

    [Fact]
    public void TheWatchAgainNames_ReadLikeTheScenes()
    {
        var text = Text();
        string Label(string id) => CutsceneCatalog.Label(Pack, Pack.FindCutscene(id)!, id, text);
        Assert.Equal("Chapter 1 — A Familiar Signature", Label("ch1.open"));
        Assert.Equal("A Familiar Signature", Label("b01")); // no title card: its chapter's title
        Assert.Equal("Chapter 3 — The Guardian", Label("ch3.open"));
        Assert.Equal("VEGA remembers — Ours, once", Label("b04"));
        Assert.Equal("Chapter 7 — The Guardian Core", Label("ch7.open"));

        var de = Text(GameLocale.German);
        Assert.Equal("Kapitel 5 — Was du bist", CutsceneCatalog.Label(Pack, Pack.FindCutscene("ch5.open")!, "ch5.open", de));
    }

    [Fact]
    public void ChapterOnesCard_LeadsStraightIntoB1sCard_ButTheNextChapterWaits()
    {
        var ch1 = Pack.FindCutscene("ch1.open")!;
        var queue = new CutsceneQueue();
        var card = new QueuedCutscene(V, "ch1.open", ch1.Tier) { ChainNext = ch1.Options.ChainNext.ToArray(), ChapterScene = true };
        var b01 = new QueuedCutscene(V, "b01", Pack.FindCutscene("b01")!.Tier) { ChapterScene = true };
        var ch2 = new QueuedCutscene(V, "ch2.open", Pack.FindCutscene("ch2.open")!.Tier) { ChapterScene = true };
        Assert.True(queue.Enqueue(card));
        Assert.True(queue.Enqueue(b01));
        Assert.True(queue.Enqueue(ch2));

        queue.Start(card);
        queue.Finish(100f);
        Assert.True(queue.MayGoNow(b01, 100.5f)); // chained: no 90 s gap
        Assert.False(queue.MayGoNow(ch2, 100.5f));

        // VEGA's prologue may chain into chapter 1's card too (a track that starts in the same join).
        Assert.Contains("ch1.open", Pack.FindCutscene(StoryCutsceneIds.Prologue)!.Options.ChainNext);
    }
}
