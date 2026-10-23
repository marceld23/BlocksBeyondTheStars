// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One size rule for how much ground an animal's drawn body covers (#2753): a circle around its position, measured from
/// the body the client's <c>CreatureBuilder</c> actually draws for each body plan. The server tracks a creature as a single
/// point and the client draws the body exactly there, so before this rule two big animals could stand half inside each
/// other — the only spacing was the herd's 1.5 blocks between members of one species.
/// <para>The radius is the distance from the position to the front of the head along the body's long axis: the body is
/// drawn centred on the position, and a circle that wide is the personal space the animal keeps. The thin end of a tail and
/// splayed legs are left out on purpose — two animals may brush tails, not stand inside each other's torso. Pure and
/// Unity-free, so the server's spacing and spawning and the tests read the same numbers.</para>
/// <para>Computed on the fly from the species and the individual's size factor, never stored: the shapeshifter changes its
/// species, and an individual's <c>SizeScale</c> (0.7–1.3) scales the body like the wire's <c>Size</c> does.</para>
/// </summary>
public static class CreatureFootprint
{
    /// <summary>Personal space between two animals as a fraction of their combined radii — the same 0.8 settlers keep
    /// between each other (#1272): the bodies may just touch at the extremities, never overlap at the torso.</summary>
    public const float SeparationFactor = 0.8f;

    /// <summary>The largest radius any non-giant body plan reaches (a size-8 titan with three segments and a three-piece
    /// neck: 0.575·4·3 + 0.6·4 + 0.18·4·3 = 11.46). The server's cheap pre-check before it looks up a neighbour's species.</summary>
    public const float MaxRadius = 11.5f;

    /// <summary>The ground radius in blocks of an animal of <paramref name="sp"/> drawn at <paramref name="sizeScale"/> times
    /// its species size. Each branch mirrors the client builder of that plan (<c>u</c> is the builder's "unit", half the
    /// clamped drawn size):
    /// <list type="bullet">
    /// <item><b>Standard / Floral</b> (u = ½·clamp(S, 0.4, 3)): 1–4 segments of 1.1u centred on the position, so the body
    /// ends 0.55u·n ahead; the head box sits 0.11u past it and reaches 0.4u·headScale further (headScale 0.75–1.4) — the
    /// middle of that band, 0.5u, is added.</item>
    /// <item><b>Titan</b> (u = ½·clamp(S, 0.4, 8)): 2–3 segments of 1.15u (front 0.575u·n), the head 0.6u past it without a
    /// neck, and every neck piece moves the head 0.18u further forward.</item>
    /// <item><b>Arachnid</b> (u = ½·clamp(S, 0.4, 8)): the head's front 1.8u ahead, the abdomen's end 2.64u behind — half the
    /// body's length, 2.2u.</item>
    /// <item><b>Biped</b> (s = clamp(S, 0.3, 2.5)): upright, so the widest part counts — the arms hang 0.3s out from the
    /// middle, the big head at most half its edge (<see cref="BipedRules.HeadEdge"/>).</item>
    /// <item><b>Worm</b> (u = ½·clamp(S, 0.4, 3)): the head's front 1.2u ahead and a chain of 6–12 links of 0.3u behind —
    /// half that length, 0.55u + 0.15u per link.</item>
    /// <item><b>Ray</b> (u = ½·clamp(S, 0.4, 3.5)): the disc's half-width 0.8u plus three wing panels of 0.7u, at the
    /// middle width factor 1.1 — 3.2u, the half span.</item>
    /// <item><b>Medusa</b> (u = ½·clamp(S, 0.4, 3)): the bell's radius, 1.5u.</item>
    /// <item>The <b>giants</b> (colossus, sandworm, leviathan, sky giant) move by their own rules and are never spaced; they
    /// get a rough envelope — half their height or length — for the diagnostics.</item>
    /// </list></summary>
    public static float Radius(CreatureSpecies sp, float sizeScale)
    {
        float size = sp.Size * (sizeScale > 0f ? sizeScale : 1f);
        switch (sp.BodyPlan)
        {
            case CreatureBodyPlan.Titan:
                {
                    float u = Unit(size, 8f);
                    int segments = Math.Clamp(sp.BodySegments, 2, 3);
                    int neck = Math.Clamp(sp.NeckLength, 0, 3);
                    return (0.575f * segments + 0.6f + 0.18f * neck) * u;
                }

            case CreatureBodyPlan.Arachnid:
                return 2.2f * Unit(size, 8f);

            case CreatureBodyPlan.Biped:
                {
                    float s = Math.Clamp(size, 0.3f, 2.5f);
                    float headRatio = sp.HeadRatio > 0f ? sp.HeadRatio : 1.6f; // the builder's fallback
                    return Math.Max(0.3f * s, 0.5f * BipedRules.HeadEdge(s, headRatio));
                }

            case CreatureBodyPlan.Worm:
                {
                    int links = Math.Clamp(sp.BodySegments, 6, 12);
                    return (0.55f + 0.15f * links) * Unit(size, 3f);
                }

            case CreatureBodyPlan.Ray:
                return 3.2f * Unit(size, 3.5f);

            case CreatureBodyPlan.Medusa:
                return 1.5f * Unit(size, 3f);

            case CreatureBodyPlan.Colossus:
            case CreatureBodyPlan.Sandworm:
            case CreatureBodyPlan.Leviathan:
            case CreatureBodyPlan.SkyGiant:
                return 0.5f * Math.Max(Math.Max(sp.GiantHeight, sp.WormLength), size);

            default: // Standard, Floral
                {
                    float u = Unit(size, 3f);
                    int segments = Math.Clamp(sp.BodySegments, 1, 4);
                    return (0.55f * segments + 0.5f) * u;
                }
        }
    }

    /// <summary>How far apart two animals of radii <paramref name="radiusA"/> and <paramref name="radiusB"/> keep.</summary>
    public static float SeparationDistance(float radiusA, float radiusB) => SeparationFactor * (radiusA + radiusB);

    /// <summary>#2943: how high above its position a ray carries its wing disc — the builder puts the flat body and the
    /// three wing panels a side at <c>0.7u</c> (u = ½·clamp(S, 0.4, 3.5)). The server checks the disc against blocks at
    /// this height, so it follows the drawing exactly as <see cref="Radius"/> does.</summary>
    public static float RayWingPlane(CreatureSpecies sp, float sizeScale) => 0.7f * Unit(ScaledSize(sp, sizeScale), 3.5f);

    /// <summary>#2943: half the height band the wing disc sweeps round <see cref="RayWingPlane"/> — the disc's own
    /// thickness plus most of the travelling wave the animator runs along each wing edge.</summary>
    public static float RayWingHalfBand(CreatureSpecies sp, float sizeScale) => 0.5f * Unit(ScaledSize(sp, sizeScale), 3.5f);

    /// <summary>#2944: how far ahead of its position an animal's head reaches along its heading — the front the body
    /// pushes into a wall first. For the standard, floral and titan plans that is the radius itself (the radius IS the
    /// head's front); an arachnid's head ends 1.8u ahead (its abdomen reaches further behind), a worm's 1.2u (its chain
    /// trails). An upright biped carries its head over its feet, and the swimming, floating and giant plans are never
    /// walked into a wall this way — 0 for them.</summary>
    public static float HeadReach(CreatureSpecies sp, float sizeScale)
    {
        float size = ScaledSize(sp, sizeScale);
        switch (sp.BodyPlan)
        {
            case CreatureBodyPlan.Arachnid:
                return 1.8f * Unit(size, 8f);

            case CreatureBodyPlan.Worm:
                return 1.2f * Unit(size, 3f);

            case CreatureBodyPlan.Biped:
            case CreatureBodyPlan.Ray:
            case CreatureBodyPlan.Medusa:
            case CreatureBodyPlan.Colossus:
            case CreatureBodyPlan.Sandworm:
            case CreatureBodyPlan.Leviathan:
            case CreatureBodyPlan.SkyGiant:
                return 0f;

            default: // Standard, Floral, Titan
                return Radius(sp, sizeScale);
        }
    }

    private static float ScaledSize(CreatureSpecies sp, float sizeScale) => sp.Size * (sizeScale > 0f ? sizeScale : 1f);

    /// <summary>The builders' "unit": half the drawn size, clamped to the plan's size band.</summary>
    private static float Unit(float size, float maxSize) => 0.5f * Math.Clamp(size, 0.4f, maxSize);
}
