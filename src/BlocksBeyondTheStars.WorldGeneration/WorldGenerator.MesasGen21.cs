// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Bridge mesas and the impossible table mountains (#2338, terrain generation 21).
/// <para><b>Table variants</b> ride the classic table mountain's own hotspot cell: a re-hash of the cell hash picks
/// Plain, Visor (the cap reaches 8–20 past the wall as a 4–7-thick roof — a <see cref="BandKind.Rock"/> band
/// following the cap's own swell), Ring (the cap's outer third is a band with air under it; the ground is the core
/// alone), TwoStorey (a smaller table on the cap), Tilted (the cap slopes along a bearing) or Holed (two to four
/// rock gates). The offset variants live in <see cref="TableMountainOffset"/>, the gates in the rock-gate family,
/// the bands here; a world below generation 21 (or off arch country) is Plain everywhere.</para>
/// <para><b>Bridge mesas</b> are a row of their own: one cell grows two to four tables of one height (one stratum)
/// joined by rock decks at rim height — anchored to the raw ground under the cell centre, so both ends of a deck
/// meet level — and one deck in three is broken in the middle, its rubble (scree and boulders) on the ground
/// below. Trig-free. (partial of <see cref="WorldGenerator"/>)</para>
/// </summary>
public sealed partial class WorldGenerator
{
    // ================= the table variants =================

    private enum TableVariant : byte
    {
        Plain = 0,
        Visor,
        Ring,
        TwoStorey,
        Tilted,
        Holed,
    }

    private const double RingCoreShare = 0.66;

    /// <summary>The table's variant from a re-hash of its cell hash: three of eight tables stay plain.</summary>
    private static TableVariant TableVariantOf(ulong h)
    {
        ulong g = h * 0x9E3779B97F4A7C15UL;
        return ((g >> 8) & 0x7) switch
        {
            3 => TableVariant.Visor,
            4 => TableVariant.Ring,
            5 => TableVariant.TwoStorey,
            6 => TableVariant.Tilted,
            7 => TableVariant.Holed,
            _ => TableVariant.Plain,
        };
    }

    private static double TableVisorReach(ulong h) => 8.0 + (((h * 0x9E3779B97F4A7C15UL) >> 12) & 0xF) * 0.8; // 8..20
    private static int TableBandThick(ulong h) => 4 + (int)(((h * 0x9E3779B97F4A7C15UL) >> 16) & 0x3);             // 4..7
    private static double TableTilt(ulong h) => 0.05 + (((h * 0x9E3779B97F4A7C15UL) >> 18) & 0x3) * 0.03;        // 3°..8°
    private static double TableStoreyHeight(ulong h) => 15.0 + (((h * 0x9E3779B97F4A7C15UL) >> 23) & 0xF);        // 15..30
    private static int TableHoleCount(ulong h) => 2 + (int)((((h * 0x9E3779B97F4A7C15UL) >> 27) & 0x3) % 3);      // 2..4

    /// <summary>The along coordinate of a cap column on the tilt's bearing (one of eight).</summary>
    private static double TableTiltAlong(ulong h, double dx, double dz)
    {
        var dir = EightDirs[(int)(((h * 0x9E3779B97F4A7C15UL) >> 20) & 0x7)];
        return dx * dir.X + dz * dir.Z;
    }

    /// <summary>The visor and the ring: a rock band outside the table's solid ground at cap height, following the
    /// cap's own swell (the classic cap is the column's raw ground plus the table height).</summary>
    private int AppendTableVariantBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetHotspot(w.Seed ^ 0x7AB1E0, ButteCellSize, ButteChance, ButteMaxRadius + 20.0, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return n;
        }

        var variant = TableVariantOf(h);
        if (variant != TableVariant.Visor && variant != TableVariant.Ring)
        {
            return n;
        }

        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ButteMaxRadius - 40.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        double inner, outer;
        if (variant == TableVariant.Visor)
        {
            inner = radius - 0.5;
            outer = radius + TableVisorReach(h) * (0.85 + FbmT(w.Seed + 0x7AB3E, worldX, worldZ, 13.0, octaves: 2) * 0.3);
        }
        else
        {
            inner = radius * RingCoreShare - 0.5;
            outer = radius;
        }

        if (dist < inner || dist > outer)
        {
            return n;
        }

        double height = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * 40.0;
        int top = RawSurfaceHeight(planet, w, worldX, worldZ) + (int)System.Math.Round(height);
        int thick = TableBandThick(h);
        bands[n++] = new ColumnBand { Bottom = top - thick + 1, Top = top, Kind = BandKind.Rock, Material = w.BandMaterial };
        return n;
    }

    // ================= bridge mesas =================

    private const double MesaCellSize = 2000.0;
    private const double MesaChance = 0.3;
    private const long MesaSalt = 0x3E5A21;
    private const double MesaMaxClusterRadius = 150.0;
    private const double MesaMaxTableRadius = 60.0;
    private const double MesaMargin = MesaMaxClusterRadius + MesaMaxTableRadius + 2.0;

    private bool HasMesaClusters(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.HasTag(TerrainTag.Buttes) && planet.HasTag(TerrainTag.Arches);

    private sealed class MesaTable
    {
        public double X, Z, R;
    }

    private sealed class MesaBridge
    {
        public int From, To;        // table indices
        public double HalfWidth;
        public int Thick;
        public bool Broken;
    }

    private sealed class MesaCluster
    {
        public int Anchor;
        public double Height;       // one stratum: every table of the cluster tops out at Anchor + Height
        public MesaTable[] Tables = System.Array.Empty<MesaTable>();
        public MesaBridge[] Bridges = System.Array.Empty<MesaBridge>();
    }

    private static readonly CellCache<MesaCluster> _mesaClusters = new();

    private bool TryGetMesaCluster(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out MesaCluster cluster, out double dx, out double dz)
    {
        cluster = null!;
        var (cell, chance) = SpectacleCell(planet, MesaCellSize, MesaChance);
        if (!TryGetHotspot(w.Seed ^ MesaSalt, cell, chance, MesaMargin, worldX, worldZ, out ulong h, out dx, out dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > MesaMargin * MesaMargin)
        {
            return false;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        cluster = _mesaClusters.GetOrAdd(w.Seed ^ MesaSalt, h, (this, planet, w, cx, cz, cell),
            static (s, hash) => s.Item1.BuildMesaCluster(s.planet, s.w, hash, s.cx, s.cz, s.cell));
        return true;
    }

    private MesaCluster BuildMesaCluster(PlanetType planet, WonderProfile w, ulong h, int cx, int cz, double cell)
    {
        ulong s = (h ^ (ulong)MesaSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        double radius = System.Math.Min(MesaMaxClusterRadius, cell * 0.35);
        int count = 2 + (int)(Next() * 3.0); // 2..4
        var tables = new System.Collections.Generic.List<MesaTable>(count);
        for (int i = 0; i < count; i++)
        {
            double r = 25.0 + Next() * (MesaMaxTableRadius - 25.0);
            bool found = false;
            double px = 0.0, pz = 0.0;
            for (int attempt = 0; attempt < 10 && !found; attempt++)
            {
                px = (Next() * 2.0 - 1.0) * (radius - r);
                pz = (Next() * 2.0 - 1.0) * (radius - r);
                found = true;
                foreach (var t in tables)
                {
                    double ex = px - t.X, ez = pz - t.Z;
                    double min = r + t.R + 20.0;
                    if (ex * ex + ez * ez < min * min)
                    {
                        found = false;
                        break;
                    }
                }
            }

            if (found)
            {
                tables.Add(new MesaTable { X = px, Z = pz, R = r });
            }
        }

        var bridges = new System.Collections.Generic.List<MesaBridge>(tables.Count);
        for (int i = 0; i + 1 < tables.Count; i++)
        {
            double ex = tables[i + 1].X - tables[i].X, ez = tables[i + 1].Z - tables[i].Z;
            if (ex * ex + ez * ez > 220.0 * 220.0)
            {
                continue; // too far apart for a deck
            }

            bridges.Add(new MesaBridge
            {
                From = i,
                To = i + 1,
                HalfWidth = 2.5 + Next() * 2.0,
                Thick = 3 + (int)(Next() * 4.0),
                Broken = Next() < 1.0 / 3.0,
            });
        }

        return new MesaCluster
        {
            Anchor = RawSurfaceHeight(planet, w, cx, cz),
            Height = 30.0 + Next() * 30.0,
            Tables = tables.ToArray(),
            Bridges = bridges.ToArray(),
        };
    }

    /// <summary>The tables' rise at a column (the row): the classic table profile, every table topping out at the
    /// cluster's one height so the decks between them are level.</summary>
    private double MesaClusterOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetMesaCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return 0.0;
        }

        double best = 0.0;
        int raw = int.MinValue;
        foreach (var table in cluster.Tables)
        {
            double ex = dx - table.X, ez = dz - table.Z;
            double dist = System.Math.Sqrt(ex * ex + ez * ez);
            if (dist >= table.R)
            {
                continue;
            }

            if (raw == int.MinValue)
            {
                raw = RawSurfaceHeight(planet, w, worldX, worldZ);
            }

            double top = cluster.Anchor + cluster.Height;
            if (top <= raw)
            {
                continue;
            }

            double t = 1.0 - dist / table.R;
            double profile = t >= 0.30 ? 1.0 : System.Math.Pow(t / 0.30, 1.8);
            double rise = (top - raw) * profile;
            if (rise > best)
            {
                best = rise;
            }
        }

        return best;
    }

    /// <summary>Where a deck covers the column: the along / across frame of the bridge from one table centre to the
    /// next; the deck runs from the first table's wall to the second's; a broken deck lacks its middle third.</summary>
    private bool MesaDeckAt(MesaCluster cluster, MesaBridge bridge, double dx, double dz, out bool gap)
    {
        gap = false;
        var a = cluster.Tables[bridge.From];
        var b = cluster.Tables[bridge.To];
        double vx = b.X - a.X, vz = b.Z - a.Z;
        double len = System.Math.Sqrt(vx * vx + vz * vz);
        if (len < 1.0)
        {
            return false;
        }

        double ux = vx / len, uz = vz / len;
        double ex = dx - a.X, ez = dz - a.Z;
        double along = ex * ux + ez * uz;
        double across = -ex * uz + ez * ux;
        if (System.Math.Abs(across) > bridge.HalfWidth || along < a.R - 1.0 || along > len - b.R + 1.0)
        {
            return false;
        }

        gap = bridge.Broken && System.Math.Abs(along - len * 0.5) < len / 6.0;
        return true;
    }

    private int AppendMesaBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetMesaCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return n;
        }

        int top = cluster.Anchor + (int)System.Math.Round(cluster.Height);
        foreach (var bridge in cluster.Bridges)
        {
            if (n >= bands.Length || !MesaDeckAt(cluster, bridge, dx, dz, out bool gap) || gap)
            {
                continue;
            }

            bands[n++] = new ColumnBand { Bottom = top - bridge.Thick + 1, Top = top, Kind = BandKind.Rock, Material = w.BandMaterial };
        }

        return n;
    }

    /// <summary>True under a broken deck's missing middle: the rubble.</summary>
    private bool MesaRubbleAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetMesaCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return false;
        }

        foreach (var bridge in cluster.Bridges)
        {
            if (MesaDeckAt(cluster, bridge, dx, dz, out bool gap) && gap)
            {
                return true;
            }
        }

        return false;
    }

    private BlockId? MesaRubblePaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!MesaRubbleAt(planet, w, worldX, worldZ))
        {
            return null;
        }

        var scree = _content.GetBlock("scree")?.NumericId ?? BlockId.Air;
        return scree.IsAir ? null : scree;
    }

    // ---------------- test seams ----------------

    /// <summary>The bridge mesas of a world (tests): per cluster the table centres and radii, the rim Y, and per deck
    /// its two table indices and whether it is broken.</summary>
    internal System.Collections.Generic.List<((int X, int Z, double R)[] Tables, int RimY, (int From, int To, bool Broken)[] Bridges)>
        MesaClustersForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<((int, int, double)[], int, (int, int, bool)[])>();
        var w = WonderFor(planet);
        if (!w.MesaClusters)
        {
            return list;
        }

        var (cell, chance) = SpectacleCell(planet, MesaCellSize, MesaChance);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / cell));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / cell));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        for (int czI = 0; czI < nz; czI++)
            for (int cxI = 0; cxI < nx; cxI++)
            {
                int x = (int)(cxI * cw + cw * 0.5);
                int z = (int)(czI * ch + ch * 0.5) - period / 2;
                if (!TryGetHotspot(w.Seed ^ MesaSalt, cell, chance, MesaMargin, x, z, out _, out double dx, out double dz))
                {
                    continue;
                }

                int cx = WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference);
                int cz = WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference);
                if (!TryGetMesaCluster(planet, w, cx, cz, out var cluster, out _, out _))
                {
                    continue;
                }

                var tables = new (int, int, double)[cluster.Tables.Length];
                for (int i = 0; i < tables.Length; i++)
                {
                    tables[i] = (WorldConstants.WrapX(cx + (int)System.Math.Round(cluster.Tables[i].X), _circumference),
                        cz + (int)System.Math.Round(cluster.Tables[i].Z), cluster.Tables[i].R);
                }

                var bridges = new (int, int, bool)[cluster.Bridges.Length];
                for (int i = 0; i < bridges.Length; i++)
                {
                    bridges[i] = (cluster.Bridges[i].From, cluster.Bridges[i].To, cluster.Bridges[i].Broken);
                }

                list.Add((tables, cluster.Anchor + (int)System.Math.Round(cluster.Height), bridges));
            }

        return list;
    }

    /// <summary>The variant of the classic table whose cell holds a column (tests), Plain when none or off the gate.</summary>
    internal string TableVariantForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (!w.TableVariants || !TryGetHotspot(w.Seed ^ 0x7AB1E0, ButteCellSize, ButteChance, ButteMaxRadius + 20.0, worldX, worldZ, out ulong h, out _, out _))
        {
            return TableVariant.Plain.ToString();
        }

        return TableVariantOf(h).ToString();
    }

    /// <summary>The classic tables of a world with their variant (tests): centre, radius, height, variant name.</summary>
    internal System.Collections.Generic.List<(int X, int Z, double R, double H, string Variant)> TablesForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, double, double, string)>();
        var w = WonderFor(planet);
        if (!w.TableMountains)
        {
            return list;
        }

        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / ButteCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / ButteCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        for (int czI = 0; czI < nz; czI++)
            for (int cxI = 0; cxI < nx; cxI++)
            {
                int x = (int)(cxI * cw + cw * 0.5);
                int z = (int)(czI * ch + ch * 0.5) - period / 2;
                if (!TryGetHotspot(w.Seed ^ 0x7AB1E0, ButteCellSize, ButteChance, ButteMaxRadius + 20.0, x, z, out ulong h, out double dx, out double dz))
                {
                    continue;
                }

                double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ButteMaxRadius - 40.0);
                double height = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * 40.0;
                list.Add((WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference), WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference),
                    radius, height, w.TableVariants ? TableVariantOf(h).ToString() : TableVariant.Plain.ToString()));
            }

        return list;
    }
}
