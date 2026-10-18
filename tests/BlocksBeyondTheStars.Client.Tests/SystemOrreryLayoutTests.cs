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
/// #2745: the travel screen's small system chart draws the REAL system, laid out like the flight chart (#623) — checked
/// against real generated systems: every dot on the chart, every orbit ring through its dot, every planet in the
/// direction the flight chart puts it, moons (and stations over a planet) outside their planet's dot, and the same chart
/// for the same bodies.
/// </summary>
public sealed class SystemOrreryLayoutTests
{
    // Mirrors SystemMapWidget: a 380-unit square, a 10-unit rim, 12-unit dots (18 for the body you are on, ×1.5 for a gas
    // giant) and a 7-unit halo.
    private const float ChartHalf = (380f * 0.5f) - 10f;
    private const float HaloPad = 7f;
    private const float Tolerance = 0.05f;

    private static float DotRadius(NetBody b, string activeId)
        => (b.Id == activeId ? 18f : 12f) * (b.PlanetType == "gas_giant" ? 1.5f : 1f) * 0.5f;

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
    };

    /// <summary>Every generated system with a planet (classic, archetype-varied and belt sweeps, like
    /// <see cref="SystemChartLayoutTests"/>), with the first planet as the body you are on.</summary>
    private static IEnumerable<(NetBody[] Bodies, string ActiveId)> RealSystems(int runs = 20)
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

    private static OrreryLayout Lay(IReadOnlyList<NetBody> bodies, string activeId)
        => SystemOrreryLayout.Layout(bodies, ChartHalf, HaloPad, b => DotRadius(b, activeId));

    private static float Hypot(float x, float y) => MathF.Sqrt((x * x) + (y * y));

    [Fact]
    public void EveryDot_AndItsHalo_StaysOnTheChart()
    {
        int checkedDots = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var layout = Lay(bodies, active);
            Assert.Equal(bodies.Length, layout.Markers.Count);
            foreach (var m in layout.Markers)
            {
                float reach = Hypot(m.X, m.Y) + m.DotRadius + HaloPad;
                Assert.True(reach <= ChartHalf + Tolerance, $"{m.Id} reaches {reach} of the chart's {ChartHalf}");
                checkedDots++;
            }
        }

        Assert.True(checkedDots > 300, $"only {checkedDots} dots checked — the sweep is not covering enough");
    }

    [Fact]
    public void OrbitRings_PassThroughTheirDots_AndOnlyStarOrbitersGetOne()
    {
        int rings = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var layout = Lay(bodies, active);
            foreach (var m in layout.Markers)
            {
                var b = bodies[m.Index];
                if (m.RingRadius <= 0f)
                {
                    continue;
                }

                Assert.True(SystemOrreryLayout.OrbitsStar(b), $"{b.Id} ({b.Kind}) circles no star but got a ring");
                Assert.Equal(Hypot(m.X, m.Y), m.RingRadius, Tolerance);
                Assert.True(SystemChartLayout.ShowRing(m.RingRadius));
                rings++;
            }

            // A belt member is covered by its band, never a ring of its own.
            foreach (var belt in layout.Belts)
            {
                Assert.True(belt.Inner <= belt.Outer);
            }
        }

        Assert.True(rings > 200, $"only {rings} orbit rings exercised");
    }

    [Fact]
    public void EveryPlanet_LiesInTheDirectionTheFlightChartPutsIt()
    {
        // The flight chart (SystemChartLayoutTests.Project) places a body other than the launch body at
        // (body − launch body) × FlightViewScale with the star at −launch body × FlightViewScale — relative to the star,
        // the body's own system position × FlightViewScale. This chart draws the same, only at its own scale.
        int planets = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var layout = Lay(bodies, active);
            foreach (var m in layout.Markers)
            {
                var b = bodies[m.Index];
                if (!SystemOrreryLayout.OrbitsStar(b) || b.Id == active)
                {
                    continue;
                }

                float fx = b.SystemX * SystemBodyLayout.FlightViewScale, fz = b.SystemZ * SystemBodyLayout.FlightViewScale;
                float flight = Hypot(fx, fz), here = Hypot(m.X, m.Y);
                Assert.Equal(fx / flight, m.X / here, 1e-3f);
                Assert.Equal(fz / flight, m.Y / here, 1e-3f); // +y on the chart = the system's +z, like the flight chart
                Assert.Equal(flight * layout.Scale, here, Tolerance); // the real distance, at the chart's scale
                planets++;
            }
        }

        Assert.True(planets > 200, $"only {planets} planets checked");
    }

    [Fact]
    public void Moons_SitOutsideTheirPlanetsDot_AndNeverOnEachOther()
    {
        int moons = 0;
        foreach (var (bodies, active) in RealSystems())
        {
            var layout = Lay(bodies, active);
            var byId = layout.Markers.ToDictionary(m => m.Id);
            foreach (var m in layout.Markers)
            {
                var b = bodies[m.Index];
                if (b.Kind == "Moon")
                {
                    Assert.Equal(b.ParentId, m.HostId); // a moon is drawn beside its own planet
                    moons++;
                }

                if (!m.IsSatellite)
                {
                    continue;
                }

                var host = byId[m.HostId];
                float apart = Hypot(m.X - host.X, m.Y - host.Y);
                Assert.True(apart >= host.DotRadius + m.DotRadius + SystemOrreryLayout.SatelliteGap - Tolerance,
                    $"{m.Id} sits {apart} from {host.Id} — inside or touching its dot");

                foreach (var other in layout.Markers.Where(o => o.HostId == m.HostId && string.CompareOrdinal(o.Id, m.Id) > 0))
                {
                    float gap = Hypot(m.X - other.X, m.Y - other.Y);
                    Assert.True(gap >= m.DotRadius + other.DotRadius - Tolerance, $"{m.Id} and {other.Id} overlap ({gap})");
                }
            }
        }

        Assert.True(moons > 100, $"only {moons} moons checked");
    }

    [Fact]
    public void TheSameBodies_GiveTheSameChart_InAnyOrder()
    {
        foreach (var (bodies, active) in RealSystems(5))
        {
            var first = Lay(bodies, active);
            var again = Lay(bodies.Select(b => ToNetCopy(b)).ToArray(), active);
            var reversed = Lay(bodies.AsEnumerable().Reverse().ToArray(), active);
            var byId = reversed.Markers.ToDictionary(m => m.Id);
            Assert.Equal(first.Scale, again.Scale);
            Assert.Equal(first.Scale, reversed.Scale);
            for (int i = 0; i < first.Markers.Count; i++)
            {
                var m = first.Markers[i];
                Assert.Equal(m.X, again.Markers[i].X);
                Assert.Equal(m.Y, again.Markers[i].Y);
                Assert.Equal(m.X, byId[m.Id].X, 1e-4f);
                Assert.Equal(m.Y, byId[m.Id].Y, 1e-4f);
                Assert.Equal(m.HostId, byId[m.Id].HostId);
            }
        }
    }

    private static NetBody ToNetCopy(NetBody b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Kind = b.Kind,
        PlanetType = b.PlanetType,
        SystemX = b.SystemX,
        SystemZ = b.SystemZ,
        ParentId = b.ParentId,
    };

    [Fact]
    public void AStationOverAPlanet_HangsBesideIt_AFarOneKeepsItsOwnPlace_AndABeltIsOneBand()
    {
        var bodies = new[]
        {
            new NetBody { Id = "p1", Kind = "Planet", PlanetType = "rocky", SystemX = 3000f },
            new NetBody { Id = "p1-st", Kind = "SpaceStation", SystemX = 3000f, SystemZ = 500f },         // the generator's StationOrbit
            new NetBody { Id = "far-st", Kind = "SpaceStation", SystemX = -1500f, SystemZ = -1500f },     // nobody near
            new NetBody { Id = "a1", Kind = "AsteroidField", SystemZ = 5000f },
            new NetBody { Id = "a2", Kind = "AsteroidField", SystemX = 5000f * 0.6f, SystemZ = -5000f * 0.8f },
            new NetBody { Id = "a3", Kind = "AsteroidField", SystemX = -5100f },
        };

        var layout = Lay(bodies, "p1");
        var byId = layout.Markers.ToDictionary(m => m.Id);

        Assert.Equal("p1", byId["p1-st"].HostId);
        Assert.Equal(string.Empty, byId["far-st"].HostId);
        float s = layout.Scale * SystemBodyLayout.FlightViewScale;
        Assert.Equal(-1500f * s, byId["far-st"].X, Tolerance);
        Assert.Equal(-1500f * s, byId["far-st"].Y, Tolerance);

        var belt = Assert.Single(layout.Belts);
        Assert.Equal(5000f * s, belt.Inner, 0.5f);
        Assert.Equal(5100f * s, belt.Outer, 0.5f);
        Assert.All(new[] { "a1", "a2", "a3" }, id => Assert.Equal(0f, byId[id].RingRadius));
        Assert.True(byId["p1"].RingRadius > 0f);
    }
}
