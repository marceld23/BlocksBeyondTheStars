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
    private const float SystemViewScale = 0.16f;
    private const float KeepOutMargin = 10f; // the shell the ship is held at, from SpaceView
    private const float Tolerance = 0.01f;

    private static float RadiusOf(string id, CelestialKind kind, string? planetType, float sizeBias = 0f)
    {
        var cls = WorldConstants.SizeClassFor(kind, planetType ?? string.Empty);
        return (8f + WorldConstants.CircumferenceFor(id, cls, sizeBias) / 220f) * 0.5f;
    }

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
                float homeRadius = RadiusOf(home.Id, home.Kind, home.PlanetType, home.SizeBias) * 3.2f;
                const float homeX = 0f, homeZ = -20f;

                var xs = new List<float>();
                var zs = new List<float>();
                var radii = new List<float>();
                var homeMoons = new List<int>();

                // Planets, at their scaled orbit coords.
                var parents = new List<(float Sx, float Sz, float X, float Z, float R)>
                {
                    (home.SystemX, home.SystemZ, homeX, homeZ, homeRadius),
                };
                var parentIndexById = new Dictionary<string, int> { [home.Id] = 0 };
                foreach (var b in system.Bodies)
                {
                    if (b.Id == home.Id || b.Kind != CelestialKind.Planet)
                    {
                        continue;
                    }

                    float x = (b.SystemX - home.SystemX) * SystemViewScale;
                    float z = (b.SystemZ - home.SystemZ) * SystemViewScale;
                    float r = RadiusOf(b.Id, b.Kind, b.PlanetType, b.SizeBias);
                    xs.Add(x); zs.Add(z); radii.Add(r);
                    parentIndexById[b.Id] = parents.Count;
                    parents.Add((b.SystemX, b.SystemZ, x, z, r));
                }

                // Moons: parented by ParentId (nearest-planet fallback), each on its own ascending orbit
                // slot around its parent — mirrors SpaceView's moon ladder (#548).
                var ladder = new Dictionary<int, (float Orbit, float Radius)>();
                foreach (var b in system.Bodies)
                {
                    if (b.Id == home.Id || b.Kind != CelestialKind.Moon)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(b.ParentId) || !parentIndexById.TryGetValue(b.ParentId, out int best))
                    {
                        best = 0;
                        float bestSq = float.MaxValue;
                        for (int i = 0; i < parents.Count; i++)
                        {
                            float ddx = b.SystemX - parents[i].Sx, ddz = b.SystemZ - parents[i].Sz;
                            float dsq = (ddx * ddx) + (ddz * ddz);
                            if (dsq < bestSq)
                            {
                                bestSq = dsq;
                                best = i;
                            }
                        }
                    }

                    var parent = parents[best];
                    float relX = (b.SystemX - parent.Sx) * SystemViewScale;
                    float relZ = (b.SystemZ - parent.Sz) * SystemViewScale;
                    float moonRadius = RadiusOf(b.Id, b.Kind, b.PlanetType, b.SizeBias);
                    float minClear = SystemBodyLayout.MinOrbitFor(parent.R, moonRadius);
                    if (ladder.TryGetValue(best, out var last))
                    {
                        minClear = MathF.Max(minClear,
                            last.Orbit + last.Radius + moonRadius + SystemBodyLayout.ClearGapFor(last.Radius, moonRadius));
                    }

                    float mag = MathF.Sqrt((relX * relX) + (relZ * relZ));
                    if (mag < minClear)
                    {
                        // The star map's moon orbit is inside the rendered planet, so this always fires.
                        if (mag > 0.01f)
                        {
                            relX = relX / mag * minClear;
                            relZ = relZ / mag * minClear;
                        }
                        else
                        {
                            relX = minClear;
                            relZ = 0f;
                        }

                        mag = minClear;
                    }

                    ladder[best] = (mag, moonRadius);

                    xs.Add(parent.X + relX); zs.Add(parent.Z + relZ); radii.Add(moonRadius);
                    if (best == 0)
                    {
                        homeMoons.Add(xs.Count - 1);
                    }
                }

                // Landable asteroids, straight from their disc coords.
                foreach (var b in system.Bodies)
                {
                    if (b.Id == home.Id || b.Kind != CelestialKind.AsteroidField)
                    {
                        continue;
                    }

                    string type = string.IsNullOrEmpty(b.PlanetType) ? "asteroid" : b.PlanetType;
                    xs.Add((b.SystemX - home.SystemX) * SystemViewScale);
                    zs.Add((b.SystemZ - home.SystemZ) * SystemViewScale);
                    radii.Add(RadiusOf(b.Id, b.Kind, type, b.SizeBias));
                }

                var bx = xs.ToArray();
                var bz = zs.ToArray();
                var br = radii.ToArray();
                SystemBodyLayout.SeparateXZ(bx, bz, br, homeX, homeZ, homeRadius, homeMoons.ToArray());

                for (int a = 0; a < bx.Length; a++)
                {
                    bodiesChecked++;
                    for (int c = a + 1; c < bx.Length; c++)
                    {
                        float dx = bx[a] - bx[c], dz = bz[a] - bz[c];
                        float gap = MathF.Sqrt((dx * dx) + (dz * dz)) - br[a] - br[c];
                        Assert.True(
                            gap >= SystemBodyLayout.ClearGapFor(br[a], br[c]) - Tolerance,
                            $"seed {seed} {system.Id}: bodies {a}/{c} left only {gap:0.00} of clear space");
                    }
                }

                // The moons laid out in home's plane must clear home itself — home never gives way.
                foreach (int a in homeMoons)
                {
                    float dx = bx[a] - homeX, dz = bz[a] - homeZ;
                    float gap = MathF.Sqrt((dx * dx) + (dz * dz)) - br[a] - homeRadius;
                    Assert.True(
                        gap >= SystemBodyLayout.ClearGapFor(br[a], homeRadius) - Tolerance,
                        $"seed {seed} {system.Id}: a moon of the launch body left only {gap:0.00} to it");
                }
            }
        }

        Assert.True(systemsChecked > 300, $"expected a broad sample, got {systemsChecked} systems");
        Assert.True(bodiesChecked > 3000, $"expected a broad sample, got {bodiesChecked} bodies");
    }
}
