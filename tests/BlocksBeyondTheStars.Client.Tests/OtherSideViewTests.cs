// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's model of the other side (#3906): the state the server sent, the blend the look eases by, what gets a
/// veil and a sound, the gate book, the "may I start this?" questions and the filter on the use key's pick.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class OtherSideViewTests
{
    private static OtherSideState State(bool over, string reason, int used = 0, int cap = 2000)
        => new OtherSideState { Over = over, HasGate = over, GateX = 10.5f, GateY = 64f, GateZ = -3.5f, Reason = reason, SoundBlocksUsed = used, SoundBlockCap = cap };

    [Fact]
    public void TheJoin_SetsTheLookAtOnce_WithoutFanfare()
    {
        var view = new OtherSideView();
        Assert.Equal(OtherSideCue.None, view.Apply(State(false, OtherSideReasons.Sync)));
        Assert.False(view.Over);
        Assert.Equal(0f, view.Blend);

        // A state that says "over there" with a reason that is no step of the player's: the look is simply there.
        Assert.Equal(OtherSideCue.None, view.Apply(State(true, OtherSideReasons.Sync)));
        Assert.True(view.Over);
        Assert.Equal(1f, view.Blend);

        Assert.Equal(OtherSideCue.None, view.Apply(State(false, OtherSideReasons.Rejoined)));
        Assert.Equal(0f, view.Blend);

        // A reason this client does not know is the state only.
        Assert.Equal(OtherSideCue.None, view.Apply(State(true, "a-later-reason")));
        Assert.Equal(1f, view.Blend);
    }

    [Fact]
    public void ACrossing_EasesIn_AndAReturnEasesOut()
    {
        var view = new OtherSideView();
        view.Apply(State(false, OtherSideReasons.Sync));

        Assert.Equal(OtherSideCue.Cross, view.Apply(State(true, OtherSideReasons.Crossed)));
        Assert.True(view.Over);
        Assert.Equal(0f, view.Blend); // the look has not moved yet
        Assert.True(view.HasGate);
        Assert.Equal(10.5f, view.GateX);

        view.Tick(OtherSideView.BlendSeconds / 2f);
        Assert.Equal(0.5f, view.Blend, 3);
        Assert.Equal(0.5f, view.Look, 3); // smoothstep meets the line in the middle
        view.Tick(OtherSideView.BlendSeconds / 4f);
        Assert.True(view.Look > view.Blend); // … and runs ahead of it on the way out of the ease
        view.Tick(10f);
        Assert.Equal(1f, view.Blend);

        Assert.Equal(OtherSideCue.Return, view.Apply(State(false, OtherSideReasons.Returned)));
        Assert.False(view.HasGate); // the marker goes with the return
        view.Tick(OtherSideView.BlendSeconds / 2f);
        Assert.Equal(0.5f, view.Blend, 3);
        view.Tick(10f);
        Assert.Equal(0f, view.Blend);
    }

    [Theory]
    [InlineData(OtherSideReasons.Forced)]
    [InlineData(OtherSideReasons.Moved)]
    public void AForcedOrSilentReturn_EasesWithoutFanfare(string reason)
    {
        var view = new OtherSideView();
        view.Apply(State(true, OtherSideReasons.Crossed));
        view.Tick(10f);

        Assert.Equal(OtherSideCue.None, view.Apply(State(false, reason)));
        Assert.Equal(1f, view.Blend); // eases back, no snap
        view.Tick(10f);
        Assert.Equal(0f, view.Blend);
    }

    [Fact]
    public void TheAdminVerb_IsStagedLikeAGate()
    {
        var view = new OtherSideView();
        Assert.Equal(OtherSideCue.Cross, view.Apply(State(true, OtherSideReasons.Admin)));
        Assert.Equal(OtherSideCue.Return, view.Apply(State(false, OtherSideReasons.Admin)));
    }

    [Fact]
    public void ACount_MovesTheMeter_AndLeavesTheLookAlone()
    {
        var view = new OtherSideView();
        view.Apply(State(true, OtherSideReasons.Crossed));
        view.Tick(0.3f);
        float blend = view.Blend;
        int version = view.Version;

        Assert.Equal(OtherSideCue.None, view.Apply(State(true, OtherSideReasons.Sync, used: 7)));
        Assert.Equal(blend, view.Blend); // a sync in the middle of the ease does not snap it
        Assert.Equal(7, view.SoundBlocksUsed);
        Assert.Equal(1993, view.SoundBlocksLeft);
        Assert.False(view.AtSoundBlockCap);
        Assert.True(view.Version > version);

        view.Apply(State(true, OtherSideReasons.Sync, used: 2000));
        Assert.True(view.AtSoundBlockCap);
        Assert.Equal(0, view.SoundBlocksLeft);

        view.Apply(State(true, OtherSideReasons.Sync, used: 3, cap: 0)); // a server without the sound layer
        Assert.False(view.AtSoundBlockCap);
        Assert.Equal(0, view.SoundBlocksLeft);
    }

    [Fact]
    public void OverThere_OnlyTheSoundersThingsMayStart()
    {
        var view = new OtherSideView();
        Assert.True(view.MayMine && view.MayPlace && view.MayOpenContainers && view.MayAttack && view.MayUseDevices && view.MayUseItems);
        Assert.True(view.ShowsNeedWarnings);
        Assert.True(view.MayTalkTo(livesOverThere: false));
        Assert.False(view.ShapesSound(blasterInHand: true)); // on our side the blaster pushes

        view.Apply(State(true, OtherSideReasons.Crossed));
        Assert.False(view.MayMine);
        Assert.False(view.MayPlace);
        Assert.False(view.MayOpenContainers);
        Assert.False(view.MayAttack);
        Assert.False(view.MayUseDevices);
        Assert.False(view.MayUseItems);
        Assert.False(view.ShowsNeedWarnings);
        Assert.True(view.MayUseBlaster);
        Assert.True(view.MayUseRuneStone);
        Assert.True(view.MayPlayMelody);
        Assert.False(view.MayTalkTo(livesOverThere: false));
        Assert.True(view.MayTalkTo(livesOverThere: true));
        Assert.True(view.ShapesSound(blasterInHand: true));
        Assert.False(view.ShapesSound(blasterInHand: false));
    }

    [Fact]
    public void TheUseKey_OverThere_WorksRuneStonesAndThoseWhoLiveThere()
    {
        var view = new OtherSideView();
        var facts = new InteractFacts { Crate = true, AimedDoor = true, UsableBlock = true, AimedNpc = true, AimedNpcTrades = true, TalkNear = true, Vendor = true, LookedStation = "workshop" };

        view.FilterInteraction(facts, aimedBlockIsRuneStone: false, aimedNpcLivesOverThere: false, nearNpcLivesOverThere: false);
        Assert.Equal(InteractKind.AimedTrade, InteractResolver.Resolve(facts)); // our side: nothing is filtered

        view.Apply(State(true, OtherSideReasons.Crossed));
        view.FilterInteraction(facts, aimedBlockIsRuneStone: true, aimedNpcLivesOverThere: false, nearNpcLivesOverThere: false);
        Assert.Equal(InteractKind.UseBlock, InteractResolver.Resolve(facts)); // the rune stone; no crate, no door, no trader
        Assert.False(facts.Crate || facts.AimedDoor || facts.AimedNpc || facts.Vendor || facts.TalkNear);
        Assert.Equal(string.Empty, facts.LookedStation);

        // A usable block that is no rune stone (a Song Spindle) offers nothing over there.
        facts = new InteractFacts { UsableBlock = true, SpawnBed = true };
        view.FilterInteraction(facts, aimedBlockIsRuneStone: false, aimedNpcLivesOverThere: false, nearNpcLivesOverThere: false);
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(facts));

        // An Echo under the crosshair talks (never trades); a villager of our side does not.
        facts = new InteractFacts { AimedNpc = true, AimedNpcTrades = true };
        view.FilterInteraction(facts, aimedBlockIsRuneStone: false, aimedNpcLivesOverThere: true, nearNpcLivesOverThere: false);
        Assert.Equal(InteractKind.AimedTalk, InteractResolver.Resolve(facts));
        facts = new InteractFacts { AimedNpc = true, TalkNear = true };
        view.FilterInteraction(facts, aimedBlockIsRuneStone: false, aimedNpcLivesOverThere: false, nearNpcLivesOverThere: true);
        Assert.Equal(InteractKind.Talk, InteractResolver.Resolve(facts));
    }

    [Fact]
    public void TheGateBook_KeepsTheServersList_AndGoesDarkWithTheReturn()
    {
        var view = new OtherSideView();
        view.Apply(State(true, OtherSideReasons.Crossed));
        int version = view.Gates.Version;
        view.Gates.Apply(new OtherSideGates
        {
            Gates = new[]
            {
                new NetOtherSideGate { X = 1f, Y = 2f, Z = 3f, Entry = true },
                new NetOtherSideGate { X = float.NaN, Y = 2f, Z = 3f }, // a broken entry draws nothing
                new NetOtherSideGate { X = 40f, Y = 9f, Z = -7f, Cracked = true },
            },
        });

        Assert.Equal(2, view.Gates.Count);
        Assert.True(view.Gates.Version > version);
        Assert.True(view.Gates[0].Entry);
        Assert.False(view.Gates[0].Cracked);
        Assert.Equal(40f, view.Gates[1].X);
        Assert.True(view.Gates[1].Cracked);

        // The return: the last gates stay while the look eases back — their lights fade with it instead of popping off —
        // through the server's empty list too, and are gone when the look has arrived (whether or not that list came).
        view.Tick(10f);
        view.Apply(State(false, OtherSideReasons.Returned));
        Assert.Equal(2, view.Gates.Count);
        view.ApplyGates(new OtherSideGates());
        Assert.Equal(2, view.Gates.Count);
        view.Tick(OtherSideView.BlendSeconds / 2f);
        Assert.Equal(2, view.Gates.Count);
        Assert.InRange(view.Look, 0.01f, 0.99f); // what the lights are drawn with meanwhile
        view.Tick(OtherSideView.BlendSeconds);
        Assert.Equal(0f, view.Blend);
        Assert.Equal(0, view.Gates.Count);

        // A return the look does not ease for (the join's state) drops them at once; so does an empty list on our side.
        view.Apply(State(true, OtherSideReasons.Sync));
        view.ApplyGates(new OtherSideGates { Gates = new[] { new NetOtherSideGate { X = 1f } } });
        Assert.Equal(1, view.Gates.Count);
        view.Apply(State(false, OtherSideReasons.Rejoined));
        Assert.Equal(0, view.Gates.Count);
        view.ApplyGates(null);
        Assert.Equal(0, view.Gates.Count);

        // Over there the server's list replaces the book, an empty one included.
        view.Apply(State(true, OtherSideReasons.Crossed));
        view.ApplyGates(new OtherSideGates { Gates = new[] { new NetOtherSideGate { X = 1f }, new NetOtherSideGate { X = 2f } } });
        Assert.Equal(2, view.Gates.Count);
        view.ApplyGates(new OtherSideGates());
        Assert.Equal(0, view.Gates.Count);

        var many = new NetOtherSideGate[OtherSideGateBook.Capacity + 5];
        for (int i = 0; i < many.Length; i++)
        {
            many[i] = new NetOtherSideGate { X = i };
        }

        view.Gates.Apply(new OtherSideGates { Gates = many });
        Assert.Equal(OtherSideGateBook.Capacity, view.Gates.Count);
    }

    [Fact]
    public void Reset_IsOurSide_AtOnce()
    {
        var view = new OtherSideView();
        view.Apply(State(true, OtherSideReasons.Crossed, used: 12));
        view.Tick(10f);
        view.Gates.Apply(new OtherSideGates { Gates = new[] { new NetOtherSideGate { Entry = true } } });

        view.Reset();
        Assert.False(view.Over);
        Assert.Equal(0f, view.Blend);
        Assert.False(view.HasGate);
        Assert.Equal(0, view.SoundBlocksUsed);
        Assert.Equal(0, view.Gates.Count);
    }

    [Fact]
    public void TheLook_GoesToGreyscale_AndTheLightsFadeInWithIt()
    {
        Assert.Equal(6f, OtherSideLook.Saturation(6f, 0f));
        Assert.Equal(-100f, OtherSideLook.Saturation(6f, 1f));
        Assert.Equal(-100f, OtherSideLook.Saturation(-40f, 1f)); // a flashback's desaturation ends in the same grey
        Assert.Equal(-47f, OtherSideLook.Saturation(6f, 0.5f), 3);
        Assert.Equal(3f, OtherSideLook.Contrast(3f, 0f));
        Assert.Equal(3f + OtherSideLook.ContrastLift, OtherSideLook.Contrast(3f, 1f));
        Assert.Equal(100f, OtherSideLook.Contrast(95f, 1f));

        // A gate's light: nothing on our side, calm over there, the entry gate brighter and breathing.
        Assert.Equal(0f, OtherSideLook.PillarStrength(0f, entry: true, time: 3.0, reducedMotion: false));
        float other = OtherSideLook.PillarStrength(1f, entry: false, time: 3.0, reducedMotion: false);
        Assert.Equal(other, OtherSideLook.PillarStrength(1f, entry: false, time: 3.7, reducedMotion: false)); // steady
        float low = float.MaxValue, high = 0f;
        for (double t = 0; t < 6.0; t += 0.05)
        {
            float s = OtherSideLook.PillarStrength(1f, entry: true, time: t, reducedMotion: false);
            low = System.Math.Min(low, s);
            high = System.Math.Max(high, s);
        }

        Assert.True(low > other, "the entry gate is always the brightest");
        Assert.True(high - low > 0.2f, "the entry gate breathes");
        Assert.Equal(1f, OtherSideLook.PillarStrength(1f, entry: true, time: 1.3, reducedMotion: true));

        // Far gates are drawn wider, so they stay a line.
        Assert.Equal(OtherSideLook.PillarWidth, OtherSideLook.PillarWidthAt(5f));
        Assert.Equal(OtherSideLook.PillarWidth * 2f, OtherSideLook.PillarWidthAt(140f), 3);
        Assert.Equal(OtherSideLook.PillarWidth * 6f, OtherSideLook.PillarWidthAt(4000f));
    }
}
