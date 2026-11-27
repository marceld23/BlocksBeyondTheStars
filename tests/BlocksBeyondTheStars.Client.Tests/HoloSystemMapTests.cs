// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4055: the cockpit's holographic system map shows the REAL system — every body where the travel chart puts it — and
/// then lets the planets travel along their rings. Checked against real generated systems: nothing is left out, every
/// ring keeps passing through its dot, no dot ever leaves the chart, the moons of one planet never run into each other,
/// and the direction and the pace follow the orbital period.
/// </summary>
public sealed class HoloSystemMapTests
{
    private const float Tolerance = 0.05f;

    /// <summary>Moments of the time lapse the sweeps look at: the start, the first seconds, minutes in, an hour in.</summary>
    private static readonly float[] Moments = { 0f, 0.4f, 3f, 7.3f, 19f, 61f, 127.5f, 600f, 3611f };

    private static NetBody ToNet(CelestialBody b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Kind = b.Kind.ToString(),
        PlanetType = b.PlanetType,
        SystemX = b.SystemX,
        SystemY = b.SystemY,
        SystemZ = b.SystemZ,
        ParentId = b.ParentId,
        OrbitPeriodDays = b.OrbitPeriodDays,
    };

    /// <summary>Generated systems with a planet (classic, archetype-varied and belt sweeps, like
    /// <see cref="SystemOrreryLayoutTests"/>), with the first planet as the body the player is at.</summary>
    private static IEnumerable<(NetBody[] Bodies, string ActiveId)> RealSystems(int runs = 8)
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        for (long run = 1; run <= runs * 3; run++)
        {
            long seed = ((run - 1) % runs) + 1;
            var desc = new WorldDescription { SystemVariance = run > runs, AsteroidBelts = run > runs * 2 };
            foreach (var system in new UniverseGenerator(seed, desc, content).Generate().Systems)
            {
                var home = system.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Planet);
                if (home is not null)
                {
                    yield return (system.Bodies.Select(ToNet).ToArray(), home.Id);
                }
            }
        }
    }

    private static float Hypot(float x, float y) => MathF.Sqrt((x * x) + (y * y));

    private static NetBody Body(string id, string kind, float x, float z, float period = 0f, string parent = "") => new()
    {
        Id = id,
        Name = id,
        Kind = kind,
        SystemX = x,
        SystemZ = z,
        OrbitPeriodDays = period,
        ParentId = parent,
    };

    [Fact]
    public void AtSecondZero_EveryBody_StandsWhereTheTravelChartPutsIt()
    {
        int bodiesChecked = 0, biggest = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var map = HoloSystemMap.Build(bodies, active);
            var chart = SystemOrreryLayout.Layout(bodies, HoloSystemMap.ChartHalf, HoloSystemMap.MarkerPad, b => HoloSystemMap.DotRadius(b, active));

            // Every body of the system — the hologram used to stop after the first five list entries.
            Assert.Equal(bodies.Length, map.Bodies.Count);
            biggest = Math.Max(biggest, bodies.Length);
            for (int k = 0; k < map.Bodies.Count; k++)
            {
                var m = chart.Markers[k];
                Assert.Equal(m.Id, map.Bodies[k].Id);
                map.PositionAt(k, 0f, out float x, out float y);
                Assert.Equal(m.X, x, Tolerance);
                Assert.Equal(m.Y, y, Tolerance);
                Assert.Equal(m.RingRadius, map.Bodies[k].RingRadius);
                bodiesChecked++;
            }

            Assert.Equal(chart.Belts.Count, map.Belts.Count);
        }

        Assert.True(bodiesChecked > 300, $"only {bodiesChecked} bodies checked — the sweep is not covering enough");
        Assert.True(biggest > 5, "no system with more than five bodies in the sweep");
    }

    [Fact]
    public void EveryRing_KeepsPassingThroughItsDot_AndNoDotEverLeavesTheChart()
    {
        int rings = 0, moving = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var map = HoloSystemMap.Build(bodies, active);
            for (int k = 0; k < map.Bodies.Count; k++)
            {
                var b = map.Bodies[k];
                foreach (float t in Moments)
                {
                    map.PositionAt(k, t, out float x, out float y);
                    float reach = Hypot(x, y) + b.DotRadius;
                    Assert.True(reach <= HoloSystemMap.ChartHalf + Tolerance, $"{b.Id} reaches {reach} at second {t}");
                    if (b.RingRadius > 0f)
                    {
                        Assert.Equal(b.RingRadius, Hypot(x, y), Tolerance);
                    }
                }

                rings += b.RingRadius > 0f ? 1 : 0;
                moving += b.Speed != 0f ? 1 : 0;
            }
        }

        Assert.True(rings > 80, $"only {rings} orbit rings exercised");
        Assert.True(moving > 150, $"only {moving} travelling bodies exercised");
    }

    [Fact]
    public void TheSatellitesOfOneBody_TurnTogether_AndNeverTouch()
    {
        int pairs = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var map = HoloSystemMap.Build(bodies, active);
            for (int a = 0; a < map.Bodies.Count; a++)
            {
                for (int b = a + 1; b < map.Bodies.Count; b++)
                {
                    var first = map.Bodies[a];
                    var second = map.Bodies[b];
                    if (first.Host < 0 || first.Host != second.Host)
                    {
                        continue;
                    }

                    Assert.Equal(first.Speed, second.Speed);
                    foreach (float t in Moments)
                    {
                        map.PositionAt(a, t, out float ax, out float ay);
                        map.PositionAt(b, t, out float bx, out float by);
                        float apart = Hypot(ax - bx, ay - by);
                        Assert.True(apart >= first.DotRadius + second.DotRadius, $"{first.Id} and {second.Id} are {apart} apart at second {t}");
                    }

                    pairs++;
                }
            }
        }

        Assert.True(pairs > 40, $"only {pairs} pairs of satellites exercised");
    }

    [Fact]
    public void TheMembersOfABelt_TurnTogether()
    {
        int belts = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var map = HoloSystemMap.Build(bodies, active);
            var chart = SystemOrreryLayout.Layout(bodies, HoloSystemMap.ChartHalf, HoloSystemMap.MarkerPad, b => HoloSystemMap.DotRadius(b, active));
            for (int belt = 0; belt < chart.Belts.Count; belt++)
            {
                var speeds = chart.Markers.Select((m, k) => (m, k)).Where(p => p.m.Belt == belt).Select(p => map.Bodies[p.k].Speed).ToArray();
                Assert.True(speeds.Length >= SystemOrreryLayout.BeltMinMembers);
                Assert.All(speeds, s => Assert.Equal(speeds[0], s));
                Assert.NotEqual(0f, speeds[0]);
                belts++;
            }
        }

        Assert.True(belts > 0, "no belt in the sweep");
    }

    [Fact]
    public void APositivePeriodTravelsOneWay_ANegativeOneTheOther_AndNoPeriodKeepsItsPlace()
    {
        var bodies = new[]
        {
            Body("forward", "Planet", 1000f, 0f, period: 10f),
            Body("backward", "Planet", 0f, 3000f, period: -10f),
            Body("moon", "Moon", 1090f, 0f, period: 1f, parent: "forward"),
            Body("over", "SpaceStation", 1000f, 500f, parent: "forward"),
            Body("free", "SpaceStation", -5000f, -5000f),
        };
        var map = HoloSystemMap.Build(bodies, "forward");
        var by = Enumerable.Range(0, map.Bodies.Count).ToDictionary(k => map.Bodies[k].Id);

        Assert.True(map.Bodies[by["forward"]].Speed > 0f);
        Assert.Equal(-map.Bodies[by["forward"]].Speed, map.Bodies[by["backward"]].Speed, 5);

        // One second on, "forward" has left +x toward +y (the chart's growing angle), "backward" has left +y toward +x.
        map.PositionAt(by["forward"], 1f, out float fx, out float fy);
        Assert.True(fy > 0f && fx > 0f, $"forward at {fx}/{fy}");
        map.PositionAt(by["backward"], 1f, out float bx, out float bz);
        Assert.True(bx > 0f && bz > 0f, $"backward at {bx}/{bz}");

        // After one whole orbit a planet is back where it started.
        float lap = HoloSystemMap.OrbitSeconds(10f, aroundStar: true);
        map.PositionAt(by["forward"], 0f, out float sx, out float sy);
        map.PositionAt(by["forward"], lap, out float ex, out float ey);
        Assert.Equal(sx, ex, 0.01f);
        Assert.Equal(sy, ey, 0.01f);

        // The moon circles its planet at a moon's pace; the station over the planet has no period, but it belongs to the
        // planet's satellites and turns with them, so the two never meet.
        Assert.Equal(by["forward"], map.Bodies[by["moon"]].Host);
        Assert.Equal(by["forward"], map.Bodies[by["over"]].Host);
        Assert.Equal(MathF.Tau / HoloSystemMap.OrbitSeconds(1f, aroundStar: false), map.Bodies[by["moon"]].Speed, 5);
        Assert.Equal(map.Bodies[by["moon"]].Speed, map.Bodies[by["over"]].Speed);

        // A body of its own without a period stays where it is.
        Assert.Equal(-1, map.Bodies[by["free"]].Host);
        Assert.Equal(0f, map.Bodies[by["free"]].Speed);
        map.PositionAt(by["free"], 0f, out float x0, out float y0);
        map.PositionAt(by["free"], 500f, out float x1, out float y1);
        Assert.Equal(x0, x1);
        Assert.Equal(y0, y1);
    }

    [Fact]
    public void SatellitesWithoutAnyPeriod_KeepTheirSideOfTheirPlanet()
    {
        var bodies = new[]
        {
            Body("planet", "Planet", 2000f, 0f, period: 20f),
            Body("station", "SpaceStation", 2000f, 500f, parent: "planet"),
        };
        var map = HoloSystemMap.Build(bodies, "planet");
        Assert.Equal(0, map.Bodies[1].Host);
        Assert.Equal(0f, map.Bodies[1].Speed);
        map.PositionAt(0, 0f, out float px0, out float py0);
        map.PositionAt(1, 0f, out float sx0, out float sy0);
        map.PositionAt(0, 33f, out float px1, out float py1);
        map.PositionAt(1, 33f, out float sx1, out float sy1);
        Assert.NotEqual(px0, px1); // the planet travelled …
        Assert.Equal(sx0 - px0, sx1 - px1, 1e-3f); // … and the station went with it, on the same side
        Assert.Equal(sy0 - py0, sy1 - py1, 1e-3f);
    }

    [Fact]
    public void TheMoonsOfOnePlanet_ShareTheLongestPeriodAmongThem()
    {
        var bodies = new[]
        {
            Body("planet", "Planet", 2000f, 0f, period: 20f),
            Body("quick", "Moon", 2090f, 0f, period: 0.5f, parent: "planet"),
            Body("slow", "Moon", 2000f, 145f, period: -2f, parent: "planet"),
        };
        var map = HoloSystemMap.Build(bodies, "planet");
        float expected = -MathF.Tau / HoloSystemMap.OrbitSeconds(2f, aroundStar: false);
        Assert.Equal(expected, map.Bodies[1].Speed, 5);
        Assert.Equal(expected, map.Bodies[2].Speed, 5);
    }

    [Fact]
    public void ALongerPeriod_NeverGivesAShorterOrbit_AndEveryOrbitIsWatchable()
    {
        foreach (bool aroundStar in new[] { true, false })
        {
            float min = aroundStar ? HoloSystemMap.MinStarOrbitSeconds : HoloSystemMap.MinMoonOrbitSeconds;
            float max = aroundStar ? HoloSystemMap.MaxStarOrbitSeconds : HoloSystemMap.MaxMoonOrbitSeconds;
            float last = 0f;
            for (float days = 0.05f; days < 400f; days *= 1.07f)
            {
                float seconds = HoloSystemMap.OrbitSeconds(days, aroundStar);
                Assert.InRange(seconds, min, max);
                Assert.True(seconds >= last, $"{days} days take {seconds} s, a shorter period took {last} s");
                Assert.Equal(seconds, HoloSystemMap.OrbitSeconds(-days, aroundStar)); // the sign is the direction only
                last = seconds;
            }
        }

        // The generator's planets (6..40 days) keep apart from each other inside the range; so do its moons (0.4..2.5).
        Assert.True(HoloSystemMap.OrbitSeconds(40f, true) > HoloSystemMap.OrbitSeconds(6f, true) * 2f);
        Assert.True(HoloSystemMap.OrbitSeconds(2.5f, false) > HoloSystemMap.OrbitSeconds(0.4f, false) * 2f);
        Assert.Equal(0f, HoloSystemMap.OrbitSeconds(0f, true));
        Assert.Equal(0f, HoloSystemMap.OrbitSeconds(float.NaN, false));
    }

    [Fact]
    public void TheBodyThePlayerIsAt_IsMarkedAndDrawnBigger()
    {
        var bodies = new[]
        {
            Body("here", "Planet", 1000f, 0f, period: 10f),
            Body("there", "Planet", 0f, 3000f, period: 30f),
            Body("moon", "Moon", 0f, 3090f, period: 1f, parent: "there"),
        };
        var map = HoloSystemMap.Build(bodies, "here");
        Assert.True(map.Bodies[0].Active);
        Assert.False(map.Bodies[1].Active);
        Assert.Equal(HoloSystemMap.PlanetDot * HoloSystemMap.ActiveScale, map.Bodies[0].DotRadius);
        Assert.Equal(HoloSystemMap.PlanetDot, map.Bodies[1].DotRadius);
        Assert.Equal(HoloSystemMap.MoonDot, map.Bodies[2].DotRadius);
        Assert.True(HoloSystemMap.MoonDot < HoloSystemMap.PlanetDot && HoloSystemMap.SmallDot < HoloSystemMap.MoonDot);
        bodies[1].PlanetType = "gas_giant";
        Assert.Equal(HoloSystemMap.GiantDot, HoloSystemMap.DotRadius(bodies[1], "here"));
    }

    [Fact]
    public void TheActiveSystem_IsTheOneHoldingTheLocation_AndNoOtherStandsIn()
    {
        var first = new NetStarSystem { Id = "s1", Bodies = new[] { Body("a", "Planet", 1000f, 0f) } };
        var second = new NetStarSystem { Id = "s2", Bodies = new[] { Body("b", "Planet", 1000f, 0f) } };
        var map = new StarMapData { Systems = new[] { first, second }, ActiveLocationId = "b" };
        Assert.Same(second, HoloSystemMap.ActiveSystem(map));

        map.ActiveLocationId = "nowhere";
        Assert.Null(HoloSystemMap.ActiveSystem(map)); // it used to show the first system of the list instead
        Assert.Null(HoloSystemMap.ActiveSystem(null));
    }

    [Fact]
    public void TheSameBodies_GiveTheSameMap_AndAnEmptySystemAnEmptyOne()
    {
        foreach (var (bodies, active) in RealSystems(runs: 2))
        {
            var one = HoloSystemMap.Build(bodies, active);
            var two = HoloSystemMap.Build(bodies, active);
            for (int k = 0; k < one.Bodies.Count; k++)
            {
                Assert.Equal(one.Bodies[k].Radius, two.Bodies[k].Radius);
                Assert.Equal(one.Bodies[k].Angle, two.Bodies[k].Angle);
                Assert.Equal(one.Bodies[k].Speed, two.Bodies[k].Speed);
                Assert.Equal(one.Bodies[k].Host, two.Bodies[k].Host);
            }
        }

        Assert.Empty(HoloSystemMap.Build(Array.Empty<NetBody>(), "x").Bodies);
        Assert.Empty(HoloSystemMap.Build(null, null).Bodies);
    }
}
