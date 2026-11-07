// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3355 — a Sounder NPC hums, never speaks (<see cref="SounderVoice"/>): the living Sounders' and the Echoes' clip ids are the
/// story pack's sound ids, a hum follows the line's length and never repeats a clip back to back, the pitch is stable and close
/// to the recording's own; and the Unity view wires the Sounder rig, the hums and the hum bubble (guarded as source text).
/// </summary>
public sealed class SounderVoiceTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheHums_AreThePacksSoundIds()
    {
        Assert.Equal(new[] { "elder_hum_1", "elder_hum_2" }, SounderVoice.HumClips(echo: false));
        Assert.Equal(new[] { "echo_hum_1", "echo_hum_2", "echo_hum_3", "echo_hum_4" }, SounderVoice.HumClips(echo: true));
    }

    [Fact]
    public void AHum_FollowsTheLine_AndNeverRepeatsAClipBackToBack()
    {
        var rng = new Random(7);
        foreach (int count in new[] { 2, 4 })
        {
            for (int len = 0; len < 200; len += 13)
            {
                for (int previous = -1; previous < count; previous++)
                {
                    var plan = SounderVoice.HumPlan(len, rng, previous, count);
                    Assert.Equal(NpcVoice.ClipCount(len), plan.Length);
                    int last = previous;
                    foreach (int clip in plan)
                    {
                        Assert.InRange(clip, 0, count - 1);
                        Assert.NotEqual(last, clip);
                        last = clip;
                    }
                }
            }
        }

        Assert.Empty(SounderVoice.HumPlan(40, rng, -1, 0));
        Assert.Throws<ArgumentNullException>(() => SounderVoice.HumPlan(40, null!, -1, 2));
    }

    [Fact]
    public void ThePitch_IsStable_AndCloseToTheRecording()
    {
        for (int seed = -3000; seed < 3000; seed += 11)
        {
            float p = SounderVoice.Pitch(seed);
            Assert.InRange(p, SounderVoice.PitchMin, SounderVoice.PitchMax);
            Assert.Equal(p, SounderVoice.Pitch(seed));
        }
    }

    [Fact]
    public void TheNpcView_DrawsTheSounderRig_HumsAndShowsTheHumBubble()
    {
        string view = Script("NpcView.cs") + Script("NpcView.Sounders.cs");
        Assert.Contains("AttachSounder(n, nd)", view);
        Assert.Contains("BuildSounderNpc(", view);
        Assert.Contains("SounderVoice.HumPlan(", view);
        Assert.Contains("HumBubbleFor(n,", view);
        Assert.Contains("SounderVoice.HumClips(echo)", view);

        string builder = Script("CreatureBuilder.cs") + Script("CreatureBuilder.Sounder.cs");
        Assert.Contains("BuildSoundersBody(root, c)", builder);
        foreach (string plan in new[] { "\"Sounder\"", "\"Soundi\"", "\"Soundrak\"", "\"Soundeling\"", "\"Quali\"" })
        {
            Assert.Contains("case " + plan + ":", builder);
        }

        Assert.Contains("PoseSounders(dt, t, moving)", Script("CreatureAnimator.cs"));
        Assert.Contains("SetCarrying(c.CarriesLight)", Script("CreatureView.cs"));
    }
}
