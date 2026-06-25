// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Per-world bark tints: one deterministic colour per world (uniform across every trunk), fully random
/// in hue like the leaves, but forced into a DARK band so a trunk always reads clearly darker than ANY
/// leaf species on the same world — the two can never be confused regardless of hue.
/// </summary>
public sealed class WoodTintTests
{
    [Fact]
    public void SameInputs_AlwaysGiveTheSameColour()
    {
        var a = FloraTints.ForWood(1234, "Karth · Velda 2");
        var b = FloraTints.ForWood(1234, "Karth · Velda 2");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Bark_ChangesColour_AcrossWorlds()
    {
        var distinct = new System.Collections.Generic.HashSet<(int, int, int)>();
        for (int w = 0; w < 8; w++)
        {
            var (r, g, b) = FloraTints.ForWood(42, $"world-{w}");
            distinct.Add(((int)(r * 100), (int)(g * 100), (int)(b * 100)));
        }

        Assert.True(distinct.Count >= 7, $"Bark should vary across worlds (got {distinct.Count}/8).");
    }

    [Fact]
    public void Bark_StaysInsideTheDarkBand()
    {
        for (int i = 0; i < 200; i++)
        {
            var (r, g, b) = FloraTints.ForWood(i * 31, "band-world-" + i);
            Assert.InRange(r, 0f, 1f);
            Assert.InRange(g, 0f, 1f);
            Assert.InRange(b, 0f, 1f);

            // HSV value == the brightest channel. The bark band is val 0.30..0.58, so the brightest channel
            // must stay under ~0.59 (always dark) and over ~0.29 (never pure black / a void-coloured trunk).
            float max = System.Math.Max(r, System.Math.Max(g, b));
            Assert.True(max <= 0.59f, $"bark too bright: {r},{g},{b}");
            Assert.True(max >= 0.29f, $"bark too dark: {r},{g},{b}");
        }
    }

    [Fact]
    public void Bark_IsAlwaysClearlyDarkerThanEveryLeaf_OnTheSameWorld()
    {
        // The guarantee the feature exists for: on any world, the single bark colour is darker than every
        // leaf species (leaves sit at value >= 0.84, bark at value <= 0.59), so trunk and foliage can never
        // read as the same colour even when a leaf happens to roll a brownish hue.
        string[] leaves =
        {
            "tree_leaves", "pine_needles", "palm_frond", "flora_fern", "flora_vine", "flora_glowcap",
        };

        for (int w = 0; w < 50; w++)
        {
            string loc = $"world-{w}";
            var (br, bg, bb) = FloraTints.ForWood(w * 7 + 1, loc);
            float barkMax = System.Math.Max(br, System.Math.Max(bg, bb));

            foreach (var leaf in leaves)
            {
                var (lr, lg, lb) = FloraTints.For(w * 7 + 1, loc, leaf);
                float leafMax = System.Math.Max(lr, System.Math.Max(lg, lb));
                Assert.True(barkMax + 0.2f < leafMax,
                    $"bark not clearly darker than {leaf} on {loc}: bark max {barkMax}, leaf max {leafMax}");
            }
        }
    }
}
