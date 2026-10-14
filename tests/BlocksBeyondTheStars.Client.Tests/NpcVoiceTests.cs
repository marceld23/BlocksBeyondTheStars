// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2592 — an NPC's wordless speech voice (<see cref="NpcVoice"/>): the bank matches the look (androids robot, the G.D.S.
/// guardians deep, a bearded face never female), every clip the code can name exists as a file, the babble length
/// follows the bubble, a babble never repeats a clip back to back, and the per-NPC pitch is stable and in range.
/// </summary>
public sealed class NpcVoiceTests
{
    private static string AudioDir => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");

    private static IEnumerable<NpcVoiceBank> AllBanks => Enum.GetValues<NpcVoiceBank>();

    [Fact]
    public void Androids_SpeakWithARobotBank_AndHumansWithAHumanBank()
    {
        for (int seed = -5000; seed < 5000; seed += 7)
        {
            Assert.True(NpcVoice.IsRobot(NpcVoice.VoiceFor(seed, isRobot: true)), $"android seed {seed}");
            Assert.False(NpcVoice.IsRobot(NpcVoice.VoiceFor(seed, isRobot: false)), $"human seed {seed}");
        }
    }

    [Fact]
    public void Guardians_AlwaysSpeakWithTheDeepRobotVoice()
    {
        for (int seed = 0; seed < 500; seed++)
        {
            Assert.Equal(NpcVoiceBank.RobotDeep, NpcVoice.VoiceFor(seed, isRobot: true, isGuardian: true));
        }
    }

    [Fact]
    public void ABeardedFace_NeverSpeaksFemale_AndMostlyMale()
    {
        int bearded = 0, male = 0;
        for (int seed = 0; seed < 20000; seed++)
        {
            if (NpcLooks.FacialHairFor(seed, robot: false) == FacialHair.None)
            {
                continue;
            }

            bearded++;
            var bank = NpcVoice.VoiceFor(seed, isRobot: false);
            Assert.NotEqual(NpcVoiceBank.HumanFemale, bank);
            if (bank == NpcVoiceBank.HumanMale)
            {
                male++;
            }
        }

        Assert.True(bearded > 1000, "the sample must hold bearded faces");
        double share = (double)male / bearded;
        Assert.InRange(share, 0.72, 0.88); // ~80 % male, the rest neutral
    }

    [Fact]
    public void CleanShavenHumans_SpreadOverFemaleMaleAndNeutral()
    {
        int total = 0, female = 0, male = 0, neutral = 0;
        for (int seed = 0; seed < 20000; seed++)
        {
            if (NpcLooks.FacialHairFor(seed, robot: false) != FacialHair.None)
            {
                continue;
            }

            total++;
            switch (NpcVoice.VoiceFor(seed, isRobot: false))
            {
                case NpcVoiceBank.HumanFemale: female++; break;
                case NpcVoiceBank.HumanMale: male++; break;
                default: neutral++; break;
            }
        }

        Assert.InRange((double)female / total, 0.40, 0.50);
        Assert.InRange((double)male / total, 0.30, 0.40);
        Assert.InRange((double)neutral / total, 0.15, 0.25);
    }

    [Fact]
    public void TheVoice_IsDeterministic_PerSeed()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            Assert.Equal(NpcVoice.VoiceFor(seed, false), NpcVoice.VoiceFor(seed, false));
            Assert.Equal(NpcVoice.VoiceFor(seed, true), NpcVoice.VoiceFor(seed, true));
            Assert.Equal(NpcVoice.Pitch(seed, false, 1f), NpcVoice.Pitch(seed, false, 1f));
        }
    }

    [Fact]
    public void EverySpeechClip_ExistsAsAFile()
    {
        foreach (var bank in AllBanks)
        {
            var clips = NpcVoice.Clips(bank);
            Assert.Equal(NpcVoice.ClipsPerBank, clips.Count);
            foreach (string id in clips)
            {
                Assert.StartsWith(NpcVoice.SpeechPrefix, id);
                Assert.True(File.Exists(Path.Combine(AudioDir, id + ".mp3")), $"missing speech clip {id}.mp3");
            }
        }
    }

    [Fact]
    public void EveryReactionClip_ExistsAsAFile_AndRobotsUseTheAlienBank()
    {
        // The maintainer's sound review (2026-10-14): the robot reactions are the existing npc_alien_* clips.
        Assert.Equal("npc_alien_", NpcVoice.RobotReactionPrefix);
        Assert.True(NpcVoice.ReactionsEnabled);
        foreach (var kind in Enum.GetValues<NpcReaction>())
        {
            foreach (bool robot in new[] { false, true })
            {
                string id = NpcVoice.ReactionClip(robot, kind);
                Assert.StartsWith(robot ? NpcVoice.RobotReactionPrefix : NpcVoice.HumanReactionPrefix, id);
                Assert.True(File.Exists(Path.Combine(AudioDir, id + ".mp3")), $"missing reaction clip {id}.mp3");
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(40, 1)]
    [InlineData(41, 2)]
    [InlineData(80, 2)]
    [InlineData(81, 3)]
    [InlineData(200, 3)]
    [InlineData(350, 3)]
    public void TheBabble_GrowsWithTheBubble_OneClipPer40Chars_UpToThree(int length, int clips)
        => Assert.Equal(clips, NpcVoice.ClipCount(length));

    [Fact]
    public void ABabble_NeverPlaysTheSameClipTwiceInARow()
    {
        var rng = new Random(1234);
        int previous = -1;
        for (int i = 0; i < 2000; i++)
        {
            int length = rng.Next(0, 260);
            var plan = NpcVoice.BabblePlan(length, rng, previous);
            Assert.Equal(NpcVoice.ClipCount(length), plan.Length);
            foreach (int clip in plan)
            {
                Assert.InRange(clip, 0, NpcVoice.ClipsPerBank - 1);
                Assert.NotEqual(previous, clip); // also across two babbles of the same NPC
                previous = clip;
            }
        }
    }

    [Fact]
    public void ThePitch_StaysInRange_AndABiggerBodySpeaksLower()
    {
        for (int seed = 0; seed < 2000; seed++)
        {
            Assert.InRange(NpcVoice.Pitch(seed, true, 1f), NpcVoice.PitchMin, NpcVoice.PitchMax);
            Assert.InRange(NpcVoice.Pitch(seed, false, 1f), NpcVoice.PitchMin, NpcVoice.PitchMax);
            Assert.InRange(NpcVoice.Pitch(seed, false, 3f), 0.75f, 1.35f);
            Assert.True(NpcVoice.Pitch(seed, false, 1.3f) < NpcVoice.Pitch(seed, false, 1f));
            Assert.True(NpcVoice.Pitch(seed, false, 0.8f) > NpcVoice.Pitch(seed, false, 1f));
            Assert.Equal(NpcVoice.Pitch(seed, true, 1f), NpcVoice.Pitch(seed, true, 1.5f)); // a robot ignores its size
        }

        // The pitches spread over the range, not one value.
        var distinct = Enumerable.Range(0, 200).Select(s => MathF.Round(NpcVoice.Pitch(s, false, 1f), 2)).Distinct().Count();
        Assert.True(distinct > 10);
    }

    [Fact]
    public void AClip_JittersAtMostTwoPercent()
    {
        Assert.Equal(1.0f * 0.98f, NpcVoice.ClipPitch(1f, 0f), 4);
        Assert.Equal(1.0f * 1.02f, NpcVoice.ClipPitch(1f, 1f), 4);
        Assert.Equal(1.05f, NpcVoice.ClipPitch(1.05f, 0.5f), 4);
        Assert.Equal(0.98f, NpcVoice.ClipPitch(1f, -3f), 4); // clamped roll
    }

    [Fact]
    public void IdleMurmurs_ComeEvery30To60Seconds_PerNpc()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            for (int n = 0; n < 5; n++)
            {
                Assert.InRange(NpcVoice.IdleGapSeconds(seed, n), 30f, 60f);
            }
        }

        Assert.NotEqual(NpcVoice.IdleGapSeconds(7, 0), NpcVoice.IdleGapSeconds(8, 0));
    }

    [Fact]
    public void TheIdleMurmur_IsTheQuietestReaction()
    {
        Assert.True(NpcVoice.ReactionVolume(NpcReaction.Idle) < NpcVoice.ReactionVolume(NpcReaction.Trade));
        Assert.True(NpcVoice.ReactionVolume(NpcReaction.Trade) < NpcVoice.ReactionVolume(NpcReaction.Alert));
    }
}
