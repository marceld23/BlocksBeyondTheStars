// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Where a listed rail line stands in a metropolis (#3985): its hall on one of the city's rail-hall squares (<c>S</c> on the
/// pinned district map, <see cref="MetropolisPlanner"/>), its track in the street beside that square, and the line running
/// straight along that street out of the city. A metropolis's districts are built to their edges, but its streets are open
/// paving (<see cref="CityGenerator"/> paints them and puts nothing on them), and a street is wide enough for the 3-wide
/// wagon: so the hall is a <b>side-platform hall</b> (<see cref="RailHallPlatforms"/>) — its platform, bench and posts on the
/// square's edge, its track bed in the street — and the line never crosses a district.
/// <para>A <b>terminus</b> (a line's first or last stop) needs the street open from the hall to the city's edge on one side;
/// a <b>through station</b> on both (the line enters over the end pylon and leaves over the exit pylon). A street is closed
/// where it runs between two districts of a landmark square (a landmark spanning several districts covers the streets
/// between them). Of every open choice — four streets round each free square, two directions each — the one with the least
/// cost wins: the street run inside the city plus the straight way from where it leaves the city to the neighbouring stop,
/// and a heavy penalty when that way runs back over the city's districts. A choice that touches what an earlier line of
/// the same city took (<c>taken</c>) is out, so two lines in one city (Paris) use different squares and streets.</para>
/// Pure: box-local coordinates (X and Z from the city box's low corner), no world, no terrain.
/// </summary>
public static class MetroRailHalls
{
    /// <summary>How far beyond the city's box the line's street run ends before it turns to the next stop.</summary>
    public const int ExitMargin = 8;

    /// <summary>The cost of a way that runs back over the city's districts (blocks): never taken while any other fits.</summary>
    public const double CrossingPenalty = 100000.0;

    /// <summary>The letter of a rail-hall square on a metropolis's district map.</summary>
    public const char StationRole = 'S';

    /// <summary>The letter of a landmark square's district.</summary>
    public const char LandmarkRole = 'L';

    /// <summary>The letter of a district left to the land (the sea, the land beyond the coast).</summary>
    public const char LandRole = '.';

    /// <summary>A box-local rectangle, inclusive corners.</summary>
    public readonly struct Rect
    {
        public Rect(int x0, int z0, int x1, int z1)
        {
            X0 = x0;
            Z0 = z0;
            X1 = x1;
            Z1 = z1;
        }

        public int X0 { get; }
        public int Z0 { get; }
        public int X1 { get; }
        public int Z1 { get; }

        public bool Overlaps(Rect o) => X0 <= o.X1 && o.X0 <= X1 && Z0 <= o.Z1 && o.Z0 <= Z1;

        public bool Contains(double x, double z) => x >= X0 && x <= X1 + 1 && z >= Z0 && z <= Z1 + 1;
    }

    /// <summary>One hall in a city (box-local): its square, heading, platforms, footprint, and the points just beyond the
    /// box where its street run ends — <see cref="Exit"/> on the heading's side (where the line leaves or, at a terminus,
    /// arrives), <see cref="Entry"/> on the other (a through station's way in).</summary>
    public sealed class Hall
    {
        public int Gx { get; set; }
        public int Gz { get; set; }
        public int Heading { get; set; }
        public RailHallPlatforms Platforms { get; set; }
        public Rect Footprint { get; set; }
        public (int X, int Z) Exit { get; set; }
        public (int X, int Z) Entry { get; set; }

        /// <summary>The street from the hall's exit end to the box edge, and from its other end to the other edge.</summary>
        public Rect ExitStreet { get; set; }
        public Rect EntryStreet { get; set; }

        /// <summary>Blocks of street between the hall's ends and the box edges.</summary>
        public int ExitRun { get; set; }
        public int EntryRun { get; set; }

        /// <summary>The footprint's centre column (rounded down, like a station's origin from its centre).</summary>
        public (int X, int Z) Centre => (Footprint.X0 + ((Footprint.X1 - Footprint.X0 + 1) / 2), Footprint.Z0 + ((Footprint.Z1 - Footprint.Z0 + 1) / 2));
    }

    /// <summary>Every hall a city can take for a line (a terminus, or a <paramref name="through"/> station): per free square
    /// (<see cref="StationRole"/>) its four streets and two directions, where the street is open as far as the line needs it
    /// (one side for a terminus, both for a through station) and the hall's track fits in the street.</summary>
    public static List<Hall> Candidates(CityLayoutSpec spec, bool through)
    {
        var halls = new List<Hall>();
        if (spec.Street < 3 || spec.DistrictSize < RailStationGenerator.Length + 2)
        {
            return halls;
        }

        for (int gz = 0; gz < spec.Grid; gz++)
        {
            for (int gx = 0; gx < spec.Grid; gx++)
            {
                if (RoleAt(spec, gx, gz) != StationRole)
                {
                    continue;
                }

                foreach (bool alongZ in new[] { true, false })
                {
                    foreach (bool highSide in new[] { true, false }) // the square on the street's high side: its west / south street
                    {
                        foreach (int dir in new[] { 1, -1 })
                        {
                            if (HallAt(spec, gx, gz, alongZ, highSide, dir) is { } hall && Open(spec, hall, alongZ, gx, gz, highSide, through))
                            {
                                halls.Add(hall);
                            }
                        }
                    }
                }
            }
        }

        return halls;
    }

    /// <summary>The cheapest hall for a line (see the class summary): <paramref name="prev"/> and <paramref name="next"/> are
    /// the neighbouring stops (box-local; null at a line's end), <paramref name="taken"/> what earlier lines of this city
    /// use. Null when nothing fits.</summary>
    public static Hall? Choose(CityLayoutSpec spec, (double X, double Z)? prev, (double X, double Z)? next, IReadOnlyList<Rect> taken)
    {
        bool through = prev is not null && next is not null;
        Hall? best = null;
        double bestCost = double.MaxValue;
        foreach (var hall in Candidates(spec, through))
        {
            if (Touches(hall, through, taken))
            {
                continue;
            }

            double cost = through
                ? Leg(spec, hall.Exit, hall.ExitRun, next!.Value) + Leg(spec, hall.Entry, hall.EntryRun, prev!.Value)
                : Leg(spec, hall.Exit, hall.ExitRun, (next ?? prev)!.Value);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = hall;
            }
        }

        return best;
    }

    /// <summary>The rectangles a chosen hall takes in its city (its square, its footprint and the street runs its line uses),
    /// for the next line's <c>taken</c> — one hall per square.</summary>
    public static IEnumerable<Rect> Takes(CityLayoutSpec spec, Hall hall, bool through)
    {
        if (SquareUnder(spec, hall.Footprint) is { } square)
        {
            yield return square;
        }

        yield return Grow(hall.Footprint, 1);
        yield return Grow(hall.ExitStreet, 1);
        if (through)
        {
            yield return Grow(hall.EntryStreet, 1);
        }
    }

    /// <summary>The rail-hall square a hall's footprint stands on (its district's rectangle), null when it stands on none.</summary>
    public static Rect? SquareUnder(CityLayoutSpec spec, Rect footprint)
    {
        for (int gz = 0; gz < spec.Grid; gz++)
        {
            for (int gx = 0; gx < spec.Grid; gx++)
            {
                var (ox, oz) = spec.DistrictOrigin(gx, gz);
                var district = new Rect(ox, oz, ox + spec.DistrictSize - 1, oz + spec.DistrictSize - 1);
                if (RoleAt(spec, gx, gz) == StationRole && district.Overlaps(footprint))
                {
                    return district;
                }
            }
        }

        return null;
    }

    /// <summary>The district map's letter at a district ('.' outside the map).</summary>
    public static char RoleAt(CityLayoutSpec spec, int gx, int gz)
        => gz >= 0 && gz < spec.RoleMap.Count && gx >= 0 && gx < spec.RoleMap[gz].Length ? spec.RoleMap[gz][gx] : LandRole;

    /// <summary>Whether a box-local point lies on a claimed district or the streets round it (not on the land the city left).</summary>
    public static bool OnDistricts(CityLayoutSpec spec, double x, double z)
    {
        int pitch = spec.DistrictSize + spec.Street;
        for (int gx = (int)Math.Floor((x - spec.Street) / pitch) - 1; gx <= (int)Math.Floor(x / pitch) + 1; gx++)
        {
            for (int gz = (int)Math.Floor((z - spec.Street) / pitch) - 1; gz <= (int)Math.Floor(z / pitch) + 1; gz++)
            {
                if (RoleAt(spec, gx, gz) == LandRole)
                {
                    continue;
                }

                var (ox, oz) = spec.DistrictOrigin(gx, gz);
                var r = new Rect(ox - spec.Street, oz - spec.Street, ox + spec.DistrictSize + spec.Street - 1, oz + spec.DistrictSize + spec.Street - 1);
                if (r.Contains(x, z))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- the geometry of one hall

    /// <summary>The hall on the street beside square (gx, gz): along Z or along X, the square on the street's high side (the
    /// square's west / south street) or its low side (its east / north street), heading +1 or −1 along the street.</summary>
    private static Hall? HallAt(CityLayoutSpec spec, int gx, int gz, bool alongZ, bool highSide, int dir)
    {
        var (ox, oz) = spec.DistrictOrigin(gx, gz);
        int size = spec.DistrictSize, box = spec.Footprint;
        int crossOrigin = alongZ ? ox : oz, alongOrigin = alongZ ? oz : ox;
        int across = RailStationGenerator.SidePlatformWidth, length = RailStationGenerator.Length;

        // The track bed takes the three street cells nearest the square, the platform the square's outer four cells.
        int c0 = highSide ? crossOrigin - 3 : crossOrigin + size - (across - 3);
        int track = highSide ? crossOrigin - 2 : crossOrigin + size + 1;
        int a0 = alongOrigin + ((size - length) / 2);
        int a1 = a0 + length - 1;
        if (c0 < 0 || c0 + across > box)
        {
            return null;
        }

        int heading = alongZ ? (dir > 0 ? 2 : 3) : (dir > 0 ? 0 : 1);
        int exitAlong = dir > 0 ? box + ExitMargin : -ExitMargin;
        int entryAlong = dir > 0 ? -ExitMargin : box + ExitMargin;
        var (sx0, sx1) = (highSide ? crossOrigin - spec.Street : crossOrigin + size, highSide ? crossOrigin - 1 : crossOrigin + size + spec.Street - 1);
        Rect R(int along0, int along1, int cross0, int cross1)
            => alongZ ? new Rect(cross0, along0, cross1, along1) : new Rect(along0, cross0, along1, cross1);
        (int X, int Z) P(int along) => alongZ ? (track, along) : (along, track);
        var high = R(a1 + 1, box - 1, sx0, sx1);
        var low = R(0, a0 - 1, sx0, sx1);
        return new Hall
        {
            Gx = gx,
            Gz = gz,
            Heading = heading,
            Platforms = highSide ? RailHallPlatforms.High : RailHallPlatforms.Low,
            Footprint = R(a0, a1, c0, c0 + across - 1),
            Exit = P(exitAlong),
            Entry = P(entryAlong),
            ExitStreet = dir > 0 ? high : low,
            EntryStreet = dir > 0 ? low : high,
            ExitRun = dir > 0 ? box - 1 - a1 : a0,
            EntryRun = dir > 0 ? a0 : box - 1 - a1,
        };
    }

    /// <summary>Whether the street the hall stands in is open on the side(s) the line needs: no stretch of it runs between
    /// two districts of a landmark square.</summary>
    private static bool Open(CityLayoutSpec spec, Hall hall, bool alongZ, int gx, int gz, bool highSide, bool through)
    {
        int k = alongZ ? (highSide ? gx : gx + 1) : (highSide ? gz : gz + 1); // the street index: between k−1 and k
        int self = alongZ ? gz : gx;
        int dir = hall.Heading is 0 or 2 ? 1 : -1;
        bool OpenToward(int step)
        {
            for (int g = self; g >= 0 && g < spec.Grid; g += step)
            {
                char a = alongZ ? RoleAt(spec, k - 1, g) : RoleAt(spec, g, k - 1);
                char b = alongZ ? RoleAt(spec, k, g) : RoleAt(spec, g, k);
                if (a == LandmarkRole && b == LandmarkRole)
                {
                    return false;
                }
            }

            return true;
        }

        return OpenToward(dir) && (!through || OpenToward(-dir));
    }

    /// <summary>Whether a hall touches what an earlier line took.</summary>
    private static bool Touches(Hall hall, bool through, IReadOnlyList<Rect> taken)
    {
        foreach (var t in taken)
        {
            if (hall.Footprint.Overlaps(t) || hall.ExitStreet.Overlaps(t) || (through && hall.EntryStreet.Overlaps(t)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One side's cost: the street run, the straight way from the box's edge to the neighbour, and the penalty when
    /// that way runs over the city's districts.</summary>
    private static double Leg(CityLayoutSpec spec, (int X, int Z) edge, int run, (double X, double Z) toward)
    {
        double dx = toward.X - edge.X, dz = toward.Z - edge.Z;
        double len = Math.Sqrt((dx * dx) + (dz * dz));
        return run + len + (CrossesDistricts(spec, edge, toward, len) ? CrossingPenalty : 0.0);
    }

    /// <summary>Whether the straight way from an edge point toward a neighbour runs over the city's districts (sampled every
    /// four blocks while inside the box).</summary>
    private static bool CrossesDistricts(CityLayoutSpec spec, (int X, int Z) edge, (double X, double Z) toward, double len)
    {
        int box = spec.Footprint;
        int steps = (int)Math.Ceiling(Math.Min(len, 4.0 * box) / 4.0);
        for (int i = 1; i <= steps; i++)
        {
            double t = i * 4.0 / Math.Max(len, 1e-6);
            double x = edge.X + ((toward.X - edge.X) * t), z = edge.Z + ((toward.Z - edge.Z) * t);
            if (x >= 0 && z >= 0 && x < box && z < box && OnDistricts(spec, x, z))
            {
                return true;
            }
        }

        return false;
    }

    private static Rect Grow(Rect r, int by) => new(r.X0 - by, r.Z0 - by, r.X1 + by, r.Z1 + by);
}
