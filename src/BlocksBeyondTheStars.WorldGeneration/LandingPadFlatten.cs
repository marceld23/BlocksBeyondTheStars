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

    public LandingPadFlatten(int centerX, int centerZ, int surfaceY, int radius)
        : this(centerX, centerZ, surfaceY, radius, islet: false, plateauRadius: radius, isletRadius: radius)
    {
    }

    public LandingPadFlatten(int centerX, int centerZ, int surfaceY, int radius, bool islet, int plateauRadius, int isletRadius)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        SurfaceY = surfaceY;
        Radius = radius;
        Islet = islet;
        PlateauRadius = islet ? System.Math.Max(radius, plateauRadius) : radius;
        IsletRadius = islet ? System.Math.Max(PlateauRadius, isletRadius) : radius;
    }
}
