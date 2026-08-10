// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The weather model (#900) on its own — no server, no world, so these stay in the fast tier.
/// <para>Deliberately NOT golden-sequence tests: the scheduler reads trig-derived biases (afternoon
/// convection, the season sine) and those bits differ between platform libms. Everything here asserts
/// PROPERTIES — divergence, bounds, reproducibility inside one process — which hold either way.</para>
/// </summary>
public sealed class WeatherModelTests
{
    private static WeatherContext Ctx(
        bool airless = false, bool toxic = false, double baseTemp = 15, string planet = "varied",
        double stormChance = 0.4, IReadOnlyDictionary<string, double>? events = null)
        => new()
        {
            StormChance = stormChance,
            AtmosphereDensity = airless ? 0 : 0.5,
            DayFraction = 0.35,
            Airless = airless,
            Toxic = toxic,
            BaseTemperature = baseTemp,
            PlanetKey = planet,
            EventWeights = events,
            Circumference = 6000,
            Dynamic = true,
        };

    private static List<string> RunStates(WeatherSim sim, WeatherContext ctx, int seconds)
    {
        var seen = new List<string>();
        for (int i = 0; i < seconds; i++)
        {
            if (sim.Advance(1.0, ctx))
            {
                seen.Add(sim.State);
            }
        }

        return seen;
    }

    /// <summary>The bug this whole package started from: the weather RNG used to be seeded from the save
    /// seed alone, so two worlds with the same storm chance ran the SAME weather, in lockstep, forever.</summary>
    [Fact]
    public void TwoWorlds_WithTheSameSeed_ButDifferentLocations_Diverge()
    {
        var ctx = Ctx();
        var a = new WeatherSim(0x1111_2222_3333_0001UL);
        var b = new WeatherSim(0x1111_2222_3333_0002UL);
        a.Start(ctx);
        b.Start(ctx);

        var seqA = RunStates(a, ctx, 3000);
        var seqB = RunStates(b, ctx, 3000);

        Assert.NotEmpty(seqA);
        Assert.NotEmpty(seqB);
        Assert.NotEqual(seqA, seqB);
    }

    /// <summary>…while the SAME world stays reproducible, so a world's weather is still deterministic.</summary>
    [Fact]
    public void TheSameWorld_ReplaysTheSameWeather()
    {
        var ctx = Ctx();
        var a = new WeatherSim(0xABCDEF01UL);
        var b = new WeatherSim(0xABCDEF01UL);
        a.Start(ctx);
        b.Start(ctx);

        Assert.Equal(RunStates(a, ctx, 2000), RunStates(b, ctx, 2000));
    }

    /// <summary>Every world rolls its own pacing, so episodes are no longer 25 s for everyone.</summary>
    [Fact]
    public void EpisodeLengths_VaryBetweenWorlds()
    {
        var lengths = new HashSet<double>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var sim = new WeatherSim(seed * 0x9E3779B9UL);
            sim.Start(Ctx());
            lengths.Add(System.Math.Round(sim.Duration, 1));
        }

        Assert.True(lengths.Count >= 8, $"expected varied episode lengths, got {lengths.Count} distinct");
    }

    /// <summary>Intensity swells and fades instead of snapping on, and never leaves 0..1.</summary>
    [Fact]
    public void Intensity_RampsWithinBounds()
    {
        var ctx = Ctx(stormChance: 0.9);
        var sim = new WeatherSim(0x5EED01UL);
        sim.Start(ctx);

        bool sawRamp = false;
        float previous = sim.Intensity;
        for (int i = 0; i < 4000; i++)
        {
            sim.Advance(0.5, ctx);
            Assert.InRange(sim.Intensity, 0f, 1f);
            if (sim.Intensity > previous + 0.0001f && sim.Intensity < 0.999f)
            {
                sawRamp = true;
            }

            previous = sim.Intensity;
        }

        Assert.True(sawRamp, "intensity should ramp, not step");
    }

    /// <summary>Airless bodies never get rain, clouds or fog — but they DO get the vacuum-safe events, which
    /// is the point: before #900 an asteroid had no weather at all.</summary>
    [Fact]
    public void AirlessWorlds_GetVacuumEventsButNoRain()
    {
        var ctx = Ctx(airless: true, baseTemp: -30, planet: "asteroid");
        var sim = new WeatherSim(0xA57E01DUL) { LadderCeiling = 0 };
        sim.Start(ctx);

        var seen = RunStates(sim, ctx, 20000).ToHashSet();
        Assert.DoesNotContain("rain", seen);
        Assert.DoesNotContain("storm", seen);
        Assert.DoesNotContain("clouds", seen);
        Assert.DoesNotContain("fog", seen);
        Assert.True(
            seen.Contains("ion_storm") || seen.Contains("meteor_shower"),
            "an airless body should still see vacuum-safe weather");
    }

    /// <summary>Acid rain belongs to toxic atmospheres and nowhere else.</summary>
    [Fact]
    public void AcidRain_OnlyOnToxicWorlds()
    {
        var breathable = new WeatherSim(0xAC1D01UL);
        var toxic = new WeatherSim(0xAC1D01UL);
        var breathableCtx = Ctx(toxic: false);
        var toxicCtx = Ctx(toxic: true);
        breathable.Start(breathableCtx);
        toxic.Start(toxicCtx);

        Assert.DoesNotContain("acid_rain", RunStates(breathable, breathableCtx, 20000));
        Assert.Contains("acid_rain", RunStates(toxic, toxicCtx, 20000));
    }

    /// <summary>Blizzards need the cold, heatwaves the heat — the hard gates come before any weighting.</summary>
    [Theory]
    [InlineData(-25.0, "blizzard", "heatwave")]
    [InlineData(45.0, "heatwave", "blizzard")]
    public void TemperatureGates_DecideWhichEventsCanHappen(double baseTemp, string expected, string forbidden)
    {
        var ctx = Ctx(baseTemp: baseTemp);
        var sim = new WeatherSim(0xC01D1UL);
        sim.Start(ctx);

        var seen = RunStates(sim, ctx, 25000).ToHashSet();
        Assert.Contains(expected, seen);
        Assert.DoesNotContain(forbidden, seen);
    }

    /// <summary>A planet may rule an event out entirely by weighting it to zero.</summary>
    [Fact]
    public void PlanetWeights_CanSuppressAnEvent()
    {
        var ctx = Ctx(events: new Dictionary<string, double> { ["fog"] = 0, ["ground_fog"] = 0 });
        var sim = new WeatherSim(0xF06111UL);
        sim.Start(ctx);

        var seen = RunStates(sim, ctx, 20000).ToHashSet();
        Assert.DoesNotContain("fog", seen);
        Assert.DoesNotContain("ground_fog", seen);
    }

    /// <summary>An overcast world is no longer frozen on "clouds": it keeps a floor but can still build
    /// into rain and storms.</summary>
    [Fact]
    public void OvercastWorlds_KeepAFloorButStillBuild()
    {
        var ctx = Ctx(stormChance: 0.6);
        var sim = new WeatherSim(0x0FC451UL) { LadderFloor = 1 };
        sim.Start(ctx);

        var seen = RunStates(sim, ctx, 20000);
        Assert.DoesNotContain("clear", seen);
        Assert.Contains("rain", seen);
    }

    /// <summary>Fronts drift along the world's east–west wrap and boost only where they actually are.</summary>
    [Fact]
    public void Fronts_DriftAndOnlyBoostWhereTheyAre()
    {
        var ctx = Ctx();
        var sim = new WeatherSim(0xF120A7UL);
        sim.Fronts.Add(new WeatherFront { CenterX = 100, HalfWidth = 50, Drift = 10, Boost = 2, Life = 1000 });

        Assert.Equal(2, sim.FrontBoostAt(100, ctx.Circumference));
        Assert.Equal(0, sim.FrontBoostAt(400, ctx.Circumference));

        sim.Advance(30, ctx); // 30 s at 10 blocks/s → the front has moved 300 blocks east
        Assert.Equal(0, sim.FrontBoostAt(100, ctx.Circumference));
        Assert.Equal(2, sim.FrontBoostAt(400, ctx.Circumference));
    }

    /// <summary>A front leaving one edge of the world comes back round the other side.</summary>
    [Fact]
    public void Fronts_WrapAroundTheWorld()
    {
        var ctx = Ctx();
        var sim = new WeatherSim(7UL);
        sim.Fronts.Add(new WeatherFront { CenterX = 5980, HalfWidth = 40, Drift = 10, Boost = 1, Life = 1000 });

        sim.Advance(5, ctx); // 5980 + 50 = 6030 → wraps to 30
        Assert.InRange(sim.Fronts[0].CenterX, 0, 100);
        Assert.Equal(1, sim.FrontBoostAt(30, ctx.Circumference));
    }

    /// <summary>The forecast peek must not disturb the live world — it forks the RNG stream by value.</summary>
    [Fact]
    public void Forecast_DoesNotDisturbTheLiveSimulation()
    {
        var ctx = Ctx();
        var live = new WeatherSim(0x0FEC0UL);
        var reference = new WeatherSim(0x0FEC0UL);
        live.Start(ctx);
        reference.Start(ctx);

        var peek = live.Forecast(3, ctx);
        Assert.Equal(3, peek.Count);
        Assert.All(peek, p => Assert.Contains(p.State, WeatherCatalog.AllKeys));

        // After peeking, the live world must still run exactly like the untouched reference.
        Assert.Equal(RunStates(reference, ctx, 1500), RunStates(live, ctx, 1500));
    }

    /// <summary>The season swings but stays a usable 0..1 weight.</summary>
    [Fact]
    public void SeasonWetness_StaysInRange()
    {
        var sim = new WeatherSim(0x5EA5011UL);
        for (double day = 0; day < 120; day += 0.25)
        {
            Assert.InRange(sim.Wetness(day), 0.0, 1.0);
        }
    }

    /// <summary>Every catalogue entry is well-formed: unique key, sane bands, and ladder severities that
    /// line up with their array slot — the biome/front/altitude shifts index straight into this.</summary>
    [Fact]
    public void Catalogue_IsWellFormed()
    {
        Assert.Equal(WeatherCatalog.AllKeys.Count(), WeatherCatalog.AllKeys.Distinct().Count());
        for (int i = 0; i < WeatherCatalog.Ladder.Length; i++)
        {
            Assert.Equal(i, WeatherCatalog.Ladder[i].Severity);
        }

        foreach (var def in WeatherCatalog.Ladder.Concat(WeatherCatalog.Events))
        {
            Assert.InRange(def.PeakLo, 0f, 1f);
            Assert.InRange(def.PeakHi, def.PeakLo, 1f);
            Assert.True(def.DurHi >= def.DurLo, $"{def.Key} has an inverted duration band");
            Assert.InRange(def.WindHi, def.WindLo, 1f);
        }

        Assert.All(WeatherCatalog.Events, e => Assert.Equal(-1, e.Severity));
    }
}
