// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Glow patterns (#2862), eye looks (#2866) and horns (#2867): who wears what, deterministic per species, and the drawn
/// textures well-formed.
/// </summary>
public sealed class GlowEyesHornsTests
{
    private const int S = CoatMasks.Size;

    private static NetCreature Species(int seed, bool glows = true, string plan = "Standard", int legs = 4,
        string temperament = "Passive", bool fins = false, bool crest = false, int tentacles = 0, string id = "sp2", int eyeRgb = 0)
        => new NetCreature
        {
            Id = "e" + seed,
            SpeciesId = id,
            VoiceSeed = seed,
            Glows = glows,
            BodyPlan = plan,
            Legs = legs,
            Temperament = temperament,
            HasFins = fins,
            HasCrest = crest,
            Tentacles = tentacles,
            ColorRgb = 0x3A7BD5,
            EyeRgb = eyeRgb,
        };

    // ---------------- glow ----------------

    [Fact]
    public void OnlyGlowingSpecies_GlowInAPattern_AndEveryPatternShowsUp()
    {
        var seen = new HashSet<GlowPattern>();
        for (int s = 1; s < 2000; s++)
        {
            Assert.Equal(GlowPattern.None, GlowMasks.PatternFor(Species(s, glows: false, fins: true)));
            var g = GlowMasks.PatternFor(Species(s, fins: true));
            Assert.NotEqual(GlowPattern.None, g);
            seen.Add(g);
            Assert.NotEqual(GlowPattern.Tips, GlowMasks.PatternFor(Species(s))); // no fins, crest or tentacles: no tips
        }

        Assert.Equal(Enum.GetValues(typeof(GlowPattern)).Length - 1, seen.Count);
    }

    [Theory]
    [InlineData(GlowPattern.SpotRows)]
    [InlineData(GlowPattern.Freckles)]
    [InlineData(GlowPattern.Veins)]
    [InlineData(GlowPattern.Rings)]
    [InlineData(GlowPattern.LateralLines)]
    [InlineData(GlowPattern.Blotches)]
    public void AGlowMask_IsDeterministic_BinarySparse_AndTiles(GlowPattern pattern)
    {
        foreach (uint seed in new uint[] { 1, 77, 0xBEEF })
        {
            var a = GlowMasks.Draw(pattern, CoatPart.Torso, seed);
            Assert.Equal(a, GlowMasks.Draw(pattern, CoatPart.Torso, seed));
            Assert.All(a, v => Assert.True(v == 0f || v == 1f));
            Assert.InRange(a.Average(), 0.005, 0.35); // a pattern, not a glowing body
            CoatMasksTests.AssertSeamless(a, horizontal: true);
            CoatMasksTests.AssertSeamless(a, horizontal: false);
        }

        Assert.All(GlowMasks.Draw(pattern, CoatPart.Belly, 1), v => Assert.Equal(0f, v));
        Assert.All(GlowMasks.Draw(pattern, CoatPart.Jaw, 1), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Tips_LightTheAccentsFully()
    {
        Assert.Equal(CoatCoverage.Full, GlowMasks.Coverage(GlowPattern.Tips, CoatPart.Ornament));
        Assert.All(GlowMasks.Draw(GlowPattern.Tips, CoatPart.Ornament, 3), v => Assert.Equal(1f, v));
        Assert.Equal(CoatCoverage.None, GlowMasks.Coverage(GlowPattern.SpotRows, CoatPart.Ornament));
        Assert.Equal(CoatCoverage.None, GlowMasks.Coverage(GlowPattern.None, CoatPart.Torso));
    }

    [Fact]
    public void TheGlowColour_IsLuminous_AndStablePerSpecies()
    {
        for (uint s = 1; s < 500; s++)
        {
            int rgb = GlowMasks.ColourFor(s, 0x404040 + (int)s);
            Assert.Equal(rgb, GlowMasks.ColourFor(s, 0x404040 + (int)s));
            CreatureCoat.Unpack(rgb, out float r, out float g, out float b);
            CreatureCoat.RgbToHsv(r, g, b, out _, out _, out float v);
            Assert.True(v >= 0.95f, $"{rgb:X6} is not luminous");
        }
    }

    // ---------------- eyes ----------------

    [Fact]
    public void EveryEyeLook_IsReachable_AndFitsTheBody()
    {
        var seen = new HashSet<EyeLook>();
        int arachnidCompound = 0, hunterSlits = 0;
        for (int s = 1; s < 3000; s++)
        {
            seen.Add(CreatureEyes.LookFor(Species(s)));
            seen.Add(CreatureEyes.LookFor(Species(s, temperament: "Aggressive")));
            seen.Add(CreatureEyes.LookFor(Species(s, plan: "Arachnid", legs: 8)));
            arachnidCompound += CreatureEyes.LookFor(Species(s, plan: "Arachnid", legs: 8)) == EyeLook.Compound ? 1 : 0;
            hunterSlits += CreatureEyes.LookFor(Species(s, temperament: "PackHunter")) == EyeLook.Slit ? 1 : 0;
        }

        Assert.Equal(Enum.GetValues(typeof(EyeLook)).Length, seen.Count);
        Assert.InRange(arachnidCompound / 3000.0, 0.45, 0.70);
        Assert.InRange(hunterSlits / 3000.0, 0.35, 0.55);
        Assert.Equal(EyeLook.Round, CreatureEyes.LookFor(Species(1, id: "au_leni")));
        Assert.Equal(EyeLook.Button, CreatureEyes.LookFor(Species(1, plan: "Floral")));
    }

    [Fact]
    public void TheEyeTexture_IsDeterministic_HasAPupil_AndOnlyAGlowingIrisShines()
    {
        foreach (EyeLook look in Enum.GetValues(typeof(EyeLook)))
        {
            var a = CreatureEyes.Draw(look, 0x4E9A3A, glows: false);
            Assert.Equal(CreatureEyes.Size * CreatureEyes.Size * 4, a.Length);
            Assert.Equal(a, CreatureEyes.Draw(look, 0x4E9A3A, glows: false));
            Assert.All(Enumerable.Range(0, a.Length / 4), i => Assert.Equal(255, a[i * 4 + 3]));

            var lit = CreatureEyes.Draw(look, 0x4E9A3A, glows: true);
            int shining = Enumerable.Range(0, lit.Length / 4).Count(i => lit[i * 4 + 3] == 0);
            Assert.True(shining > 20, look + " has no shining iris");

            // The centre of every non-compound eye is dark (a pupil) or the iris; the corner is the sclera.
            int corner = 0;
            Assert.Equal(CreatureEyes.SphereRgb(look, 0x4E9A3A, false),
                (a[corner] << 16) | (a[corner + 1] << 8) | a[corner + 2]);
        }

        Assert.Equal(0x4E9A3A, CreatureEyes.IrisRgb(Species(3, eyeRgb: 0x4E9A3A)));
        Assert.Contains(CreatureEyes.IrisRgb(Species(3)), CreatureEyes.IrisPalette);
        Assert.True(CreatureEyes.IrisGlows(Species(3, glows: false, eyeRgb: 0x30FF30)));
    }

    [Fact]
    public void SphereUv_ProjectsTheFrontOntoTheTexture_AndTheBackOntoTheSclera()
    {
        CreatureEyes.SphereUv(0f, 0f, 0.5f, out float u, out float v);
        Assert.Equal(0.5f, u, 4);
        Assert.Equal(0.5f, v, 4);
        CreatureEyes.SphereUv(0.2f, -0.1f, 0.4f, out u, out v);
        Assert.Equal(0.7f, u, 4);
        Assert.Equal(0.4f, v, 4);
        CreatureEyes.SphereUv(0.2f, 0.1f, -0.4f, out u, out v);
        Assert.True(u < 0.1f && v < 0.1f);
    }

    [Fact]
    public void TheEyeSphere_IsClosed_WoundOutward_AndNeverMixesTheFrontTextureWithTheBack()
    {
        var d = CreatureEyes.BuildSphere();
        int n = d.Positions.Length / 3;
        for (int i = 0; i < n; i++)
        {
            float x = d.Positions[i * 3], y = d.Positions[i * 3 + 1], z = d.Positions[i * 3 + 2];
            Assert.InRange((float)Math.Sqrt(x * x + y * y + z * z), 0.499f, 0.501f);
        }

        for (int t = 0; t < d.Triangles.Length; t += 3)
        {
            int a = d.Triangles[t], b = d.Triangles[t + 1], c = d.Triangles[t + 2];
            (float X, float Y, float Z) P(int i) => (d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
            var (ax, ay, az) = P(a);
            var (bx, by, bz) = P(b);
            var (cx, cy, cz) = P(c);
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float mx = (ax + bx + cx) / 3f, my = (ay + by + cy) / 3f, mz = (az + bz + cz) / 3f;
            Assert.True(nx * mx + ny * my + nz * mz > 0f, $"triangle {t / 3} faces inward");

            // A triangle is all front (projected) or all back (the sclera corner) — none spans both.
            bool Back(int i) => d.Uvs[i * 2] == 0.03f && d.Uvs[i * 2 + 1] == 0.03f;
            Assert.True(Back(a) == Back(b) && Back(b) == Back(c), $"triangle {t / 3} mixes front and back");
        }

        // The front pole looks at the texture's centre.
        int front = Enumerable.Range(0, n).First(i => d.Positions[i * 3 + 2] > 0.4999f);
        Assert.Equal(0.5f, d.Uvs[front * 2], 4);
        Assert.Equal(0.5f, d.Uvs[front * 2 + 1], 4);
    }

    // ---------------- horns ----------------

    [Fact]
    public void EveryHornTile_IsReachable_AndCrystalWorldsGrowCrystal()
    {
        var seen = new HashSet<string>();
        int crystal = 0;
        var crystalWorld = new FaunaThemes.Look("crystal", "crystal", "scales");
        for (int s = 1; s < 1000; s++)
        {
            string tile = CreatureHorns.TileFor(Species(s), null);
            Assert.Contains(tile, CreatureHorns.Tiles);
            Assert.Equal(tile, CreatureHorns.TileFor(Species(s), null));
            seen.Add(tile);
            crystal += CreatureHorns.TileFor(Species(s), crystalWorld) == "crystal" ? 1 : 0;
        }

        Assert.Equal(CreatureHorns.Tiles.Count, seen.Count);
        Assert.InRange(crystal / 1000.0, 0.45, 0.65);
        for (int s = 1; s < 200; s++)
        {
            // An authored species wears the same horns on every world.
            Assert.Equal(CreatureHorns.TileFor(Species(s, id: "au_leni"), null), CreatureHorns.TileFor(Species(s, id: "au_leni"), crystalWorld));
        }
        Assert.Equal("creature_horn_ridged", CreatureHorns.TileKey("ridged"));
    }

    [Fact]
    public void HornColours_SpreadOverThePalette_AndToothColoursStayLight()
    {
        var colours = Enumerable.Range(1, 600).Select(s => CreatureHorns.ColourFor(Species(s), "ridged")).ToHashSet();
        Assert.Equal(CreatureHorns.Palette.Count, colours.Count);
        for (int s = 1; s < 300; s++)
        {
            Assert.Contains(CreatureHorns.ToothColourFor(Species(s)), CreatureHorns.ToothPalette);
            Assert.Contains(CreatureHorns.TuskColourFor(Species(s)), CreatureHorns.TuskPalette);
            Assert.Contains(CreatureHorns.DarkColourFor(Species(s)), CreatureHorns.DarkPalette);
        }
    }
}
