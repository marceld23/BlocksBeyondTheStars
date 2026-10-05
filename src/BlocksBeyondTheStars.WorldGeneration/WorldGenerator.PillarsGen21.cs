// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Pillar islands (#2336, terrain generation 21): thin stone towers on one or two pillars, balconies on the way up
/// and a crown island on top — a meadow with a pond that spills an endless waterfall down the pillar on a wet world,
/// bare rock with an ore clump on a dry one, a second storey now and then. One hotspot cell grows a cluster of
/// islands; the cluster's rolls are a pure function of the cell hash and are memoised per cell
/// (<see cref="CellCache{T}"/>). The stems are the landmark row (a sheer <c>pow 0.2</c> pillar whose top is level
/// with every other stem of the cluster, anchored to the raw ground under the cell centre); everything above a stem
/// top is a band — <see cref="BandKind.Crown"/> for the crown and the balconies, <see cref="BandKind.IslandPond"/>
/// for the pond, <see cref="BandKind.Waterfall"/> for the fall, <see cref="BandKind.Rock"/> for a second storey's
/// stem. Trig-free: bearings are the eight integer directions. Floating-island worlds never grow one (their landmark
/// rows are off by design); the tag <c>pillars</c> and <see cref="PlanetType.SpectacleDensity"/> decide where and how
/// many. (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    private const double PillarCellSize = 900.0;
    private const double PillarChance = 0.35;
    private const long PillarSalt = 0x9111A0;
    private const double PillarMaxClusterRadius = 110.0;
    private const double PillarMaxCrownReach = 42.0; // the widest crown (two stems 30 apart + r 24) past a stem centre
    private const double PillarMargin = PillarMaxClusterRadius + PillarMaxCrownReach + 2.0;

    /// <summary>Pillar country: a solid world with air and the <c>pillars</c> tag, never a sky world.</summary>
    private bool HasPillarIslands(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.HasTag(TerrainTag.Pillars);

    /// <summary>The block the package's rock bands are made of on a world (#2332): sandstone on butte or wind country,
    /// basalt on volcanic worlds, ice in the deep cold, else Air — the planet's deep block.</summary>
    private BlockId BandMaterialFor(PlanetType planet)
    {
        string key = planet.HasTag(TerrainTag.Volcanic) ? "basalt"
            : planet.BaseTemperature <= -8.0 ? "ice"
            : planet.HasTag(TerrainTag.Buttes) || planet.HasTag(TerrainTag.Wind) ? "sandstone"
            : string.Empty;
        return key.Length == 0 ? BlockId.Air : _content.GetBlock(key)?.NumericId ?? BlockId.Air;
    }

    /// <summary>A family's cell pitch and chance on a world: the type's <see cref="PlanetType.SpectacleDensity"/>
    /// scales the chance up and the pitch down (density 4 = a quarter of the cell area, every cell rolled).</summary>
    private static (double Cell, double Chance) SpectacleCell(PlanetType planet, double baseCell, double baseChance)
    {
        double d = System.Math.Max(0.25, planet.SpectacleDensity);
        return (baseCell / System.Math.Sqrt(d), System.Math.Min(0.95, baseChance * d));
    }

    private sealed class PillarBalcony
    {
        public int Dir;        // one of the eight bearings — the half-disc lies on that side of the stem
        public double Frac;    // height on the stem, 0.25..0.8
        public double R;       // how far past the stem wall the balcony reaches
        public int Thick;      // 2..4
        public bool Seep;      // a one-cell waterfall off the outer edge (wet worlds)
    }

    private sealed class PillarStem
    {
        public double X, Z, R, H; // cell-local centre, radius, height above the cluster anchor
        public PillarBalcony[] Balconies = System.Array.Empty<PillarBalcony>();
    }

    private sealed class PillarIsland
    {
        public PillarStem[] Stems = System.Array.Empty<PillarStem>();
        public int Dir;                   // the stem axis (two stems) — the crown's long axis
        public double CrownX, CrownZ;     // the crown centre (the stem, or the midpoint of two)
        public double CrownRx, CrownRz;   // the crown's half-axes along / across the axis
        public int CrownThick;
        public bool Pond;
        public int FallDir;               // which way the pond spills
        public bool TwoStorey;
        public double UpperR, UpperH, UpperCrownR;
    }

    private sealed class PillarCluster
    {
        public int Anchor;                // the raw ground under the cell centre: every stem tops out relative to it
        public PillarIsland[] Islands = System.Array.Empty<PillarIsland>();
    }

    private static readonly CellCache<PillarCluster> _pillarClusters = new();

    /// <summary>The cluster whose cell holds this column (its rolls memoised), with the column's offset from the
    /// cell centre; false outside every pillar cell or beyond the cluster's reach.</summary>
    private bool TryGetPillarCluster(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out PillarCluster cluster, out double dx, out double dz)
    {
        cluster = null!;
        var (cell, chance) = SpectacleCell(planet, PillarCellSize, PillarChance);
        if (!TryGetHotspot(w.Seed ^ PillarSalt, cell, chance, PillarMargin, worldX, worldZ, out ulong h, out dx, out dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > PillarMargin * PillarMargin)
        {
            return false; // the cheap reject: most columns of a cell lie far from its cluster
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        cluster = _pillarClusters.GetOrAdd(w.Seed ^ PillarSalt, h, (this, planet, w, cx, cz, cell),
            static (s, hash) => s.Item1.BuildPillarCluster(s.planet, s.w, hash, s.cx, s.cz, s.cell));
        return true;
    }

    /// <summary>Rolls one cell's cluster from its hash — an xorshift stream like the worm carver's.</summary>
    private PillarCluster BuildPillarCluster(PlanetType planet, WonderProfile w, ulong h, int cx, int cz, double cell)
    {
        ulong s = (h ^ (ulong)PillarSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        bool wet = WaterAbundanceOf(planet) >= 0.4;
        double density = System.Math.Max(0.25, planet.SpectacleDensity);
        double radius = System.Math.Min(PillarMaxClusterRadius, cell * 0.4);
        int count = 1 + (int)(Next() * (2.0 + 2.0 * density));
        count = System.Math.Min(count, 12);

        var islands = new System.Collections.Generic.List<PillarIsland>(count);
        var placed = new System.Collections.Generic.List<(double X, double Z, double Reach)>(count);
        for (int i = 0; i < count; i++)
        {
            bool two = Next() < 0.4;
            double stemR = 4.0 + Next() * 6.0;
            double height = 40.0 + Next() * 50.0;
            int dir = (int)(Next() * 8.0) & 7;
            double gap = two ? 12.0 + Next() * 18.0 : 0.0;
            double crownR = 8.0 + Next() * 16.0;
            double reach = gap * 0.5 + crownR + 2.0;

            // A spot inside the cluster that keeps clear of every island placed so far (a few tries, then skip).
            double px = 0.0, pz = 0.0;
            bool found = false;
            for (int attempt = 0; attempt < 8 && !found; attempt++)
            {
                px = (Next() * 2.0 - 1.0) * (radius - reach);
                pz = (Next() * 2.0 - 1.0) * (radius - reach);
                found = true;
                foreach (var p in placed)
                {
                    double ex = px - p.X, ez = pz - p.Z;
                    double min = reach + p.Reach + 4.0;
                    if (ex * ex + ez * ez < min * min)
                    {
                        found = false;
                        break;
                    }
                }
            }

            if (!found)
            {
                continue;
            }

            placed.Add((px, pz, reach));
            var axis = EightDirs[dir];
            var stems = new PillarStem[two ? 2 : 1];
            stems[0] = new PillarStem { X = px - axis.X * gap * 0.5, Z = pz - axis.Z * gap * 0.5, R = stemR, H = height };
            if (two)
            {
                stems[1] = new PillarStem { X = px + axis.X * gap * 0.5, Z = pz + axis.Z * gap * 0.5, R = 4.0 + Next() * 6.0, H = height };
            }

            foreach (var stem in stems)
            {
                double roll = Next();
                int balconies = roll < 0.35 ? 0 : roll < 0.70 ? 1 : roll < 0.90 ? 2 : 3;
                var list = new PillarBalcony[balconies];
                int usedDirs = 0;
                for (int b = 0; b < balconies; b++)
                {
                    int bdir = (int)(Next() * 8.0) & 7;
                    for (int k = 0; k < 8 && ((usedDirs >> bdir) & 1) != 0; k++)
                    {
                        bdir = (bdir + 3) & 7; // the next free bearing, never the same side twice
                    }

                    usedDirs |= 1 << bdir;
                    list[b] = new PillarBalcony
                    {
                        Dir = bdir,
                        Frac = 0.25 + Next() * 0.55,
                        R = 3.0 + Next() * 4.0,
                        Thick = 2 + (int)(Next() * 3.0),
                        Seep = wet && Next() < 0.2,
                    };
                }

                stem.Balconies = list;
            }

            var island = new PillarIsland
            {
                Stems = stems,
                Dir = dir,
                CrownX = px,
                CrownZ = pz,
                CrownRx = gap * 0.5 + crownR,
                CrownRz = two ? crownR * 0.8 : crownR,
                CrownThick = 6 + (int)(Next() * 9.0),
                Pond = wet && Next() < 0.6,
                FallDir = (int)(Next() * 8.0) & 7,
                TwoStorey = Next() < 0.15,
            };
            if (island.TwoStorey)
            {
                island.UpperR = 3.0 + Next() * 2.0;
                island.UpperH = 15.0 + Next() * 15.0;
                island.UpperCrownR = System.Math.Min(crownR * 0.5, 8.0);
            }

            islands.Add(island);
        }

        return new PillarCluster
        {
            Anchor = RawSurfaceHeight(planet, w, cx, cz),
            Islands = islands.ToArray(),
        };
    }

    /// <summary>The stems' rise at a column (the landmark row): a sheer pillar whose top is the cluster anchor plus the
    /// stem height — level across the whole island, whatever the ground under each stem does. 0 off every stem.</summary>
    private double PillarIslandOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetPillarCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return 0.0;
        }

        double best = 0.0;
        int raw = int.MinValue;
        foreach (var island in cluster.Islands)
        {
            foreach (var stem in island.Stems)
            {
                double sx = dx - stem.X, sz = dz - stem.Z;
                double d2 = sx * sx + sz * sz;
                if (d2 >= stem.R * stem.R)
                {
                    continue;
                }

                if (raw == int.MinValue)
                {
                    raw = RawSurfaceHeight(planet, w, worldX, worldZ);
                }

                double top = cluster.Anchor + stem.H;
                if (top <= raw)
                {
                    continue; // the ground already stands higher than this stem would
                }

                double rise = (top - raw) * System.Math.Pow(1.0 - System.Math.Sqrt(d2) / stem.R, 0.2);
                if (rise > best)
                {
                    best = rise;
                }
            }
        }

        return best;
    }

    /// <summary>The bands of the pillar islands at a column: the crown (a lens, thicker at the centre, stalactite
    /// tapers under it, a pond in its middle on a wet world), the fall off the crown rim, the balconies (half-discs
    /// on their bearing, a seep off a few), and the second storey's stem and crown.</summary>
    private int AppendPillarBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetPillarCluster(planet, w, worldX, worldZ, out var cluster, out double dx, out double dz))
        {
            return n;
        }

        var material = w.BandMaterial;
        int ground = int.MinValue;
        int Ground()
        {
            if (ground == int.MinValue)
            {
                ground = SurfaceHeight(planet, worldX, worldZ);
            }

            return ground;
        }

        foreach (var island in cluster.Islands)
        {
            int stemTop = cluster.Anchor + (int)System.Math.Round(island.Stems[0].H);
            double ex = dx - island.CrownX, ez = dz - island.CrownZ;
            AlongAcross(ex, ez, EightDirs[island.Dir], out double along, out double across);
            double rim = 1.0 + (FbmT(w.Seed + 0x9111B, worldX, worldZ, 11.0, octaves: 2) - 0.5) * 0.24; // 0.88..1.12
            double u = along / (island.CrownRx * rim), v = across / (island.CrownRz * rim);
            double q = u * u + v * v;

            if (q <= 1.0 && n < bands.Length)
            {
                int top = stemTop + island.CrownThick;
                int bottom = stemTop - 1 + (int)(q * island.CrownThick * 0.6); // a lens: the underside rises toward the rim
                double sp = FbmT(w.Seed + 0x9111C, worldX, worldZ, 7.0, octaves: 2);
                if (sp > 0.72 && q > 0.3)
                {
                    bottom -= (int)((sp - 0.72) / 0.28 * 6.0); // stalactite tapers under the rim
                }

                bool pondHere = island.Pond && q <= 0.35;
                bands[n++] = new ColumnBand { Bottom = bottom, Top = top, Kind = pondHere ? BandKind.IslandPond : BandKind.Crown, Material = material };

                if (island.TwoStorey && n < bands.Length)
                {
                    double d2 = ex * ex + ez * ez;
                    int upperTop = top + (int)System.Math.Round(island.UpperH);
                    if (d2 <= island.UpperR * island.UpperR)
                    {
                        bands[n++] = new ColumnBand { Bottom = top + 1, Top = upperTop, Kind = BandKind.Rock, Material = material };
                    }

                    if (d2 <= island.UpperCrownR * island.UpperCrownR && n < bands.Length)
                    {
                        bands[n++] = new ColumnBand { Bottom = upperTop, Top = upperTop + 4, Kind = BandKind.Crown, Material = material };
                    }
                }
            }
            else if (island.Pond && q > 1.0 && q <= 1.6 && n < bands.Length)
            {
                // The endless waterfall: the pond spills over the rim on the rolled side — a standing water column
                // just outside the crown, from the ground up to under the crown's underside.
                AlongAcross(ex, ez, EightDirs[island.FallDir], out double fa, out double fc);
                if (fa > 0.0 && System.Math.Abs(fc) <= 1.0)
                {
                    int fallTop = stemTop - 2 + (int)(island.CrownThick * 0.6); // just under the crown's rim underside
                    int fallBottom = Ground() + 1;
                    if (fallBottom < fallTop)
                    {
                        bands[n++] = new ColumnBand { Bottom = fallBottom, Top = fallTop, Kind = BandKind.Waterfall };
                    }
                }
            }

            foreach (var stem in island.Stems)
            {
                double sx = dx - stem.X, sz = dz - stem.Z;
                double dist = System.Math.Sqrt(sx * sx + sz * sz);
                if (dist < stem.R - 1.0)
                {
                    continue; // inside the stem: the ground is the pillar
                }

                foreach (var balcony in stem.Balconies)
                {
                    if (dist > stem.R + balcony.R || n >= bands.Length)
                    {
                        continue;
                    }

                    AlongAcross(sx, sz, EightDirs[balcony.Dir], out double ba, out double bc);
                    if (ba < 0.0)
                    {
                        continue; // the half-disc lies on the bearing's side
                    }

                    int top = cluster.Anchor + (int)System.Math.Round(stem.H * balcony.Frac);
                    bands[n++] = new ColumnBand { Bottom = top - balcony.Thick + 1, Top = top, Kind = BandKind.Crown, Material = material };

                    if (balcony.Seep && dist >= stem.R + balcony.R - 1.5 && System.Math.Abs(bc) <= 0.8 && n < bands.Length)
                    {
                        int seepBottom = Ground() + 1;
                        int seepTop = top - balcony.Thick;
                        if (seepBottom < seepTop)
                        {
                            bands[n++] = new ColumnBand { Bottom = seepBottom, Top = seepTop, Kind = BandKind.Waterfall };
                        }
                    }
                }
            }
        }

        return n;
    }

    // ---------------- test seams ----------------

    /// <summary>The pillar clusters of a world (tests): every island as (stem centres, stem radius, stem top Y, crown
    /// centre, crown half-axes, crown top Y, balcony count, two-storey, pond).</summary>
    internal System.Collections.Generic.List<(int[] StemX, int[] StemZ, double StemR, int StemTop, int CrownX, int CrownZ, double CrownRx, double CrownRz, int CrownTop, int Balconies, bool TwoStorey, bool Pond)>
        PillarIslandsForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int[], int[], double, int, int, int, double, double, int, int, bool, bool)>();
        var w = WonderFor(planet);
        if (!w.PillarIslands)
        {
            return list;
        }

        var (cell, _) = SpectacleCell(planet, PillarCellSize, PillarChance);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / cell));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / cell));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        for (int czI = 0; czI < nz; czI++)
            for (int cxI = 0; cxI < nx; cxI++)
            {
                // The cell's centre column: TryGetHotspot resolves the same cell and hash from any column inside it.
                int x = (int)(cxI * cw + cw * 0.5);
                int z = (int)(czI * ch + ch * 0.5) - period / 2;
                if (!TryGetHotspot(w.Seed ^ PillarSalt, cell, SpectacleCell(planet, PillarCellSize, PillarChance).Chance, PillarMargin,
                        x, z, out _, out double dx, out double dz))
                {
                    continue;
                }

                int cx = WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference);
                int cz = WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference);
                if (!TryGetPillarCluster(planet, w, cx, cz, out var cluster, out _, out _))
                {
                    continue;
                }

                foreach (var island in cluster.Islands)
                {
                    var sxs = new int[island.Stems.Length];
                    var szs = new int[island.Stems.Length];
                    int balconies = 0;
                    for (int i = 0; i < island.Stems.Length; i++)
                    {
                        sxs[i] = WorldConstants.WrapX(cx + (int)System.Math.Round(island.Stems[i].X), _circumference);
                        szs[i] = cz + (int)System.Math.Round(island.Stems[i].Z);
                        balconies += island.Stems[i].Balconies.Length;
                    }

                    int stemTop = cluster.Anchor + (int)System.Math.Round(island.Stems[0].H);
                    list.Add((sxs, szs, island.Stems[0].R, stemTop,
                        WorldConstants.WrapX(cx + (int)System.Math.Round(island.CrownX), _circumference), cz + (int)System.Math.Round(island.CrownZ),
                        island.CrownRx, island.CrownRz, stemTop + island.CrownThick, balconies, island.TwoStorey, island.Pond));
                }
            }

        return list;
    }
}
