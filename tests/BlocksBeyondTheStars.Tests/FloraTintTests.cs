using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Per-(species × planet) flora tints: deterministic pure function (every client/server agrees with
/// zero traffic), uniform per species within a world, varied across species and across worlds, and
/// always inside the friendly saturation/value band the shader expects.
/// </summary>
public sealed class FloraTintTests
{
    [Fact]
    public void SameInputs_AlwaysGiveTheSameColour()
    {
        var a = FloraTints.For(1234, "Karth · Velda 2", "flora_fern");
        var b = FloraTints.For(1234, "Karth · Velda 2", "flora_fern");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Species_GetDistinctColours_WithinOneWorld()
    {
        string[] species =
        {
            "flora_fern", "flora_cactus", "flora_mushroom", "flora_tendril", "flora_glowcap",
            "flora_bulb", "flora_shardbloom", "tree_leaves",
        };

        var distinct = new HashSet<(int, int, int)>();
        foreach (var s in species)
        {
            var (r, g, b) = FloraTints.For(777, "world-a", s);
            distinct.Add(((int)(r * 100), (int)(g * 100), (int)(b * 100)));
        }

        // Hashes can collide in principle — but most species must clearly differ.
        Assert.True(distinct.Count >= species.Length - 1,
            $"Expected near-unique species colours, got {distinct.Count}/{species.Length}.");
    }

    [Fact]
    public void TheSameSpecies_ChangesColour_AcrossWorlds()
    {
        var distinct = new HashSet<(int, int, int)>();
        for (int w = 0; w < 8; w++)
        {
            var (r, g, b) = FloraTints.For(42, $"world-{w}", "flora_fern");
            distinct.Add(((int)(r * 100), (int)(g * 100), (int)(b * 100)));
        }

        Assert.True(distinct.Count >= 7, $"The same species should vary across worlds (got {distinct.Count}/8).");
    }

    [Fact]
    public void Colours_StayInsideTheFriendlyBand()
    {
        for (int i = 0; i < 200; i++)
        {
            var (r, g, b) = FloraTints.For(i * 31, "band-world", "flora_" + i);
            Assert.InRange(r, 0f, 1f);
            Assert.InRange(g, 0f, 1f);
            Assert.InRange(b, 0f, 1f);

            // Value floor of the HSV band: the brightest channel equals V ≥ 0.85 (never a murky tint),
            // and saturation ≥ 0.45 means the channels can't all be equal (never plain grey).
            float max = System.Math.Max(r, System.Math.Max(g, b));
            float min = System.Math.Min(r, System.Math.Min(g, b));
            Assert.True(max >= 0.84f, $"tint too dark: {r},{g},{b}");
            Assert.True(max - min >= 0.3f, $"tint too grey: {r},{g},{b}");
        }
    }
}
