// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The G.D.S. city that grew (#3599), the parts the classic composer does not have: streets only between the districts that
/// exist (the district map's <c>.</c> leaves a district to the land, so the outline is irregular; every other row is shifted
/// by half a district, so the streets meet in T junctions), the main axes paved in steel, the wall along the outline of the
/// districts with a tower on every outer corner and a gate where each main road leaves, and outside every gate a suburb
/// grown by the road-first planner (#3587) — inside the city's one structure, so its houses are climate-controlled too.
/// </summary>
internal static partial class CityGrowth
{
    /// <summary>A gate: the side of the city it opens to (0 −Z, 1 +Z, 2 −X, 3 +X), the first column of its road across its
    /// direction, and the road's width.</summary>
    internal readonly struct Gate
    {
        public Gate(int side, int across, int width)
        {
            Side = side;
            Across = across;
            Width = width;
        }

        public int Side { get; }

        public int Across { get; }

        public int Width { get; }
    }

    private static bool Occupied(CityLayoutSpec spec, int gx, int gz)
        => gx >= 0 && gz >= 0 && gx < spec.Grid && gz < spec.Grid
           && CityGenerator.RoleAtFor(gx, gz, spec.Grid, spec.RoleMap) != CityGenerator.Role.None;

    /// <summary>The streets: a band above and below every row only as far as its districts reach, the streets between the
    /// districts of a row, the gate roads out to the footprint's edge; the band above the middle row and the gate roads in
    /// steel.</summary>
    internal static void PaintStreets(CityLayoutSpec spec, System.Action<int, int, int, int, ushort> fill, ushort street, ushort main)
    {
        int size = spec.DistrictSize, st = spec.Street, c = spec.Grid / 2;
        for (int i = 0; i <= spec.Grid; i++)
        {
            int z0 = spec.Margin + (i * (size + st));
            for (int gz = i - 1; gz <= i; gz++)
            {
                for (int gx = 0; gx < spec.Grid; gx++)
                {
                    if (Occupied(spec, gx, gz))
                    {
                        int ox = spec.DistrictOrigin(gx, gz).X;
                        fill(ox - st, z0, ox + size + st - 1, z0 + st - 1, i == c ? main : street);
                    }
                }
            }
        }

        for (int gz = 0; gz < spec.Grid; gz++)
        {
            int top = spec.DistrictOrigin(0, gz).Z;
            for (int i = 0; i <= spec.Grid; i++)
            {
                if (Occupied(spec, i - 1, gz) || Occupied(spec, i, gz))
                {
                    int x0 = spec.Margin + spec.RowShift(gz) + (i * (size + st));
                    fill(x0, top, x0 + st - 1, top + size - 1, street);
                }
            }
        }

        foreach (var gate in Gates(spec))
        {
            var (x0, z0, x1, z1) = GateRoad(spec, gate);
            fill(x0, z0, x1, z1, main);
        }
    }

    /// <summary>The four gates: west and east on the band above the middle row, north and south on the street of the top /
    /// bottom row nearest the middle.</summary>
    internal static List<Gate> Gates(CityLayoutSpec spec)
    {
        int size = spec.DistrictSize, st = spec.Street, c = spec.Grid / 2;
        var gates = new List<Gate>();
        int band = spec.Margin + (c * (size + st));
        gates.Add(new Gate(2, band, st));
        gates.Add(new Gate(3, band, st));
        int topRow = -1, bottomRow = -1;
        for (int gz = 0; gz < spec.Grid; gz++)
        {
            for (int gx = 0; gx < spec.Grid; gx++)
            {
                if (Occupied(spec, gx, gz))
                {
                    if (topRow < 0) topRow = gz;
                    bottomRow = gz;
                }
            }
        }

        if (topRow >= 0)
        {
            gates.Add(new Gate(0, StreetNearestMiddle(spec, topRow), st));
            gates.Add(new Gate(1, StreetNearestMiddle(spec, bottomRow), st));
        }

        return gates;
    }

    /// <summary>The first column of the street of a row that runs beside an existing district, nearest the city's middle.</summary>
    private static int StreetNearestMiddle(CityLayoutSpec spec, int gz)
    {
        int best = spec.Grid / 2, bestD = int.MaxValue;
        for (int i = 0; i <= spec.Grid; i++)
        {
            int d = System.Math.Abs((2 * i) - spec.Grid);
            if ((Occupied(spec, i - 1, gz) || Occupied(spec, i, gz)) && d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return spec.Margin + spec.RowShift(gz) + (best * (spec.DistrictSize + spec.Street));
    }

    /// <summary>A gate road's rectangle, from the footprint's edge into the street network.</summary>
    internal static (int X0, int Z0, int X1, int Z1) GateRoad(CityLayoutSpec spec, Gate gate)
    {
        int w = spec.Footprint, size = spec.DistrictSize, st = spec.Street;
        int top = TopRowZ(spec), bottom = BottomRowZ(spec);
        return gate.Side switch
        {
            2 or 3 => (0, gate.Across, w - 1, gate.Across + gate.Width - 1),
            0 => (gate.Across, 0, gate.Across + gate.Width - 1, top - 1),
            _ => (gate.Across, bottom + size, gate.Across + gate.Width - 1, w - 1),
        };
    }

    private static int TopRowZ(CityLayoutSpec spec)
    {
        for (int gz = 0; gz < spec.Grid; gz++)
        {
            for (int gx = 0; gx < spec.Grid; gx++)
            {
                if (Occupied(spec, gx, gz))
                {
                    return spec.DistrictOrigin(gx, gz).Z;
                }
            }
        }

        return spec.Margin;
    }

    private static int BottomRowZ(CityLayoutSpec spec)
    {
        for (int gz = spec.Grid - 1; gz >= 0; gz--)
        {
            for (int gx = 0; gx < spec.Grid; gx++)
            {
                if (Occupied(spec, gx, gz))
                {
                    return spec.DistrictOrigin(gx, gz).Z;
                }
            }
        }

        return spec.Margin;
    }

    /// <summary>The city's inside: every existing district grown by a street on each side.</summary>
    internal static bool[] Inside(CityLayoutSpec spec)
    {
        int w = spec.Footprint, size = spec.DistrictSize, st = spec.Street;
        var inside = new bool[w * w];
        for (int gx = 0; gx < spec.Grid; gx++)
        {
            for (int gz = 0; gz < spec.Grid; gz++)
            {
                if (!Occupied(spec, gx, gz))
                {
                    continue;
                }

                var (ox, oz) = spec.DistrictOrigin(gx, gz);
                for (int x = System.Math.Max(0, ox - st); x < System.Math.Min(w, ox + size + st); x++)
                    for (int z = System.Math.Max(0, oz - st); z < System.Math.Min(w, oz + size + st); z++)
                        inside[(x * w) + z] = true;
            }
        }

        return inside;
    }
}
