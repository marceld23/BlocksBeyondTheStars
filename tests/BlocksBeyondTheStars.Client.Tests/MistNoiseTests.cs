// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3272/#3273: the mist cards and the sea of clouds scroll their noise with the wind, so <see cref="MistNoise"/> must tile
/// without a seam and fill the 0..1 range (the shaders' thresholds assume it).
/// </summary>
public sealed class MistNoiseTests
{
    private const int Size = 64;

    [Fact]
    public void TheNoise_FillsZeroToOne_AndIsDeterministic()
    {
        float[] a = MistNoise.Tileable(Size, 4, 4, 3273);
        float[] b = MistNoise.Tileable(Size, 4, 4, 3273);
        float[] other = MistNoise.Tileable(Size, 4, 4, 3272);

        Assert.Equal(Size * Size, a.Length);
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        Assert.Equal(0f, a.Min(), 4);
        Assert.Equal(1f, a.Max(), 4);
        Assert.InRange(a.Average(), 0.25f, 0.75f);
    }

    [Fact]
    public void TheNoise_TilesWithoutASeam()
    {
        float[] v = MistNoise.Tileable(Size, 4, 4, 77);
        float maxStep = 0f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x + 1 < Size; x++)
            {
                maxStep = Math.Max(maxStep, Math.Abs(v[(y * Size) + x + 1] - v[(y * Size) + x]));
                maxStep = Math.Max(maxStep, Math.Abs(v[(x * Size) + y] - v[((x + 1) * Size) + y]));
            }
        }

        // Across the wrap (last column → first column, last row → first row) the noise steps no more than anywhere inside.
        for (int i = 0; i < Size; i++)
        {
            float acrossX = Math.Abs(v[(i * Size) + Size - 1] - v[i * Size]);
            float acrossY = Math.Abs(v[((Size - 1) * Size) + i] - v[i]);
            Assert.True(acrossX <= maxStep + 1e-4f, $"seam along x in row {i}: {acrossX} > {maxStep}");
            Assert.True(acrossY <= maxStep + 1e-4f, $"seam along y in column {i}: {acrossY} > {maxStep}");
        }
    }

    [Fact]
    public void ABadSize_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MistNoise.Tileable(0, 4, 4, 1));
        Assert.Single(MistNoise.Tileable(1, 0, 0, 1)); // degenerate but valid: cells and octaves clamp to 1
    }
}
