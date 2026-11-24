// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3969: a fixed star system as data — the checks a <c>data/systems/</c> file passes (a broken one is reported and
/// dropped, never a failed load), the builder that turns it into a galaxy system, and the body profile that takes a fixed
/// value where the data sets one and today's roll everywhere else.
/// </summary>
public sealed class FixedSystemDefinitionTests
{
    private static FixedBodyDefinition Planet(string id, int circumference = 0, float gravity = 0f) => new()
    {
        Id = id,
        Kind = "planet",
        PlanetType = "rocky",
        OrbitRadius = 500f,
        OrbitAngle = 90f,
        Circumference = circumference,
        Gravity = gravity,
    };

    private static FixedSystemDefinition System(string id, params FixedBodyDefinition[] bodies)
    {
        var def = new FixedSystemDefinition { Key = id, Name = "Test", Appear = new FixedSystemAppearance { AfterGrowth = 2 } };
        def.Bodies.AddRange(bodies);
        return def;
    }

    private static GameContent ContentWith(out List<string> warnings, params FixedSystemDefinition[] systems)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var seen = new List<string>();
        content.SetFixedSystems(systems, seen.Add);
        warnings = seen;
        return content;
    }

    [Fact]
    public void ABrokenSystem_IsDroppedWithAWarning_AndAGoodOneKept()
    {
        var good = System("good", Planet("a", 1600, 0.5f), new FixedBodyDefinition { Id = "m", Kind = "moon", Parent = "a", PlanetType = "rocky" });
        var rolledId = System("sys9", Planet("a"));
        var unknownType = System("badtype", new FixedBodyDefinition { Id = "a", PlanetType = "no_such_type" });
        var orphanMoon = System("orphan", new FixedBodyDefinition { Id = "m", Kind = "moon", PlanetType = "rocky" });
        var oddSize = System("oddsize", Planet("a", 1601));
        var heavy = System("heavy", Planet("a", gravity: 3f));
        var twoRules = System("tworules", Planet("a"));
        twoRules.Appear.VisibleIf.Add("milestone:x");
        var badColour = System("badcolour", Planet("a"));
        badColour.Star = new FixedStarDefinition { Color = "yellow" };

        var content = ContentWith(out var warnings, good, rolledId, unknownType, orphanMoon, oddSize, heavy, twoRules, badColour,
            System("good", Planet("b")));

        Assert.Equal("good", Assert.Single(content.FixedSystems).Id);
        Assert.Equal(8, warnings.Count);
        Assert.All(warnings, w => Assert.StartsWith("Skipping fixed system", w));
        Assert.Contains(warnings, w => w.Contains("already loaded", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBuilder_PlacesBodiesAroundTheStarAndTheirParent_WithTheirValues()
    {
        var def = System("tst", Planet("a", 1600, 0.5f), new FixedBodyDefinition
        {
            Id = "m",
            Kind = "moon",
            Parent = "a",
            PlanetType = "rocky",
            OrbitRadius = 90f,
            OrbitAngle = 0f,
            OrbitPeriodDays = 1.5f,
            Ring = true,
            NameKey = "body.none",
            VisibleDiameter = 40f,
        });
        def.Star = new FixedStarDefinition { Color = "0xFFF4E0" };
        FixedSystemRules.Normalize(def);

        var system = FixedSystemBuilder.Build(def, StarSystemOrigin.Fixed, 10f, 20f, (key, fallback) => fallback);

        Assert.Equal(StarSystemOrigin.Fixed, system.Origin);
        Assert.False(system.IsRolled);
        Assert.Equal(0xFFF4E0, system.Fixed!.Star!.ColorRgb);
        Assert.Equal(new[] { "tst-a", "tst-m" }, system.Bodies.Select(b => b.Id));
        var (planet, moon) = (system.Bodies[0], system.Bodies[1]);
        Assert.Equal(0f, planet.SystemX, 3);
        Assert.Equal(500f, planet.SystemZ, 3);
        Assert.Equal(CelestialKind.Moon, moon.Kind);
        Assert.Equal("tst-a", moon.ParentId);
        Assert.Equal(planet.SystemX + 90f, moon.SystemX, 3);
        Assert.Equal(1.5f, moon.OrbitPeriodDays);
        Assert.NotEqual(0, moon.RingSeed);
        Assert.Equal(0, planet.RingSeed);
        Assert.Equal("Test", moon.Name); // no own name: the system's
    }

    [Fact]
    public void TheProfile_TakesTheFixedValues_AndRollsEverythingElseBitForBit()
    {
        const long seed = 4242;
        int gen = WorldDescription.CurrentTerrainGeneration;
        var fixedValues = new FixedBodyDefinition { Circumference = 16000, Gravity = 1.0f, VisibleDiameter = 242f };
        var fixedProfile = BodyProfile.Resolve(fixedValues, "sol-earth", WorldConstants.WorldSizeClass.Planet, 0f, seed, gen);
        Assert.Equal(16000, fixedProfile.Circumference);
        Assert.Equal(1.0f, fixedProfile.GravityFactor);
        Assert.Equal(242f, fixedProfile.VisibleDiameter);

        foreach (var (id, cls, bias) in new[]
                 {
                     ("sys3-p1", WorldConstants.WorldSizeClass.Planet, 0.4f), ("sys0-p0-m1", WorldConstants.WorldSizeClass.Moon, 0f),
                     ("sys7-a2", WorldConstants.WorldSizeClass.Asteroid, 0f),
                 })
        {
            foreach (var g in new[] { gen, WorldDescription.HashVarietyGeneration - 1 })
            {
                var rolled = BodyProfile.Resolve(null, id, cls, bias, seed, g);
                var zeros = BodyProfile.Resolve(new FixedBodyDefinition(), id, cls, bias, seed, g);
                int circ = WorldConstants.CircumferenceFor(id, cls, bias, seed, g);
                Assert.Equal(circ, rolled.Circumference);
                Assert.Equal(BodyGravity.FactorFor(id, cls, seed, g), rolled.GravityFactor);
                Assert.Equal(SystemBodyLayout.OrbitDiameter(circ), rolled.VisibleDiameter);
                Assert.Equal((rolled.Circumference, rolled.GravityFactor, rolled.VisibleDiameter), (zeros.Circumference, zeros.GravityFactor, zeros.VisibleDiameter));
            }
        }
    }

    [Fact]
    public void TheRolledIdHelper_ReadsOnlyRolledIds()
    {
        Assert.Equal(12, StarSystemIds.RolledIndex("sys12"));
        Assert.Equal(-1, StarSystemIds.RolledIndex("sys"));
        Assert.Equal(-1, StarSystemIds.RolledIndex("sys-1"));
        Assert.Equal(-1, StarSystemIds.RolledIndex("sol"));
        Assert.Equal(-1, StarSystemIds.RolledIndex(FixedSystemRules.GuardianSystemId));
        Assert.True(StarSystemIds.HasRolledPrefix("sysx"));
        Assert.False(StarSystemIds.IsRolled("sysx"));
        Assert.True(StarSystemIds.IsStartRolled("sys3", 4));
        Assert.False(StarSystemIds.IsStartRolled("sys4", 4));
        Assert.True(new StarSystem { Id = "sys2" }.IsRolled);
        Assert.False(new StarSystem { Id = "sys2", Origin = StarSystemOrigin.Pack }.IsRolled);
    }
}
