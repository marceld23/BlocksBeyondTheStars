// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The motion-class derivation (#1331): a species' class follows from the traits it already has (no RNG —
/// existing worlds keep their species), and the capability rules (giants and crawlers never jump, ground
/// birds glide) hold across every generated roster.
/// </summary>
public sealed class CreatureMotionTests
{
    private static CreatureSpecies Species(CreatureHabitat habitat, int legs = 4, bool wings = false, bool gasSac = false,
        LocomotionStyle style = LocomotionStyle.Strider, CreatureBodyPlan plan = CreatureBodyPlan.Standard, float size = 1f)
        => new()
        {
            Id = "spX",
            Habitat = habitat,
            Legs = legs,
            HasWings = wings,
            HasGasSac = gasSac,
            LocoStyle = style,
            BodyPlan = plan,
            Size = size,
        };

    [Fact]
    public void ClassOf_FollowsTheBody()
    {
        Assert.Equal(MotionClass.Swimmer, CreatureMotion.ClassOf(Species(CreatureHabitat.Water, legs: 0)));
        Assert.Equal(MotionClass.Flier, CreatureMotion.ClassOf(Species(CreatureHabitat.Air, legs: 2, wings: true, style: LocomotionStyle.Glider)));
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(Species(CreatureHabitat.Air, legs: 2, wings: true, gasSac: true)));
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(Species(CreatureHabitat.Air, legs: 0, plan: CreatureBodyPlan.Medusa, style: LocomotionStyle.Drifter)));
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 4, gasSac: true, style: LocomotionStyle.Drifter)));
        Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 4)));
        Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 2, style: LocomotionStyle.Hopper)));
        Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 0, style: LocomotionStyle.Slitherer)));
        Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 4, style: LocomotionStyle.Slitherer)));
        Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(Species(CreatureHabitat.Cave, legs: 8)));   // Q2: many-legged scuttlers crawl
        Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(Species(CreatureHabitat.Land, legs: 6)));
        Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(Species(CreatureHabitat.Amphibian, legs: 4)));  // ashore class
        Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(Species(CreatureHabitat.Amphibian, legs: 0)));
    }

    [Fact]
    public void Amphibian_SwimsInWater_WalksAshore()
    {
        var frog = Species(CreatureHabitat.Amphibian, legs: 4);
        Assert.Equal(MotionClass.Swimmer, CreatureMotion.EffectiveClass(frog, inWater: true));
        Assert.Equal(MotionClass.Walker, CreatureMotion.EffectiveClass(frog, inWater: false));
        Assert.Equal(MotionClass.Walker, CreatureMotion.EffectiveClass(Species(CreatureHabitat.Land), inWater: true)); // only amphibians switch
    }

    [Fact]
    public void Jumping_OnlyNonGiantWalkers()
    {
        Assert.True(CreatureMotion.CanJump(Species(CreatureHabitat.Land, legs: 4, size: 1.2f)));
        Assert.False(CreatureMotion.CanJump(Species(CreatureHabitat.Land, legs: 4, size: 2.1f)));                    // Q3: giant by size
        Assert.False(CreatureMotion.CanJump(Species(CreatureHabitat.Land, legs: 4, plan: CreatureBodyPlan.Titan, size: 4f)));
        Assert.False(CreatureMotion.CanJump(Species(CreatureHabitat.Land, legs: 0, style: LocomotionStyle.Slitherer))); // crawlers never
        Assert.False(CreatureMotion.CanJump(Species(CreatureHabitat.Air, legs: 2, wings: true)));                   // fliers take off instead
    }

    [Fact]
    public void GroundBirds_AreWingedLandWalkers_ThatGlide()
    {
        Assert.True(CreatureMotion.Glides(Species(CreatureHabitat.Land, legs: 2, wings: true)));
        Assert.False(CreatureMotion.Glides(Species(CreatureHabitat.Land, legs: 2, wings: false)));
        Assert.False(CreatureMotion.Glides(Species(CreatureHabitat.Air, legs: 2, wings: true)));   // a real flier
        Assert.False(CreatureMotion.Glides(Species(CreatureHabitat.Land, legs: 4, wings: true, size: 2.5f))); // giants don't bound
    }

    [Fact]
    public void StepLimits_MatchTheDecisions()
    {
        Assert.Equal(1, CreatureMotion.StepUpLimit(MotionClass.Walker));
        Assert.Equal(1, CreatureMotion.StepUpLimit(MotionClass.Crawler));
        Assert.Equal(3, CreatureMotion.StepDownLimit(MotionClass.Walker, giant: false));
        Assert.Equal(1, CreatureMotion.StepDownLimit(MotionClass.Walker, giant: true));
        Assert.Equal(2, CreatureMotion.StepDownLimit(MotionClass.Crawler, giant: false));
    }

    [Fact]
    public void Key_RoundTrips_AndUnknownReadsAsWalker()
    {
        foreach (MotionClass cls in System.Enum.GetValues(typeof(MotionClass)))
        {
            Assert.Equal(cls, CreatureMotion.Parse(CreatureMotion.Key(cls)));
        }

        Assert.Equal(MotionClass.Walker, CreatureMotion.Parse(null));
        Assert.Equal(MotionClass.Walker, CreatureMotion.Parse("")); // a legacy server sends nothing
    }

    [Fact]
    public void GeneratedRosters_HoldTheInvariants_AcrossManyWorlds()
    {
        // Every class shows up somewhere, no titan or crawler ever jumps, air species are never ground
        // classes and water species always swim — over a spread of seeds and planet types.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var seen = new HashSet<MotionClass>();
        foreach (var planet in content.Planets.Values)
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                foreach (var sp in CreatureGenerator.GenerateRoster(planet, seed * 7919))
                {
                    var cls = CreatureMotion.ClassOf(sp);
                    seen.Add(cls);
                    if (sp.BodyPlan == CreatureBodyPlan.Titan || cls == MotionClass.Crawler)
                    {
                        Assert.False(CreatureMotion.CanJump(sp), $"{sp.Name} ({cls}) must not jump");
                    }

                    if (sp.Habitat == CreatureHabitat.Air)
                    {
                        Assert.True(cls is MotionClass.Flier or MotionClass.Hoverer, $"{sp.Name} is an air species but {cls}");
                    }

                    if (sp.Habitat == CreatureHabitat.Water)
                    {
                        Assert.Equal(MotionClass.Swimmer, cls);
                    }

                    if (sp.BodyPlan == CreatureBodyPlan.Medusa)
                    {
                        Assert.True(cls is MotionClass.Hoverer or MotionClass.Swimmer, $"a medusa drifts or swims, not {cls}");
                    }
                }
            }
        }

        Assert.Contains(MotionClass.Walker, seen);
        Assert.Contains(MotionClass.Crawler, seen);
        Assert.Contains(MotionClass.Flier, seen);
        Assert.Contains(MotionClass.Hoverer, seen);
        Assert.Contains(MotionClass.Swimmer, seen);
    }
}
