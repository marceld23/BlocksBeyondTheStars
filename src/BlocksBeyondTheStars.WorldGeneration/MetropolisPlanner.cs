// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The ground plan of a metropolis (#3983): a city of a place list laid out on the land it stands on, decided once when the
/// city is composed and pinned as its district map (<see cref="CityLayoutSpec.RoleMap"/>, <see cref="CityLayoutSpec.Metro"/>).
/// <list type="number">
/// <item><b>The land.</b> Every district is <see cref="CellGround.Kept">kept</see> (mostly dry ground near the city's floor),
/// <see cref="CellGround.IsLand">land</see> out of reach of the floor (too high, too low), or sea. Only districts on the plaza's
/// landmass count: a flood fill over land from the plaza, so a city never jumps a strait (London stays on its island).</item>
/// <item><b>The landmark squares</b> (<c>L</c>), the largest first: the free rectangle of districts nearest the plaza that holds
/// the template's footprint with a block to spare on each side, either way round; land out of reach may be part of it (the
/// square is levelled). A landmark that finds none is reported in <see cref="Plan.Unplaced"/>.</item>
/// <item><b>The services</b> next to the plaza on kept districts: the market halls (<c>M</c>) and the halls of the notice house,
/// the inn and the workshop (<c>H</c>); then the free square(s) for the rail hall (<c>S</c>, two districts or more from the
/// plaza when there is room), the high-rises (<c>T</c>) round the centre, the quays (<c>Q</c>) where a district touches the sea,
/// a park (<c>G</c>) in about every fifth of the rest, housing (<c>R</c>) everywhere else; every other district is <c>.</c>.</item>
/// </list>
/// Deterministic: the same request lays the same plan (ties break by a hash of the seed and the district).
/// </summary>
public static class MetropolisPlanner
{
    /// <summary>A metropolis's district size, street width and height: the G.D.S. city's districts, room for high-rises.</summary>
    public const int DistrictSize = 32;
    public const int Street = 4;
    public const int Height = 48;

    /// <summary>The share of a district's samples that must be kept ground for it to be built on.</summary>
    public const double KeepShare = 0.5;

    /// <summary>The share of sea samples that makes a kept district a quay.</summary>
    public const double QuayShare = 0.1;

    /// <summary>How far a column's ground may lie from the city's floor and still count as kept (the grade and the stamper's cut
    /// and plinth take up the rest).</summary>
    public const int FloorReach = 24;

    /// <summary>The blocks kept free round a landmark inside its square.</summary>
    public const int LandmarkMargin = 1;

    /// <summary>What a district's ground is: the shares of its samples that are kept (dry, within <see cref="FloorReach"/>), dry
    /// out of reach, and sea.</summary>
    public readonly struct CellGround
    {
        public CellGround(double kept, double dry, double sea)
        {
            KeptShare = kept;
            DryShare = dry;
            SeaShare = sea;
        }

        public double KeptShare { get; }
        public double DryShare { get; }
        public double SeaShare { get; }

        /// <summary>Built on: mostly kept ground.</summary>
        public bool Kept => KeptShare >= KeepShare;

        /// <summary>Part of the landmass: mostly dry (kept or not).</summary>
        public bool IsLand => KeptShare + DryShare >= KeepShare;
    }

    /// <summary>A landmark to place: its template key and footprint.</summary>
    public readonly struct LandmarkRequest
    {
        public LandmarkRequest(string key, int width, int length)
        {
            Key = key;
            Width = width;
            Length = length;
        }

        public string Key { get; }
        public int Width { get; }
        public int Length { get; }
    }

    /// <summary>What the plan is laid from.</summary>
    public sealed class Request
    {
        /// <summary>Districts per side.</summary>
        public int Grid { get; set; } = 5;

        /// <summary>The plaza's district (column = X index, row = Z index).</summary>
        public (int Gx, int Gz) Plaza { get; set; } = (2, 2);

        /// <summary>The ground of district (gx, gz) — see <see cref="SampleDistrict"/>.</summary>
        public System.Func<int, int, CellGround> Ground { get; set; } = (_, _) => new CellGround(1, 0, 0);

        public IReadOnlyList<LandmarkRequest> Landmarks { get; set; } = System.Array.Empty<LandmarkRequest>();

        /// <summary>How many free squares are kept for rail halls (Paris has two).</summary>
        public int Stations { get; set; } = 1;

        public long Seed { get; set; }
    }

    /// <summary>A landmark's square: its districts (inclusive), the quarter turn it stands at and its centre column
    /// (structure-local).</summary>
    public sealed class LandmarkSquare
    {
        public string Key { get; set; } = string.Empty;
        public int Gx0 { get; set; }
        public int Gz0 { get; set; }
        public int Gx1 { get; set; }
        public int Gz1 { get; set; }
        public int Turns { get; set; }
        public int CentreX { get; set; }
        public int CentreZ { get; set; }
    }

    /// <summary>The laid plan.</summary>
    public sealed class Plan
    {
        public CityLayoutSpec Layout { get; set; }
        public List<LandmarkSquare> Squares { get; } = new();

        /// <summary>The landmark keys no square could be found for.</summary>
        public List<string> Unplaced { get; } = new();
    }

    /// <summary>Lays the plan (see the class summary).</summary>
    public static Plan Lay(Request r)
    {
        int n = System.Math.Clamp(r.Grid, 3, 12);
        var map = new char[n, n];
        var ground = new CellGround[n, n];
        for (int gx = 0; gx < n; gx++)
        {
            for (int gz = 0; gz < n; gz++)
            {
                ground[gx, gz] = r.Ground(gx, gz);
                map[gx, gz] = '.';
            }
        }

        var (px, pz) = (System.Math.Clamp(r.Plaza.Gx, 0, n - 1), System.Math.Clamp(r.Plaza.Gz, 0, n - 1));
        var land = Landmass(ground, n, px, pz);
        for (int gx = 0; gx < n; gx++)
        {
            for (int gz = 0; gz < n; gz++)
            {
                if (land[gx, gz])
                {
                    map[gx, gz] = Free;
                }
            }
        }

        map[px, pz] = 'P';
        var spec = new CityLayoutSpec(n, DistrictSize, Street, Height, null, metro: true);
        var plan = new Plan();
        PlaceLandmarks(r, map, n, (px, pz), spec, plan);
        AssignServices(r, map, ground, n, (px, pz));
        plan.Layout = new CityLayoutSpec(n, DistrictSize, Street, Height, RoleRows(map, n), metro: true);
        return plan;
    }

    /// <summary>The blocks between two ground samples of a district.</summary>
    public const int SampleStride = 4;

    /// <summary>The ground of district (<paramref name="gx"/>, <paramref name="gz"/>) of a city whose structure-local origin lies
    /// at world column (<paramref name="originX"/>, <paramref name="originZ"/>): every <see cref="SampleStride"/>-th column of
    /// the district is sea (<paramref name="wet"/>), kept (its <paramref name="surface"/> within <see cref="FloorReach"/> of
    /// <paramref name="floorY"/>) or dry ground out of reach.</summary>
    public static CellGround SampleDistrict(CityLayoutSpec spec, int originX, int originZ, int gx, int gz, int floorY,
        System.Func<int, int, bool> wet, System.Func<int, int, int> surface)
    {
        var (ox, oz) = spec.DistrictOrigin(gx, gz);
        int kept = 0, dry = 0, sea = 0;
        for (int x = 0; x < spec.DistrictSize; x += SampleStride)
        {
            for (int z = 0; z < spec.DistrictSize; z += SampleStride)
            {
                int wx = originX + ox + x, wz = originZ + oz + z;
                if (wet(wx, wz))
                {
                    sea++;
                }
                else if (System.Math.Abs(surface(wx, wz) - floorY) <= FloorReach)
                {
                    kept++;
                }
                else
                {
                    dry++;
                }
            }
        }

        double all = System.Math.Max(1, kept + dry + sea);
        return new CellGround(kept / all, dry / all, sea / all);
    }

    /// <summary>A free district on the landmass (none of the letters).</summary>
    private const char Free = '?';

    /// <summary>The districts of the plaza's landmass: a four-way flood fill over land from the plaza.</summary>
    private static bool[,] Landmass(CellGround[,] ground, int n, int px, int pz)
    {
        var seen = new bool[n, n];
        var queue = new Queue<(int, int)>();
        seen[px, pz] = true;
        queue.Enqueue((px, pz));
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;
                if (nx >= 0 && nz >= 0 && nx < n && nz < n && !seen[nx, nz] && ground[nx, nz].IsLand)
                {
                    seen[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }

        return seen;
    }

    /// <summary>The districts a footprint of <paramref name="blocks"/> (plus the margin each side) needs along one axis.</summary>
    public static int DistrictsFor(int blocks)
    {
        int k = 1;
        while ((k * DistrictSize) + ((k - 1) * Street) < blocks + (2 * LandmarkMargin))
        {
            k++;
        }

        return k;
    }

    private static void PlaceLandmarks(Request r, char[,] map, int n, (int X, int Z) plaza, CityLayoutSpec spec, Plan plan)
    {
        var order = new List<LandmarkRequest>(r.Landmarks);
        order.Sort((a, b) =>
        {
            int c = ((long)b.Width * b.Length).CompareTo((long)a.Width * a.Length);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });

        foreach (var lm in order)
        {
            if (BestSquare(r, map, n, plaza, lm) is not { } best)
            {
                plan.Unplaced.Add(lm.Key);
                continue;
            }

            var (gx0, gz0, cw, cl, turns) = best;
            for (int gx = gx0; gx < gx0 + cw; gx++)
            {
                for (int gz = gz0; gz < gz0 + cl; gz++)
                {
                    map[gx, gz] = 'L';
                }
            }

            var (x0, z0) = spec.DistrictOrigin(gx0, gz0);
            var (x1, z1) = spec.DistrictOrigin(gx0 + cw - 1, gz0 + cl - 1);
            plan.Squares.Add(new LandmarkSquare
            {
                Key = lm.Key,
                Gx0 = gx0,
                Gz0 = gz0,
                Gx1 = gx0 + cw - 1,
                Gz1 = gz0 + cl - 1,
                Turns = turns,
                CentreX = (x0 + x1 + DistrictSize) / 2,
                CentreZ = (z0 + z1 + DistrictSize) / 2,
            });
        }
    }

    /// <summary>The free rectangle nearest the plaza that holds the landmark (either way round): its corner, its size in
    /// districts and the turn; null when none fits.</summary>
    private static (int Gx, int Gz, int W, int L, int Turns)? BestSquare(Request r, char[,] map, int n, (int X, int Z) plaza, LandmarkRequest lm)
    {
        (int, int, int, int, int)? best = null;
        long bestScore = long.MaxValue;
        for (int turns = 0; turns <= 1; turns++)
        {
            int cw = DistrictsFor(turns == 0 ? lm.Width : lm.Length), cl = DistrictsFor(turns == 0 ? lm.Length : lm.Width);
            for (int gx = 0; gx + cw <= n; gx++)
            {
                for (int gz = 0; gz + cl <= n; gz++)
                {
                    if (!AllFree(map, gx, gz, cw, cl))
                    {
                        continue;
                    }

                    // twice the rectangle's centre minus twice the plaza's, squared: whole numbers for every size
                    long dx = (2 * gx) + cw - 1 - (2 * plaza.X), dz = (2 * gz) + cl - 1 - (2 * plaza.Z);
                    long score = (((dx * dx) + (dz * dz)) << 16) + (long)(TieBreak(r.Seed, lm.Key, gx, gz) & 0xFFFF) + (turns << 15);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (gx, gz, cw, cl, turns);
                    }
                }
            }
        }

        return best;
    }

    private static bool AllFree(char[,] map, int gx0, int gz0, int w, int l)
    {
        for (int gx = gx0; gx < gx0 + w; gx++)
        {
            for (int gz = gz0; gz < gz0 + l; gz++)
            {
                if (map[gx, gz] != Free)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ulong TieBreak(long seed, string lane, int gx, int gz)
        => StableHashing.Mixed("metro:" + lane + ":" + gx.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                                + gz.ToString(System.Globalization.CultureInfo.InvariantCulture), seed);

    /// <summary>The services, the station squares, the high-rises, the quays, the parks and the housing on the kept districts.</summary>
    private static void AssignServices(Request r, char[,] map, CellGround[,] ground, int n, (int X, int Z) plaza)
    {
        var kept = new List<(int Gx, int Gz)>();
        for (int gx = 0; gx < n; gx++)
        {
            for (int gz = 0; gz < n; gz++)
            {
                if (map[gx, gz] == Free && ground[gx, gz].Kept)
                {
                    kept.Add((gx, gz));
                }
                else if (map[gx, gz] == Free)
                {
                    map[gx, gz] = '.'; // land out of the floor's reach: left to the land
                }
            }
        }

        kept.Sort((a, b) => Nearness(r.Seed, a, plaza).CompareTo(Nearness(r.Seed, b, plaza)));
        Take(map, kept, 'M', n >= 6 ? 2 : 1);
        Take(map, kept, 'H', 3);
        TakeStations(map, kept, plaza, r.Stations);
        Take(map, kept, 'T', System.Math.Max(2, kept.Count / 6));
        bool parked = false;
        for (int i = kept.Count - 1; i >= 0; i--) // outside in: the first park of a small city lies at its edge
        {
            var (gx, gz) = kept[i];
            if (map[gx, gz] != Free)
            {
                continue;
            }

            if (ground[gx, gz].SeaShare >= QuayShare && TouchesSea(map, n, gx, gz))
            {
                map[gx, gz] = 'Q';
            }
            else if (!parked || TieBreak(r.Seed, "park", gx, gz) % 5 == 0)
            {
                map[gx, gz] = 'G';
                parked = true;
            }
            else
            {
                map[gx, gz] = 'R';
            }
        }
    }

    /// <summary>Sort key: the ring round the plaza first, then the distance, then a hash.</summary>
    private static long Nearness(long seed, (int Gx, int Gz) c, (int X, int Z) plaza)
    {
        int dx = c.Gx - plaza.X, dz = c.Gz - plaza.Z;
        int ring = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz));
        return ((long)ring << 40) + ((long)((dx * dx) + (dz * dz)) << 20) + (long)(TieBreak(seed, "near", c.Gx, c.Gz) & 0xFFFFF);
    }

    /// <summary>The <paramref name="count"/> free kept districts nearest the plaza take <paramref name="letter"/>.</summary>
    private static void Take(char[,] map, List<(int Gx, int Gz)> kept, char letter, int count)
    {
        for (int i = 0; i < kept.Count && count > 0; i++)
        {
            var (gx, gz) = kept[i];
            if (map[gx, gz] == Free)
            {
                map[gx, gz] = letter;
                count--;
            }
        }
    }

    /// <summary>The rail halls' squares: the free kept districts nearest the plaza that lie two rings out or more (a station
    /// beside the plaza would crowd the services), else the nearest free ones.</summary>
    private static void TakeStations(char[,] map, List<(int Gx, int Gz)> kept, (int X, int Z) plaza, int count)
    {
        for (int pass = 0; pass < 2 && count > 0; pass++)
        {
            foreach (var (gx, gz) in kept)
            {
                int ring = System.Math.Max(System.Math.Abs(gx - plaza.X), System.Math.Abs(gz - plaza.Z));
                if (count > 0 && map[gx, gz] == Free && (pass == 1 || ring >= 2))
                {
                    map[gx, gz] = 'S';
                    count--;
                }
            }
        }
    }

    private static bool TouchesSea(char[,] map, int n, int gx, int gz)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int x = gx + dx, z = gz + dz;
            if (x < 0 || z < 0 || x >= n || z >= n || map[x, z] == '.')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The district map as the layout spec keeps it: one string per row (Z index), one letter per column (X index).</summary>
    private static List<string> RoleRows(char[,] map, int n)
    {
        var rows = new List<string>(n);
        var sb = new StringBuilder(n);
        for (int gz = 0; gz < n; gz++)
        {
            sb.Clear();
            for (int gx = 0; gx < n; gx++)
            {
                sb.Append(map[gx, gz] == Free ? 'R' : map[gx, gz]);
            }

            rows.Add(sb.ToString());
        }

        return rows;
    }

    /// <summary>
    /// The column map of a metropolis (<see cref="SettlementStructure.ColumnFloors"/>): every district on the map and the streets
    /// round it claimed at floor 0, every column of a district the map leaves to the land (<c>.</c>) and of the streets that
    /// touch no other district unclaimed (−1) — so the sea and the land beyond the coast keep their ground. Null when the map
    /// leaves nothing to the land (the box is the city).
    /// </summary>
    public static sbyte[]? ColumnFloors(CityLayoutSpec spec)
    {
        int size = spec.Footprint;
        var floors = new sbyte[size * size];
        System.Array.Fill(floors, (sbyte)-1);
        bool any = false;
        for (int gx = 0; gx < spec.Grid; gx++)
        {
            for (int gz = 0; gz < spec.Grid; gz++)
            {
                if (CityGenerator.RoleAtFor(gx, gz, spec.Grid, spec.RoleMap) == CityGenerator.Role.None)
                {
                    any = true;
                    continue;
                }

                var (ox, oz) = spec.DistrictOrigin(gx, gz);
                for (int x = System.Math.Max(0, ox - spec.Street); x < System.Math.Min(size, ox + spec.DistrictSize + spec.Street); x++)
                {
                    for (int z = System.Math.Max(0, oz - spec.Street); z < System.Math.Min(size, oz + spec.DistrictSize + spec.Street); z++)
                    {
                        floors[(x * size) + z] = 0;
                    }
                }
            }
        }

        return any ? floors : null;
    }
}
