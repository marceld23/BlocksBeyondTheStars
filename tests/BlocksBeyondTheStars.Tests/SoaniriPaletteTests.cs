// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3423 (Justus, F1 report 2026-11-08: "everything on the planet — the Sounders, Soundelings, Soundis, Soundraks, the plants —
/// light blue and light green"): every colour Soaniri's creatures and plants are drawn in sits in the blue/green hue band
/// (<see cref="MinHue"/>–<see cref="MaxHue"/>: light green to light blue) — the four authored records and the rules' own
/// colours, the Sounders' skins and sashes, the Soundelings' pastels, the moss and the singing plants (their block colours and
/// their bundled tiles), and the wild flora's per-world colours from the planet's palette. The bodies stay readable on the moss,
/// and a type without a palette keeps the classic any-hue roll. #3448 (Marcel, 2026-11-09): the rolled animals wear the planet's
/// fauna palette too — light, clear of the moss, without moving any other roll — while the authored records keep their colours.
/// </summary>
public sealed class SoaniriPaletteTests
{
    /// <summary>The band's lower edge in degrees: a yellowish light green.</summary>
    private const double MinHue = 85.0;

    /// <summary>The band's upper edge in degrees: a light cornflower blue.</summary>
    private const double MaxHue = 230.0;

    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));
    private static readonly string TexturesDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
    private static readonly string[] Creatures = { SoundersIds.Soundi, SoundersIds.Soundrak, SoundersIds.Quali, SoundersIds.Soundeling };
    private static readonly string[] Plants = { "chime_moss", "chime_reed", "bell_bloom", "bell_bloom_open", "harp_vine", "echo_flower" };

    [Fact]
    public void TheCreaturesOfSoaniri_AreLightBlueAndLightGreen()
    {
        var authored = Shipped.Value.AuthoredCreatures;
        foreach (string key in Creatures)
        {
            AssertLight(authored[key].ColorRgb, key + " body");
            AssertInBand(authored[key].BellyRgb, key + " belly");
        }

        Assert.Equal(SoundiRules.Fur, authored[SoundersIds.Soundi].ColorRgb);
        Assert.Equal(SoundrakRules.Skin, authored[SoundersIds.Soundrak].ColorRgb);
        Assert.Equal(SoundrakRules.Belly, authored[SoundersIds.Soundrak].BellyRgb);
        Assert.All(SoundelingRules.Pastels, c => AssertLight(c, "Soundeling pastel"));
        Assert.All(SounderRules.SkinTones, c => AssertLight((int)c, "Sounder skin"));
        Assert.All(SounderRules.SashTones, c => AssertInBand((int)c, "Sounder sash"));
        AssertInBand((int)SounderRules.EchoTeal, "Echo");
        AssertInBand((int)SounderRules.EchoGlow, "Echo glow");
    }

    [Fact]
    public void TheBodies_StandOutAgainstTheMoss()
    {
        var (mossH, _, mossV) = Hsv(MeanColour("chime_moss"));
        var bodies = Creatures.Where(k => k != SoundersIds.Quali).Select(k => Shipped.Value.AuthoredCreatures[k].ColorRgb)
            .Concat(SoundelingRules.Pastels).Concat(SounderRules.SkinTones.Select(c => (int)c));
        foreach (int body in bodies)
        {
            var (h, _, v) = Hsv(body);
            Assert.True(HueDistance(h, mossH) >= 30.0 || v - mossV >= 0.15,
                $"#{body:X6} (hue {h:0}°, value {v:0.00}) melts into the moss (hue {mossH:0}°, value {mossV:0.00})");
        }
    }

    [Fact]
    public void TheRolledAnimalsOfSoaniri_WearTheFaunaPalette_LightAndClearOfTheMoss()
    {
        // #3448: the five rolled species of the "few" roster used to take any colour (the biome anchors land on orange, aqua
        // and magenta); the planet's fauna palette dresses them now — and the authored four (the Soundi!) keep theirs.
        var soaniri = Shipped.Value.GetPlanet(SoundersIds.PlanetType)!;
        Assert.Equal(soaniri.FaunaPalette.Count, FaunaTints.PaletteOf(soaniri).Count); // every entry parses
        Assert.All(FaunaTints.PaletteOf(soaniri), c => AssertLight(c, "fauna palette entry"));
        var (mossH, _, mossV) = Hsv(MeanColour("chime_moss"));
        for (long seed = 1; seed <= 24; seed++)
        {
            var roster = Roster(Shipped.Value, soaniri, seed * 7919);
            var rolled = roster.Where(s => !s.Id.StartsWith(AuthoredPrefix, StringComparison.Ordinal)).ToList();
            Assert.Equal(5, rolled.Count);
            foreach (var sp in rolled)
            {
                AssertLight(sp.ColorRgb, sp.Name + " body");
                AssertLight(sp.BellyRgb, sp.Name + " belly");
                var (h, _, v) = Hsv(sp.ColorRgb);
                Assert.True(HueDistance(h, mossH) >= 30.0 || v - mossV >= 0.15, $"{sp.Name} #{sp.ColorRgb:X6} melts into the moss");
            }

            foreach (var sp in roster.Where(s => s.Id.StartsWith(AuthoredPrefix, StringComparison.Ordinal)))
            {
                var record = Shipped.Value.AuthoredCreatures[sp.Id.Substring(AuthoredPrefix.Length)];
                Assert.Equal(record.ColorRgb, sp.ColorRgb);
                Assert.Equal(record.BellyRgb, sp.BellyRgb);
            }
        }
    }

    [Fact]
    public void TheFaunaPalette_ChangesOnlyTheRolledColours_AndNoOtherTypeHasOne()
    {
        // No RNG draw is added: the same world without the palette rolls the very same species, only their colours differ —
        // and every other type has no palette, so its rosters are bit-identical to before.
        var bareContent = ContentLoader.LoadFromDirectory(TestPaths.DataDir()); // a private copy: its Soaniri loses the palette
        var bare = bareContent.GetPlanet(SoundersIds.PlanetType)!;
        bare.FaunaPalette = new List<string>();
        var soaniri = Shipped.Value.GetPlanet(SoundersIds.PlanetType)!;
        var palette = FaunaTints.PaletteOf(soaniri);
        for (long seed = 1; seed <= 12; seed++)
        {
            var dressed = Roster(Shipped.Value, soaniri, seed * 104729);
            var classic = Roster(bareContent, bare, seed * 104729);
            Assert.Equal(classic.Count, dressed.Count);
            for (int i = 0; i < dressed.Count; i++)
            {
                Assert.Equal((classic[i].Id, classic[i].Name, classic[i].VoiceSeed, classic[i].Size, classic[i].BodyPlan, classic[i].Habitat),
                    (dressed[i].Id, dressed[i].Name, dressed[i].VoiceSeed, dressed[i].Size, dressed[i].BodyPlan, dressed[i].Habitat));
                var expected = dressed[i].Id.StartsWith(AuthoredPrefix, StringComparison.Ordinal)
                    ? (classic[i].ColorRgb, classic[i].BellyRgb)
                    : FaunaTints.CoatFor(classic[i].VoiceSeed, palette);
                Assert.Equal(expected, (dressed[i].ColorRgb, dressed[i].BellyRgb));
            }
        }

        Assert.All(Shipped.Value.Planets.Values.Where(p => p.Key != SoundersIds.PlanetType), p => Assert.Empty(p.FaunaPalette));
    }

    [Fact]
    public void TheFaunaPalette_SkipsBadEntries_AndTheBellyIsALighterEntryOrTheBodyLightened()
    {
        var planet = new PlanetType { Key = "x", FaunaPalette = new List<string> { "#9fd8f5", "blue", "#12345", " #a8f0d0 " } };
        Assert.Equal(new[] { 0x9FD8F5, 0xA8F0D0 }, FaunaTints.PaletteOf(planet));
        Assert.Empty(FaunaTints.PaletteOf(null));
        Assert.Equal((0, 0), FaunaTints.CoatFor(7, FaunaTints.PaletteOf(null)));

        var palette = FaunaTints.PaletteOf(Shipped.Value.GetPlanet(SoundersIds.PlanetType));
        int fromAnotherEntry = 0;
        for (int seed = 1; seed <= 500; seed++)
        {
            var (body, belly) = FaunaTints.CoatFor(seed, palette);
            Assert.Equal((body, belly), FaunaTints.CoatFor(seed, palette)); // a pure function of the species' seed
            Assert.True(FaunaTints.Luma(belly) > FaunaTints.Luma(body), $"#{body:X6} / #{belly:X6}");
            bool entry = palette.Contains(belly);
            Assert.True(entry || belly == FaunaTints.Lighten(body, FaunaTints.BellyLighten), $"#{belly:X6}");
            fromAnotherEntry += entry ? 1 : 0;
        }

        Assert.InRange(fromAnotherEntry, 50, 450); // both kinds of belly occur
    }

    [Fact]
    public void TheMossAndTheSingingPlants_SitInTheBand_TheirBlockColoursAndTheirTiles()
    {
        foreach (string key in Plants)
        {
            var block = Shipped.Value.GetBlock(key)!;
            AssertInBand(block.Color!.Value, key);
        }

        AssertInBand(Shipped.Value.GetBlock("resonance_crystal")!.LightColor!.Value & 0xFFFFFF, "resonance_crystal light");
        foreach (string key in Plants.Append("resonance_crystal"))
        {
            int coloured = 0, inBand = 0;
            byte[] raw = File.ReadAllBytes(Path.Combine(TexturesDir, key + ".bytes"));
            Assert.Equal(64 * 64 * 4, raw.Length);
            for (int i = 0; i < raw.Length; i += 4)
            {
                var (h, s, v) = Hsv(raw[i] / 255.0, raw[i + 1] / 255.0, raw[i + 2] / 255.0);
                if (raw[i + 3] < 128 || s < 0.15 || v < 0.15)
                {
                    continue; // the cut-out black, greys, the white of a glow
                }

                coloured++;
                inBand += h is >= MinHue and <= MaxHue ? 1 : 0;
            }

            Assert.True(coloured > 0, key);
            Assert.True(inBand >= coloured * 0.95, $"{key}: only {inBand} of {coloured} coloured pixels are light blue or light green");
        }
    }

    [Fact]
    public void TheWildFlora_WearsThePlanetsLightGreens_AllYear()
    {
        var soaniri = Shipped.Value.GetPlanet(SoundersIds.PlanetType)!;
        Assert.True(soaniri.EvergreenFlora, "autumn must not turn Soaniri's plants amber and red");
        Assert.NotEmpty(soaniri.FloraPalette);
        Assert.Equal(soaniri.FloraPalette.Count, FloraTints.PaletteOf(soaniri).Count); // every entry parses
        string[] keys = FloraCatalog.All.Select(s => s.Key).Append("tree_leaves").Append("flora_grasstuft").ToArray();
        for (long seed = 1; seed <= 12; seed++)
        {
            string location = "Ornemis · Soaniri";
            foreach (string key in keys)
            {
                var (r, g, b) = FloraTints.For(seed * 7919, location, key, soaniri);
                AssertInBand(FloraTints.ToRgb24((r, g, b), 1f), key + " tint");
            }

            AssertInBand(FloraTints.ForWorld(seed * 7919, location, WorldDescription.CurrentTerrainGeneration, soaniri), "world hue");
        }
    }

    [Fact]
    public void ATypeWithoutAPalette_KeepsTheClassicRoll()
    {
        var meadow = Shipped.Value.GetPlanet("meadowlands")!;
        Assert.Empty(meadow.FloraPalette);
        Assert.False(meadow.EvergreenFlora);
        foreach (var sp in FloraCatalog.All.Take(20))
        {
            Assert.Equal(FloraTints.For(42, "Somewhere", sp.Key), FloraTints.For(42, "Somewhere", sp.Key, meadow));
            Assert.Equal(FloraTints.For(42, "Somewhere", sp.Key), FloraTints.For(42, "Somewhere", sp.Key, null));
        }

        int gen = WorldDescription.CurrentTerrainGeneration;
        Assert.Equal(FloraTints.ForWorld(42, "Somewhere", gen), FloraTints.ForWorld(42, "Somewhere", gen, meadow));
        var vine = Shipped.Value.Blocks.Values.First(b => b.LightColor is int l && (l & 0xFFFFFF) != 0 && b.Key.StartsWith("flora_", StringComparison.Ordinal));
        Assert.Equal(PlantGlowLight.PackedFor(42, "Somewhere", vine), PlantGlowLight.PackedFor(42, "Somewhere", vine, meadow));
    }

    /// <summary>The roster id prefix of an authored species.</summary>
    private const string AuthoredPrefix = "au_";

    /// <summary>A current-generation world's roster of <paramref name="planet"/>, its authored species included.</summary>
    private static IReadOnlyList<CreatureSpecies> Roster(GameContent content, PlanetType planet, long seed)
        => CreatureGenerator.GenerateRoster(planet, seed, WorldDescription.CurrentTerrainGeneration, content.AuthoredCreaturesFor(planet));

    private static void AssertInBand(int rgb, string what)
    {
        var (h, s, _) = Hsv(rgb);
        Assert.True(s < 0.08 || h is >= MinHue and <= MaxHue, $"{what} #{rgb:X6} has hue {h:0}° — not light blue or light green");
    }

    private static void AssertLight(int rgb, string what)
    {
        AssertInBand(rgb, what);
        Assert.True(Hsv(rgb).V >= 0.55, $"{what} #{rgb:X6} is too dark to read as light blue or light green");
    }

    private static int MeanColour(string tile)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(TexturesDir, tile + ".bytes"));
        long r = 0, g = 0, b = 0, n = raw.Length / 4;
        for (int i = 0; i < raw.Length; i += 4)
        {
            r += raw[i];
            g += raw[i + 1];
            b += raw[i + 2];
        }

        return (int)((r / n) << 16 | (g / n) << 8 | (b / n));
    }

    private static double HueDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }

    private static (double H, double S, double V) Hsv(int rgb)
        => Hsv(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);

    private static (double H, double S, double V) Hsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h;
        if (d <= 0.0)
        {
            h = 0.0;
        }
        else if (r >= g && r >= b)
        {
            h = 60.0 * (((g - b) / d) % 6.0);
        }
        else if (g >= b)
        {
            h = 60.0 * (((b - r) / d) + 2.0);
        }
        else
        {
            h = 60.0 * (((r - g) / d) + 4.0);
        }

        return (h < 0.0 ? h + 360.0 : h, max <= 0.0 ? 0.0 : d / max, max);
    }
}
