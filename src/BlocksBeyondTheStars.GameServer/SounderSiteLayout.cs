// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: the pure geometry of Soaniri's ruin city Ornasa and the Choir Tower — where the ruined halls, the three sound
/// chambers, the bell-bloom trail and the eight pipes stand relative to a site's centre. The stamp writes these cells once;
/// every load re-derives the chambers, the trail and the melody site from the same functions, so the two always agree.
/// Bearings follow the compass: north is +Z, east is +X, angles run clockwise from north.
/// </summary>
internal static class SounderSiteLayout
{
    /// <summary>Ornasa's six slots on a ring around the amphitheatre: the even ones are ruined halls, the odd ones the three
    /// sealed sound chambers (slot 1 → chamber 0, 3 → 1, 5 → 2).</summary>
    public const int OrnasaSlots = 6;

    /// <summary>How far Ornasa's halls and chambers stand from its centre.</summary>
    public const double OrnasaSlotRing = 16.0;

    /// <summary>The sound chambers of Ornasa.</summary>
    public const int OrnasaChambers = 3;

    /// <summary>The chamber the bell-bloom trail leads to (and opens).</summary>
    public const int BloomTrailChamber = 2;

    /// <summary>How many bell blooms the trail has.</summary>
    public const int BloomTrailLength = 5;

    /// <summary>The Choir Tower's height (the top platform's floor sits one above it).</summary>
    public const int ChoirTowerHeight = 24;

    /// <summary>The pipes stand this far from the tower's axis on the platform.</summary>
    public const double ChoirPipeRing = 5.0;

    /// <summary>The bearing (radians, clockwise from north) of Ornasa slot <paramref name="slot"/>.</summary>
    public static double OrnasaSlotBearing(int slot) => (slot * Math.PI * 2.0 / OrnasaSlots) + (Math.PI / 6.0);

    /// <summary>The centre of Ornasa slot <paramref name="slot"/>.</summary>
    public static (int X, int Z) OrnasaSlotCentre(int cx, int cz, int slot) => Polar(cx, cz, OrnasaSlotRing, OrnasaSlotBearing(slot));

    /// <summary>The slot of chamber <paramref name="chamber"/>.</summary>
    public static int ChamberSlot(int chamber) => (chamber * 2) + 1;

    /// <summary>The axis step from a slot's centre toward the site's centre (the face the doorway opens in): one of the four
    /// compass steps, along the larger offset.</summary>
    public static (int X, int Z) TowardCentre(int slotX, int slotZ, int cx, int cz)
    {
        int dx = cx - slotX, dz = cz - slotZ;
        return Math.Abs(dx) >= Math.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));
    }

    /// <summary>The two seal cells of chamber <paramref name="chamber"/> (its doorway, two high, in the face toward the
    /// centre) for a site at (<paramref name="cx"/>, <paramref name="floorY"/>, <paramref name="cz"/>).</summary>
    public static Vector3i[] ChamberSeal(int cx, int floorY, int cz, int chamber)
    {
        var (sx, sz) = OrnasaSlotCentre(cx, cz, ChamberSlot(chamber));
        var (tx, tz) = TowardCentre(sx, sz, cx, cz);
        return new[]
        {
            new Vector3i(sx + (tx * 2), floorY + 1, sz + (tz * 2)),
            new Vector3i(sx + (tx * 2), floorY + 2, sz + (tz * 2)),
        };
    }

    /// <summary>The interior floor cell of chamber <paramref name="chamber"/> (where its cache stands).</summary>
    public static Vector3i ChamberInside(int cx, int floorY, int cz, int chamber)
    {
        var (sx, sz) = OrnasaSlotCentre(cx, cz, ChamberSlot(chamber));
        return new Vector3i(sx, floorY + 1, sz);
    }

    /// <summary>The cell of trail bloom <paramref name="k"/> (0 = at the amphitheatre's edge … last = before the trail
    /// chamber's door): an arc from 7 to 12 blocks out that ends straight in front of the doorway.</summary>
    public static Vector3i BloomTrailCell(int cx, int floorY, int cz, int k)
    {
        double t = k / (double)(BloomTrailLength - 1);
        double bearing = OrnasaSlotBearing(ChamberSlot(BloomTrailChamber)) - (0.9 * (1.0 - t));
        var (x, z) = Polar(cx, cz, 7.0 + (5.0 * t), bearing);
        return new Vector3i(x, floorY + 1, z);
    }

    /// <summary>The base cell (the lowest pipe cell, the melody pillar) of Choir Tower pipe <paramref name="i"/> — tone
    /// <paramref name="i"/>, clockwise from north — on the platform whose floor is at <paramref name="platformY"/>.</summary>
    public static Vector3i ChoirPipeBase(int cx, int platformY, int cz, int i)
    {
        var (x, z) = Polar(cx, cz, ChoirPipeRing, i * Math.PI / 4.0);
        return new Vector3i(x, platformY + 1, z);
    }

    /// <summary>How many carved-obsidian cells pipe <paramref name="i"/> has under its crystal: the low tones are the tall
    /// pipes (3, 3, 2, 2, 1, 1, 0, 0), so the crystal of every pipe is within three cells of its base.</summary>
    public static int ChoirPipeStem(int i) => 3 - (i / 2);

    /// <summary>The cell (<paramref name="r"/>, <paramref name="bearing"/>) from a centre, rounded to the grid.</summary>
    public static (int X, int Z) Polar(int cx, int cz, double r, double bearing)
        => (cx + (int)Math.Round(Math.Sin(bearing) * r), cz + (int)Math.Round(Math.Cos(bearing) * r));
}
