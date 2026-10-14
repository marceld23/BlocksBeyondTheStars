// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2592 — the NPC voice budget (<see cref="NpcVoiceGate"/>, maintainer requirement 2026-10-14: "never an annoying jumble
/// when many NPCs stand close together"): one babble at a time with priorities, audible radii, crowd damping, one reaction
/// at a time with its own gaps, VEGA and cutscenes first, and a whole station deck of 30 chatty NPCs simulated for a minute.
/// </summary>
public sealed class NpcVoiceGateTests
{
    private const float Babble = 3.2f; // two clips
    private const float Reaction = 1f;

    [Fact]
    public void OnlyOneBabble_PlaysAtATime_AnEqualPriorityIsDropped()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestBabble(1, NpcVoicePriority.Greeting, 0f, Babble, 3f, false, false).Granted);
        Assert.False(gate.RequestBabble(2, NpcVoicePriority.Greeting, 0.5f, Babble, 2f, false, false).Granted);
        Assert.False(gate.RequestBabble(3, NpcVoicePriority.Chatter, 1f, Babble, 1f, false, false).Granted);
        Assert.Equal(1, gate.BabbleNpc(1f));

        // Once the babble is over the channel is free again — nothing was queued.
        Assert.False(gate.BabbleBusy(Babble + 0.01f));
        Assert.Equal(NpcVoiceGate.NoNpc, gate.BabbleNpc(Babble + 0.01f));
        Assert.True(gate.RequestBabble(2, NpcVoicePriority.Greeting, Babble + 0.01f, Babble, 2f, false, false).Granted);
    }

    [Fact]
    public void AHigherPriority_CutsTheCurrentBabble_AndTheAddressedNpcAlwaysWins()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestBabble(1, NpcVoicePriority.Chatter, 0f, Babble, 4f, false, false).Granted);

        var greeting = gate.RequestBabble(2, NpcVoicePriority.Greeting, 0.4f, Babble, 6f, false, false);
        Assert.True(greeting.Granted);
        Assert.Equal(1, greeting.CutNpcId);

        var addressed = gate.RequestBabble(3, NpcVoicePriority.Addressed, 0.8f, Babble, 15f, false, false);
        Assert.True(addressed.Granted);
        Assert.Equal(2, addressed.CutNpcId);
        Assert.Equal(NpcVoicePriority.Addressed, gate.BabblePriority);

        // Nothing cuts the addressed NPC — not even another greeting right next to the player.
        Assert.False(gate.RequestBabble(4, NpcVoicePriority.Greeting, 1f, Babble, 1f, false, false).Granted);
    }

    [Fact]
    public void TheSpeakingNpc_MayStartItsNextLine_AfterItsOwnOneSecondGap()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestBabble(7, NpcVoicePriority.Addressed, 0f, 4.8f, 2f, false, false).Granted);
        Assert.False(gate.RequestBabble(7, NpcVoicePriority.Addressed, 0.5f, Babble, 2f, false, false).Granted); // < 1 s
        var answer = gate.RequestBabble(7, NpcVoicePriority.Addressed, 1.2f, Babble, 2f, false, false);
        Assert.True(answer.Granted); // the dialog's answer replaces the NPC's own babble
        Assert.Equal(NpcVoiceGate.NoNpc, answer.CutNpcId);
        Assert.False(gate.RequestBabble(7, NpcVoicePriority.Chatter, 2.5f, Babble, 2f, false, false).Granted); // lower: no
    }

    [Theory]
    [InlineData(NpcVoicePriority.Greeting, 11.9f, true)]
    [InlineData(NpcVoicePriority.Greeting, 12.1f, false)]
    [InlineData(NpcVoicePriority.Chatter, 12.1f, false)]
    [InlineData(NpcVoicePriority.Addressed, 19.9f, true)]
    [InlineData(NpcVoicePriority.Addressed, 20.1f, false)]
    public void ABabble_IsAudibleOnlyWithinItsRadius(NpcVoicePriority priority, float distance, bool granted)
        => Assert.Equal(granted, new NpcVoiceGate().RequestBabble(1, priority, 0f, Babble, distance, false, false).Granted);

    [Fact]
    public void AReaction_IsAudibleOnlyWithin10Metres()
    {
        Assert.True(new NpcVoiceGate().RequestReaction(1, NpcReaction.Trade, 0f, Reaction, 9.9f, false, false).Granted);
        Assert.False(new NpcVoiceGate().RequestReaction(1, NpcReaction.Trade, 0f, Reaction, 10.1f, false, false).Granted);
    }

    [Theory]
    [InlineData(NpcGreetingKinds.Addressed, NpcVoicePriority.Addressed)]
    [InlineData(NpcGreetingKinds.Passing, NpcVoicePriority.Greeting)]
    [InlineData(NpcGreetingKinds.Welcome, NpcVoicePriority.Greeting)]
    [InlineData(NpcGreetingKinds.Guest, NpcVoicePriority.Greeting)]
    [InlineData(NpcGreetingKinds.Chatter, NpcVoicePriority.Chatter)]
    [InlineData(NpcGreetingKinds.Cheer, NpcVoicePriority.Chatter)]
    [InlineData((byte)99, NpcVoicePriority.Greeting)]
    public void EachBubbleKind_HasItsPriority(byte kind, NpcVoicePriority priority)
        => Assert.Equal(priority, NpcVoiceGate.PriorityOf(kind));

    [Fact]
    public void ACrowd_IsMoreThanSixNpcsAround()
    {
        Assert.False(NpcVoiceGate.IsCrowd(6));
        Assert.True(NpcVoiceGate.IsCrowd(7));
    }

    [Fact]
    public void InACrowd_GreetingsShareOneSixSecondGap_AndPlayQuieter()
    {
        var gate = new NpcVoiceGate();
        var first = gate.RequestBabble(1, NpcVoicePriority.Greeting, 0f, 1.6f, 4f, crowd: true, narratorBusy: false);
        Assert.True(first.Granted);
        Assert.Equal(NpcVoiceGate.CrowdGreetingVolume, first.Volume, 4);

        Assert.False(gate.RequestBabble(2, NpcVoicePriority.Greeting, 3f, 1.6f, 4f, true, false).Granted);  // channel free, gap not
        Assert.False(gate.RequestBabble(3, NpcVoicePriority.Chatter, 5f, 1.6f, 2f, true, false).Granted);   // shared gap
        Assert.True(gate.RequestBabble(2, NpcVoicePriority.Greeting, 6.1f, 1.6f, 4f, true, false).Granted);

        // The same greeting outside a crowd plays at full volume.
        Assert.Equal(1f, new NpcVoiceGate().RequestBabble(1, NpcVoicePriority.Greeting, 0f, 1.6f, 4f, false, false).Volume, 4);
    }

    [Fact]
    public void InACrowd_OnlyTheConversationNearestThePlayerBabbles()
    {
        var gate = new NpcVoiceGate();

        // A near conversation (3 m) heard first: a farther one (9 m) stays silent even once the gap has passed.
        Assert.True(gate.RequestBabble(10, NpcVoicePriority.Chatter, 0f, 1.6f, 3f, true, false).Granted);
        Assert.False(gate.RequestBabble(20, NpcVoicePriority.Chatter, 6.5f, 1.6f, 9f, true, false).Granted);

        // The near conversation's partner (3.5 m from its speaker) still may.
        Assert.True(gate.RequestBabble(11, NpcVoicePriority.Chatter, 7f, 1.6f, 5.5f, true, false).Granted);

        // Once the near conversation is over (nothing heard for 10 s), the farther one is the nearest.
        float later = 7f + NpcVoiceGate.ChatterMemorySeconds + 0.5f;
        Assert.True(gate.RequestBabble(20, NpcVoicePriority.Chatter, later, 1.6f, 9f, true, false).Granted);
    }

    [Fact]
    public void Reactions_NeverOverlapABabble_OrEachOther()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestBabble(1, NpcVoicePriority.Greeting, 0f, Babble, 3f, false, false).Granted);
        Assert.False(gate.RequestReaction(2, NpcReaction.Trade, 1f, Reaction, 2f, false, false).Granted); // babble plays

        Assert.True(gate.RequestReaction(2, NpcReaction.Trade, Babble + 0.1f, Reaction, 2f, false, false).Granted);
        Assert.False(gate.RequestReaction(3, NpcReaction.Alert, Babble + 0.5f, Reaction, 2f, false, false).Granted); // one at a time
        Assert.True(gate.ReactionBusy(Babble + 0.5f));

        // A babble cuts a reaction that plays.
        var cut = gate.RequestBabble(4, NpcVoicePriority.Chatter, Babble + 0.6f, Babble, 3f, false, false);
        Assert.True(cut.Granted);
        Assert.True(cut.CutReaction);
        Assert.False(gate.ReactionBusy(Babble + 0.6f));
    }

    [Fact]
    public void AnAlarm_SoundsAtMostEveryEightSeconds()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestReaction(1, NpcReaction.Alert, 0f, Reaction, 3f, false, false).Granted);
        Assert.False(gate.RequestReaction(2, NpcReaction.Alert, 5f, Reaction, 3f, false, false).Granted);
        Assert.True(gate.RequestReaction(2, NpcReaction.Alert, 8.1f, Reaction, 3f, false, false).Granted);
    }

    [Fact]
    public void IdleMurmurs_AreRare_QuietAfterSpeech_AndOffInACrowd()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestReaction(1, NpcReaction.Idle, 0f, Reaction, 3f, false, false).Granted);
        Assert.False(gate.RequestReaction(2, NpcReaction.Idle, 15f, Reaction, 3f, false, false).Granted); // < 20 s
        Assert.True(gate.RequestReaction(2, NpcReaction.Idle, 20.5f, Reaction, 3f, false, false).Granted);

        // Not within 5 s of a babble's end (the 20-s gap since the last murmur has long passed).
        Assert.True(gate.RequestBabble(3, NpcVoicePriority.Greeting, 50f, 1.6f, 3f, false, false).Granted);
        Assert.False(gate.RequestReaction(1, NpcReaction.Idle, 51.6f + 4f, Reaction, 3f, false, false).Granted);
        Assert.True(gate.RequestReaction(1, NpcReaction.Idle, 51.6f + 5.1f, Reaction, 3f, false, false).Granted);

        // Never in a crowd.
        Assert.False(new NpcVoiceGate().RequestReaction(1, NpcReaction.Idle, 0f, Reaction, 3f, crowd: true, narratorBusy: false).Granted);
    }

    [Fact]
    public void WhileVegaSpeaks_OnlyTheAddressedNpcBabbles_AtHalfVolume()
    {
        var gate = new NpcVoiceGate();
        Assert.False(gate.RequestBabble(1, NpcVoicePriority.Greeting, 0f, Babble, 3f, false, narratorBusy: true).Granted);
        Assert.False(gate.RequestBabble(2, NpcVoicePriority.Chatter, 0f, Babble, 3f, false, narratorBusy: true).Granted);
        Assert.False(gate.RequestReaction(3, NpcReaction.Trade, 0f, Reaction, 3f, false, narratorBusy: true).Granted);
        Assert.False(gate.RequestReaction(3, NpcReaction.Alert, 0f, Reaction, 3f, false, narratorBusy: true).Granted);

        var addressed = gate.RequestBabble(4, NpcVoicePriority.Addressed, 0f, Babble, 3f, false, narratorBusy: true);
        Assert.True(addressed.Granted);
        Assert.Equal(NpcVoiceGate.NarratorAddressedVolume, addressed.Volume, 4);

        Assert.Equal(1f, NpcVoiceGate.NarratorDuck(NpcVoicePriority.Greeting, false));
        Assert.Equal(0f, NpcVoiceGate.NarratorDuck(NpcVoicePriority.Greeting, true));
        Assert.Equal(0.5f, NpcVoiceGate.NarratorDuck(NpcVoicePriority.Addressed, true));
    }

    [Fact]
    public void EndingABabbleEarly_FreesTheChannel_ForgetDoesToo()
    {
        var gate = new NpcVoiceGate();
        Assert.True(gate.RequestBabble(1, NpcVoicePriority.Chatter, 0f, 4.8f, 3f, false, false).Granted);
        gate.EndBabble(2, 0.5f); // another NPC: no effect
        Assert.True(gate.BabbleBusy(0.6f));
        gate.EndBabble(1, 0.7f);
        Assert.False(gate.BabbleBusy(0.7f));

        Assert.True(gate.RequestBabble(3, NpcVoicePriority.Chatter, 1f, 4.8f, 3f, false, false).Granted);
        gate.Forget(3, 1.5f);
        Assert.False(gate.BabbleBusy(1.5f));

        gate.Reset();
        Assert.True(gate.RequestBabble(1, NpcVoicePriority.Chatter, 1.6f, 4.8f, 3f, false, false).Granted); // per-NPC gap forgotten
    }

    /// <summary>
    /// A space station deck: 30 NPCs between 1.5 and 14 m around the player for a minute — passing greetings every
    /// couple of seconds, three conversations with a line every 3.6 s each, every NPC's own idle murmur, the player
    /// talking to a vendor twice and VEGA speaking for six seconds. Never more than one babble and one reaction at a
    /// time, a sane number of babbles, the addressed vendor always heard, nothing else while VEGA speaks.
    /// </summary>
    [Fact]
    public void AStationDeckOf30Npcs_NeverBecomesAJumble()
    {
        var gate = new NpcVoiceGate();
        var rng = new Random(2589);
        const int npcs = 30;
        var distance = new float[npcs + 1];
        var nextIdle = new float[npcs + 1];
        for (int id = 1; id <= npcs; id++)
        {
            distance[id] = 1.5f + (float)rng.NextDouble() * 12.5f;
            nextIdle[id] = NpcVoice.IdleGapSeconds(id, 0) * (float)rng.NextDouble();
        }

        int within = distance.Skip(1).Count(d => d <= NpcVoiceGate.CrowdRadius);
        bool crowd = NpcVoiceGate.IsCrowd(within);
        Assert.True(crowd);

        // Three conversations: pairs of NPCs, alternating every 3.6 s, staggered.
        var talks = new[] { (A: 3, B: 4, Start: 0.5f), (A: 10, B: 11, Start: 1.7f), (A: 20, B: 21, Start: 2.9f) };
        const int vendor = 7;
        var addressedAt = new[] { 20f, 40f };
        const float vegaFrom = 28f, vegaTo = 34f;

        float babbleEnd = float.NegativeInfinity, reactionEnd = float.NegativeInfinity;
        int babbleNpc = NpcVoiceGate.NoNpc;
        int babbles = 0, nonAddressed = 0, addressedHeard = 0, reactions = 0;
        float nextPassing = 0.3f;

        void Babble(int id, NpcVoicePriority priority, float now, int length)
        {
            float seconds = NpcVoice.ClipCount(length) * NpcVoice.ClipSeconds;
            bool vega = now >= vegaFrom && now < vegaTo;
            var grant = gate.RequestBabble(id, priority, now, seconds, distance[id], crowd, vega);
            if (!grant.Granted)
            {
                return;
            }

            Assert.True(!vega || priority == NpcVoicePriority.Addressed, $"t={now}: babble over VEGA");
            if (now < babbleEnd)
            {
                // Something still plays: the grant must cut exactly that one (else two would overlap).
                Assert.True(grant.CutNpcId == babbleNpc || babbleNpc == id, $"t={now}: two babbles at once");
            }

            if (now < reactionEnd)
            {
                Assert.True(grant.CutReaction, $"t={now}: a babble over a reaction");
                reactionEnd = now;
            }

            babbleEnd = now + seconds;
            babbleNpc = id;
            babbles++;
            if (priority == NpcVoicePriority.Addressed)
            {
                addressedHeard++;
            }
            else
            {
                nonAddressed++;
            }
        }

        for (float now = 0f; now < 60f; now += 0.1f)
        {
            bool vega = now >= vegaFrom && now < vegaTo;

            if (now >= nextPassing)
            {
                int passer = 1 + rng.Next(npcs);
                Babble(passer == vendor ? vendor + 1 : passer, NpcVoicePriority.Greeting, now, 14);
                nextPassing = now + 1.5f + (float)rng.NextDouble();
            }

            foreach (var (a, b, start) in talks)
            {
                float t = now - start;
                int line = (int)MathF.Floor(t / 3.6f);
                if (t >= 0f && t - (line * 3.6f) < 0.1f)
                {
                    Babble(line % 2 == 0 ? a : b, NpcVoicePriority.Chatter, now, 50);
                }
            }

            foreach (float at in addressedAt)
            {
                if (now >= at && now < at + 0.1f)
                {
                    int before = addressedHeard;
                    Babble(vendor, NpcVoicePriority.Addressed, now, 90);
                    Assert.Equal(before + 1, addressedHeard); // the NPC the player talks to is always heard
                }
            }

            for (int id = 1; id <= npcs; id++)
            {
                if (now < nextIdle[id])
                {
                    continue;
                }

                nextIdle[id] = now + NpcVoice.IdleGapSeconds(id, (int)now);
                var grant = gate.RequestReaction(id, NpcReaction.Idle, now, Reaction, distance[id], crowd, vega);
                if (grant.Granted)
                {
                    Assert.False(now < babbleEnd, $"t={now}: a murmur over a babble");
                    Assert.False(now < reactionEnd, $"t={now}: two reactions at once");
                    Assert.False(vega, $"t={now}: a murmur over VEGA");
                    reactionEnd = now + Reaction;
                    reactions++;
                }
            }
        }

        Assert.Equal(addressedAt.Length, addressedHeard);
        Assert.Equal(0, reactions); // a crowd murmurs not at all
        Assert.InRange(nonAddressed, 1, 10); // one shared six-second gap
        Assert.True(babbles <= 12, $"{babbles} babbles in a minute");
    }

    /// <summary>The same deck with few people about (no crowd): still one babble and one reaction at a time, the murmurs rare.</summary>
    [Fact]
    public void AQuietVillage_StillSpeaksOneAtATime_AndMurmursRarely()
    {
        var gate = new NpcVoiceGate();
        var rng = new Random(7);
        float babbleEnd = float.NegativeInfinity, reactionEnd = float.NegativeInfinity;
        int babbleNpc = NpcVoiceGate.NoNpc, murmurs = 0;
        for (float now = 0f; now < 60f; now += 0.1f)
        {
            int id = 1 + rng.Next(5);
            float dist = 1f + (float)rng.NextDouble() * 9f;
            var babble = gate.RequestBabble(id, rng.Next(3) == 0 ? NpcVoicePriority.Chatter : NpcVoicePriority.Greeting, now, 3.2f, dist, false, false);
            if (babble.Granted)
            {
                Assert.True(now >= babbleEnd || babble.CutNpcId == babbleNpc || babbleNpc == id);
                babbleEnd = now + 3.2f;
                babbleNpc = id;
                reactionEnd = Math.Min(reactionEnd, now);
            }

            var murmur = gate.RequestReaction(id, NpcReaction.Idle, now, 1f, dist, false, false);
            if (murmur.Granted)
            {
                Assert.False(now < babbleEnd);
                Assert.False(now < reactionEnd);
                reactionEnd = now + 1f;
                murmurs++;
            }
        }

        Assert.True(murmurs <= 3, $"{murmurs} murmurs in a minute");
    }
}
