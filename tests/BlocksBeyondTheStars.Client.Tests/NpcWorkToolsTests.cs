// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2647 — the wilderness folk's tools on the client (<see cref="NpcWorkTools"/>, <see cref="NpcChopGate"/>): the swing
/// cadence table (moved from NpcView, its old rows pinned), the woodcutter's steady chop, the hunter's bow that never swings
/// by itself, and the work sounds kept quiet and short-range — no sound jumble at a busy lodge.
/// </summary>
public sealed class NpcWorkToolsTests
{
    private static string ResourcesDir => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources");

    [Theory]
    [InlineData("npc_hammer", 1.2f, 2.4f)]
    [InlineData("npc_hoe", 2.0f, 3.5f)]
    [InlineData("blade", 30f, 60f)]
    [InlineData("npc_pickaxe", 1.0f, 2.0f)]
    [InlineData("npc_camera", 3.0f, 5.5f)]
    [InlineData("npc_microphone", 4.0f, 7.0f)]
    [InlineData("npc_axe", 2.2f, 3.4f)]
    public void ATool_SetsThePace(string held, float lo, float hi)
    {
        Assert.Equal((lo, hi), NpcWorkTools.Cadence(held, "Miner", "settler"));
    }

    [Fact]
    public void WithoutATool_TheThemeAndRoleSetThePace_AsBefore()
    {
        Assert.Equal((1.2f, 2.4f), NpcWorkTools.Cadence(string.Empty, "Deep Miners", "vendor"));
        Assert.Equal((2.6f, 4.6f), NpcWorkTools.Cadence(null, null, "Settler"));
        Assert.Equal((2.6f, 4.6f), NpcWorkTools.Cadence(null, "builders", null));
        Assert.Equal((4.5f, 8.5f), NpcWorkTools.Cadence(null, "traders", "vendor"));
        Assert.Equal((4.5f, 8.5f), NpcWorkTools.Cadence(null, null, null));
    }

    [Fact]
    public void TheBow_NeverSwingsByItself_EveryOtherToolDoes()
    {
        Assert.False(NpcWorkTools.SwingsPeriodically(NpcWorkTools.Bow));
        foreach (string held in new[] { NpcWorkTools.Axe, "npc_hammer", "npc_hoe", "npc_basket", "npc_pickaxe", string.Empty })
        {
            Assert.True(NpcWorkTools.SwingsPeriodically(held), held);
        }

        Assert.True(NpcWorkTools.SwingsPeriodically(null));
    }

    [Fact]
    public void AChop_IsHeardOnlyNearby_WhileFelling_AndNeverStacks()
    {
        var gate = new NpcChopGate();
        const string chopping = NpcWorkTools.ChoppingActivity;

        Assert.Equal(0f, gate.Request(10f, NpcWorkTools.ChopRadius + 0.5f, chopping, narratorBusy: false)); // too far
        Assert.Equal(0f, gate.Request(10f, 5f, "npc.activity.walking", narratorBusy: false));             // not felling
        Assert.Equal(0f, gate.Request(10f, 5f, null, narratorBusy: false));
        Assert.Equal(0f, gate.Request(10f, 5f, chopping, narratorBusy: true));                            // VEGA speaks

        Assert.Equal(NpcWorkTools.ChopVolume, gate.Request(10f, 5f, chopping, narratorBusy: false));
        Assert.Equal(0f, gate.Request(10.2f, 3f, chopping, narratorBusy: false)); // a second woodcutter right after: silent
        Assert.Equal(NpcWorkTools.ChopVolume, gate.Request(10f + NpcWorkTools.ChopGapSeconds, 3f, chopping, narratorBusy: false));

        gate.Reset();
        Assert.Equal(NpcWorkTools.ChopVolume, gate.Request(0f, NpcWorkTools.ChopRadius, chopping, narratorBusy: false));
    }

    [Fact]
    public void TheChop_IsQuiet_AndLandsInsideTheSwing()
    {
        Assert.InRange(NpcWorkTools.ChopVolume, 0.1f, 0.5f);
        Assert.InRange(NpcWorkTools.ChopRadius, 8f, 20f);
        Assert.InRange(NpcWorkTools.ChopImpactSeconds, 0.1f, 0.42f); // PlayerAvatar.SwingDuration
    }

    [Fact]
    public void TheServersWorkSounds_AreQuietAndShortRange_OthersUnchanged()
    {
        Assert.True(NpcWorkTools.FxVolume(NpcWorkTools.BowShotClip, 0.8f) < 0.8f);
        Assert.True(NpcWorkTools.FxVolume(NpcWorkTools.TreeFallClip, 0.8f) < 0.8f);
        Assert.Equal(0.8f, NpcWorkTools.FxVolume("chime_0", 0.8f));
        Assert.Equal(0.8f, NpcWorkTools.FxVolume(null, 0.8f));

        Assert.True(NpcWorkTools.FxAudible(NpcWorkTools.BowShotClip, NpcWorkTools.WorkSoundRadius));
        Assert.False(NpcWorkTools.FxAudible(NpcWorkTools.BowShotClip, NpcWorkTools.WorkSoundRadius + 1f));
        Assert.False(NpcWorkTools.FxAudible(NpcWorkTools.TreeFallClip, 40f));
        Assert.True(NpcWorkTools.FxAudible("alarm_siren_0", 400f)); // a device's own rolloff decides
    }

    [Fact]
    public void TheWorkSoundClips_AndTheLodgeMarker_Exist()
    {
        foreach (string clip in new[] { NpcWorkTools.ChopClip, NpcWorkTools.BowShotClip, NpcWorkTools.TreeFallClip })
        {
            Assert.True(File.Exists(Path.Combine(ResourcesDir, "audio", clip + ".mp3")), clip);
        }

        Assert.True(File.Exists(Path.Combine(ResourcesDir, "icons", "map_lodge.png")), "map_lodge");
    }
}
