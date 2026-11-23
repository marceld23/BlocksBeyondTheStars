// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>Whether a body's tail can wear tail rings (#2830).</summary>
public enum BodyTail
{
    None,    // no tail the coat can ring
    IfTrait, // a tail when the species has one (HasTail)
    Always,  // the body IS a tail (the worm's chain of links)
}

/// <summary>Which rule measures a body's ground radius (<see cref="CreatureFootprint.Radius"/>).</summary>
public enum BodyFootprint
{
    Segments, // the classic segment row with a box head (standard, floral, reindeer, hound)
    Titan,
    Arachnid,
    Upright,  // the biped's proportions (biped, robot)
    Worm,
    Ray,
    Medusa,
    Soaniri,  // each body of Soaniri by its own rule
    Giant,    // a rough envelope: half the height or length
}

/// <summary>How far a body's head reaches ahead of its position (<see cref="CreatureFootprint.HeadReach"/>).</summary>
public enum BodyHeadReach
{
    Radius,   // the radius is the head's front
    Arachnid,
    Worm,
    None,     // upright, floating, swimming and giant bodies are never walked into a wall
    Muzzle,   // the radius plus the hound's muzzle
}

/// <summary>The pool a body plan calls from instead of its habitat's (<see cref="CreatureVoices.PoolFor(string, string, Func{string, bool})"/>).</summary>
public enum BodyVoice
{
    Habitat,    // the habitat's pool (every rolled body)
    Biped,      // #2083: the biped's gibberish
    Robot,      // #3295: the robot's beeps
    Canine,     // #3328: the hound's barks
    Soundi,     // #3359: the recorded voices of Soaniri — a sung voice
    Soundrak,
    Quali,
    Soundeling,
}

/// <summary>
/// One body plan's facts (#3972): what the scattered decisions used to ask by comparing the plan's NAME — which coat
/// patterns its parts can carry, its voice pool, how its footprint is measured. One row per <see cref="CreatureBodyPlan"/>
/// in <see cref="CreatureBodyPlans"/>, so a new plan is a new row instead of a new branch in every decision.
/// </summary>
public sealed class BodyPlanProfile
{
    /// <summary>The plan; null for <see cref="CreatureBodyPlans.Unknown"/> (a name this build does not know).</summary>
    public CreatureBodyPlan? Plan { get; init; }

    /// <summary>A box torso: the back overlays (dorsal stripe, saddle, two-tone) fit on it.</summary>
    public bool BoxTorso { get; init; }

    /// <summary>A box head: the eye mask fits on it (when the species has eyes).</summary>
    public bool BoxHead { get; init; }

    /// <summary>Legs with shins the socks fit on (when the species has legs).</summary>
    public bool SockLegs { get; init; }

    /// <summary>Which tail can wear tail rings.</summary>
    public BodyTail Tail { get; init; }

    /// <summary>The body always carries accents the crest band colours (sails, fins), crest or not.</summary>
    public bool Accents { get; init; }

    /// <summary>The flanks can wear the countershading (on a body up to size 2).</summary>
    public bool Countershade { get; init; }

    /// <summary>A belly slab the belly blaze fits on.</summary>
    public bool BellySlab { get; init; } = true;

    /// <summary>The armour hides the core (the sandworm): only the plate and belly colours, no surface pattern.</summary>
    public bool ArmourCoat { get; init; }

    /// <summary>The body wears its named hide head to tail (the Schuppenhund's scales).</summary>
    public bool WearsOneHide { get; init; }

    /// <summary>The pool it calls from.</summary>
    public BodyVoice Voice { get; init; }

    /// <summary>Its calls are real recordings — one per call, at its own pitch.</summary>
    public bool SungVoice { get; init; }

    /// <summary>How its ground radius is measured.</summary>
    public BodyFootprint Footprint { get; init; }

    /// <summary>How far its head reaches ahead.</summary>
    public BodyHeadReach HeadReach { get; init; }

    /// <summary>A one-per-world giant (#1998, #2111, #2112): never from the spawner, moves by its own rules.</summary>
    public bool Giant { get; init; }
}

/// <summary>
/// The body-plan profile table (#3972): one <see cref="BodyPlanProfile"/> per <see cref="CreatureBodyPlan"/>. The wire carries
/// the plan as its name; <see cref="Of(string)"/> reads that name (an empty one is the standard body, an unknown one — a newer
/// server's plan — the <see cref="Unknown"/> row that claims nothing but a belly).
/// </summary>
public static class CreatureBodyPlans
{
    /// <summary>The row of a plan name this build does not know: no part a placed pattern needs, a belly slab, the habitat's
    /// voice, the classic footprint.</summary>
    public static readonly BodyPlanProfile Unknown = new();

    private static readonly Dictionary<CreatureBodyPlan, BodyPlanProfile> Rows = Build();

    private static readonly Dictionary<string, BodyPlanProfile> ByName = BuildNames();

    /// <summary>Every plan's row, in enum order.</summary>
    public static IEnumerable<BodyPlanProfile> All
    {
        get
        {
            foreach (CreatureBodyPlan plan in Enum.GetValues(typeof(CreatureBodyPlan)))
            {
                yield return Rows[plan];
            }
        }
    }

    /// <summary>The row of a plan.</summary>
    public static BodyPlanProfile Of(CreatureBodyPlan plan) => Rows.TryGetValue(plan, out var row) ? row : Unknown;

    /// <summary>The row of a plan as the wire names it (case-sensitive, as the enum's name): empty or null = the standard body,
    /// an unknown name = <see cref="Unknown"/>.</summary>
    public static BodyPlanProfile Of(string? planName)
    {
        if (string.IsNullOrEmpty(planName))
        {
            return Rows[CreatureBodyPlan.Standard];
        }

        return ByName.TryGetValue(planName!, out var row) ? row : Unknown;
    }

    private static Dictionary<string, BodyPlanProfile> BuildNames()
    {
        var names = new Dictionary<string, BodyPlanProfile>(StringComparer.Ordinal);
        foreach (var pair in Rows)
        {
            names[pair.Key.ToString()] = pair.Value;
        }

        return names;
    }

    private static Dictionary<CreatureBodyPlan, BodyPlanProfile> Build() => new()
    {
        // The classic quadruped: every placed pattern fits.
        [CreatureBodyPlan.Standard] = new() { Plan = CreatureBodyPlan.Standard, BoxTorso = true, BoxHead = true, SockLegs = true, Tail = BodyTail.IfTrait, Countershade = true },
        [CreatureBodyPlan.Medusa] = new() { Plan = CreatureBodyPlan.Medusa, BellySlab = false, Footprint = BodyFootprint.Medusa, HeadReach = BodyHeadReach.None },
        [CreatureBodyPlan.Titan] = new() { Plan = CreatureBodyPlan.Titan, BoxTorso = true, BoxHead = true, SockLegs = true, Tail = BodyTail.IfTrait, Footprint = BodyFootprint.Titan },
        [CreatureBodyPlan.Floral] = new() { Plan = CreatureBodyPlan.Floral },
        [CreatureBodyPlan.Ray] = new() { Plan = CreatureBodyPlan.Ray, Footprint = BodyFootprint.Ray, HeadReach = BodyHeadReach.None },
        [CreatureBodyPlan.Colossus] = new() { Plan = CreatureBodyPlan.Colossus, BoxTorso = true, BoxHead = true, SockLegs = true, Tail = BodyTail.IfTrait, Footprint = BodyFootprint.Giant, HeadReach = BodyHeadReach.None, Giant = true },
        [CreatureBodyPlan.Sandworm] = new() { Plan = CreatureBodyPlan.Sandworm, ArmourCoat = true, Footprint = BodyFootprint.Giant, HeadReach = BodyHeadReach.None, Giant = true },
        [CreatureBodyPlan.Arachnid] = new() { Plan = CreatureBodyPlan.Arachnid, SockLegs = true, Footprint = BodyFootprint.Arachnid, HeadReach = BodyHeadReach.Arachnid },
        [CreatureBodyPlan.Biped] = new() { Plan = CreatureBodyPlan.Biped, BoxHead = true, SockLegs = true, Tail = BodyTail.IfTrait, Countershade = true, Voice = BodyVoice.Biped, Footprint = BodyFootprint.Upright, HeadReach = BodyHeadReach.None },
        [CreatureBodyPlan.Worm] = new() { Plan = CreatureBodyPlan.Worm, BoxHead = true, Tail = BodyTail.Always, Footprint = BodyFootprint.Worm, HeadReach = BodyHeadReach.Worm },
        [CreatureBodyPlan.Leviathan] = new() { Plan = CreatureBodyPlan.Leviathan, Accents = true, Footprint = BodyFootprint.Giant, HeadReach = BodyHeadReach.None, Giant = true },
        [CreatureBodyPlan.SkyGiant] = new() { Plan = CreatureBodyPlan.SkyGiant, Accents = true, Footprint = BodyFootprint.Giant, HeadReach = BodyHeadReach.None, Giant = true },
        [CreatureBodyPlan.Robot] = new() { Plan = CreatureBodyPlan.Robot, Voice = BodyVoice.Robot, Footprint = BodyFootprint.Upright, HeadReach = BodyHeadReach.None },
        // #3327: the hound is a standard quadruped underneath — the same box torso, box head, legs and tail.
        [CreatureBodyPlan.Hound] = new() { Plan = CreatureBodyPlan.Hound, BoxTorso = true, BoxHead = true, SockLegs = true, Tail = BodyTail.IfTrait, Countershade = true, WearsOneHide = true, Voice = BodyVoice.Canine, HeadReach = BodyHeadReach.Muzzle },
        [CreatureBodyPlan.Reindeer] = new() { Plan = CreatureBodyPlan.Reindeer },
        [CreatureBodyPlan.Sounder] = new() { Plan = CreatureBodyPlan.Sounder, Footprint = BodyFootprint.Soaniri, HeadReach = BodyHeadReach.None },
        [CreatureBodyPlan.Soundi] = new() { Plan = CreatureBodyPlan.Soundi, Voice = BodyVoice.Soundi, SungVoice = true, Footprint = BodyFootprint.Soaniri },
        [CreatureBodyPlan.Soundrak] = new() { Plan = CreatureBodyPlan.Soundrak, Voice = BodyVoice.Soundrak, SungVoice = true, Footprint = BodyFootprint.Soaniri, HeadReach = BodyHeadReach.None },
        [CreatureBodyPlan.Soundeling] = new() { Plan = CreatureBodyPlan.Soundeling, Voice = BodyVoice.Soundeling, SungVoice = true, Footprint = BodyFootprint.Soaniri },
        [CreatureBodyPlan.Quali] = new() { Plan = CreatureBodyPlan.Quali, Voice = BodyVoice.Quali, SungVoice = true, Footprint = BodyFootprint.Soaniri, HeadReach = BodyHeadReach.None },
    };
}
