// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The per-world water colour (#1758): which types opt in, what an old save keeps, what a new one rolls.</summary>
public sealed class FluidTintTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void EveryWaterSeaTypeWithAir_OptsIntoAWaterColour()
    {
        // Marcel 2026-09-11 ("ja mach das"): the children's "water in different colours per world" — every type
        // with a real water sea and an atmosphere carries a water tint; dry, lava and airless bodies stay blue.
        foreach (var p in Content.Planets.Values)
        {
            if (p.Key is "orbital_station" or "ship_interior")
            {
                continue;
            }

            bool air = !string.Equals(p.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);
            double water = p.WaterAbundance ?? (air ? 0.55 : 0.0);
            bool sea = air && water >= 0.3 && (p.LavaAbundance ?? 0.0) <= 0.0;
            if (sea)
            {
                Assert.False(string.IsNullOrEmpty(p.WaterTint), $"{p.Key} has a water sea and air but no waterTint");
            }
            else
            {
                Assert.True(string.IsNullOrEmpty(p.WaterTint), $"{p.Key} has no water sea yet a waterTint");
            }
        }
    }

    [Fact]
    public void AutoTint_KeepsOldSavesBlue_AndRollsPerWorldOnGenerationFive()
    {
        var ocean = Content.GetPlanet("ocean")!;
        Assert.Equal("auto", ocean.WaterTint);
        Assert.Equal((FluidTints.ClassicWater, FluidTints.Mode.Classic), FluidTints.ForWorld(4242, "sys0-p1", ocean, 4));
        Assert.Equal((FluidTints.ClassicWater, FluidTints.Mode.Classic), FluidTints.ForWorld(4242, "sys0-p1", ocean, 0));

        var a = FluidTints.ForWorld(4242, "sys0-p1", ocean, WorldDescription.AuthoredContentGeneration);
        Assert.Equal(FluidTints.Mode.Tint, a.Mode);
        Assert.Equal(a, FluidTints.ForWorld(4242, "sys0-p1", ocean, WorldDescription.AuthoredContentGeneration));

        // The palette is blue-dominant but not all blue: across many worlds more than one colour family shows up,
        // and the classic blue stays the most common pick.
        var families = new HashSet<int>();
        int classicLike = 0, total = 0;
        for (long seed = 1; seed <= 60; seed++)
            for (int body = 0; body < 6; body++)
            {
                var (rgb, mode) = FluidTints.ForWorld(seed * 7919, $"sys{body}-p{body}", ocean, WorldDescription.AuthoredContentGeneration);
                Assert.Equal(FluidTints.Mode.Tint, mode);
                int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
                families.Add(b > r && b > g ? 0 : g > r ? 1 : 2); // blue / green-teal / warm
                if (System.Math.Abs(r - 0x33) <= 16 && System.Math.Abs(g - 0x6B) <= 16 && System.Math.Abs(b - 0xD9) <= 16)
                {
                    classicLike++;
                }

                total++;
            }

        Assert.True(families.Count >= 2, "every world rolled the same colour family");
        Assert.True(classicLike >= total * 0.35, $"the classic blue should stay the most common pick ({classicLike}/{total})");
    }

    [Fact]
    public void RainbowType_IsRainbowOnlyOnGenerationFive()
    {
        var rainbow = Content.GetPlanet("rainbow_sea")!;
        Assert.Equal(FluidTints.Mode.Rainbow, FluidTints.ForWorld(1, "sys0-p0", rainbow, 5).Mode);
        Assert.Equal(FluidTints.Mode.Classic, FluidTints.ForWorld(1, "sys0-p0", rainbow, 4).Mode);
    }
}
