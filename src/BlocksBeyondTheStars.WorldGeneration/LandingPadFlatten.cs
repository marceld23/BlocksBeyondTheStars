// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A planned landing pad the generator levels at generation time (ship-as-object: the landed
/// ship is a placed structure that needs flat, clear ground — terrain is never mutated per landing).
/// Positions come from the server's deterministic pad planning (same every load).</summary>
public readonly struct LandingPadFlatten
{
    public readonly int CenterX;
    public readonly int CenterZ;
    public readonly int SurfaceY;
    public readonly int Radius;

    /// <summary>An islet pad (#1453/#1620): the pad sits ABOVE the sea on a world whose pad footprint is
    /// all water, so the generator raises a mound from the seabed up to <see cref="SurfaceY"/> — level
    /// over <see cref="PlateauRadius"/>, then a 2:1 beach slope out to <see cref="IsletRadius"/>, both
    /// rims wobbled by seeded noise so the island is not a perfect disc.</summary>
    public readonly bool Islet;
    public readonly int PlateauRadius;
    public readonly int IsletRadius;

    /// <summary>The islet as worlds before terrain generation 2 got it (#1453, #1665): a plain sand mound with a
    /// 1:1 slope from the pad rim straight down to the sea, no plateau, no rim wobble, no flora. A save created
    /// before the ocean-pad wave keeps exactly this shape, so the mound a player already lives on never changes
    /// under them when a neighbouring chunk generates later.</summary>
    public readonly bool ClassicShape;

    public LandingPadFlatten(int centerX, int centerZ, int surfaceY, int radius)
        : this(centerX, centerZ, surfaceY, radius, islet: false, plateauRadius: radius, isletRadius: radius)
    {
    }

    public LandingPadFlatten(int centerX, int centerZ, int surfaceY, int radius, bool islet, int plateauRadius, int isletRadius, bool classicShape = false)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        SurfaceY = surfaceY;
        Radius = radius;
        Islet = islet;
        PlateauRadius = islet ? System.Math.Max(radius, plateauRadius) : radius;
        IsletRadius = islet ? System.Math.Max(PlateauRadius, isletRadius) : radius;
        ClassicShape = islet && classicShape;
    }

    /// <summary>Field-wise equality (the generator's mode checks and the chunk-generation pool compare pad lists).</summary>
    public bool SameAs(LandingPadFlatten other)
        => CenterX == other.CenterX && CenterZ == other.CenterZ && SurfaceY == other.SurfaceY && Radius == other.Radius
            && Islet == other.Islet && PlateauRadius == other.PlateauRadius && IsletRadius == other.IsletRadius
            && ClassicShape == other.ClassicShape;

    /// <summary>Whether two pad lists hold the same pads in the same order (null = empty).</summary>
    public static bool SameList(IReadOnlyList<LandingPadFlatten>? a, IReadOnlyList<LandingPadFlatten>? b)
    {
        int count = a?.Count ?? 0;
        if ((b?.Count ?? 0) != count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!a![i].SameAs(b![i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A detached copy of a pad list (the server's list is mutable and grows in place).</summary>
    public static LandingPadFlatten[] Snapshot(IReadOnlyList<LandingPadFlatten>? pads)
    {
        if (pads is null || pads.Count == 0)
        {
            return System.Array.Empty<LandingPadFlatten>();
        }

        var copy = new LandingPadFlatten[pads.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = pads[i];
        }

        return copy;
    }
}
