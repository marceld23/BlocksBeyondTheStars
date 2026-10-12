// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2468 — when a cutscene may start (<see cref="SafeMomentGate"/>: every unsafe reason, banners vs cards vs camera
/// scenes, "prefer at home", "now or never", giving up, the danger interrupt and the multiplayer camera cap) and in
/// which order (<see cref="CutsceneQueue"/>: one at a time, dedupe, requests first, the 90 s cooldown, chains).
/// </summary>
public sealed class SafeMomentGateTests
{
    private static SafeMomentInputs Calm() => new() { AtHome = true };

    private static SafeMomentRequest Ask(int tier, bool camera = false, bool nowOrNever = false, bool requested = false, float waited = 0f)
        => new() { Tier = tier, TakesCamera = camera, NowOrNever = nowOrNever, Requested = requested, WaitedSeconds = waited };

    [Theory]
    [InlineData(CutsceneTiers.Banner, false)]
    [InlineData(CutsceneTiers.Card, false)]
    [InlineData(CutsceneTiers.Scene, true)]
    [InlineData(CutsceneTiers.SetPiece, true)]
    public void ACalmMoment_PlaysEveryTier(int tier, bool camera)
        => Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(tier, camera), Calm()).Verdict);

    /// <summary>Puts the player into one named situation.</summary>
    private static SafeMomentInputs With(string situation)
    {
        var s = Calm();
        switch (situation)
        {
            case "veil": s.LoadingVeil = true; break;
            case "scene": s.AnotherScene = true; break;
            case "observer": s.Spectating = true; break;
            case "menu": s.MenuOpen = true; break;
            case "respawn": s.RespawnPrompt = true; break;
            case "hostile": s.HostileDistance = 12f; break;
            case "damage": s.SecondsSinceDamage = 3f; break;
            case "space_combat": s.SecondsSinceSpaceCombat = 5f; break;
            case "health": s.Health = 0.2f; break;
            case "oxygen": s.Oxygen = 0.1f; break;
            case "chat": s.ChatTyping = true; break;
            case "duel": s.DuelOpen = true; break;
            case "settling": s.Settling = true; break;
            case "falling": s.Falling = true; break;
            case "climbing": s.Climbing = true; break;
            case "underwater": s.HeadUnderwater = true; break;
            case "vehicle": s.InVehicle = true; break;
            case "eva": s.InEva = true; break;
            case "piloting": s.PilotingInSpace = true; break;
            case "warp": s.Warping = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(situation), situation, null);
        }

        return s;
    }

    [Theory]
    [InlineData("veil", SafeMomentGate.ReasonLoading)]
    [InlineData("scene", SafeMomentGate.ReasonScene)]
    [InlineData("observer", SafeMomentGate.ReasonObserver)]
    [InlineData("menu", SafeMomentGate.ReasonMenu)]
    [InlineData("respawn", SafeMomentGate.ReasonRespawn)]
    public void ABusyScreen_HoldsBackEvenABanner(string situation, string reason)
    {
        var s = With(situation);
        foreach (int tier in new[] { CutsceneTiers.Banner, CutsceneTiers.Card, CutsceneTiers.Scene })
        {
            var d = SafeMomentGate.Evaluate(Ask(tier, camera: tier >= CutsceneTiers.Scene), s);
            Assert.Equal(SafeMomentVerdict.Defer, d.Verdict);
            Assert.Equal(reason, d.Reason);
        }
    }

    [Theory]
    [InlineData("hostile", SafeMomentGate.ReasonHostile)]
    [InlineData("damage", SafeMomentGate.ReasonDamage)]
    [InlineData("space_combat", SafeMomentGate.ReasonSpaceCombat)]
    [InlineData("health", SafeMomentGate.ReasonVitals)]
    [InlineData("oxygen", SafeMomentGate.ReasonVitals)]
    [InlineData("chat", SafeMomentGate.ReasonChat)]
    [InlineData("duel", SafeMomentGate.ReasonDuel)]
    [InlineData("settling", SafeMomentGate.ReasonSettling)]
    public void DangerAndBusyHands_DeferCardsAndScenes_ButNotABanner(string situation, string reason)
    {
        var s = With(situation);
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Banner), s).Verdict);
        var card = SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card), s);
        Assert.Equal(SafeMomentVerdict.Defer, card.Verdict);
        Assert.Equal(reason, card.Reason);
        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true), s).Verdict);
    }

    [Fact]
    public void TheEdgesOfTheDangerWindows_AreSafe()
    {
        var s = Calm();
        s.HostileDistance = SafeMomentGate.HostileRadius + 0.1f;
        s.SecondsSinceDamage = SafeMomentGate.DamageWindow;
        s.SecondsSinceSpaceCombat = SafeMomentGate.SpaceCombatWindow;
        s.Health = SafeMomentGate.LowVital;
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card), s).Verdict);
    }

    [Theory]
    [InlineData("falling")]
    [InlineData("climbing")]
    [InlineData("underwater")]
    [InlineData("vehicle")]
    [InlineData("eva")]
    [InlineData("piloting")]
    [InlineData("warp")]
    public void Moving_DefersOnlyAScene_ThatTakesTheCamera(string situation)
    {
        var s = With(situation);
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card), s).Verdict);
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: false), s).Verdict);
        var scene = SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true), s);
        Assert.Equal(SafeMomentVerdict.Defer, scene.Verdict);
        Assert.Equal(SafeMomentGate.ReasonMoving, scene.Reason);
    }

    [Fact]
    public void ACameraScene_PrefersThePlayerAtHome_UntilTheRelaxTime()
    {
        var away = new SafeMomentInputs { AtHome = false };
        var early = SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, waited: 10f), away);
        Assert.Equal(SafeMomentVerdict.Defer, early.Verdict);
        Assert.Equal(SafeMomentGate.ReasonPreferHome, early.Reason);
        Assert.Equal(SafeMomentVerdict.Play,
            SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, waited: SafeMomentGate.PreferAtHomeSeconds), away).Verdict);
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, requested: true), away).Verdict);
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card), away).Verdict); // a card plays anywhere
    }

    [Fact]
    public void NowOrNever_AbortsInsteadOfWaiting_ButABannerOrARequestWaits()
    {
        var s = Calm();
        s.MenuOpen = true;
        var d = SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card, nowOrNever: true), s);
        Assert.Equal(SafeMomentVerdict.Abort, d.Verdict);
        Assert.Equal(SafeMomentGate.ReasonMenu, d.Reason);
        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Banner, nowOrNever: true), s).Verdict);
        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card, nowOrNever: true, requested: true), s).Verdict);

        s.MenuOpen = false;
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card, nowOrNever: true), s).Verdict);
    }

    [Fact]
    public void AWaitingScene_GivesUpAfterItsTime_ButARequestNever()
    {
        var s = Calm();
        s.HostileDistance = 5f;
        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card, waited: SafeMomentGate.CardGiveUpSeconds - 1f), s).Verdict);
        var card = SafeMomentGate.Evaluate(Ask(CutsceneTiers.Card, waited: SafeMomentGate.CardGiveUpSeconds), s);
        Assert.Equal(SafeMomentVerdict.Abort, card.Verdict);
        Assert.Equal(SafeMomentGate.ReasonGaveUp, card.Reason);

        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, waited: SafeMomentGate.CardGiveUpSeconds), s).Verdict);
        Assert.Equal(SafeMomentVerdict.Abort, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, waited: SafeMomentGate.SceneGiveUpSeconds), s).Verdict);
        Assert.Equal(SafeMomentVerdict.Defer, SafeMomentGate.Evaluate(Ask(CutsceneTiers.Scene, camera: true, requested: true, waited: 99999f), s).Verdict);
    }

    [Fact]
    public void DangerWhilePlaying_EndsTheScene_AHostileOnlyWhenTheWorldRuns()
    {
        var s = Calm();
        Assert.False(SafeMomentGate.ShouldInterrupt(s, secondsPlaying: 5f, worldHeld: false));

        s.SecondsSinceDamage = 2f; // hit two seconds ago, the scene runs for five
        Assert.True(SafeMomentGate.ShouldInterrupt(s, 5f, worldHeld: true));
        Assert.False(SafeMomentGate.ShouldInterrupt(s, 1f, worldHeld: false)); // the hit came before the scene

        s = Calm();
        s.SecondsSinceSpaceCombat = 0.5f;
        Assert.True(SafeMomentGate.ShouldInterrupt(s, 3f, worldHeld: false));

        s = Calm();
        s.HostileDistance = 8f;
        Assert.True(SafeMomentGate.ShouldInterrupt(s, 3f, worldHeld: false));
        Assert.False(SafeMomentGate.ShouldInterrupt(s, 3f, worldHeld: true)); // a held world does not move
    }

    [Fact]
    public void TheCameraCap_AppliesOnlyInMultiplayer()
    {
        Assert.False(SafeMomentGate.CameraCapReached(multiplayer: false, 600f));
        Assert.False(SafeMomentGate.CameraCapReached(multiplayer: true, SafeMomentGate.CameraCapSeconds - 0.1f));
        Assert.True(SafeMomentGate.CameraCapReached(multiplayer: true, SafeMomentGate.CameraCapSeconds));
    }

    // ---------------- The queue ----------------

    private static QueuedCutscene Q(string id, int tier = CutsceneTiers.Card, string pack = "p", bool replay = false, bool local = false,
        bool exempt = false, params string[] chain)
        => new(pack, id, tier) { Replay = replay, Local = local, CooldownExempt = exempt, ChainNext = chain };

    [Fact]
    public void AScene_IsQueuedOnce_WhileWaitingOrPlaying()
    {
        var q = new CutsceneQueue();
        Assert.True(q.Enqueue(Q("a")));
        Assert.False(q.Enqueue(Q("a")));
        Assert.True(q.Enqueue(Q("a", pack: "other")));
        q.Start(q.Items[0]);
        Assert.False(q.Enqueue(Q("a"))); // still playing
        Assert.True(q.Contains("p", "a"));
        q.Finish(0f);
        Assert.True(q.Enqueue(Q("a"))); // a new moment later on
    }

    [Fact]
    public void Requests_GoAheadOfTheStorysScenes_InTheirOwnOrder()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("story1"));
        q.Enqueue(Q("replay1", replay: true));
        q.Enqueue(Q("story2"));
        q.Enqueue(Q("dev", local: true));
        Assert.Equal(new[] { "replay1", "dev", "story1", "story2" }, q.Items.Select(i => i.Id));
    }

    [Fact]
    public void NothingStarts_WhileAScenePlays_NotEvenABanner()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("card"));
        q.Enqueue(Q("banner", CutsceneTiers.Banner));
        q.Start(q.Items[0]);
        Assert.Empty(q.Candidates(0f));
        q.Finish(1f);
        Assert.Equal(new[] { "banner" }, q.Candidates(1f).Select(i => i.Id));
    }

    [Fact]
    public void AfterACard_TheNextWaitsForTheCooldown_ButBannersAndNowOrNeverDoNot()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("first"));
        q.Start(q.Items[0]);
        q.Finish(10f);
        q.Enqueue(Q("second"));
        q.Enqueue(Q("banner", CutsceneTiers.Banner));
        q.Enqueue(Q("arrival", exempt: true));

        Assert.Equal(new[] { "banner", "arrival" }, q.Candidates(20f).Select(i => i.Id)); // the card behind blocks neither
        Assert.False(q.CooldownOver(10f + CutsceneQueue.CooldownSeconds - 1f));
        Assert.Contains(q.Candidates(10f + CutsceneQueue.CooldownSeconds), i => i.Id == "second");
    }

    [Fact]
    public void AChain_LiftsTheCooldown_ForTheNamedFollowerOnly()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("prologue", CutsceneTiers.Scene, "vega_protocol", chain: new[] { "tutorial/tut.welcome", "ch1.open", "__recap" }));
        q.Start(q.Items[0]);
        q.Finish(5f);
        q.Enqueue(Q("tut.welcome", pack: "tutorial"));
        q.Enqueue(Q("ch1.open", pack: "vega_protocol"));
        q.Enqueue(Q("__recap", pack: "vega_protocol"));
        q.Enqueue(Q("ch2.open", pack: "vega_protocol"));
        Assert.Equal(new[] { "tut.welcome", "ch1.open", "__recap" }, q.Candidates(6f).Select(i => i.Id));

        q.Start(q.Items[0]); // the follower uses the chain up
        q.Finish(10f);
        Assert.Empty(q.Candidates(11f));
    }

    /// <summary>#2470 (synthesis §5.3 "Spurstart-Folge"): "The story so far" leads straight into its pack's next chapter
    /// scene — the current chapter's opening at a story-track start — not into the intro, not into another pack's scene.</summary>
    [Fact]
    public void TheRecap_LeadsIntoItsPacksNextChapterScene_Only()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("__recap", pack: "vega_protocol"));
        q.Start(q.Items[0]);
        q.Finish(5f);
        var opening = Q("ch4.open", pack: "vega_protocol");
        opening.ChapterScene = true;
        q.Enqueue(opening);
        q.Enqueue(Q("prologue", CutsceneTiers.Scene, "vega_protocol")); // the intro: no chapter scene
        q.Enqueue(Q("tut.strip@b3", pack: "tutorial"));
        Assert.Equal(new[] { "ch4.open" }, q.Candidates(6f).Select(i => i.Id));
    }

    [Fact]
    public void ATemplateInstance_IsChainedByItsDefinitionId()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("welcome", chain: new[] { "strip" }));
        q.Start(q.Items[0]);
        q.Finish(0f);
        q.Enqueue(Q("strip@one"));
        Assert.Single(q.Candidates(1f));
    }

    [Fact]
    public void RequestsAndBanners_LeaveTheStorysPacingAlone()
    {
        var q = new CutsceneQueue();
        q.Enqueue(Q("replay", replay: true));
        q.Start(q.Items[0]);
        q.Finish(3f);
        q.Enqueue(Q("banner", CutsceneTiers.Banner));
        q.Start(q.Items[0]);
        q.Finish(4f);
        Assert.True(q.CooldownOver(4f)); // neither started the cooldown
        q.Enqueue(Q("story"));
        Assert.Single(q.Candidates(4f));
        Assert.Equal("p/story", q.Items[0].Key);
        Assert.Equal("p/x", QueuedCutscene.Qualify("p", "x"));
        Assert.Equal("q/x", QueuedCutscene.Qualify("p", "q/x"));
        Assert.Equal(StoryCutsceneIds.Recap, QueuedCutscene.Qualify("p", StoryCutsceneIds.Recap));
    }
}
