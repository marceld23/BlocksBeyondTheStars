// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The flight system chart's projection (#623): the chart is centred on the STAR and draws an orbit
/// path for every body circling it. Both invariants are checked against REAL generated systems, fed
/// through the same conversion <c>SpaceMap</c> uses, so a regression shows up here and not only on
/// screen.
/// <list type="number">
/// <item>Everything fits — the star can be the chart centre only if no body needs to be pushed
/// outside it, otherwise the rings centred on the star would be in the wrong place.</item>
/// <item>Rings pass through their markers — the radius comes from the body's PROJECTED position, not
/// from its star-map orbit radius, which the render layout deliberately distorts.</item>
/// </list>
/// </summary>
public sealed class SystemChartLayoutTests
{
    // Mirrors SpaceMap / SpaceView.
    private const float ChartHalf = (900f * 0.5f) - 30f; // usable chart radius, canvas units
    private const float MaxBodyDisc = 46f;               // discs are clamped to this diameter
    private const float MarkerPad = (MaxBodyDisc * 0.5f) + 8f;
    private const float SystemViewScale = 0.16f;         // system units → flight-view units
    private const float HomeZ = -20f;                    // the launch body's fixed spot (x = 0)
    private const float Tolerance = 0.05f;

    private static float RadiusOf(CelestialBody b)
    {
        var cls = WorldConstants.SizeClassFor(b.Kind, b.PlanetType ?? string.Empty);
        return (8f + WorldConstants.CircumferenceFor(b.Id, cls, b.SizeBias) / 220f) * 0.5f;
    }

    /// <summary>One projected body: where the chart draws it, and how big.</summary>
    private readonly record struct Drawn(string Id, float X, float Z, float Radius, bool OrbitsStar, float StarMapRadius);

    /// <summary>Rebuilds what the chart would draw for one system, launched from <paramref name="home"/>:
    /// scene coordinates relative to the launch body, exactly as <c>SpaceView.BuildSystemBodies</c> does.
    /// Moons are skipped — they get no rings and their ladder placement is covered by
    /// <see cref="SystemBodyLayoutTests"/>.</summary>
    private static (List<Drawn> Bodies, float StarX, float StarZ) Project(StarSystem system, CelestialBody home)
    {
        var bodies = new List<Drawn>
        {
            // The launch body: pinned below the flight plane, drawn oversized, and — being a planet —
            // ringed like any other. Its star-map radius is its true distance from the star.
            new(home.Id, 0f, HomeZ, RadiusOf(home) * 3.2f, home.Kind == CelestialKind.Planet,
                MathF.Sqrt((home.SystemX * home.SystemX) + (home.SystemZ * home.SystemZ)) * SystemViewScale),
        };

        foreach (var b in system.Bodies)
        {
            if (b.Id == home.Id || b.Kind == CelestialKind.Moon)
            {
                continue;
            }

            bool orbits = b.Kind is CelestialKind.Planet or CelestialKind.AsteroidField
                       && string.IsNullOrEmpty(b.ParentId);
            bodies.Add(new(
                b.Id,
                (b.SystemX - home.SystemX) * SystemViewScale,
                (b.SystemZ - home.SystemZ) * SystemViewScale,
                RadiusOf(b),
                orbits,
                MathF.Sqrt((b.SystemX * b.SystemX) + (b.SystemZ * b.SystemZ)) * SystemViewScale));
        }

        return (bodies, -home.SystemX * SystemViewScale, -home.SystemZ * SystemViewScale);
    }

    private static float Fit(List<Drawn> bodies, float centreX, float centreZ)
        => SystemChartLayout.FitScale(
            ChartHalf,
            bodies.Select(b => b.X).ToArray(),
            bodies.Select(b => b.Z).ToArray(),
            centreX,
            centreZ,
            MarkerPad);

    /// <summary>Every generated system, launched from its first planet — the shape the chart faces.</summary>
    private static IEnumerable<(StarSystem System, CelestialBody Home)> RealSystems(int runs = 30)
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        for (long run = 1; run <= runs * 2; run++)
        {
            long seed = run <= runs ? run : run - runs;
            var desc = new WorldDescription { SystemVariance = run > runs }; // classic AND archetype-varied
            foreach (var system in new UniverseGenerator(seed, desc, content).Generate().Systems)
            {
                var home = system.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Planet);
                if (home is not null)
                {
                    yield return (system, home);
                }
            }
        }
    }

    [Fact]
    public void FitScale_KeepsEveryBodyAndItsOwnRadiusInsideTheChart()
    {
        int checked_ = 0;
        foreach (var (system, home) in RealSystems())
        {
            var (bodies, starX, starZ) = Project(system, home);
            float scale = Fit(bodies, starX, starZ);

            foreach (var b in bodies)
            {
                // What the chart actually occupies at this body: its projected distance from the star plus
                // its DRAWN disc (clamped, with the dark backing ring around it) — not its scene radius.
                float dx = b.X - starX, dz = b.Z - starZ;
                float discHalf = (Math.Clamp(b.Radius * scale * 2f, 12f, MaxBodyDisc) + 8f) * 0.5f;
                float reach = (MathF.Sqrt((dx * dx) + (dz * dz)) * scale) + discHalf;
                Assert.True(
                    reach <= ChartHalf + Tolerance,
                    $"{b.Id} reaches {reach} of the chart's {ChartHalf} — it would be clipped or rim-pinned");
                checked_++;
            }
        }

        Assert.True(checked_ > 200, $"only {checked_} bodies checked — the generator sweep is not covering enough");
    }

    [Fact]
    public void OrbitRings_PassThroughTheirMarkerAndStayInsideTheChart()
    {
        int rings = 0;
        foreach (var (system, home) in RealSystems())
        {
            var (bodies, starX, starZ) = Project(system, home);
            float scale = Fit(bodies, starX, starZ);

            foreach (var b in bodies.Where(b => b.OrbitsStar))
            {
                // Projected chart position (the star is the chart's origin).
                float px = (b.X - starX) * scale, pz = (b.Z - starZ) * scale;
                float radius = SystemChartLayout.OrbitRadius(px, pz);
                if (!SystemChartLayout.ShowRing(radius))
                {
                    continue;
                }

                // The ring passes exactly through the marker: the marker lies ON the circle.
                Assert.Equal(MathF.Sqrt((px * px) + (pz * pz)), radius, Tolerance);

                // …and the fit guarantees the whole ring is on the chart with the marker margin intact,
                // which is what makes centring on the star possible at all (the old chart had to pin the
                // star to the rim).
                Assert.True(
                    radius <= ChartHalf - MarkerPad + Tolerance,
                    $"{b.Id}'s orbit ring of {radius} spills outside the chart's usable {ChartHalf - MarkerPad}");
                rings++;
            }
        }

        Assert.True(rings > 150, $"only {rings} orbit rings exercised — expected the sweep to draw far more");
    }

    [Fact]
    public void StarCentring_CostsAtMostABoundedZoomOut()
    {
        // Centring on the star is what makes the orbit paths possible, and it is not free: where the
        // launch body sits INSIDE a far-flung asteroid's orbit, framing on the launch body is tighter
        // than framing on the star, so the chart zooms out. Measured worst case across real systems is
        // ~1.72×, and the 12-unit minimum disc size keeps every marker legible through it. This bounds
        // the cost so a future layout change can't quietly make the chart useless.
        const float Budget = 2.0f;

        float worst = 1f;
        string worstId = string.Empty;
        foreach (var (system, home) in RealSystems(15))
        {
            var (bodies, starX, starZ) = Project(system, home);
            float starScale = Fit(bodies, starX, starZ);
            float homeScale = Fit(bodies, 0f, HomeZ); // today's framing: launch body, star free to rim-pin

            float zoomOut = homeScale / starScale;
            if (zoomOut > worst)
            {
                worst = zoomOut;
                worstId = system.Id;
            }
        }

        Assert.True(
            worst <= Budget,
            $"star-centring zooms the chart out {worst}× at {worstId} — over the {Budget}× budget");
    }

    [Fact]
    public void ProjectedRadius_IsNotInterchangeableWithTheStarMapOrbitRadius()
    {
        // Guards invariant 2 against a "simplification": the launch body is pinned to a fixed spot and
        // SeparateXZ nudges crowded pairs, so a ring drawn at the star-map orbit radius visibly misses
        // its planet. If this ever stops finding disagreements, the projection has changed and the
        // comment in SystemChartLayout needs re-checking.
        int disagreements = 0;
        foreach (var (system, home) in RealSystems(15))
        {
            var (bodies, starX, starZ) = Project(system, home);
            float scale = Fit(bodies, starX, starZ);

            foreach (var b in bodies.Where(b => b.OrbitsStar))
            {
                float px = (b.X - starX) * scale, pz = (b.Z - starZ) * scale;
                if (MathF.Abs(SystemChartLayout.OrbitRadius(px, pz) - (b.StarMapRadius * scale)) > 1f)
                {
                    disagreements++;
                }
            }
        }

        Assert.True(disagreements > 0, "the star-map radius now matches the projection everywhere — re-check the invariant");
    }

    [Fact]
    public void FromChart_IsTheExactInverseOfToChart_AcrossRealSystems()
    {
        // A click has to resolve to the scene point it was made on. The chart being star-centred means the
        // centre offset appears in BOTH directions; when only the drawing path had it, every free waypoint
        // landed somewhere else. Round-tripping every real body pins the pair together.
        foreach (var (system, home) in RealSystems(10))
        {
            var (bodies, starX, starZ) = Project(system, home);
            float scale = Fit(bodies, starX, starZ);

            foreach (var b in bodies)
            {
                SystemChartLayout.ToChart(b.X, b.Z, scale, starX, starZ, out float cx, out float cy);
                SystemChartLayout.FromChart(cx, cy, scale, starX, starZ, out float sx, out float sz);
                Assert.Equal(b.X, sx, Tolerance);
                Assert.Equal(b.Z, sz, Tolerance);
            }

            // And the chart's own centre must map back to the star, not to the launch body.
            SystemChartLayout.FromChart(0f, 0f, scale, starX, starZ, out float centreX, out float centreZ);
            Assert.Equal(starX, centreX, Tolerance);
            Assert.Equal(starZ, centreZ, Tolerance);
        }
    }

    [Fact]
    public void ShowRing_SuppressesOrbitsTooSmallToRead()
    {
        Assert.False(SystemChartLayout.ShowRing(0f));
        Assert.False(SystemChartLayout.ShowRing(SystemChartLayout.MinRingRadius - 0.1f));
        Assert.True(SystemChartLayout.ShowRing(SystemChartLayout.MinRingRadius));
        Assert.True(SystemChartLayout.ShowRing(ChartHalf));
    }

    [Fact]
    public void FitScale_FallsBackToAMinimumExtent_ForASystemWithNothingInIt()
    {
        // A lone body right next to the chart centre must not zoom the chart in absurdly far.
        float scale = SystemChartLayout.FitScale(ChartHalf, new[] { 1f }, new[] { 1f }, 0f, 0f);
        Assert.Equal(ChartHalf / (SystemChartLayout.MinExtent * SystemChartLayout.FitMargin), scale, Tolerance);
    }
}
