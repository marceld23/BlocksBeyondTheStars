// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The per-world mist profile (#3269) as a pure function: deterministic per (seed, location), different between two
/// worlds of one type, nothing on airless bodies, bright colours with about 60 % of the worlds clearly tinted, a cloud band
/// on the server's cloud line, and the fog frequency in the weather model. Property tests only — no golden float values.
/// </summary>
public sealed class MistProfileTests
{
    private const long Seed = 4242;

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static IEnumerable<string> Worlds(int count) => Enumerable.Range(0, count).Select(i => $"sys{i % 40}-p{i / 40}");

    private static PlanetType Planet(string key) => Content.GetPlanet(key) ?? throw new KeyNotFoundException(key);

    private static bool HasAir(PlanetType p) => !p.IsAirless && !p.SpaceSky && !p.Void && !p.IsGasWorld;

    private static MistProfile Profile(string world, PlanetType planet, double density = 0.5, long seed = Seed)
        => MistProfiles.For(seed, world, planet, density, airless: false);

    private static (int R, int G, int B) Rgb(int c) => ((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);

    private static int Chroma(int c)
    {
        var (r, g, b) = Rgb(c);
        return System.Math.Max(r, System.Math.Max(g, b)) - System.Math.Min(r, System.Math.Min(g, b));
    }

    private static double Luma(int c)
    {
        var (r, g, b) = Rgb(c);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    [Fact]
    public void EveryPlanetType_HasAMistValue_InRange()
    {
        Assert.NotEmpty(Content.Planets);
        foreach (var p in Content.Planets.Values)
        {
            Assert.True(p.Mist.HasValue, $"planet type '{p.Key}' has no \"mist\" value in planets.json");
            Assert.InRange(p.Mist!.Value, 0.0, 1.0);
        }
    }

    [Fact]
    public void TheSameWorld_AlwaysGetsTheSameProfile()
    {
        foreach (var p in Content.Planets.Values)
        {
            foreach (var world in Worlds(12))
            {
                Assert.Equal(Profile(world, p), Profile(world, p));
                Assert.Equal(MistProfiles.SummitLineY(Seed, world, p), MistProfiles.SummitLineY(Seed, world, p));
            }
        }
    }

    [Fact]
    public void TwoWorldsOfOneType_Differ()
    {
        var jungle = Planet("jungle");
        var profiles = Worlds(200).Select(w => Profile(w, jungle)).ToList();
        Assert.True(profiles.Select(m => m.Amount).Distinct().Count() > 150, "the mist amount must vary per world");
        Assert.True(profiles.Select(m => m.ColorRgb).Distinct().Count() > 100, "the mist colour must vary per world");
        Assert.True(profiles.Select(m => m.Wispiness).Distinct().Count() > 150, "the wispiness must vary per world");
        Assert.True(profiles.Select(m => m.FogFrequency).Distinct().Count() > 150, "the fog frequency must vary per world");
        Assert.True(profiles.Select(m => m.CloudBaseY).Distinct().Count() >= 12, "the cloud band must sit at different heights"); // a 0..12 lift

        // Neighbouring bodies and the same body in another save differ too (the mixed hash, not h·31+c).
        Assert.NotEqual(Profile("sys0-p1", jungle), Profile("sys0-p2", jungle));
        Assert.NotEqual(Profile("sys0-p1", jungle), Profile("sys0-p1", jungle, seed: Seed + 1));
    }

    [Fact]
    public void AirlessSpaceSkyVoidAndGasWorlds_HaveNoMistAndNoBand()
    {
        var none = Content.Planets.Values.Where(p => !HasAir(p)).ToList();
        Assert.Contains(none, p => p.IsAirless);
        Assert.Contains(none, p => p.SpaceSky);
        Assert.Contains(none, p => p.IsGasWorld);
        foreach (var p in none)
        {
            foreach (var world in Worlds(20))
            {
                var m = Profile(world, p);
                Assert.Equal(0f, m.Amount);
                Assert.Equal(-1, m.ColorRgb);
                Assert.False(m.HasCloudBand, $"{p.Key} must have no cloud band");
                Assert.Equal(WeatherProjection.CloudLineY(p), MistProfiles.SummitLineY(Seed, world, p));
            }
        }

        // The caller's own "no air" verdict wins as well.
        Assert.Equal(MistProfile.None, MistProfiles.For(Seed, "sys0-p1", Planet("jungle"), 0.8, airless: true));
    }

    [Fact]
    public void MistColours_AreAlwaysBright_AndNeverNeon()
    {
        foreach (var p in Content.Planets.Values.Where(HasAir))
        {
            foreach (var world in Worlds(150))
            {
                int c = Profile(world, p).ColorRgb;
                var (r, g, b) = Rgb(c);
                int max = System.Math.Max(r, System.Math.Max(g, b));
                int min = System.Math.Min(r, System.Math.Min(g, b));
                Assert.True(c is >= 0 and <= 0xFFFFFF, $"{p.Key}/{world}: {c:X6} is not a colour");
                Assert.True(Luma(c) >= MistProfiles.MinLuma - 0.005, $"{p.Key}/{world}: {c:X6} reads dark (luma {Luma(c):F3})");
                Assert.True(max >= 215, $"{p.Key}/{world}: {c:X6} is not bright");
                Assert.True(min >= 110, $"{p.Key}/{world}: {c:X6} has a dark channel");
                Assert.True((max - min) / (double)max <= 0.5, $"{p.Key}/{world}: {c:X6} is too saturated");
            }
        }
    }

    [Fact]
    public void AboutSixtyPercentOfTheWorlds_AreClearlyTinted_TheRestWhiteOrLightGrey()
    {
        int tinted = 0, total = 0;
        foreach (var p in Content.Planets.Values.Where(p => HasAir(p) && p.MistColor == 0))
        {
            foreach (var world in Worlds(120))
            {
                int chroma = Chroma(Profile(world, p).ColorRgb);
                Assert.True(chroma <= 20 || chroma >= 30, $"{p.Key}/{world}: chroma {chroma} is neither white nor a clear tint");
                total++;
                if (chroma >= 30)
                {
                    tinted++;
                }
            }
        }

        double share = tinted / (double)total;
        Assert.InRange(share, 0.5, 0.7);
    }

    [Fact]
    public void TintedColours_SuitTheirType()
    {
        static IEnumerable<int> Tints(string key) => Worlds(200).Select(w => Profile(w, Planet(key)).ColorRgb).Where(c => Chroma(c) >= 30);

        Assert.All(Tints("ice"), c => Assert.True(Rgb(c).B >= Rgb(c).R, $"ice mist {c:X6} should lean blue"));
        Assert.All(Tints("lava"), c => Assert.True(Rgb(c).R >= Rgb(c).B, $"volcanic mist {c:X6} should lean warm"));
        Assert.All(Tints("desert"), c => Assert.True(Rgb(c).R >= Rgb(c).B, $"desert mist {c:X6} should lean warm"));
        Assert.NotEmpty(Tints("swamp"));

        // An authored type keeps its colour on every world.
        var toxica = Planet("toxica_maxima");
        Assert.True(toxica.MistColor > 0);
        Assert.Single(Worlds(30).Select(w => Profile(w, toxica).ColorRgb).Distinct());
    }

    [Fact]
    public void Families_ComeFromTheTypeTraits()
    {
        Assert.Equal(MistFamily.Toxic, MistProfiles.FamilyOf(Planet("toxic_world")));
        Assert.Equal(MistFamily.Lush, MistProfiles.FamilyOf(Planet("swamp")));
        Assert.Equal(MistFamily.Lush, MistProfiles.FamilyOf(Planet("jungle")));
        Assert.Equal(MistFamily.Ice, MistProfiles.FamilyOf(Planet("ice")));
        Assert.Equal(MistFamily.Ice, MistProfiles.FamilyOf(Planet("titas"))); // ashen flora, but -100 °C
        Assert.Equal(MistFamily.Volcanic, MistProfiles.FamilyOf(Planet("lava")));
        Assert.Equal(MistFamily.Arid, MistProfiles.FamilyOf(Planet("desert")));
        Assert.Equal(MistFamily.Arid, MistProfiles.FamilyOf(Planet("sand_sea"))); // a volcanic-tagged sand desert
        Assert.Equal(MistFamily.Exotic, MistProfiles.FamilyOf(Planet("crystal_living")));
        Assert.Equal(MistFamily.Exotic, MistProfiles.FamilyOf(Planet("rainbow_sea")));
        Assert.Equal(MistFamily.Pastel, MistProfiles.FamilyOf(Planet("rocky")));
        Assert.Equal(MistFamily.Pastel, MistProfiles.FamilyOf(null));
    }

    [Fact]
    public void MistAmount_FollowsTheTypeValue_AndTheAirDensity()
    {
        foreach (var p in Content.Planets.Values.Where(HasAir))
        {
            foreach (var world in Worlds(40))
            {
                float thin = Profile(world, p, density: 0.0).Amount;
                float thick = Profile(world, p, density: 1.0).Amount;
                Assert.InRange(thin, 0f, 1f);
                Assert.InRange(thick, 0f, 1f);
                Assert.True(thick >= thin, $"{p.Key}/{world}: thicker air must not breed less mist");
                // The air factor is 0.5 in thin air: what remains is the type's value ± the per-world jitter.
                Assert.InRange(thin / 0.5, System.Math.Max(0.0, p.Mist!.Value - MistProfiles.MistJitter) - 1e-4,
                    p.Mist.Value + MistProfiles.MistJitter + 1e-4);
            }
        }

        static double Mean(string key) => Worlds(200).Average(w => Profile(w, Planet(key)).Amount);
        Assert.True(Mean("swamp") > Mean("desert") + 0.3, "a swamp world must be far mistier than a desert");
        Assert.Equal(0f, Profile("sys0-p1", new PlanetType { Key = "dry", Atmosphere = "breathable", Mist = 0 }).Amount);
    }

    [Fact]
    public void FogFrequency_StaysInRange_AndLeansOnTheMist()
    {
        var all = new List<float>();
        foreach (var p in Content.Planets.Values.Where(HasAir))
        {
            foreach (var world in Worlds(60))
            {
                float f = Profile(world, p).FogFrequency;
                Assert.InRange(f, MistProfiles.MinFogFrequency, MistProfiles.MaxFogFrequency);
                all.Add(f);
            }
        }

        Assert.True(all.Min() < 0.6f, "some worlds must be almost fog-free");
        Assert.True(all.Max() > 1.6f, "some worlds must be fog worlds");
        static double Mean(string key) => Worlds(200).Average(w => Profile(w, Planet(key)).FogFrequency);
        Assert.True(Mean("swamp") > Mean("desert"), "misty worlds fog more often");
    }

    [Fact]
    public void Wispiness_StaysInRange_AndVaries()
    {
        var values = Worlds(150).Select(w => Profile(w, Planet("varied")).Wispiness).ToList();
        Assert.All(values, v => Assert.InRange(v, 0f, 1f));
        Assert.True(values.Min() < 0.4f && values.Max() > 0.8f, "both smooth blankets and torn wisps must occur");
    }

    [Fact]
    public void CloudBand_SitsOnTheCloudLine_AndGrowsWithTheCloudCover()
    {
        foreach (var p in Content.Planets.Values.Where(p => HasAir(p) && p.CloudDensity >= 0.05))
        {
            double line = System.Math.Max(WeatherProjection.CloudLineY(p), p.BaseHeight + MistProfiles.FallbackBandClearance);
            foreach (var world in Worlds(60))
            {
                var m = Profile(world, p);
                Assert.True(m.HasCloudBand, $"{p.Key}/{world}: a cloudy world needs a band");
                Assert.True(m.CloudTopY > m.CloudBaseY + 4f, $"{p.Key}/{world}: the band is too thin");
                Assert.InRange(m.CloudBaseY, line - 0.001, line + 12.001);
                Assert.InRange(m.CloudTopY - m.CloudBaseY, 14f - 0.001f, 34f + 0.001f);
                Assert.Equal((double)m.CloudBaseY, MistProfiles.SummitLineY(Seed, world, p)); // the weather's summit line
                Assert.Equal((m.CloudBaseY, m.CloudTopY), MistProfiles.CloudBand(Seed, world, p, airless: false));
            }
        }

        // #3272: with the world's measured relief the band clears the median ground and the upper tenth of the land.
        var swamp = Planet("swamp");
        foreach (var world in Worlds(40))
        {
            var flat = MistProfiles.CloudBand(Seed, world, swamp, airless: false, new TerrainAltitudes(70, 76));
            Assert.InRange(flat.BaseY, 70 + MistProfiles.MinBandClearance - 0.001, 70 + MistProfiles.MinBandClearance + 12.001);
            var peaks = MistProfiles.CloudBand(Seed, world, swamp, airless: false, new TerrainAltitudes(90, 140));
            Assert.InRange(peaks.BaseY, 144 - 0.001, 144 + 12.001);
        }

        static double Thickness(string key) => Worlds(200).Select(w => Profile(w, Planet(key))).Average(m => m.CloudTopY - m.CloudBaseY);
        Assert.True(Thickness("swamp") > Thickness("gds_desert") + 5, "a cloudier type has a thicker band");
        var clear = new PlanetType { Key = "cloudless", Atmosphere = "breathable", CloudDensity = 0 };
        Assert.False(Profile("sys0-p1", clear).HasCloudBand);
    }

    [Fact]
    public void FogWeights_ScaleWithTheWorldsFogFrequency()
    {
        var fog = WeatherCatalog.Find("fog")!;
        var groundFog = WeatherCatalog.Find("ground_fog")!;
        var gale = WeatherCatalog.Find("gale")!;
        static WeatherContext Ctx(double frequency) => new()
        {
            AtmosphereDensity = 0.6,
            DayFraction = 0.25,
            PlanetKey = "varied",
            FogFrequency = frequency,
        };

        Assert.Equal(1.0, new WeatherContext().FogFrequency); // the type's own weights by default
        Assert.True(WeatherSim.EventWeight(fog, Ctx(1.0)) > 0);
        Assert.Equal(2.0 * WeatherSim.EventWeight(fog, Ctx(1.0)), WeatherSim.EventWeight(fog, Ctx(2.0)), 9);
        Assert.Equal(0.5 * WeatherSim.EventWeight(groundFog, Ctx(1.0)), WeatherSim.EventWeight(groundFog, Ctx(0.5)), 9);
        Assert.Equal(0.0, WeatherSim.EventWeight(fog, Ctx(0.0)));
        Assert.Equal(WeatherSim.EventWeight(gale, Ctx(1.0)), WeatherSim.EventWeight(gale, Ctx(2.2))); // other events untouched
    }
}
