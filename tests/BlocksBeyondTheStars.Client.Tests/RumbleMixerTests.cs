// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2746: the gamepad's rumble. Pulses fade linearly in their own time, combine by the strongest (never a sum that
/// shakes on), split frames land on the same curve as one long frame, a frame in which rumble is not allowed gives zero
/// and forgets every pulse, and a broken input never poisons the motors.
/// </summary>
public sealed class RumbleMixerTests
{
    [Fact]
    public void APulse_FadesLinearly_ToZeroInItsTime()
    {
        var mixer = new RumbleMixer();
        mixer.Pulse(1f, 0.5f, 0.5f);
        var (low, high) = mixer.Step(0f, allowed: true);
        Assert.Equal(1f, low, 4);
        Assert.Equal(0.5f, high, 4);

        (low, high) = mixer.Step(0.25f, allowed: true);
        Assert.Equal(0.5f, low, 4);
        Assert.Equal(0.25f, high, 4);

        (low, high) = mixer.Step(0.25f, allowed: true);
        Assert.Equal(0f, low);
        Assert.Equal(0f, high);
        Assert.False(mixer.Active);
    }

    [Fact]
    public void Pulses_CombineByTheStrongest_PerMotor()
    {
        var mixer = new RumbleMixer();
        mixer.Pulse(0.5f, 0.2f, 1f);
        mixer.Pulse(0.3f, 0.6f, 1f);
        var (low, high) = mixer.Step(0f, allowed: true);
        Assert.Equal(0.5f, low, 4);
        Assert.Equal(0.6f, high, 4);

        // Many hits at once never add up past the strongest.
        for (int i = 0; i < 40; i++)
        {
            mixer.Pulse(0.4f, 0.4f, 0.3f);
        }

        (low, high) = mixer.Step(0f, allowed: true);
        Assert.Equal(0.5f, low, 4);
        Assert.Equal(0.6f, high, 4);

        // A long weak pulse outlives a short strong one.
        mixer.Clear();
        mixer.Pulse(1f, 0f, 0.2f);
        mixer.Pulse(0.3f, 0f, 1f);
        (low, _) = mixer.Step(0.5f, allowed: true);
        Assert.Equal(0.15f, low, 4);
    }

    [Fact]
    public void SplitSteps_EqualOneStep()
    {
        var a = new RumbleMixer();
        var b = new RumbleMixer();
        foreach (var m in new[] { a, b })
        {
            m.Pulse(0.9f, 0.4f, 0.8f);
            m.Pulse(0.2f, 0.7f, 0.3f);
        }

        (float Low, float High) split = default;
        for (int i = 0; i < 6; i++)
        {
            split = a.Step(0.05f, allowed: true);
        }

        var once = b.Step(0.3f, allowed: true);
        Assert.Equal(once.Low, split.Low, 4);
        Assert.Equal(once.High, split.High, 4);
    }

    [Fact]
    public void NotAllowed_GivesZero_AndForgetsEveryPulse()
    {
        var mixer = new RumbleMixer();
        mixer.Pulse(1f, 1f, 1f);
        Assert.Equal((0f, 0f), mixer.Step(0.01f, allowed: false));
        Assert.False(mixer.Active);
        Assert.Equal((0f, 0f), mixer.Step(0f, allowed: true)); // nothing buzzes on once it is allowed again
    }

    [Fact]
    public void BadInput_IsIgnored_AndLevelsAreClamped()
    {
        var mixer = new RumbleMixer();
        mixer.Pulse(float.NaN, 1f, 1f);
        mixer.Pulse(1f, float.PositiveInfinity, 1f);
        mixer.Pulse(1f, 1f, float.NaN);
        mixer.Pulse(1f, 1f, 0f);
        mixer.Pulse(0f, 0f, 1f);
        Assert.False(mixer.Active);

        mixer.Pulse(3f, -2f, 0.5f);
        var (low, high) = mixer.Step(float.NaN, allowed: true);
        Assert.Equal(1f, low);
        Assert.Equal(0f, high);

        (low, _) = mixer.Step(-1f, allowed: true); // a negative frame advances nothing
        Assert.Equal(1f, low);
    }

    [Fact]
    public void Trauma_RumblesFromTheThreshold_TheHeatShimmerDoesNot()
    {
        var mixer = new RumbleMixer();
        Assert.False(mixer.PulseTrauma(0.02f)); // a hot hull's shimmer, every frame
        Assert.False(mixer.PulseTrauma(float.NaN));
        Assert.Equal((0f, 0f), mixer.Step(0f, allowed: true));

        Assert.True(mixer.PulseTrauma(0.8f)); // an explosion close by
        var (low, high) = mixer.Step(0f, allowed: true);
        Assert.Equal(0.8f, low, 4);
        Assert.True(high > 0f && high < low);
        mixer.Step(RumbleMixer.MaxSeconds, allowed: true);
        Assert.False(mixer.Active);
    }

    [Fact]
    public void AFullMixer_KeepsTheStrongestPulses()
    {
        var mixer = new RumbleMixer();
        for (int i = 0; i < RumbleMixer.MaxPulses; i++)
        {
            mixer.Pulse(0.1f, 0.1f, 1f);
        }

        mixer.Pulse(0.9f, 0f, 1f); // replaces a weak one
        Assert.Equal(0.9f, mixer.Step(0f, allowed: true).Low, 4);
    }

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>The Unity half, guarded as source text (the Unity scripts cannot load headless; the EditMode suite
    /// PadRumbleEditModeTests drives the component itself): every trigger reaches the mixer, the world's teardown stops
    /// the motors, the settings hint asks whether rumble exists here, and the XInput P/Invoke never compiles into WebGL.</summary>
    [Fact]
    public void TheUnityHalf_IsWired_AndTheXInputBackendStaysOffWebGl()
    {
        string fx = Script("FxCamera.cs");
        Assert.Contains("PadRumble.Trauma(amount)", fx);
        Assert.Contains("PadRumble.WeaponKick(degrees)", fx);
        Assert.Contains("PadRumble.StopAll()", fx);
        Assert.Contains("PadRumble.Damage(pulse.Trauma)", ClientTestPaths.ScriptPartsText("HudUi"));
        Assert.Contains("PadRumble.BlockBroken()", Script("MiningFx.cs"));
        Assert.Contains("PadRumble.Landing(", Script("PlayerController.cs"));
        Assert.Contains("rumble: false", Script("PlayerController.cs")); // a jump's landing does not buzz
        Assert.Contains("AddComponent<PadRumble>()", Script("WorldRig.cs"));
        Assert.Contains("PadRumble.Supported ? \"ui.settings.pad_vibration_hint\" : \"ui.settings.pad_vibration_unsupported\"", Script("UiSettings.cs"));

        // Every P/Invoke sits inside the Windows-only, non-WebGL block.
        string pad = Script("PadRumble.cs");
        const string Guard = "#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL";
        int from = 0;
        int imports = 0;
        while ((from = pad.IndexOf("[DllImport(", from, StringComparison.Ordinal)) >= 0)
        {
            int open = pad.LastIndexOf(Guard, from, StringComparison.Ordinal);
            int close = pad.LastIndexOf("#endif", from, StringComparison.Ordinal);
            Assert.True(open >= 0 && open > close, "a DllImport outside the Windows-only block");
            from++;
            imports++;
        }

        Assert.Equal(2, imports);
    }
}
