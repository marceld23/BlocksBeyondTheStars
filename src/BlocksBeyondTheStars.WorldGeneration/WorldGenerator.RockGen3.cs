// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 2 — the rock landforms: slot canyons, arêtes and tooth rows, rock
/// gates and mountain halls (both worm families), desert pavement and the petrified-dune skin. Every family
/// is a hotspot-cell feature on the #477 recipe and gates on a profile flag <c>WonderFor</c> only sets from
/// generation 3, so the classic table order and every older world are untouched.
/// (partial of <see cref="WorldGenerator"/>)</summary>
public sealed partial class WorldGenerator
{
    // ================= Slot canyons =================
    // A crack you can walk into and not turn around in: the crevasse frame at rock scale, one gentle bend.
    private const double SlotCellSize = 700.0;
    private const double SlotChance = 0.35;
    private const double SlotMaxHalfLen = 70.0;
    private const long SlotSalt = 0x0BAD30;

    /// <summary>Dry rock country — the same land predicate the generation-1 rows use, plus a wind- or
    /// butte-carved surface and little water to soften it.</summary>
    private bool HasSlotCanyons(PlanetType planet)
        => HasMassifs(planet)
           && (planet.HasTag(TerrainTag.Buttes) || planet.HasTag(TerrainTag.Wind))
           && WaterAbundanceOf(planet) <= 0.35;

    /// <summary>The slot's (negative) offset at a column: a 2–4-wide crack 18–35 deep, its walls near-vertical
    /// and its course bent by a single arc so it never reads as a ruled line.</summary>
    private double SlotCanyonOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ SlotSalt, SlotCellSize, SlotChance, SlotMaxHalfLen + 12.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * (SlotMaxHalfLen - 30.0);
        double halfWidth = 1.0 + ((h >> 56) & 0xFF) / 255.0; // 1..2 — the narrowest gorge in the generator
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        if (System.Math.Abs(along) > halfLen)
        {
            return 0.0;
        }

        // One gentle S over the length, so the walk through it turns.
        double bend = 3.0 * System.Math.Sin(along / halfLen * System.Math.PI);
        double across = -dx * sin + dz * cos - bend;
        if (System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        double depth = 18.0 + ((h >> 36) & 0x3FF) / 1023.0 * 17.0; // 18..35
        double wSpan = System.Math.Abs(across) / halfWidth;
        double wall = wSpan <= 0.7 ? 1.0 : Smooth01((1.0 - wSpan) / 0.3);
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double taper = endT >= 0.2 ? 1.0 : Smooth01(endT / 0.2);
        return -depth * wall * taper;
    }

    // ================= Arêtes and tooth rows =================
    private const double AreteCellSize = 900.0;
    private const double AreteChance = 0.30;
    private const double AreteMaxHalfLen = 130.0;
    private const long AreteSalt = 0x0BAD31;

    private const double ToothCellSize = 1100.0;
    private const double ToothChance = 0.25;
    private const int ToothMaxCount = 7;
    private const long ToothSalt = 0x0BAD32;

    /// <summary>High relief: a mountain-styled world, or one whose amplitude alone makes real ridges.</summary>
    private bool HasAretes(PlanetType planet, string[] styles)
        => HasMassifs(planet) && (System.Array.IndexOf(styles, "mountains") >= 0 || planet.Amplitude >= 24);

    /// <summary>A knife ridge: a long thin crest with both flanks falling away at once, saw-toothed along its
    /// length so the skyline reads jagged rather than milled.</summary>
    private double AreteOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ AreteSalt, AreteCellSize, AreteChance, AreteMaxHalfLen + 12.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 60.0 + ((h >> 26) & 0x3FF) / 1023.0 * (AreteMaxHalfLen - 60.0);
        double halfWidth = 3.0 + ((h >> 56) & 0xFF) / 255.0 * 3.0; // 3..6
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;
        if (System.Math.Abs(along) > halfLen || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        double rise = 25.0 + ((h >> 36) & 0x3FF) / 1023.0 * 25.0; // 25..50
        rise = System.Math.Min(rise, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        if (rise <= 0.0)
        {
            return 0.0;
        }

        // Saw-tooth along the crest — a ridged sample of the along-distance, never of the column itself, so
        // the notches run across the ridge instead of pitting it.
        double teeth = FbmT(seed + AreteSalt, along, 0.0, 9.0, octaves: 2);
        double crest = 0.7 + 0.3 * (1.0 - System.Math.Abs(teeth * 2.0 - 1.0));
        double wSpan = System.Math.Abs(across) / halfWidth;
        double flank = wSpan <= 0.35 ? 1.0 : Smooth01((1.0 - wSpan) / 0.65);
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double taper = endT >= 0.2 ? 1.0 : Smooth01(endT / 0.2);
        return rise * crest * flank * taper;
    }

    /// <summary>A row of rock teeth: four to seven cones in a line, spaced so each keeps its own summit.</summary>
    private double ToothRowOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ ToothSalt, ToothCellSize, ToothChance, 140.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        int count = 4 + (int)((h >> 26) & 0x3);                      // 4..7
        double spacing = 20.0 + ((h >> 30) & 0xFF) / 255.0 * 10.0;   // 20..30
        double radius = 8.0 + ((h >> 40) & 0xFF) / 255.0 * 8.0;      // 8..16
        double height = 30.0 + ((h >> 48) & 0xFF) / 255.0 * 30.0;    // 30..60
        height = System.Math.Min(height, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        if (height <= 0.0)
        {
            return 0.0;
        }

        double best = 0.0;
        double first = -(count - 1) * 0.5 * spacing;
        for (int i = 0; i < count; i++)
        {
            double t = first + i * spacing;
            double ox = dx - t * cos;
            double oz = dz - t * sin;
            double dist = System.Math.Sqrt(ox * ox + oz * oz);
            if (dist >= radius)
            {
                continue;
            }

            double rise = height * System.Math.Pow(1.0 - dist / radius, 1.6);
            if (rise > best)
            {
                best = rise;
            }
        }

        return best;
    }

    // ================= Desert pavement =================
    private const long PavementSalt = 0x0BAD34;
    private const long PavementDitherSalt = 0x0BAD3A;

    /// <summary>Wind-swept dry flats: the stones the wind left behind when it took the sand.</summary>
    private bool HasDesertPavement(PlanetType planet)
        => HasMassifs(planet) && planet.HasTag(TerrainTag.Wind) && WaterAbundanceOf(planet) <= 0.15;

    /// <summary>A sheet of close-packed stones on level ground — scree with a third of its cells bare rock, so
    /// the surface reads as a pavement rather than a slope's rubble. Topsoil only.</summary>
    private BlockId? DesertPavementPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int fillToY)
    {
        fillToY = int.MinValue;
        if (FbmT(w.Seed + PavementSalt, worldX, worldZ, 420.0, octaves: 2) <= 0.62)
        {
            return null;
        }

        if (SurfaceSlope(planet, worldX, worldZ) > 1)
        {
            return null; // pavement is a flat-ground form; a slope keeps its own skin
        }

        var scree = _content.GetBlock("scree")?.NumericId ?? BlockId.Air;
        var stone = _content.GetBlock("stone")?.NumericId ?? BlockId.Air;
        if (scree.IsAir)
        {
            return null;
        }

        bool bare = !stone.IsAir && Noise.Value01(w.Seed + PavementDitherSalt,
            WorldConstants.WrapX(worldX, _circumference), 0, Wz(worldZ)) < 0.33;
        return bare ? stone : scree;
    }

    // ================= Petrified dunes =================
    private const long PetrifiedSalt = 0x0BAD39;

    /// <summary>True on a world that rolled the petrified-dune style at all.</summary>
    private static bool HasPetrifiedDunes(string[] styles) => System.Array.IndexOf(styles, "petrified-dunes") >= 0;

    /// <summary>The dune sea's rock skin: sandstone through the top of a crest, so a cut face shows the
    /// cross-bedding the style quantised into decks. Only on the crests — the troughs stay sand.</summary>
    private BlockId? PetrifiedDunePaint(WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double d = GrainFbm(w.Seed + PetrifiedSalt, w.Grain, worldX, worldZ, w.Scale * 0.45, octaves: 2);
        if (1.0 - System.Math.Abs(d * 2.0 - 1.0) <= 0.5)
        {
            return null;
        }

        var sandstone = _content.GetBlock("sandstone")?.NumericId ?? BlockId.Air;
        if (sandstone.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - 12;
        return sandstone;
    }

    // ================= Rock gates (worm family) =================
    // A hole clean through a table mountain's wall. It is neither a landmark row (a classic row already owns
    // every column of the table, and the loop takes the first non-zero overlay) nor a band (a band ADDS solid
    // where the gate needs air) — it is exactly what it looks like: a short horizontal tunnel.
    private const long RockGateSalt = 0x0BAD35;

    private bool HasRockGates(PlanetType planet) => HasTableMountains(planet);

    /// <summary>One straight capsule through the steep upper wall of the table this cell grew, at a rolled
    /// bearing. Empty when the cell's table is too small to carry a gate.</summary>
    private TunnelSeg[] RockGateSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        // The table's radius, re-derived from the same bits TableMountainOffset reads (its height roll sits in
        // bits 26–35 and is not needed here: the gate is cut at the wall's FOOT).
        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ButteMaxRadius - 40.0);
        if (radius < 60.0)
        {
            return System.Array.Empty<TunnelSeg>(); // a small butte keeps its wall whole
        }

        ulong g = h * 0x9E3779B97F4A7C15UL;
        double angle = ((g >> 12) & 0x3FF) / 1023.0 * System.Math.PI * 2.0;
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double halfHeight = 2.5 + ((g >> 24) & 0xFF) / 255.0 * 2.0; // a 5–9-tall opening

        // The wall band is the outer 30 % of the radius (TableMountainOffset: t < 0.30 is the talus-to-cliff
        // ramp). The gate runs from just inside that band out past the foot, at the height of the foot itself
        // — an opening you walk through, not a window up the cliff. The foot is the RAW ground under the
        // table's centre (the table rises from wherever the swell put it, not from BaseHeight).
        double inner = radius * 0.62;
        double outer = radius + 6.0;
        double y = RawSurfaceHeight(planet, w, centreX, centreZ) + halfHeight + 1.0;
        return new[]
        {
            new TunnelSeg(inner * cos, y, inner * sin, outer * cos, y, outer * sin, halfHeight),
        };
    }

    // ================= Mountain halls (worm family) =================
    // A massif you can walk into: a short cave system inside the mountain, one mouth on a flank and one
    // skylight through the summit.
    private const long MountainHallSalt = 0x0BAD36;

    private bool HasMountainHalls(PlanetType planet) => HasMassifs(planet) && planet.CaveThreshold > 0.0;

    private static readonly System.Collections.Generic.Dictionary<(long, ulong), TunnelSeg[]> _hallSegCache = new();
    private static readonly object _hallSegLock = new object();

    /// <summary>The hall polyline inside the massif this cell grew — cached per cell like the classic worms.</summary>
    private TunnelSeg[] MountainHallSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        var key = (w.Seed ^ MountainHallSalt, h);
        lock (_hallSegLock)
        {
            if (_hallSegCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        // The massif's own rolls, re-derived from the bits MassifOffset reads (clamp included), on the raw
        // ground under its centre — the mountain rises from the swell, not from BaseHeight.
        double radius = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MassifMaxRadius - 150.0);
        double massifHeight = 120.0 + ((h >> 26) & 0x3FF) / 1023.0 * 100.0;
        massifHeight = System.Math.Min(massifHeight, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        double ground = RawSurfaceHeight(planet, w, centreX, centreZ);

        ulong s = (h ^ (ulong)MountainHallSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        int segs = 3 + (int)(Next() * 3); // 3..5
        double py = ground + massifHeight * 0.3;
        double px = 0.0, pz = 0.0;
        double vx = Next() * 2.0 - 1.0, vz = Next() * 2.0 - 1.0;
        double vlen = System.Math.Sqrt(vx * vx + vz * vz);
        if (vlen < 0.2) { vx = 1.0; vz = 0.0; vlen = 1.0; }
        vx /= vlen;
        vz /= vlen;

        var list = new System.Collections.Generic.List<TunnelSeg>(segs + 2);

        // The mouth: out through the flank at the hall's own level, so the entrance is a hole in the mountain.
        double mouthR = radius * 0.55 + 12.0;
        list.Add(new TunnelSeg(0.0, py, 0.0, mouthR * vx, py - 6.0, mouthR * vz, 4.0));

        for (int i = 0; i < segs; i++)
        {
            double len = 20.0 + Next() * 22.0;
            double vy = (Next() - 0.5) * 0.25;
            double qx = px + vx * len, qz = pz + vz * len, qy = py + vy * len;
            double lim = radius * 0.6;
            qx = System.Math.Clamp(qx, -lim, lim);
            qz = System.Math.Clamp(qz, -lim, lim);
            double r = 5.0 + Next() * 4.0; // 5..9 — a hall, not a crawl
            list.Add(new TunnelSeg(px, py, pz, qx, qy, qz, r));
            px = qx; py = qy; pz = qz;
            double turn = (Next() - 0.5) * 1.1;
            double nvx = vx + turn * -vz, nvz = vz + turn * vx;
            double nl = System.Math.Sqrt(nvx * nvx + nvz * nvz);
            vx = nvx / nl;
            vz = nvz / nl;
        }

        // One shaft to the daylight above the summit.
        list.Add(new TunnelSeg(px, py, pz, px, ground + massifHeight + 20.0, pz, 1.7));

        var arr = list.ToArray();
        lock (_hallSegLock)
        {
            if (_hallSegCache.Count >= 512)
            {
                _hallSegCache.Clear();
            }

            _hallSegCache[key] = arr;
        }

        return arr;
    }

    // ================= Rainbow strata (Bunte Berge) =================
    // Layered colour on every cut face of hoodoo-and-butte country: inside a broad region the paint fill
    // claims the column forty deep and CYCLES four blocks in 3-thick bands parallel to the surface, so a
    // cliff, a canyon wall or a mined shaft all show the same stripes. The cycle is the reference consumer
    // of the paint-cycle extension (ColumnProfile.PaintCycle).
    private const long RainbowRegionSalt = 0x0BAD33;
    private const int RainbowFillDepth = 40;
    private const int RainbowBandThickness = 3;

    private bool HasRainbowStrata(PlanetType planet)
        => HasMassifs(planet) && planet.HasTag(TerrainTag.Buttes) && planet.HasTag(TerrainTag.Hoodoos);

    private bool RainbowRegionAt(WonderProfile w, int worldX, int worldZ)
        => FbmT(w.Seed + RainbowRegionSalt, worldX, worldZ, 460.0, octaves: 2) > 0.58;

    /// <summary>The four bands, top first: the order a cut face shows from the surface down.</summary>
    private BlockId[]? RainbowCycle()
    {
        var sandstone = _content.GetBlock("sandstone")?.NumericId ?? BlockId.Air;
        var granite = _content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        var salt = _content.GetBlock("salt")?.NumericId ?? BlockId.Air;
        var basalt = _content.GetBlock("basalt")?.NumericId ?? BlockId.Air;
        if (sandstone.IsAir || granite.IsAir || salt.IsAir || basalt.IsAir)
        {
            return null;
        }

        return new[] { sandstone, granite, salt, basalt };
    }

    /// <summary>The surface block of a rainbow column (the first band) with the fill depth; the cycle below
    /// comes from <see cref="RainbowStrataCycle"/>. Rock ground only — a soil biome keeps its topsoil.</summary>
    private BlockId? RainbowStrataPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!RainbowRegionAt(w, worldX, worldZ))
        {
            return null;
        }

        var cycle = RainbowCycle();
        if (cycle is null)
        {
            return null;
        }

        fillToY = surfaceY - RainbowFillDepth;
        return cycle[0];
    }

    private BlockId[]? RainbowStrataCycle(WonderProfile w, int worldX, int worldZ)
        => RainbowRegionAt(w, worldX, worldZ) ? RainbowCycle() : null;

    /// <summary>The block a rainbow fill puts <paramref name="depthBelowSurface"/> cells under the surface —
    /// what the y-loop computes, exposed for the tests.</summary>
    internal static BlockId CycleBlockAt(BlockId[] cycle, int depthBelowSurface)
        => cycle[(depthBelowSurface / RainbowBandThickness) % cycle.Length];

    // ---------------- test seams ----------------

    /// <summary>A generation-3 rock family's height overlay at a column (tests).</summary>
    internal double RockOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return name switch
        {
            "slot-canyon" => w.SlotCanyons ? SlotCanyonOffset(w.Seed, worldX, worldZ) : 0.0,
            "arete" => w.Aretes ? AreteOffset(planet, w.Seed, worldX, worldZ) : 0.0,
            "tooth-row" => w.ToothRows ? ToothRowOffset(planet, w.Seed, worldX, worldZ) : 0.0,
            _ => throw new System.ArgumentException($"unknown rock family '{name}'", nameof(name)),
        };
    }
}
