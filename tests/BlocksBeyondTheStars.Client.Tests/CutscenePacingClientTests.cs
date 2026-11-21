// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3686 — story scenes played late and at the wrong place, the client's half: the place-bound Sounders and tutorial scenes
/// (<c>playInPlace</c>, from the shipped data) start away from the ship as soon as the moment is safe — no five-minute wait
/// for "home" — while danger still holds them; and the client queue spaces two scenes of the same story by 15 s (the
/// server's own spacing), two stories by the full 90 s.
/// </summary>
public sealed class CutscenePacingClientTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    /// <summary>The gate's request for a scene of the shipped data, built the way the director builds it.</summary>
    private static SafeMomentRequest RequestFor(string pack, string id, float waited)
    {
        var def = Content.Value.Stories[pack].FindCutscene(id)!;
        return new SafeMomentRequest
        {
            Tier = def.Tier,
            TakesCamera = def.TakesCamera,
            NowOrNever = def.Options.CooldownExempt,
            PlayInPlace = def.Options.PlayInPlace,
            WaitedSeconds = waited,
        };
    }

    [Theory]
    [InlineData("sounders", "snd.stones_sing")]
    [InlineData("sounders", "snd.shrine_answers")]
    [InlineData("sounders", "snd.first_echo")]
    [InlineData("sounders", "snd.great_song")]
    [InlineData("sounders", "snd.song_of_return")]
    [InlineData("sounders", "snd.landing")]
    [InlineData("sounders", "snd.elder")]
    [InlineData("sounders", "snd.choir_tower")]
    [InlineData("sounders", "snd.bridge_song")]
    [InlineData("sounders", "snd.soundi_joins")]
    [InlineData("sounders", "snd.threshold")]
    [InlineData("sounders", "snd.other_song")]
    [InlineData("tutorial", "tut.first_base")]
    [InlineData("tutorial", "tut.first_landing")]
    public void APlaceBoundScene_PlaysAwayFromHome_AtOnce_WhenSafe(string pack, string id)
    {
        var request = RequestFor(pack, id, waited: 1f);
        Assert.True(request.TakesCamera && request.Tier >= CutsceneTiers.Scene, id); // the camera scenes the wait applied to
        var away = new SafeMomentInputs { AtHome = false };
        Assert.Equal(SafeMomentVerdict.Play, SafeMomentGate.Evaluate(request, away).Verdict);

        away.HostileDistance = 8f; // danger still holds it
        Assert.Equal(SafeMomentGate.ReasonHostile, SafeMomentGate.Evaluate(request, away).Reason);
    }

    [Fact]
    public void ASceneWithoutPlayInPlace_StillPrefersHome()
    {
        // The ship's commissioning happens at the new ship — it keeps the "at home" preference.
        var request = RequestFor("tutorial", "tut.ship_commissioned", waited: 1f);
        var decision = SafeMomentGate.Evaluate(request, new SafeMomentInputs { AtHome = false });
        Assert.Equal(SafeMomentVerdict.Defer, decision.Verdict);
        Assert.Equal(SafeMomentGate.ReasonPreferHome, decision.Reason);
    }

    [Fact]
    public void TheQueue_SpacesOneStorysScenesBy15Seconds_AndTwoStoriesBy90()
    {
        var q = new CutsceneQueue();
        q.Enqueue(new QueuedCutscene("sounders", "snd.landing", CutsceneTiers.Scene));
        q.Start(q.Items[0]);
        q.Finish(100f);

        var elder = new QueuedCutscene("sounders", "snd.elder", CutsceneTiers.Scene);
        var vega = new QueuedCutscene(StoryRegistry.DefaultStoryId, "b07", CutsceneTiers.Card);
        q.Enqueue(elder);
        q.Enqueue(vega);
        Assert.Empty(q.Candidates(100f + CutsceneQueue.SamePackCooldownSeconds - 1f));
        Assert.Equal(new[] { "snd.elder" }, q.Candidates(100f + CutsceneQueue.SamePackCooldownSeconds).Select(i => i.Id));

        q.Start(elder);
        q.Finish(200f);
        Assert.False(q.MayGoNow(vega, 200f + CutsceneQueue.CooldownSeconds - 1f)); // another story: the full cooldown
        Assert.True(q.MayGoNow(vega, 200f + CutsceneQueue.CooldownSeconds));
    }
}
