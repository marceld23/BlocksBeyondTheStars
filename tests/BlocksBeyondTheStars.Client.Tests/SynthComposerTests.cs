// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The generative Synth engine (#1176): deterministic per seed, different per seed, biome-fixed root/mode,
/// sane length and level, click-free loop seams, and chunked rendering identical to one-shot rendering.
/// </summary>
public sealed class SynthComposerTests
{
    [Theory]
    [InlineData(SynthMood.Menu)]
    [InlineData(SynthMood.Planet)]
    [InlineData(SynthMood.Space)]
    [InlineData(SynthMood.Combat)]
    public void Compose_IsDeterministicPerSeed(SynthMood mood)
    {
        var a = SynthComposer.Compose(mood, 1234, MusicLibrary.PlanetIce);
        var b = SynthComposer.Compose(mood, 1234, MusicLibrary.PlanetIce);
        Assert.Equal(a.Tempo, b.Tempo);
        Assert.Equal(a.ModeName, b.ModeName);
        Assert.Equal(a.Chords.Count, b.Chords.Count);
        for (int i = 0; i < a.Chords.Count; i++)
        {
            Assert.Equal(a.Chords[i], b.Chords[i]);
        }

        Assert.Equal(a.ArpPatterns[0], b.ArpPatterns[0]);
        Assert.Equal(a.ArpPatterns[1], b.ArpPatterns[1]);
    }

    [Fact]
    public void Compose_DifferentSeeds_ProduceDifferentPieces()
    {
        int different = 0;
        var reference = SynthComposer.Compose(SynthMood.Planet, 0, MusicLibrary.PlanetIce);
        for (int seed = 1; seed <= 20; seed++)
        {
            var other = SynthComposer.Compose(SynthMood.Planet, seed, MusicLibrary.PlanetIce);
            bool same = other.Tempo == reference.Tempo
                        && other.ArpPatterns[0].SequenceEqual(reference.ArpPatterns[0])
                        && other.Chords.Select(c => c[0]).SequenceEqual(reference.Chords.Select(c => c[0]));
            if (!same)
            {
                different++;
            }
        }

        Assert.True(different >= 18, $"only {different}/20 seeds differed from seed 0");
    }

    [Fact]
    public void Compose_PlanetFlavor_FixesRootAndMode_AcrossSeeds()
    {
        // Biome identity (decision 2026-08-22): every ice planet shares root + mode; seeds vary the rest.
        var first = SynthComposer.Compose(SynthMood.Planet, 1, MusicLibrary.PlanetIce);
        for (int seed = 2; seed < 12; seed++)
        {
            var s = SynthComposer.Compose(SynthMood.Planet, seed, MusicLibrary.PlanetIce);
            Assert.Equal(first.RootHz, s.RootHz);
            Assert.Equal(first.ModeName, s.ModeName);
        }

        var lava = SynthComposer.Compose(SynthMood.Planet, 1, MusicLibrary.PlanetLava);
        Assert.NotEqual(first.RootHz, lava.RootHz);
    }

    [Theory]
    [InlineData(SynthMood.Menu, null)]
    [InlineData(SynthMood.Planet, MusicLibrary.PlanetOcean)]
    [InlineData(SynthMood.Planet, MusicLibrary.PlanetCave)]
    [InlineData(SynthMood.Space, null)]
    [InlineData(SynthMood.Combat, null)]
    public void Compose_LengthIsAMinuteOrTwo(SynthMood mood, string? flavor)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            var s = SynthComposer.Compose(mood, seed, flavor);
            Assert.InRange(s.Seconds, 30f, 130f);
            Assert.Equal(8, s.Chords.Count);
            Assert.Equal(s.Chords.Count, s.DroneHz.Count);
        }
    }

    [Fact]
    public void Compose_SpaceUsesLowDyads_CombatPulses()
    {
        var space = SynthComposer.Compose(SynthMood.Space, 3);
        Assert.All(space.Chords, c => Assert.Equal(2, c.Length));
        Assert.True(space.RootHz < 120f);
        Assert.False(space.Pulse);

        var combat = SynthComposer.Compose(SynthMood.Combat, 3);
        Assert.True(combat.Pulse);
        Assert.All(combat.ArpPatterns[0], step => Assert.Equal(0, step)); // steady root pulse, no rests
    }

    [Theory]
    [InlineData(SynthMood.Menu, null)]
    [InlineData(SynthMood.Planet, MusicLibrary.PlanetVerdant)]
    [InlineData(SynthMood.Space, null)]
    [InlineData(SynthMood.Combat, null)]
    public void Render_IsBoundedAndSilentAtTheSeams(SynthMood mood, string? flavor)
    {
        // Render at a low rate to keep the test fast; the maths is rate-independent.
        var s = SynthComposer.Compose(mood, 5, flavor, sampleRate: 8000);
        var data = SynthComposer.RenderAll(s);
        Assert.Equal(s.TotalSamples, data.Length);
        float peak = 0f;
        foreach (float v in data)
        {
            Assert.False(float.IsNaN(v));
            peak = Math.Max(peak, Math.Abs(v));
        }

        Assert.True(peak <= 1f, $"peak {peak}");
        Assert.True(peak > 0.05f, "silent render");
        Assert.True(Math.Abs(data[0]) < 1e-3f, $"loop start not silent: {data[0]}");
        Assert.True(Math.Abs(data[^1]) < 1e-2f, $"loop end not silent: {data[^1]}");
        // Every chord boundary is a zero crossing of the envelope → no click between chords.
        for (int c = 1; c < s.Chords.Count; c++)
        {
            Assert.True(Math.Abs(data[c * s.ChordSamples]) < 1e-2f, $"chord seam {c} not silent");
        }
    }

    [Fact]
    public void Render_Chunked_EqualsOneShot()
    {
        var s = SynthComposer.Compose(SynthMood.Planet, 9, MusicLibrary.PlanetDesert, sampleRate: 8000);
        var whole = SynthComposer.RenderAll(s);
        var chunked = new float[s.TotalSamples];
        var buffer = new float[1234];
        for (int start = 0; start < chunked.Length; start += buffer.Length)
        {
            int count = Math.Min(buffer.Length, chunked.Length - start);
            SynthComposer.Render(s, buffer, start, count);
            Array.Copy(buffer, 0, chunked, start, count);
        }

        Assert.Equal(whole, chunked);
    }

    [Theory]
    [InlineData(SynthMood.Menu, null)]
    [InlineData(SynthMood.Planet, MusicLibrary.PlanetIce)]
    [InlineData(SynthMood.Space, null)]
    [InlineData(SynthMood.Combat, null)]
    public void Normalize_LandsEveryPieceAtTheModestTargetLevel(SynthMood mood, string? flavor)
    {
        // The Synth style must never be the loud one (owner decision 2026-08-22): every piece is pulled to
        // the same RMS (~7 dB under the track library) and capped in peak.
        for (int seed = 0; seed < 6; seed++)
        {
            var data = SynthComposer.RenderAll(SynthComposer.Compose(mood, seed, flavor, sampleRate: 8000));
            SynthComposer.Normalize(data);
            double sum = 0.0;
            float peak = 0f;
            foreach (float v in data)
            {
                sum += (double)v * v;
                peak = Math.Max(peak, Math.Abs(v));
            }

            float rms = (float)Math.Sqrt(sum / data.Length);
            Assert.True(peak <= SynthComposer.PeakCap + 1e-4f, $"{mood}/{flavor} seed {seed}: peak {peak}");
            Assert.True(rms <= SynthComposer.TargetRms + 1e-3f, $"{mood}/{flavor} seed {seed}: rms {rms}");
            Assert.True(rms >= SynthComposer.TargetRms * 0.5f, $"{mood}/{flavor} seed {seed}: rms {rms} far below target (peak-capped too hard)");
        }
    }

    [Fact]
    public void Normalize_SilentOrEmpty_IsLeftAlone()
    {
        var silent = new float[100];
        Assert.Equal(1f, SynthComposer.Normalize(silent));
        Assert.All(silent, v => Assert.Equal(0f, v));
        Assert.Equal(1f, SynthComposer.Normalize(Array.Empty<float>()));
    }

    [Fact]
    public void Render_PastTheEnd_WritesSilence()
    {
        var s = SynthComposer.Compose(SynthMood.Menu, 1, sampleRate: 8000);
        var buffer = new float[16];
        Array.Fill(buffer, 0.5f);
        SynthComposer.Render(s, buffer, s.TotalSamples + 100, buffer.Length);
        Assert.All(buffer, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Compose_RejectsTinySampleRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SynthComposer.Compose(SynthMood.Menu, 1, sampleRate: 100));
    }
}
