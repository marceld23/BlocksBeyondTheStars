// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The rules of the arachnid body plan (#2009): a speeder-sized eight-legger rolled into the Land pool of a
/// generation-10 world. Pure and Unity-free so the server, the client and the tests read the same numbers —
/// how tall its collision body is, when it lies in wait, and what the tiers of each head shape are.
/// </summary>
public static class ArachnidRules
{
    /// <summary>The species size band: unit 1.5–1.8 blocks, so the body is about the speeder hull (3 × 2 × 5).</summary>
    public const float MinSize = 3.0f;
    public const float MaxSize = 3.6f;

    /// <summary>How close a player must come before an ambusher rushes (blocks, horizontal).</summary>
    public const float LurkRange = 6f;

    /// <summary>How close an arachnid must come before VEGA points it out (blocks).</summary>
    public const float SightingRange = 40f;

    /// <summary>Whether this species sits in wait for its prey: an arachnid that hunts (aggressive, pack hunter) or
    /// defends a patch (territorial). Passive and skittish ones roam like any other animal.</summary>
    public static bool Lurks(CreatureSpecies sp)
        => sp.BodyPlan == CreatureBodyPlan.Arachnid
           && sp.Temperament is CreatureTemperament.Territorial or CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;

    /// <summary>How many cells tall the collision body is. The general rule (<c>Size × 1.8</c>) assumes an upright body;
    /// an arachnid carries its body two blocks off the ground on splayed legs, so it is gated by that, not by a
    /// crown it does not have — a 3.3 body is 3 cells, not 6.</summary>
    public static int BodyHeightCells(float size, int min, int max)
        => Math.Clamp((int)Math.Ceiling(size * 0.8f), min, max);

    /// <summary>Whether a head shape is one of the pyramids (anything but the box).</summary>
    public static bool IsPyramid(CreatureHeadShape shape) => shape != CreatureHeadShape.Box;

    /// <summary>The head's height relative to the box head of the same body.</summary>
    public static float HeightScale(CreatureHeadShape shape) => shape switch
    {
        CreatureHeadShape.Spire => 1.6f,
        CreatureHeadShape.Ziggurat => 1.25f,
        _ => 1f,
    };

    /// <summary>One stacked tier of a pyramid head: a square frustum from height <see cref="Y0"/> to <see cref="Y1"/>
    /// (both 0..1 of the head's height) whose footprint scales from <see cref="Bottom"/> to <see cref="Top"/>
    /// (1 = the head's full width and depth, 0 = an apex).</summary>
    public readonly struct HeadTier
    {
        public HeadTier(float y0, float y1, float bottom, float top)
        {
            Y0 = y0;
            Y1 = y1;
            Bottom = bottom;
            Top = top;
        }

        public float Y0 { get; }
        public float Y1 { get; }
        public float Bottom { get; }
        public float Top { get; }
    }

    private static readonly HeadTier[] PyramidTiers = { new HeadTier(0f, 1f, 1f, 0f) };
    private static readonly HeadTier[] SpireTiers = { new HeadTier(0f, 1f, 0.8f, 0f) };
    private static readonly HeadTier[] FrustumTiers = { new HeadTier(0f, 1f, 1f, 0.5f) };
    private static readonly HeadTier[] ZigguratTiers = { new HeadTier(0f, 0.5f, 1f, 0.78f), new HeadTier(0.5f, 1f, 0.62f, 0f) };

    /// <summary>The tiers of a pyramid head, bottom first; empty for the box (which is a cube, not a stack).</summary>
    public static IReadOnlyList<HeadTier> HeadTiers(CreatureHeadShape shape) => shape switch
    {
        CreatureHeadShape.Pyramid => PyramidTiers,
        CreatureHeadShape.Spire => SpireTiers,
        CreatureHeadShape.Frustum => FrustumTiers,
        CreatureHeadShape.Ziggurat => ZigguratTiers,
        _ => Array.Empty<HeadTier>(),
    };

    /// <summary>The footprint scale of the head's slope at a height <paramref name="y01"/> (0 = base, 1 = top): where the
    /// front face is at that height, so eyes can sit ON the slope instead of inside it. A box is 1 everywhere.</summary>
    public static float FootprintAt(CreatureHeadShape shape, float y01)
    {
        var tiers = HeadTiers(shape);
        if (tiers.Count == 0)
        {
            return 1f;
        }

        y01 = Math.Clamp(y01, 0f, 1f);
        for (int i = 0; i < tiers.Count; i++)
        {
            var t = tiers[i];
            if (y01 <= t.Y1 || i == tiers.Count - 1)
            {
                float span = Math.Max(1e-4f, t.Y1 - t.Y0);
                float f = Math.Clamp((y01 - t.Y0) / span, 0f, 1f);
                return t.Bottom + (t.Top - t.Bottom) * f;
            }
        }

        return 1f;
    }
}
