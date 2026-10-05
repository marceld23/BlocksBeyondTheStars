// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Arch lands (#2337, terrain generation 21): one hotspot cell grows a cluster of three to seven rock arches that
/// cross in plan at different heights, a tall one passing over a low one; a quarter of the clusters are an arch
/// ROW — two to four arcs end to end sharing their abutments, a natural viaduct; some arcs carry a second deck on
/// the same abutments; and one arc in some clusters has fallen — its abutments stand over a field of scree and
/// boulders. An arc is a tube around a parabola: two abutment pillars (the landmark row, level with each other,
/// anchored to the raw ground under the cell centre) and a bar (one <see cref="BandKind.Rock"/> band per arc that
/// covers the column) whose underside rises from the pillar tops to the apex and whose thickness grows from three
/// at the apex to six–eight at the abutments — a real intrados. The classic single arch (#706) keeps its place in
/// the table; this row stands after it. Trig-free: bearings are the eight integer directions; the cluster's rolls
/// are memoised per cell. (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    private const double ArchClusterCellSize = 1400.0;
    private const double ArchClusterChance = 0.4;
    private const long ArchClusterSalt = 0xA2C421;
    private const double ArchMaxClusterRadius = 120.0;
    private const double ArchMaxReach = 46.0; // the farthest an arc's rock lies from its centre: half-span 35 + abutment r 9 + 2
    private const double ArchClusterMargin = ArchMaxClusterRadius + ArchMaxReach + 2.0;

    /// <summary>Arch country: a solid world with air and the <c>arches</c> tag.</summary>
    private bool HasArchClusters(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.HasTag(TerrainTag.Arches);

    private sealed class Arc
    {
        public double X, Z;        // cell-local centre of the span
        public int Dir;            // the bearing of the span
        public double HalfSpan, HalfWidth, AbutR;
        public double Hab;         // the abutment height: where the bar's underside leaves the pillar
        public double H;           // the apex height of the underside
        public int ThickAbut;      // bar thickness at the abutments (three at the apex)
        public bool Collapsed;     // the bar has fallen: pillars and rubble only
    }

    private sealed class ArchCluster
    {
        public int Anchor;
        public Arc[] Arcs = System.Array.Empty<Arc>();
    }

    private static readonly CellCache<ArchCluster> _archClusters = new();

    private bool TryGetArchCluster(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out ArchCluster cluster, out double dx, out double dz)
    {
        cluster = null!;
        var (cell, chance) = SpectacleCell(planet, ArchClusterCellSize, ArchClusterChance);
        if (!TryGetHotspot(w.Seed ^ ArchClusterSalt, cell, chance, ArchClusterMargin, worldX, worldZ, out ulong h, out dx, out dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > ArchClusterMargin * ArchClusterMargin)
        {
            return false;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        cluster = _archClusters.GetOrAdd(w.Seed ^ ArchClusterSalt, h, (this, planet, w, cx, cz, cell),
            static (s, hash) => s.Item1.BuildArchCluster(s.planet, s.w, hash, s.cx, s.cz, s.cell));
        return true;
    }

    private ArchCluster BuildArchCluster(PlanetType planet, WonderProfile w, ulong h, int cx, int cz, double cell)
    {
        ulong s = (h ^ (ulong)ArchClusterSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        double radius = System.Math.Min(ArchMaxClusterRadius, cell * 0.4);
        var arcs = new System.Collections.Generic.List<Arc>(10);

        Arc Roll(double px, double pz, int dir, double halfSpan)
        {
            double hab = 6.0 + Next() * 14.0;
            double halfWidth = 2.0 + Next() * 2.5;
            return new Arc
            {
                X = px,
                Z = pz,
                Dir = dir,
                HalfSpan = halfSpan,
                HalfWidth = halfWidth,
                AbutR = System.Math.Max(halfWidth + 1.5, 4.0 + Next() * 5.0),
                Hab = hab,
                H = hab + 12.0 + Next() * 40.0,
                ThickAbut = 6 + (int)(Next() * 3.0),
            };
        }

        if (Next() < 0.25)
        {
            // An arch row: two to four arcs end to end on one bearing, their abutments shared — a viaduct.
            int m = 2 + (int)(Next() * 3.0);
            double halfSpan = System.Math.Min(10.0 + Next() * 25.0, radius * 0.8 / m);
            int dir = (int)(Next() * 8.0) & 7;
            var axis = EightDirs[dir];
            var first = Roll(0.0, 0.0, dir, halfSpan);
            for (int k = 0; k < m; k++)
            {
                double offset = (k - (m - 1) * 0.5) * 2.0 * halfSpan;
                arcs.Add(new Arc
                {
                    X = axis.X * offset,
                    Z = axis.Z * offset,
                    Dir = dir,
                    HalfSpan = halfSpan,
                    HalfWidth = first.HalfWidth,
                    AbutR = first.AbutR,
                    Hab = first.Hab,
                    H = first.H,
                    ThickAbut = first.ThickAbut,
                });
            }
        }
        else
        {
            int count = 3 + (int)(Next() * 5.0);
            bool collapsedRolled = false;
            for (int i = 0; i < count; i++)
            {
                double halfSpan = 10.0 + Next() * 25.0;
                double inner = System.Math.Max(4.0, radius - halfSpan - 11.0);
                double px = (Next() * 2.0 - 1.0) * inner;
                double pz = (Next() * 2.0 - 1.0) * inner;
                var arc = Roll(px, pz, (int)(Next() * 8.0) & 7, halfSpan);
                if (!collapsedRolled && Next() < 0.3)
                {
                    arc.Collapsed = true; // the one that fell
                    collapsedRolled = true;
                }

                arcs.Add(arc);
                if (!arc.Collapsed && Next() < 0.2)
                {
                    // A double-decker: a second bar on the same abutments, above the first apex.
                    double hab2 = arc.H + 4.0;
                    arcs.Add(new Arc
                    {
                        X = arc.X,
                        Z = arc.Z,
                        Dir = arc.Dir,
                        HalfSpan = arc.HalfSpan,
                        HalfWidth = arc.HalfWidth,
                        AbutR = arc.AbutR,
                        Hab = hab2,
                        H = hab2 + 10.0 + Next() * 15.0,
                        ThickAbut = arc.ThickAbut,
                    });
                }
            }
        }

        return new ArchCluster { Anchor = RawSurfaceHeight(planet, w, cx, cz), Arcs = arcs.ToArray() };
    }

    /// <summary>The abutment pillars' rise at a column (the landmark row): sheer, level with the bar's top where it
    /// meets them (a double-decker's pillar reaches the upper bar). 0 off every abutment.</summary>
    private double ArchClusterOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetArchCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return 0.0;
        }

        double best = 0.0;
        int raw = int.MinValue;
        foreach (var arc in cluster.Arcs)
        {
            var axis = EightDirs[arc.Dir];
            for (int side = -1; side <= 1; side += 2)
            {
                double ax = arc.X + axis.X * arc.HalfSpan * side, az = arc.Z + axis.Z * arc.HalfSpan * side;
                double ex = dx - ax, ez = dz - az;
                double d2 = ex * ex + ez * ez;
                if (d2 >= arc.AbutR * arc.AbutR)
                {
                    continue;
                }

                if (raw == int.MinValue)
                {
                    raw = RawSurfaceHeight(planet, w, worldX, worldZ);
                }

                double top = cluster.Anchor + arc.Hab + arc.ThickAbut;
                if (top <= raw)
                {
                    continue;
                }

                double rise = (top - raw) * System.Math.Pow(1.0 - System.Math.Sqrt(d2) / arc.AbutR, 0.25);
                if (rise > best)
                {
                    best = rise;
                }
            }
        }

        return best;
    }

    /// <summary>The bars at a column: one band per arc whose tube covers it — the underside a parabola from the
    /// pillar tops to the apex, the thickness three at the apex growing to the abutment thickness.</summary>
    private int AppendArchBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetArchCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return n;
        }

        var material = w.BandMaterial;
        foreach (var arc in cluster.Arcs)
        {
            if (arc.Collapsed || n >= bands.Length)
            {
                continue;
            }

            AlongAcross(dx - arc.X, dz - arc.Z, EightDirs[arc.Dir], out double along, out double across);
            if (System.Math.Abs(along) > arc.HalfSpan || System.Math.Abs(across) > arc.HalfWidth)
            {
                continue;
            }

            double t = along / arc.HalfSpan;
            double t2 = t * t;
            double under = cluster.Anchor + arc.Hab + (arc.H - arc.Hab) * (1.0 - t2);
            double thick = 3.0 + (arc.ThickAbut - 3.0) * t2;
            int bottom = (int)System.Math.Round(under);
            int top = bottom + (int)System.Math.Round(thick) - 1;
            bands[n++] = new ColumnBand { Bottom = bottom, Top = top, Kind = BandKind.Rock, Material = material };
        }

        return n;
    }

    /// <summary>True under a fallen arc's span (between its abutments): where the rubble lies.</summary>
    private bool ArchRubbleAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetArchCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return false;
        }

        foreach (var arc in cluster.Arcs)
        {
            if (!arc.Collapsed)
            {
                continue;
            }

            AlongAcross(dx - arc.X, dz - arc.Z, EightDirs[arc.Dir], out double along, out double across);
            if (System.Math.Abs(along) <= arc.HalfSpan - arc.AbutR && System.Math.Abs(across) <= arc.HalfWidth + 2.0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The rubble paint of a fallen arc (the row's paint delegate): scree between the abutments, topsoil only.</summary>
    private BlockId? ArchRubblePaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!ArchRubbleAt(planet, w, worldX, worldZ))
        {
            return null;
        }

        var scree = _content.GetBlock("scree")?.NumericId ?? BlockId.Air;
        return scree.IsAir ? null : scree;
    }

    /// <summary>The rubble prop row's gate (#2337): arch worlds only; the shape decides per column.</summary>
    private static bool PropArchRubble(WonderProfile w, PlanetType p) => w.ArchClusters && PropSolidGround(w, p);

    /// <summary>A boulder of the fallen bar, only where the rubble lies — elsewhere the roll leaves nothing.</summary>
    private static void StampArchRubble(PropStamp s)
    {
        var w = s.Generator.WonderFor(s.Planet);
        if (!s.Generator.ArchRubbleAt(s.Planet, w, s.Wx, s.Wz))
        {
            return;
        }

        var material = w.BandMaterial.IsAir ? s.Material : w.BandMaterial;
        s.Set(s.Wx, s.Sy + 1, s.Wz, material);
        if ((s.ShapeHash & 1) == 0) s.Set(s.Wx + 1, s.Sy + 1, s.Wz, material);
        if ((s.ShapeHash & 2) == 0) s.Set(s.Wx, s.Sy + 1, s.Wz + 1, material);
        if ((s.ShapeHash & 12) == 0) s.Set(s.Wx, s.Sy + 2, s.Wz, material);
    }

    // ---------------- test seams ----------------

    /// <summary>Every arc of a world's clusters (tests): the span centre in world coordinates, the bearing, the
    /// half-span, the half-width, the abutment radius, the pillar top Y, the apex underside Y, and whether it fell.</summary>
    internal System.Collections.Generic.List<(int X, int Z, int Dir, double HalfSpan, double HalfWidth, double AbutR, int PillarTop, int ApexUnder, bool Collapsed)>
        ArchClustersForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, int, double, double, double, int, int, bool)>();
        var w = WonderFor(planet);
        if (!w.ArchClusters)
        {
            return list;
        }

        var (cell, chance) = SpectacleCell(planet, ArchClusterCellSize, ArchClusterChance);
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
                if (!TryGetHotspot(w.Seed ^ ArchClusterSalt, cell, chance, ArchClusterMargin, x, z, out _, out double dx, out double dz))
                {
                    continue;
                }

                int cx = WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference);
                int cz = WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference);
                if (!TryGetArchCluster(planet, w, cx, cz, out var cluster, out _, out _))
                {
                    continue;
                }

                foreach (var arc in cluster.Arcs)
                {
                    list.Add((WorldConstants.WrapX(cx + (int)System.Math.Round(arc.X), _circumference), cz + (int)System.Math.Round(arc.Z),
                        arc.Dir, arc.HalfSpan, arc.HalfWidth, arc.AbutR,
                        cluster.Anchor + (int)System.Math.Round(arc.Hab) + arc.ThickAbut, cluster.Anchor + (int)System.Math.Round(arc.H), arc.Collapsed));
                }
            }

        return list;
    }
}
