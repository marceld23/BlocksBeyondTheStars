// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 40 (#3567): the ground a stamped structure reserved. The generator levels the box to
/// <see cref="FloorY"/> and grades the land within <see cref="Radius"/> blocks around it, so a town sits in the terrain
/// instead of on a plinth — and because the generator does it, chunks, flora, the far view and the client agree by
/// construction. The server writes the list while it stamps a body for the first time and pins it in the save
/// (<c>WorldMetadata.BodySites</c>); a landing pad is a different record (<see cref="LandingPadFlatten"/>) and wins over a
/// grade.
/// </summary>
public readonly struct SiteFlat
{
    /// <summary>The ints one site packs into (<see cref="Pack"/>): minX, minZ, width, length, floorY, radius, maxCut, maxFill.</summary>
    public const int Stride = 8;

    public SiteFlat(int minX, int minZ, int width, int length, int floorY, int radius, int maxCut, int maxFill)
    {
        MinX = minX;
        MinZ = minZ;
        Width = System.Math.Max(1, width);
        Length = System.Math.Max(1, length);
        FloorY = floorY;
        Radius = System.Math.Max(0, radius);
        MaxCut = System.Math.Max(0, maxCut);
        MaxFill = System.Math.Max(0, maxFill);
    }

    /// <summary>The box's west edge (world X, as the structure's origin — may lie on either side of the X seam).</summary>
    public int MinX { get; }

    /// <summary>The box's north edge (world Z).</summary>
    public int MinZ { get; }

    /// <summary>The box's extent along X and Z, in blocks.</summary>
    public int Width { get; }

    public int Length { get; }

    /// <summary>The ground top inside the box — the structure's foundation row.</summary>
    public int FloorY { get; }

    /// <summary>How far around the box the land is graded toward the floor (0 = the box only).</summary>
    public int Radius { get; }

    /// <summary>The most the grade lowers a column below its height before the grade.</summary>
    public int MaxCut { get; }

    /// <summary>The most the grade raises a column above its height before the grade.</summary>
    public int MaxFill { get; }

    /// <summary>Field-wise equality (the generator's mode checks and the chunk-generation pool compare site lists).</summary>
    public bool SameAs(SiteFlat other)
        => MinX == other.MinX && MinZ == other.MinZ && Width == other.Width && Length == other.Length && FloorY == other.FloorY
            && Radius == other.Radius && MaxCut == other.MaxCut && MaxFill == other.MaxFill;

    /// <summary>Whether two site lists hold the same sites in the same order (null = empty).</summary>
    public static bool SameList(IReadOnlyList<SiteFlat>? a, IReadOnlyList<SiteFlat>? b)
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

    /// <summary>A detached copy of a site list (the server's list grows in place while it stamps).</summary>
    public static SiteFlat[] Snapshot(IReadOnlyList<SiteFlat>? sites)
    {
        if (sites is null || sites.Count == 0)
        {
            return System.Array.Empty<SiteFlat>();
        }

        var copy = new SiteFlat[sites.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = sites[i];
        }

        return copy;
    }

    /// <summary>The sites as <see cref="Stride"/> ints each — the far-terrain handshake's form.</summary>
    public static int[] Pack(IReadOnlyList<SiteFlat>? sites)
    {
        if (sites is null || sites.Count == 0)
        {
            return System.Array.Empty<int>();
        }

        var packed = new int[sites.Count * Stride];
        for (int i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            int o = i * Stride;
            packed[o] = s.MinX;
            packed[o + 1] = s.MinZ;
            packed[o + 2] = s.Width;
            packed[o + 3] = s.Length;
            packed[o + 4] = s.FloorY;
            packed[o + 5] = s.Radius;
            packed[o + 6] = s.MaxCut;
            packed[o + 7] = s.MaxFill;
        }

        return packed;
    }

    /// <summary>The inverse of <see cref="Pack"/>; a trailing partial entry is ignored.</summary>
    public static List<SiteFlat> Unpack(int[]? packed)
    {
        var sites = new List<SiteFlat>();
        if (packed is null)
        {
            return sites;
        }

        for (int o = 0; o + Stride <= packed.Length; o += Stride)
        {
            sites.Add(new SiteFlat(packed[o], packed[o + 1], packed[o + 2], packed[o + 3], packed[o + 4], packed[o + 5],
                packed[o + 6], packed[o + 7]));
        }

        return sites;
    }

    /// <summary>The save's text form (<c>WorldMetadata.BodySites</c>): the packed ints of each site comma-separated, sites
    /// separated by <c>;</c>.</summary>
    public static string Format(IReadOnlyList<SiteFlat>? sites)
    {
        var text = new StringBuilder();
        var packed = Pack(sites);
        for (int o = 0; o < packed.Length; o += Stride)
        {
            if (text.Length > 0)
            {
                text.Append(';');
            }

            for (int k = 0; k < Stride; k++)
            {
                if (k > 0)
                {
                    text.Append(',');
                }

                text.Append(packed[o + k].ToString(CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    /// <summary>Parses <see cref="Format"/>'s text; a malformed entry is skipped (and reported through
    /// <paramref name="malformed"/>), never thrown on.</summary>
    public static List<SiteFlat> Parse(string? text, out int malformed)
    {
        malformed = 0;
        var sites = new List<SiteFlat>();
        if (string.IsNullOrEmpty(text))
        {
            return sites;
        }

        var values = new int[Stride];
        foreach (var entry in text!.Split(';'))
        {
            var parts = entry.Split(',');
            bool ok = parts.Length == Stride;
            for (int k = 0; ok && k < Stride; k++)
            {
                ok = int.TryParse(parts[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[k]);
            }

            if (!ok)
            {
                malformed++;
                continue;
            }

            sites.Add(new SiteFlat(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]));
        }

        return sites;
    }
}
