// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3279–#3283: the automatic graphics-preset calibration. The shell (menu) sample steps at most to Medium; High needs a
/// world measurement at Medium on the same GPU, read from the GPU time where there is one; a slow world only earns a hint.
/// </summary>
public sealed class QualityCalibrationTests
{
    private const string Gpu = "NVIDIA RTX 2000 Ada Generation Laptop GPU";

    private static FrameSampleStats Stats(float median, float p90, float gpuP90 = 0f, int frames = 900)
        => new FrameSampleStats(frames, median, p90, gpuP90);

    private static WorldSampleRecord World(FrameSampleStats s, int preset = QualityCalibration.Medium, string gpu = Gpu)
        => new WorldSampleRecord(preset, s, gpu);

    [Fact]
    public void Stats_FromFrames_TakesMedianAndP90_AndGpuOnlyWhenMostFramesReportedIt()
    {
        var wall = Enumerable.Range(1, 100).Select(i => (float)i).ToList();
        var gpu = Enumerable.Range(1, 60).Select(i => i / 10f).ToList();
        var s = FrameSampleStats.From(wall, gpu);
        Assert.Equal(100, s.Frames);
        Assert.Equal(51f, s.MedianMs);
        Assert.Equal(91f, s.P90Ms);
        Assert.True(s.HasGpuTime);
        Assert.Equal(5.5f, s.GpuP90Ms, 3);

        var sparse = FrameSampleStats.From(Enumerable.Range(1, 100).Select(i => (float)i).ToList(), new List<float> { 3f, 4f });
        Assert.False(sparse.HasGpuTime);
        Assert.Equal(0, FrameSampleStats.From(new List<float>(), null).Frames);
    }

    [Fact]
    public void ShellStep_StepsDownWhenTheMenuStruggles_ButNeverBelowPotato()
    {
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.ShellStep(QualityCalibration.High, Stats(45f, 60f), mayStepUp: true));
        Assert.Equal(QualityCalibration.Potato, QualityCalibration.ShellStep(QualityCalibration.Potato, Stats(80f, 90f), mayStepUp: true));
    }

    [Fact]
    public void ShellStep_StepsUpWithHeadroom_ButNeverToHigh()
    {
        var fast = Stats(16.7f, 17f);
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.ShellStep(QualityCalibration.Low, fast, mayStepUp: true));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.ShellStep(QualityCalibration.Medium, fast, mayStepUp: true));
        Assert.Equal(QualityCalibration.Low, QualityCalibration.ShellStep(QualityCalibration.Low, fast, mayStepUp: false));
    }

    [Fact]
    public void ShellStep_WithGpuTime_ReadsTheGpu_NotTheVSyncCappedFrame()
    {
        // VSync-capped 16.7 ms frames but a GPU that needs 15 ms already: no headroom.
        Assert.Equal(QualityCalibration.Low, QualityCalibration.ShellStep(QualityCalibration.Low, Stats(16.7f, 17f, gpuP90: 15f), mayStepUp: true));
        // Missed VSync frames (33 ms p90) but a nearly idle GPU and a fine median: the GPU has room.
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.ShellStep(QualityCalibration.Low, Stats(16.7f, 33f, gpuP90: 4f), mayStepUp: true));
    }

    [Fact]
    public void StartStep_GoesToHigh_OnlyAfterAWorldMeasurementAtMediumWithRoom()
    {
        float roomyGpu = QualityCalibration.FrameBudgetMs * QualityCalibration.GpuBudgetShare / QualityCalibration.HighGpuCostFactor - 0.5f;
        var roomy = World(Stats(16.7f, 18f, roomyGpu));
        Assert.Equal(QualityCalibration.High, QualityCalibration.StartStep(QualityCalibration.Medium, true, roomy, Gpu, string.Empty));

        var tight = World(Stats(16.7f, 18f, roomyGpu + 2f));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, tight, Gpu, string.Empty));

        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, WorldSampleRecord.None, Gpu, string.Empty));
    }

    [Fact]
    public void StartStep_NeedsACpuWithRoom_TheSamePresetAndTheSameGpu()
    {
        var cpuBound = World(Stats(28f, 35f, gpuP90: 2f));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, cpuBound, Gpu, string.Empty));

        var good = Stats(16.7f, 18f, gpuP90: 3f);
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, World(good, QualityCalibration.Low), Gpu, string.Empty));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, World(good, gpu: "Intel(R) UHD Graphics"), Gpu, string.Empty));
        Assert.Equal(QualityCalibration.Low, QualityCalibration.StartStep(QualityCalibration.Low, true, World(good), Gpu, string.Empty));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, false, World(good), Gpu, string.Empty));
    }

    [Fact]
    public void StartStep_DoesNotRetryHigh_OnAGpuThatAlreadyFailedIt()
    {
        var good = World(Stats(16.7f, 18f, gpuP90: 3f));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, good, Gpu, Gpu));
        Assert.Equal(QualityCalibration.High, QualityCalibration.StartStep(QualityCalibration.Medium, true, good, Gpu, "an older GPU"));
        Assert.True(QualityCalibration.RejectsHigh(QualityCalibration.High, QualityCalibration.Medium));
        Assert.False(QualityCalibration.RejectsHigh(QualityCalibration.Medium, QualityCalibration.Low));
    }

    [Fact]
    public void Browser_WithoutGpuTime_UsesTheVSyncRuleForHigh()
    {
        Assert.Equal(QualityCalibration.High, QualityCalibration.StartStep(QualityCalibration.Medium, true, World(Stats(16.7f, 17f)), Gpu, string.Empty));
        Assert.Equal(QualityCalibration.Medium, QualityCalibration.StartStep(QualityCalibration.Medium, true, World(Stats(16.7f, 25f)), Gpu, string.Empty));
    }

    [Theory]
    [InlineData("Microsoft Basic Render Driver", true)]
    [InlineData("llvmpipe (LLVM 15.0.7, 256 bits)", true)]
    [InlineData("Google SwiftShader", true)]
    [InlineData("NVIDIA GeForce RTX 4070", false)]
    [InlineData("", false)]
    public void SoftwareRenderers_NeverStepUp(string device, bool software)
    {
        Assert.Equal(software, QualityCalibration.IsSoftwareRenderer(device));
        Assert.Equal(!software, QualityCalibration.MayStepUp(device, onBattery: false));
        Assert.False(QualityCalibration.MayStepUp(device, onBattery: true));
    }

    [Fact]
    public void SlowWorldWatch_FiresOnce_WhenAWholeWindowIsSlow()
    {
        var watch = new SlowWorldWatch();
        int fired = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (watch.Add(50f))
            {
                fired++;
            }
        }

        Assert.Equal(1, fired);
        Assert.True(watch.Fired);
    }

    [Fact]
    public void SlowWorldWatch_IgnoresShortHitches_AndResets()
    {
        var watch = new SlowWorldWatch();
        for (int i = 0; i < 4000; i++)
        {
            Assert.False(watch.Add(i % 50 == 0 ? 250f : 16.7f)); // a hitch every 50 frames
        }

        watch.Reset();
        for (int i = 0; i < 300; i++)
        {
            Assert.False(watch.Add(60f)); // 18 s: less than one window
        }

        watch.Reset(); // e.g. a cutscene started — the 18 s are dropped
        for (int i = 0; i < 300; i++)
        {
            Assert.False(watch.Add(60f));
        }

        Assert.False(watch.Fired);
    }

    [Fact]
    public void AHiddenGpuName_StillMatchesItsOwnMeasurement()
    {
        string key = QualityCalibration.DeviceKey("");
        Assert.Equal(key, QualityCalibration.DeviceKey(null));
        Assert.Equal(Gpu, QualityCalibration.DeviceKey(" " + Gpu + " "));
        var good = World(Stats(16.7f, 17f), gpu: key);
        Assert.Equal(QualityCalibration.High, QualityCalibration.StartStep(QualityCalibration.Medium, true, good, key, string.Empty));
    }

    [Fact]
    public void HintUseful_OnlyWhenSomethingCanBeLowered()
    {
        Assert.True(QualityCalibration.HintUseful(QualityCalibration.Low, 2));
        Assert.True(QualityCalibration.HintUseful(QualityCalibration.Potato, 4));
        Assert.False(QualityCalibration.HintUseful(QualityCalibration.Potato, 2));
    }
}
