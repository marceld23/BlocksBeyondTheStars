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
/// #2478 — VEGA's finale on the client, Unity-free: the duel's dialogue mode (the state it shows, where it shows — only on the
/// core world —, its answer buttons that wait for the core, the core's words and "Argument n of 4", stepping back and facing
/// the core again near it, never a world hold in multiplayer), the close framing of the core column in its chamber, the beat
/// moment b10 (the other players named on the live chart; alone the card it was), and the set pieces planned from the data
/// pack in both languages.
/// </summary>
public sealed class FinaleSetPiecesClientTests
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string CoreBody = "guardian_finale-core";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static StoryDefinition Pack => Content.Value.Stories[V];

    private static CutsceneTextSource Text(GameLocale locale = GameLocale.English)
    {
        var loc = Content.Value.CreateLocalizer(locale);
        return new CutsceneTextSource(loc.Get, loc.Has, a => a);
    }

    private static CutscenePlan Plan(string id, GameLocale locale = GameLocale.English, bool waitForReader = true)
    {
        var def = Pack.FindCutscene(id)!;
        return CutscenePlan.Build(def, V, id, CutsceneArgs.For(Pack, def, id), Text(locale), waitForReader);
    }

    private static string Localized(string key, GameLocale locale = GameLocale.English) => Content.Value.CreateLocalizer(locale).Get(key);

    private static CoreDialogueMessage Node(int node, string response = "", bool anchor = true) => new()
    {
        Node = node,
        NodeCount = 4,
        PromptKey = $"core.duel.n{node}.prompt",
        ChoiceKeys = new[] { $"core.duel.n{node}.c0", $"core.duel.n{node}.c1", $"core.duel.n{node}.c2" },
        ResponseKey = response,
        HasAnchor = anchor,
        AnchorX = 48.5f,
        AnchorY = 41f,
        AnchorZ = 24.5f,
    };

    // ---------------- The duel's state ----------------

    [Fact]
    public void TheDuelState_FollowsTheNodes_AWrongPickReappears_AndTheWinClosesIt()
    {
        var duel = new CoreDuelState();
        Assert.False(duel.Open);

        Assert.True(duel.Apply(Node(0))); // opened
        Assert.Equal((true, 0, 4, "core.duel.n0.prompt", 3), (duel.Open, duel.Node, duel.NodeCount, duel.PromptKey, duel.ChoiceKeys.Count));
        Assert.True(duel.HasAnchor);
        Assert.Equal((48.5f, 41f, 24.5f), (duel.AnchorX, duel.AnchorY, duel.AnchorZ));
        int v0 = duel.Version;

        Assert.False(duel.Apply(Node(0, "core.duel.n0.r1"))); // a wrong answer: the same node again, with the core's reply
        Assert.Equal((0, "core.duel.n0.r1"), (duel.Node, duel.ResponseKey));
        Assert.True(duel.Version > v0); // a new version frees the buttons again

        duel.Apply(Node(1, "core.duel.n0.r0", anchor: false)); // the anchor once known stays
        Assert.Equal(1, duel.Node);
        Assert.True(duel.HasAnchor);

        duel.Apply(new CoreDialogueMessage { Node = 3, Won = true, ResponseKey = "core.duel.n3.r0" });
        Assert.False(duel.Open);
        Assert.True(duel.Won);
        Assert.Empty(duel.ChoiceKeys);

        duel.Reset();
        Assert.False(duel.Won || duel.Open || duel.HasAnchor);
    }

    [Fact]
    public void TheDialogue_ShowsOnlyOnTheCoreWorld_NeverInItsFlight_ToAnObserver_OrAfterAStepBack()
    {
        var duel = new CoreDuelState();
        duel.Apply(Node(0));
        Assert.True(CoreDuelRules.ShowsDialogue(duel, CoreBody, spaceView: false, spectating: false, steppedBack: false));
        Assert.False(CoreDuelRules.ShowsDialogue(duel, "sys0-p1", false, false, false));   // elsewhere: the server's banner
        Assert.False(CoreDuelRules.ShowsDialogue(duel, CoreBody, spaceView: true, false, false)); // flying over the core
        Assert.False(CoreDuelRules.ShowsDialogue(duel, CoreBody, false, spectating: true, false));
        Assert.False(CoreDuelRules.ShowsDialogue(duel, CoreBody, false, false, steppedBack: true));
        Assert.False(CoreDuelRules.ShowsDialogue(new CoreDuelState(), CoreBody, false, false, false)); // no duel open
        Assert.False(CoreDuelRules.ShowsDialogue(null, CoreBody, false, false, false));
    }

    [Fact]
    public void AfterAPick_TheButtonsWaitForTheCore_ThenOpenAgain()
    {
        var duel = new CoreDuelState();
        duel.Apply(Node(0));
        Assert.True(CoreDuelRules.ChoicesOpen(duel, pickedVersion: -1, pickedAt: float.NegativeInfinity, now: 10f));

        int picked = duel.Version;
        Assert.False(CoreDuelRules.ChoicesOpen(duel, picked, pickedAt: 10f, now: 11f)); // the answer is on its way
        Assert.True(CoreDuelRules.ChoicesOpen(duel, picked, 10f, 10f + CoreDuelRules.PickRetrySeconds)); // lost on the way

        duel.Apply(Node(0, "core.duel.n0.r2")); // the core answered
        Assert.True(CoreDuelRules.ChoicesOpen(duel, picked, 10f, 11f));

        duel.Apply(new CoreDialogueMessage { Won = true });
        Assert.False(CoreDuelRules.ChoicesOpen(duel, -1, float.NegativeInfinity, 99f));
    }

    [Fact]
    public void TheCoresWords_ItsReplyBeforeItsStatement_AndTheProgress_InBothLanguages()
    {
        var duel = new CoreDuelState();
        duel.Apply(Node(0));
        Assert.Equal(Localized("core.duel.n0.prompt"), CoreDuelRules.SpeakerText(duel, k => Localized(k)));

        duel.Apply(Node(0, "core.duel.n0.r1"));
        string words = CoreDuelRules.SpeakerText(duel, k => Localized(k), r => "<i>" + r + "</i>");
        Assert.Equal("<i>" + Localized("core.duel.n0.r1") + "</i>\n\n" + Localized("core.duel.n0.prompt"), words);

        duel.Apply(Node(1, "core.duel.n0.r0"));
        Assert.Equal("Argument 2 of 4", CoreDuelRules.ProgressText(duel, Localized("ui.finale.duel_progress")));
        Assert.Equal("Argument 2 von 4", CoreDuelRules.ProgressText(duel, Localized("ui.finale.duel_progress", GameLocale.German)));
        Assert.Equal(string.Empty, CoreDuelRules.ProgressText(new CoreDuelState(), "Argument {0} of {1}")); // an older server: no count
        Assert.Equal("Argument {0", CoreDuelRules.ProgressText(duel, "Argument {0")); // a broken translation never blanks it
    }

    [Fact]
    public void FacingTheCoreAgain_NeedsThePlayerNearTheColumn_AcrossTheWorldsSeam()
    {
        var duel = new CoreDuelState();
        duel.Apply(Node(0));
        Assert.True(CoreDuelRules.NearCore(duel, 50f, 41f, 26f, circumference: 4096));
        Assert.False(CoreDuelRules.NearCore(duel, 48.5f, 70f, 24.5f, 4096)); // on the surface above
        var seam = new CoreDuelState();
        seam.Apply(new CoreDialogueMessage { NodeCount = 4, PromptKey = "p", HasAnchor = true, AnchorX = 2f, AnchorY = 10f, AnchorZ = 0f });
        Assert.True(CoreDuelRules.NearCore(seam, 4090f, 10f, 0f, 4096)); // 8 blocks the short way round
        Assert.True(CoreDuelRules.NearCore(new CoreDuelState(), 0f, 0f, 0f, 4096)); // no column known: anywhere on the core world
    }

    [Fact]
    public void TheDialogue_OpensInSingleplayerEvenWithAMachineNear_InMultiplayerOnlyWhenSafe()
    {
        var s = new SafeMomentInputs { HostileDistance = 5f };
        Assert.True(CoreDuelRules.MayOpen(s)); // singleplayer: the dialogue holds the world — nothing reaches the player
        s.Multiplayer = true;
        Assert.False(CoreDuelRules.MayOpen(s)); // multiplayer: it waits for the danger to pass
        s.HostileDistance = float.MaxValue;
        Assert.True(CoreDuelRules.MayOpen(s));

        foreach (Action<SafeMomentInputs, bool> set in new Action<SafeMomentInputs, bool>[]
        {
            (i, v) => i.LoadingVeil = v, (i, v) => i.MenuOpen = v, (i, v) => i.ChatTyping = v, (i, v) => i.RespawnPrompt = v,
            (i, v) => i.AnotherScene = v, (i, v) => i.Falling = v, (i, v) => i.Settling = v, (i, v) => i.PilotingInSpace = v,
        })
        {
            set(s, true);
            Assert.False(CoreDuelRules.MayOpen(s));
            set(s, false);
        }

        Assert.False(CoreDuelRules.MayOpen(null));

        // Open: only multiplayer danger makes it step aside (damage since it opened, a machine closing in).
        s.SecondsSinceDamage = 1f;
        Assert.True(CoreDuelRules.StepsAsideForDanger(s, secondsOpen: 5f));
        Assert.False(CoreDuelRules.StepsAsideForDanger(s, secondsOpen: 0.5f)); // the damage came before it opened
        s.Multiplayer = false;
        Assert.False(CoreDuelRules.StepsAsideForDanger(s, 5f));
    }

    [Fact]
    public void TheDuel_HoldsASingleplayerWorld_NeverAMultiplayerOne()
    {
        Assert.True(CutsceneRules.DuelHoldsWorld(dialogueShown: true, multiplayer: false));
        Assert.False(CutsceneRules.DuelHoldsWorld(true, multiplayer: true));
        Assert.False(CutsceneRules.DuelHoldsWorld(false, false));
    }

    // ---------------- The camera in the chamber ----------------

    /// <summary>The Guardian core chamber as the server stamps it (floor plate y 9, the pedestal at y 10, the column at x/z 0
    /// from y 11 to 14, pillars on the diagonals, glass panes on the axes at y 12–13, walls at ±5, the ceiling at y 16); rock
    /// all round. Only the camera-spot sampler matters here.</summary>
    private static bool ChamberAir(int x, int y, int z)
    {
        int ax = Math.Abs(x), az = Math.Abs(z);
        if (ax > 4 || az > 4 || y < 10 || y > 15)
        {
            return false; // walls, floor, ceiling, rock
        }

        if (ax <= 1 && az <= 1 && y == 10)
        {
            return false; // the pedestal
        }

        if (x == 0 && z == 0 && y >= 11 && y <= 14)
        {
            return false; // the red column
        }

        if (ax == 1 && az == 1 && y >= 11 && y <= 15)
        {
            return false; // the pillars
        }

        return !((ax + az == 1) && y >= 12 && y <= 13); // the glass panes
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(37f)]
    [InlineData(200f)]
    public void TheBreachAndTheDuel_FrameTheColumnUpClose_InsideTheChamber(float yaw)
    {
        CinematicStageScan.ClearSampler spot = ChamberAir;
        Assert.True(CutsceneStaging.WantsCloseUp(CutsceneAnchors.Core));
        var staged = CutsceneStaging.Stage(CutsceneCameras.Static, spot, spot, 0.5f, 11f, 0.5f, 1f, yaw, skyOpen: false,
            chartAvailable: false, closeUp: true, syntheticAllowed: false);
        Assert.True(staged.CloseUp);
        double rad = staged.StartAngle * Math.PI / 180.0;
        float cx = staged.CenterX + (float)Math.Sin(rad) * staged.StartRadius;
        float cz = staged.CenterZ + (float)Math.Cos(rad) * staged.StartRadius;
        float cy = staged.CenterY + staged.StartHeight;
        Assert.True(ChamberAir((int)Math.Floor(cx), (int)Math.Floor(cy), (int)Math.Floor(cz))); // inside, in the open
        Assert.True(CinematicStageScan.PathClear(CutsceneStaging.ObjectLine(spot, 0.5f, 11f, 0.5f),
            0.5f, 11f + CutsceneStaging.CloseUpLookHeight, 0.5f, cx, cy, cz)); // and it sees the column
    }

    // ---------------- b10: the other players on the live chart ----------------

    private static StarMapData Map(params (string Name, string Body)[] players) => new()
    {
        Systems = new[]
        {
            new NetStarSystem { Id = "sys0", Name = "Home", Bodies = new[] { new NetBody { Id = "sys0-p1" }, new NetBody { Id = "sys0-p2" } } },
            new NetStarSystem { Id = "sys1", Name = "Far", Bodies = new[] { new NetBody { Id = "sys1-p1" } } },
        },
        Players = players.Select(p => new NetPlayerLocation { Name = p.Name, LocationId = p.Body }).ToArray(),
    };

    [Fact]
    public void TheChartsPlayerLabels_NameTheOthersBySystem_NeverMe()
    {
        var map = Map(("Me", "sys0-p1"), ("Anna", "sys0-p2"), ("Ben", "sys1-p1"), ("Cleo", "sys1-p1"), ("Ghost", "nowhere"));
        var labels = CutsceneChart.PlayersBySystem(map, "Me");
        Assert.Equal("Anna", labels["sys0"]);
        Assert.Equal("Ben, Cleo", labels["sys1"]);
        Assert.Equal(2, labels.Count);
        Assert.True(CutsceneChart.AnyOtherPlayer(map, "Me"));
        Assert.False(CutsceneChart.AnyOtherPlayer(Map(("Me", "sys0-p1")), "Me"));
        Assert.False(CutsceneChart.AnyOtherPlayer(null, "Me"));
        Assert.Equal("sys1", CutsceneChart.SystemOfBody(map, "sys1-p1"));
    }

    [Fact]
    public void ManyMinds_ChartsTheOtherPlayers_AndAloneIsTheCardItWas()
    {
        var plan = Plan("b10");
        Assert.Equal(CutsceneTiers.Scene, plan.Tier);
        Assert.Contains(plan.Shots, s => s.Camera == CutsceneCameras.Chart && s.Anchor == CutsceneAnchors.Players);
        Assert.True(CutsceneRules.NeedsObject(CutsceneAnchors.Players));

        // Somebody else on the map: the live chart (a short scene; the world never holds in multiplayer).
        Assert.False(CutsceneRules.NeedsCardFallback(plan, _ => true));
        Assert.False(CutsceneRules.HoldsWorld(plan, cameraTaken: true, multiplayer: true));

        // Alone: the card — the same title and VEGA's words, input free, nothing held.
        Assert.True(CutsceneRules.NeedsCardFallback(plan, anchor => anchor != CutsceneAnchors.Players));
        var card = plan.AsCard();
        Assert.Equal(CutsceneTiers.Card, card.Tier);
        Assert.False(card.TakesCamera || card.HoldWorld || card.LockInput);
        var captions = card.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(("VEGA remembers", "Many minds"), (captions[0].Kicker, captions[0].Text));
        Assert.Equal(Localized("story.vega.beat10"), captions[1].Text);
        Assert.Contains("story.vega.beat10", card.ReplaceLineKeys);
    }

    // ---------------- The set pieces from the data pack ----------------

    [Fact]
    public void TheArrival_IsACard_TitledAsTheWarpClears_InBothLanguages()
    {
        var plan = Plan("fin.arrival");
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.False(plan.TakesCamera || plan.HoldWorld || plan.LockInput || plan.OpensFromBlack);
        var title = Assert.Single(plan.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Arrival", "The Guardian Core"), (title.Kicker, title.Text));
        Assert.True(title.At >= 2.6f); // after the hyperspace warp's 2.6 s
        var de = Assert.Single(Plan("fin.arrival", GameLocale.German).Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Ankunft", "Der Wächterkern"), (de.Kicker, de.Text));
    }

    [Fact]
    public void TheDescent_ShowsTheHint_WithItsKeyResolved_AndGivesItBackOnAnEarlySkip()
    {
        var plan = Plan("fin.descent");
        Assert.True(plan.TakesCamera && plan.OpensFromBlack);
        var hint = Assert.Single(plan.Shots.SelectMany(s => s.Captions), c => c.Style == CutsceneCaptionStyles.Speaker);
        Assert.DoesNotContain("{key:", hint.Text, StringComparison.Ordinal); // the one token resolver filled it
        Assert.Contains("PlanetMap", hint.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { "vega.hint.guardian_core" }, plan.ReturnOnSkip);
        Assert.Contains(plan.Shots, s => s.Camera == CutsceneCameras.Flyover && s.Anchor == CutsceneAnchors.Core);
    }

    [Fact]
    public void TheStanddownAndTheEnding_ReadAsTheyShould_AndTheEndingRollsTheCredits()
    {
        var standdown = Plan("fin.standdown");
        Assert.Equal(CutsceneTiers.SetPiece, standdown.Tier);
        var line = Assert.Single(standdown.Shots.SelectMany(s => s.Captions), c => c.Style == CutsceneCaptionStyles.Speaker);
        Assert.Equal(Localized("story.vega.finale_resolved"), line.Text);
        Assert.True(line.PageGated); // VEGA's words wait for the reader

        var ending = Plan("resolution", GameLocale.German);
        var captions = ending.Shots.SelectMany(s => s.Captions).ToList();
        var card = captions[0];
        Assert.Equal((Localized("story.vega.ch.resolution", GameLocale.German), Localized("story.vega_protocol.name", GameLocale.German)),
            (card.Kicker, card.Text));
        var credits = Assert.Single(captions, c => c.Style == CutsceneCaptionStyles.Credits);
        Assert.False(credits.PageGated); // the roll runs on its own
        var epilogue = Assert.Single(captions, c => c.Style == CutsceneCaptionStyles.ReadingFrame);
        Assert.Equal(("Das Relais-Netz", Localized("story.vega.beat13", GameLocale.German)), (epilogue.Kicker, epilogue.Text));
        Assert.True(ending.TakesCamera); // a set piece: the controls freeze, a singleplayer world holds while the camera is away
        Assert.Contains("story.vega.beat13", ending.ReplaceLineKeys);

        // On the low presets its synthetic shots fall back to the sky — never a card (the sky shot opens it).
        Assert.False(CutsceneRules.NeedsCardFallback(ending, _ => true, syntheticAllowed: false));
    }

    // ---------------- #2497: the ending listed once, a missed arrival, "plays when you land" ----------------

    [Fact]
    public void TheEnding_IsTheStanddownAndTheResolution_AndWatchAgainLeavesItOut()
    {
        Assert.Equal(new[] { "fin.standdown", StoryCutsceneIds.Resolution }, CutsceneCatalog.EndingIds(Pack));
        Assert.Empty(CutsceneCatalog.EndingIds(Content.Value.Stories["tutorial"]));
        Assert.Empty(CutsceneCatalog.EndingIds(null));

        var seen = new[] { StoryCutsceneIds.Prologue, "fin.arrival", "fin.standdown", StoryCutsceneIds.Resolution }
            .Select(id => CutsceneCatalog.SeenKey(V, id)).ToList();
        var all = CutsceneCatalog.ReplayEntries(new[] { Pack }, seen).Select(e => e.CutsceneId).ToList();
        Assert.Equal(new[] { StoryCutsceneIds.Prologue, "fin.arrival", "fin.standdown", StoryCutsceneIds.Resolution }, all);

        // While the Journal shows "Watch the ending again" (it replays both), "Watch again" lists neither.
        var listed = CutsceneCatalog.ReplayEntries(new[] { Pack }, seen, CutsceneCatalog.EndingKeys(Pack)).Select(e => e.CutsceneId).ToList();
        Assert.Equal(new[] { StoryCutsceneIds.Prologue, "fin.arrival" }, listed);
    }

    [Fact]
    public void AMissedArrival_LeavesItsTitleAsABanner_InBothLanguages()
    {
        var banner = Plan("fin.arrival").MissedBanner()!;
        Assert.Equal(CutsceneTiers.Banner, banner.Tier);
        var line = Assert.Single(banner.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Arrival", "The Guardian Core"), (line.Kicker, line.Text));
        var de = Assert.Single(Plan("fin.arrival", GameLocale.German).MissedBanner()!.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Ankunft", "Der Wächterkern"), (de.Kicker, de.Text));
    }

    [Fact]
    public void AReplayOfACameraScene_WaitsForTheLanding_ACardDoesNot()
    {
        var flying = new SafeMomentInputs { PilotingInSpace = true };
        var warping = new SafeMomentInputs { Warping = true };
        var eva = new SafeMomentInputs { InEva = true };
        var landed = new SafeMomentInputs();
        var resolution = Pack.FindCutscene(StoryCutsceneIds.Resolution);
        var prologue = Pack.FindCutscene(StoryCutsceneIds.Prologue);

        Assert.True(CutsceneRules.ReplayWaitsForLanding(resolution, flying, _ => true));
        Assert.True(CutsceneRules.ReplayWaitsForLanding(prologue, warping, _ => true));
        Assert.True(CutsceneRules.ReplayWaitsForLanding(prologue, eva, _ => true));
        Assert.False(CutsceneRules.ReplayWaitsForLanding(prologue, landed, _ => true));
        Assert.False(CutsceneRules.ReplayWaitsForLanding(Pack.FindCutscene("fin.arrival"), flying, _ => true)); // a card plays in flight
        // A scene that plays as a card there: only synthetic stages on a low preset, or its object (the heal tank) not found.
        Assert.False(CutsceneRules.ReplayWaitsForLanding(Pack.FindCutscene("fin.standdown"), flying, _ => true, syntheticAllowed: false));
        Assert.True(CutsceneRules.ReplayWaitsForLanding(Pack.FindCutscene("fin.standdown"), flying, _ => true, syntheticAllowed: true));
        Assert.False(CutsceneRules.ReplayWaitsForLanding(Pack.FindCutscene("ch5.open"), flying, _ => false));
        Assert.False(CutsceneRules.ReplayWaitsForLanding(null, flying, _ => true));
    }
}
