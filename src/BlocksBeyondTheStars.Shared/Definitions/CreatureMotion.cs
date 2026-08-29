// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// HOW a creature moves through the world (#1331) — the mechanical class the server's vertical model and
/// the client's animator key off. Distinct from <see cref="CreatureHabitat"/> (WHERE it lives) and
/// <see cref="LocomotionStyle"/> (the RHYTHM it moves with): a cave dweller with eight legs is a crawler,
/// a winged land species is a walker that glides, a gas-sac drifter is a hoverer whatever the air around it.
/// </summary>
public enum MotionClass : byte
{
    Walker,   // legs on the ground — gravity, real jumps (unless a giant), 1-block ledges
    Crawler,  // legless / slitherers / many-legged scuttlers — gravity, never jumps, climbs 1 block slowly
    Flier,    // winged air species — flies, lands to rest and sleep, takes off again
    Hoverer,  // buoyant — gas sacs and medusae — never lands, never sinks
    Swimmer,  // in the water column (water species; amphibians while they are in water)
}

/// <summary>
/// Derives a species' <see cref="MotionClass"/> and movement capabilities from the traits it already has.
/// Pure and deterministic, and — deliberately — <b>not</b> a generator roll: no RNG is consumed, so every
/// world created before motion classes existed keeps its species bit-for-bit and simply gains the class
/// its body implies (decision Q8/A, 2026-08-29).
/// </summary>
public static class CreatureMotion
{
    /// <summary>Standard-plan species at or above this size count as giants (Q3): no jumping, 1-block steps.
    /// Titans (3.5–6) are giants by plan; the standard roll caps at 2.2, so this is a narrow band of big
    /// grazers that should plod rather than bound.</summary>
    public const float GiantSize = 2.0f;

    /// <summary>The class a species moves in when it is on its home ground. Amphibians report their
    /// <i>ashore</i> class here — the server swaps them to <see cref="MotionClass.Swimmer"/> while they are
    /// actually in water (see <see cref="EffectiveClass"/>).</summary>
    public static MotionClass ClassOf(CreatureSpecies sp)
    {
        if (sp.Habitat == CreatureHabitat.Water)
        {
            return MotionClass.Swimmer;
        }

        if (sp.Habitat == CreatureHabitat.Air)
        {
            return sp.BodyPlan == CreatureBodyPlan.Medusa || sp.HasGasSac || sp.LocoStyle == LocomotionStyle.Drifter
                ? MotionClass.Hoverer
                : MotionClass.Flier;
        }

        if (sp.HasGasSac)
        {
            return MotionClass.Hoverer; // the odd floating land grazer — buoyant, hugs the ground at a hover
        }

        if (sp.Legs == 0 || sp.LocoStyle == LocomotionStyle.Slitherer || sp.Legs >= 6)
        {
            return MotionClass.Crawler; // legless, serpentine, or a many-legged scuttler (Q2)
        }

        return MotionClass.Walker;
    }

    /// <summary>The class in effect right now: amphibians swim while in water and walk/crawl ashore
    /// (#1334); everyone else keeps <see cref="ClassOf"/>.</summary>
    public static MotionClass EffectiveClass(CreatureSpecies sp, bool inWater)
        => sp.Habitat == CreatureHabitat.Amphibian && inWater ? MotionClass.Swimmer : ClassOf(sp);

    /// <summary>Whether the species is an amphibian — its ground class may leave the water gate open.</summary>
    public static bool IsAmphibious(CreatureSpecies sp) => sp.Habitat == CreatureHabitat.Amphibian;

    /// <summary>Giants never jump (Q3): the titan plan, or any standard body at/above <see cref="GiantSize"/>.</summary>
    public static bool IsGiant(CreatureSpecies sp)
        => sp.BodyPlan == CreatureBodyPlan.Titan || sp.Size >= GiantSize;

    /// <summary>Only non-giant walkers jump (Q1/Q3). Crawlers never do; fliers take off instead.</summary>
    public static bool CanJump(CreatureSpecies sp)
        => ClassOf(sp) == MotionClass.Walker && !IsGiant(sp);

    /// <summary>A winged land walker is a <b>ground bird</b> (#1334, Q7): it jumps like a walker but falls under
    /// reduced gravity while airborne, so its bounds are long and flat. Never a true flier.</summary>
    public static bool Glides(CreatureSpecies sp)
        => sp.HasWings && sp.Habitat != CreatureHabitat.Air && CanJump(sp);

    /// <summary>Highest ground rise (in blocks) a ground mover may take in one column step: one block for
    /// everyone — a jump for walkers, a slow climb-over for crawlers and giants. Two is a wall, as it is for
    /// the player (Q1a).</summary>
    public static int StepUpLimit(MotionClass cls) => cls is MotionClass.Walker or MotionClass.Crawler ? 1 : int.MaxValue;

    /// <summary>Deepest drop a ground mover will take in one column step (Q1b) — a real fall under gravity:
    /// walkers 3, crawlers 2, giants 1. Fliers/hoverers/swimmers are not gated.</summary>
    public static int StepDownLimit(MotionClass cls, bool giant) => cls switch
    {
        MotionClass.Walker => giant ? 1 : 3,
        MotionClass.Crawler => 2,
        _ => int.MaxValue,
    };

    /// <summary>Whether the class lives on the ground under gravity (as opposed to flying, hovering or swimming).</summary>
    public static bool IsGroundBound(MotionClass cls) => cls is MotionClass.Walker or MotionClass.Crawler;

    /// <summary>Lower-case name for locale keys (<c>ui.scan.motion.*</c>) and the wire.</summary>
    public static string Key(MotionClass cls) => cls switch
    {
        MotionClass.Walker => "walker",
        MotionClass.Crawler => "crawler",
        MotionClass.Flier => "flier",
        MotionClass.Hoverer => "hoverer",
        _ => "swimmer",
    };

    /// <summary>Parses a wire/class name back (unknown → <see cref="MotionClass.Walker"/>, the legacy default).</summary>
    public static MotionClass Parse(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return MotionClass.Walker;
        }

        return name.ToLowerInvariant() switch
        {
            "crawler" => MotionClass.Crawler,
            "flier" => MotionClass.Flier,
            "hoverer" => MotionClass.Hoverer,
            "swimmer" => MotionClass.Swimmer,
            _ => MotionClass.Walker,
        };
    }
}
