// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The spacing guarantee for the space-flight view (#493): planets and their satellites must end up
/// visibly apart, not hairline-close. The layout stage under test is
/// <see cref="SystemBodyLayout"/>; the geometry is fed from the REAL <see cref="UniverseGenerator"/>
/// through the same conversion the space view uses, so a regression in either the gap rule or the
/// relaxation shows up here rather than only on screen.
/// </summary>
public sealed class SystemBodyLayoutTests
{
    // Mirrors SpaceView: system units → flight-view units, and body diameter from real circumference.
    private const float SystemViewScale = SystemBodyLayout.FlightViewScale;
    private const float KeepOutMargin = 10f; // the shell the ship is held at, from SpaceView
    private const float Tolerance = 0.01f;

    private static float RadiusOf(string id, CelestialKind kind, string? planetType, float sizeBias = 0f)
    {
        var cls = WorldConstants.SizeClassFor(kind, planetType ?? string.Empty);
        return (8f + WorldConstants.CircumferenceFor(id, cls, sizeBias) / 220f) * 0.5f;
    }

    private static float RadiusOf(CelestialBody b) => RadiusOf(b.Id, b.Kind, b.PlanetType, b.SizeBias);

    [Fact]
    public void ClearGap_NeverDropsBelowTheShipsOwnKeepOutMargin()
    {
        // Two of the smallest bodies in the game still have to leave a gap the ship fits through,
        // otherwise "fly between those two rocks" is impossible and they read as one clump.
        float smallest = RadiusOf("sys0-a0", CelestialKind.AsteroidField, "asteroid");
        Assert.True(
            SystemBodyLayout.ClearGapFor(smallest, smallest) > KeepOutMargin,
            $"gap {SystemBodyLayout.ClearGapFor(smallest, smallest)} must exceed the keep-out margin {KeepOutMargin}");
    }

    [Fact]
    public void ClearGap_GrowsWithTheBodies()
    {
        // The bug was a FLAT gap: two gas giants got the same hairline as two pebbles.
        float small = SystemBodyLayout.ClearGapFor(6f, 6f);
        float large = SystemBodyLayout.ClearGapFor(31f, 31f);
        Assert.True(large > small * 2f, $"large-body gap {large} should dwarf small-body gap {small}");
    }

    [Fact]
    public void MinOrbit_PutsAMoonClearOfItsParentsSurface()
    {
        const float planetRadius = 27f, moonRadius = 11.5f;
        float orbit = SystemBodyLayout.MinOrbitFor(planetRadius, moonRadius);
        Assert.True(
            orbit - planetRadius - moonRadius >= SystemBodyLayout.MinBodyGap - Tolerance,
            $"orbit {orbit} leaves only {orbit - planetRadius - moonRadius} of clear space");
    }

    [Fact]
    public void MinOrbit_GivesAMoonMoreRoomThanTwoStrangers()
    {
        // #1601: every moon's star-map orbit lies inside its drawn planet, so the clamp IS the moon layout.
        // At the plain body gap a planet and its moons read as one clump; the parent pair gets 1.5× it.
        const float planetRadius = 27f, moonRadius = 11.5f;
        float clear = SystemBodyLayout.MinOrbitFor(planetRadius, moonRadius) - planetRadius - moonRadius;
        float plain = SystemBodyLayout.ClearGapFor(planetRadius, moonRadius);
        Assert.Equal(1.5f, SystemBodyLayout.MoonOrbitGapFactor);
        Assert.InRange(clear, plain * SystemBodyLayout.MoonOrbitGapFactor - Tolerance, plain * SystemBodyLayout.MoonOrbitGapFactor + Tolerance);
    }

    [Fact]
    public void NextMoonOrbit_StacksASlotAClearGapAboveTheLastOne()
    {
        const float planetRadius = 27f, inner = 11.5f, outer = 9f;
        float first = SystemBodyLayout.MinOrbitFor(planetRadius, inner);
        float second = SystemBodyLayout.NextMoonOrbit(planetRadius, outer, first, inner);
        Assert.Equal(first + inner + outer + SystemBodyLayout.ClearGapFor(inner, outer), second, 3);

        // With no slot below it (an orbit of 0), the next slot is the first one: just outside the parent's surface.
        Assert.Equal(SystemBodyLayout.MinOrbitFor(planetRadius, outer), SystemBodyLayout.NextMoonOrbit(planetRadius, outer, 0f, 0f), 3);
    }

    [Fact]
    public void FlightViewScale_IsTheRoomierSetting()
    {
        // #1600: 0.16 read as "planets huddle"; 0.24 is the measured compromise (nearest planet ≈ 20 s
        // in the starter ship, ~2× the empty space). A silent drift either way is a gameplay change.
        Assert.Equal(0.24f, SystemBodyLayout.FlightViewScale);
    }

    [Fact]
    public void AMoonsParent_KeepsItsTrueDirection_AtAClearDistance()
    {
        // #3919: the star map puts a first moon's planet 21.6 flight units from the launch column — inside its shell.
        const float planetRadius = 23f, moonRadius = 11f;
        var (x, z) = SystemBodyLayout.HomeParentPosition(-12.96f, 17.28f, planetRadius, moonRadius); // 21.6 units away
        float dist = MathF.Sqrt((x * x) + (z * z));

        Assert.Equal(SystemBodyLayout.MinOrbitFor(planetRadius, moonRadius), dist, 3);
        Assert.True(dist >= planetRadius + KeepOutMargin + SystemBodyLayout.LaunchColumnClearance);
        Assert.Equal(-0.6f, x / dist, 3); // the same direction as the star map's offset
        Assert.Equal(0.8f, z / dist, 3);

        // A planet the star map already puts further out stays where it is.
        var (fx, fz) = SystemBodyLayout.HomeParentPosition(300f, 0f, planetRadius, moonRadius);
        Assert.Equal(300f, fx, 3);
        Assert.Equal(0f, fz, 3);
    }

    [Fact]
    public void SeparateXZ_HoldsTheFlightPlaneClearOfTheLaunchColumn_ButNotTheLaunchBodysOwnMoons()
    {
        // #3919: a body on the column is pushed straight out until its keep-out shell clears it; the launch body's own
        // moons hang in its plane far below the climb and are left to the launch body's rule.
        float[] x = { 6f, 300f };
        float[] z = { 8f, 0f };
        float[] r = { 12f, 30f };
        SystemBodyLayout.SeparateXZ(x, z, r, 0f, -20f, 5f, System.Array.Empty<int>());

        float need = r[0] + KeepOutMargin + SystemBodyLayout.LaunchColumnClearance;
        Assert.Equal(need, MathF.Sqrt((x[0] * x[0]) + (z[0] * z[0])), 3);
        Assert.Equal(0.6f, x[0] / need, 3); // straight out, in the direction it already had
        Assert.Equal(300f, x[1], 3);        // a body clear of everything stays put
        Assert.Equal(0f, z[1], 3);

        // A moon of the launch body, laid out in its plane: only the launch body's own rule moves it.
        float[] mx = { 0f };
        float[] mz = { 5f }; // 25 from home: clear of it (4 + 5 + a gap of 14), well inside the column's reach
        float[] mr = { 4f };
        SystemBodyLayout.SeparateXZ(mx, mz, mr, 0f, -20f, 5f, new[] { 0 });
        Assert.Equal(0f, mx[0], 3);
        Assert.Equal(5f, mz[0], 3);
    }

    [Fact]
    public void EveryBodyKeepsItsClearGap_AcrossRealGeneratedSystems()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        int systemsChecked = 0, bodiesChecked = 0;

        // Replay classic worlds AND archetype-varied ones (#546): the lone giant's 8-moon ladder and the
        // size-biased bodies must keep the same clear-gap guarantee as the uniform layout.
        // Runs 1..40 use the classic description, 41..80 re-run the same 40 seeds with variance on, and
        // 81..120 add asteroid belts (#683) on top — belt members share an orbit annulus, so they are
        // exactly the pairs the angular-slot spacing must keep apart.
        for (long run = 1; run <= 120; run++)
        {
            long seed = ((run - 1) % 40) + 1;
            var desc = new WorldDescription { SystemVariance = run > 40, AsteroidBelts = run > 80 };
            var galaxy = new UniverseGenerator(seed, desc, content).Generate();
            foreach (var system in galaxy.Systems)
            {
                // The body you launched from: it stays put and is drawn oversized, as in the view.
                var home = system.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Planet);
                if (home is null)
                {
                    continue;
                }

                systemsChecked++;
                var scene = LayOut(system, home);
                bodiesChecked += scene.R.Length;
                AssertClearGaps(scene, $"seed {seed} {system.Id}");
            }
        }

        Assert.True(systemsChecked > 300, $"expected a broad sample, got {systemsChecked} systems");
        Assert.True(bodiesChecked > 3000, $"expected a broad sample, got {bodiesChecked} bodies");
    }

    [Fact]
    public void LaunchedFromAMoon_NoBodysKeepOutShellHoldsTheLaunchColumn()
    {
        // #3919: after a lift-off from a planet's first moon the ship was thrown far away and drifted backwards — the
        // parent planet hung at the star map's moon orbit, so the top of the climb lay inside its keep-out shell. With
        // every moon of a broad sample as the launch body, no body's shell (nor its clear margin) may reach the column,
        // and the usual clear gaps between the bodies still hold.
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        int moonsChecked = 0, firstMoons = 0;
        for (long run = 1; run <= 60; run++)
        {
            long seed = ((run - 1) % 20) + 1;
            var desc = new WorldDescription { SystemVariance = run > 20, AsteroidBelts = run > 40 };
            var galaxy = new UniverseGenerator(seed, desc, content).Generate();
            foreach (var system in galaxy.Systems)
            {
                foreach (var home in system.Bodies.Where(b => b.Kind == CelestialKind.Moon))
                {
                    moonsChecked++;
                    firstMoons += home.Id.EndsWith("-m0", StringComparison.Ordinal) ? 1 : 0;
                    var scene = LayOut(system, home);
                    string where = $"seed {seed} run {run}: launched from {home.Id}";
                    AssertColumnClear(scene, where);
                    AssertClearGaps(scene, where);
                }
            }
        }

        Assert.True(moonsChecked > 500, $"expected a broad sample, got {moonsChecked} moons");
        Assert.True(firstMoons > 200, $"expected many first moons (the reported case), got {firstMoons}");
    }

    [Fact]
    public void LaunchedFromAPlanet_TheLaunchColumnIsClearToo()
    {
        // The same guarantee for the classic launch from a planet: its moons hang in its own plane far below, but a
        // landable asteroid orbiting right by it, or a neighbour's moon the relax pass shoved inward, could reach the
        // column too — the relax pass holds them clear of it (#3919).
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        int checkedHomes = 0;
        for (long seed = 1; seed <= 20; seed++)
        {
            var galaxy = new UniverseGenerator(seed, new WorldDescription { SystemVariance = true }, content).Generate();
            foreach (var system in galaxy.Systems)
            {
                foreach (var home in system.Bodies.Where(b => b.Kind == CelestialKind.Planet))
                {
                    checkedHomes++;
                    AssertColumnClear(LayOut(system, home), $"seed {seed}: launched from {home.Id}");
                }
            }
        }

        Assert.True(checkedHomes > 300, $"expected a broad sample, got {checkedHomes} planets");
    }

    // ---- the scene, laid out as SpaceView.BuildSystemBodies does ----

    /// <summary>One flight scene: every body but the launch body, after the relax pass.</summary>
    private sealed record Scene(float[] X, float[] Y, float[] Z, float[] R, int[] HomeMoons, float HomeRadius, string[] Ids);

    private const float HomeX = SystemBodyLayout.HomeX, HomeY = SystemBodyLayout.HomeY, HomeZ = SystemBodyLayout.HomeZ;

    /// <summary>Mirrors SpaceView.BuildSystemBodies for a launch from <paramref name="home"/>: planets at their scaled
    /// orbit coords (a moon home's parent at its clear distance, #3919), moons on their ascending orbit slots around their
    /// parent (#548), asteroids straight from their disc coords, then the relax pass.</summary>
    private static Scene LayOut(StarSystem system, CelestialBody home)
    {
        float homeTrueRadius = RadiusOf(home);
        float homeRadius = homeTrueRadius * SystemBodyLayout.HomeDiameterFactor;
        var xs = new List<float>();
        var ys = new List<float>();
        var zs = new List<float>();
        var radii = new List<float>();
        var ids = new List<string>();
        var homeMoons = new List<int>();

        void Add(string id, float x, float y, float z, float r)
        {
            ids.Add(id); xs.Add(x); ys.Add(y); zs.Add(z); radii.Add(r);
        }

        var parents = new List<(float Sx, float Sz, float X, float Y, float Z, float R)>
        {
            (home.SystemX, home.SystemZ, HomeX, HomeY, HomeZ, homeRadius),
        };
        var parentIndexById = new Dictionary<string, int> { [home.Id] = 0 };
        var ladder = new Dictionary<int, (float Orbit, float Radius)>();
        foreach (var b in system.Bodies.Where(b => b.Id != home.Id && b.Kind == CelestialKind.Planet))
        {
            float x = (b.SystemX - home.SystemX) * SystemViewScale;
            float z = (b.SystemZ - home.SystemZ) * SystemViewScale;
            float r = RadiusOf(b);
            if (home.Kind == CelestialKind.Moon && b.Id == home.ParentId)
            {
                // #3919: the planet the launch moon orbits — at a clear distance, the launch moon holding its first slot.
                (x, z) = SystemBodyLayout.HomeParentPosition(x, z, r, homeTrueRadius);
                ladder[parents.Count] = (MathF.Sqrt((x * x) + (z * z)), homeTrueRadius);
            }

            Add(b.Id, x, 0f, z, r);
            parentIndexById[b.Id] = parents.Count;
            parents.Add((b.SystemX, b.SystemZ, x, 0f, z, r));
        }

        foreach (var b in system.Bodies.Where(b => b.Id != home.Id && b.Kind == CelestialKind.Moon))
        {
            int best = ParentIndexOf(b, parents, parentIndexById);
            var parent = parents[best];
            float relX = (b.SystemX - parent.Sx) * SystemViewScale;
            float relZ = (b.SystemZ - parent.Sz) * SystemViewScale;
            float moonRadius = RadiusOf(b);
            float minClear = ladder.TryGetValue(best, out var last)
                ? SystemBodyLayout.NextMoonOrbit(parent.R, moonRadius, last.Orbit, last.Radius)
                : SystemBodyLayout.MinOrbitFor(parent.R, moonRadius);
            float mag = MathF.Sqrt((relX * relX) + (relZ * relZ));
            if (mag < minClear)
            {
                // The star map's moon orbit is inside the rendered planet, so this always fires.
                (relX, relZ) = mag > 0.01f ? (relX / mag * minClear, relZ / mag * minClear) : (minClear, 0f);
                mag = minClear;
            }

            ladder[best] = (mag, moonRadius);
            Add(b.Id, parent.X + relX, parent.Y, parent.Z + relZ, moonRadius);
            if (best == 0)
            {
                homeMoons.Add(xs.Count - 1);
            }
        }

        foreach (var b in system.Bodies.Where(b => b.Id != home.Id && b.Kind == CelestialKind.AsteroidField))
        {
            string type = string.IsNullOrEmpty(b.PlanetType) ? "asteroid" : b.PlanetType;
            Add(b.Id, (b.SystemX - home.SystemX) * SystemViewScale, 0f, (b.SystemZ - home.SystemZ) * SystemViewScale,
                RadiusOf(b.Id, b.Kind, type, b.SizeBias));
        }

        var bx = xs.ToArray();
        var bz = zs.ToArray();
        var br = radii.ToArray();
        SystemBodyLayout.SeparateXZ(bx, bz, br, HomeX, HomeZ, homeRadius, homeMoons.ToArray());
        return new Scene(bx, ys.ToArray(), bz, br, homeMoons.ToArray(), homeRadius, ids.ToArray());
    }

    /// <summary>A moon's parent: by ParentId (authoritative, #548), else the nearest planet — as SpaceView does.</summary>
    private static int ParentIndexOf(
        CelestialBody moon, List<(float Sx, float Sz, float X, float Y, float Z, float R)> parents, Dictionary<string, int> byId)
    {
        if (!string.IsNullOrEmpty(moon.ParentId) && byId.TryGetValue(moon.ParentId, out int index))
        {
            return index;
        }

        int best = 0;
        float bestSq = float.MaxValue;
        for (int i = 0; i < parents.Count; i++)
        {
            float ddx = moon.SystemX - parents[i].Sx, ddz = moon.SystemZ - parents[i].Sz;
            float dsq = (ddx * ddx) + (ddz * ddz);
            if (dsq < bestSq)
            {
                bestSq = dsq;
                best = i;
            }
        }

        return best;
    }

    private static void AssertClearGaps(Scene s, string where)
    {
        for (int a = 0; a < s.R.Length; a++)
        {
            for (int c = a + 1; c < s.R.Length; c++)
            {
                float dx = s.X[a] - s.X[c], dz = s.Z[a] - s.Z[c];
                float gap = MathF.Sqrt((dx * dx) + (dz * dz)) - s.R[a] - s.R[c];
                Assert.True(
                    gap >= SystemBodyLayout.ClearGapFor(s.R[a], s.R[c]) - Tolerance,
                    $"{where}: bodies {s.Ids[a]}/{s.Ids[c]} left only {gap:0.00} of clear space");
            }
        }

        // The moons laid out in home's plane must clear home itself — home never gives way.
        foreach (int a in s.HomeMoons)
        {
            float dx = s.X[a] - HomeX, dz = s.Z[a] - HomeZ;
            float gap = MathF.Sqrt((dx * dx) + (dz * dz)) - s.R[a] - s.HomeRadius;
            Assert.True(
                gap >= SystemBodyLayout.ClearGapFor(s.R[a], s.HomeRadius) - Tolerance,
                $"{where}: a moon of the launch body left only {gap:0.00} to it");
        }
    }

    /// <summary>The climb ends at the top of the launch column (the scene's origin); there the keep-out rules start. No
    /// body's shell may hold that point, with <see cref="SystemBodyLayout.LaunchColumnClearance"/> to spare.</summary>
    private static void AssertColumnClear(Scene s, string where)
    {
        for (int a = 0; a < s.R.Length; a++)
        {
            float dist = MathF.Sqrt((s.X[a] * s.X[a]) + (s.Y[a] * s.Y[a]) + (s.Z[a] * s.Z[a]));
            float need = s.R[a] + KeepOutMargin + SystemBodyLayout.LaunchColumnClearance;
            Assert.True(dist >= need - Tolerance, $"{where}: {s.Ids[a]} sits {dist:0.0} from the launch column, its shell needs {need:0.0}");
        }

        float home = MathF.Sqrt((HomeX * HomeX) + (HomeY * HomeY) + (HomeZ * HomeZ));
        Assert.True(home >= s.HomeRadius + KeepOutMargin, $"{where}: the launch body's own shell holds the top of the climb");
    }
}
