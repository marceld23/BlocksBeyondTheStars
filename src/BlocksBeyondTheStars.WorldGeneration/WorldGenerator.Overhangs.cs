// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>extra column bands: sky-island tiers, arches, sea stacks, hoodoos, cenotes, crevasses (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    // ================= Overhang landforms (#705/#706/#707) =================
    // The engine's terrain is a strict heightfield; the floating-island slab proved a SECOND solid band
    // per column works end to end. #705 generalises that: a column may carry up to a few extra bands
    // (island tiers, arch bars, pillar caps, cenote lips, island waterfalls), each filled by Generate.

    /// <summary>What an extra column band is made of (#705). Island reproduces the classic sky-island
    /// fill bit-for-bit; IslandPond is an island whose top cell is water; Cap is bare rock (arch bars,
    /// hoodoo/sea-stack caps, cenote lips); Waterfall is a standing column of water.</summary>
    public enum BandKind : byte
    {
        Island = 0,
        IslandPond = 1,
        Cap = 2,
        Waterfall = 3,
    }

    /// <summary>One extra solid/fluid band of a column (#705), in inclusive world-Y coordinates.</summary>
    public struct ColumnBand
    {
        public int Bottom;
        public int Top;
        public BandKind Kind;
    }

    /// <summary>Max extra bands a column can carry (#705): 3 island tiers + a cap + a waterfall.</summary>
    public const int MaxColumnBands = 6;

    /// <summary>Collects every extra band covering this column (#705): sky-island tiers (with ponds,
    /// stalactites and edge waterfalls, #707), arch bars, sea-stack and hoodoo caps and cenote lips
    /// (#706/#707). Returns the band count written into <paramref name="bands"/>. The single source of
    /// truth — Generate, placement queries and tests all agree on what hangs where.</summary>
    public int GetExtraBands(PlanetType planet, int worldX, int worldZ, System.Span<ColumnBand> bands)
    {
        int n = 0;
        var w = WonderFor(planet); // #712: gates + seed resolved once per world
        long seed = w.Seed;
        if (planet.FloatingIslands)
        {
            int tiers = w.SkyTiers;
            for (int t = 0; t < tiers && n < bands.Length; t++)
            {
                if (FloatingIslandTier(planet, seed, t, worldX, worldZ, out int top, out int bottom, out double it))
                {
                    var kind = BandKind.Island;
                    double pond = FbmT(seed + 0x5C1A7F + t * 0x1010, worldX, worldZ, planet.TerrainScale * 0.8, octaves: 2);
                    if (pond > 0.62 && it > 0.55)
                    {
                        kind = BandKind.IslandPond; // a meadow pool sunk into the island top (#707)
                    }

                    bands[n++] = new ColumnBand { Bottom = bottom, Top = top, Kind = kind };

                    // Endless waterfall (#707): island ponds spill over the rim — a standing water column
                    // from just under the island down to the ground (or the sea it plunges into).
                    if (pond > 0.62 && it > 0.02 && it <= 0.10 && n < bands.Length)
                    {
                        int ground = SurfaceHeight(planet, worldX, worldZ);
                        if (ground + 1 < bottom - 1)
                        {
                            bands[n++] = new ColumnBand { Bottom = ground + 1, Top = bottom - 1, Kind = BandKind.Waterfall };
                        }
                    }
                }
            }
        }

        if (n < bands.Length && w.Arches && TryGetArchBar(planet, seed, worldX, worldZ, out int abLo, out int abHi))
        {
            bands[n++] = new ColumnBand { Bottom = abLo, Top = abHi, Kind = BandKind.Cap };
        }

        if (n < bands.Length && w.SeaStacks && TryGetSeaStackCap(planet, seed, worldX, worldZ, out int scLo, out int scHi))
        {
            bands[n++] = new ColumnBand { Bottom = scLo, Top = scHi, Kind = BandKind.Cap };
        }

        if (n < bands.Length && w.Hoodoos && TryGetHoodooCap(planet, seed, worldX, worldZ, out int hcLo, out int hcHi))
        {
            bands[n++] = new ColumnBand { Bottom = hcLo, Top = hcHi, Kind = BandKind.Cap };
        }

        if (n < bands.Length && w.Cenotes && TryGetCenoteLip(planet, seed, worldX, worldZ, out int clLo, out int clHi))
        {
            bands[n++] = new ColumnBand { Bottom = clLo, Top = clHi, Kind = BandKind.Cap };
        }

        return n;
    }

    /// <summary>True when any band-producing feature can exist on this world at all — Generate's cheap
    /// whole-chunk gate so classic worlds pay nothing for #705.</summary>
    public bool HasExtraBands(PlanetType planet) => WonderFor(planet).AnyBands;

    // --- Multi-tier skylands (#707): floating worlds roll 1–3 island tiers; tier 0 is the classic band
    // (same seeds and shaping, so existing sky worlds keep their islands), upper tiers stack ~36 blocks
    // apart. All tiers grow rocky stalactite tapers where a high-frequency mask spikes. ---
    private static int SkyTiersFor(long seed) => 1 + (int)(Noise.Hash(seed ^ 0x5C1A7E, 3, 3, 3) % 3UL);

    private bool FloatingIslandTier(PlanetType planet, long seed, int tier, int worldX, int worldZ,
        out int top, out int bottom, out double t)
    {
        top = int.MinValue;
        bottom = int.MaxValue;
        t = 0.0;
        double im = FbmT(seed + 0x15A4D + tier * 0x1010, worldX, worldZ, planet.TerrainScale * 1.4, octaves: 3);
        if (im <= 0.60)
        {
            return false;
        }

        t = (im - 0.60) / 0.40;
        double alt = FbmT(seed + 0x15A4E + tier * 0x1010, worldX, worldZ, planet.TerrainScale * 3.0, octaves: 2);
        int center = planet.BaseHeight + 28 + tier * 36 + (int)((alt - 0.5) * 24.0);
        int half = 2 + (int)(t * 8.0);
        top = center + half;
        bottom = center - half - (int)(t * 6.0);

        // Stalactites (#707): rocky icicles hanging beneath the islands.
        double sp = FbmT(seed + 0x57A1AC + tier, worldX, worldZ, 9.0, octaves: 2);
        if (sp > 0.70)
        {
            bottom -= (int)((sp - 0.70) / 0.30 * 8.0);
        }

        return true;
    }

    // --- Natural arches & rock bridges (#706): two steep abutment pillars joined by a solid bar band —
    // the first freestanding overhang landform. Dry rocky worlds (the table-mountain gate). ---
    private const double ArchCellSize = 1900.0;
    private const double ArchChance = 0.45;        // visibility tuning 2026-08-03
    private const double ArchMargin = 60.0;

    private bool HasArches(PlanetType planet) => HasTableMountains(planet);

    private bool TryGetArchGeometry(long seed, int worldX, int worldZ,
        out double along, out double across, out double halfSpan, out double abutR, out double height,
        out double barHalfW, out double barThick, out ulong hash, out double dx, out double dz)
    {
        along = across = halfSpan = abutR = height = barHalfW = barThick = 0.0;
        if (!TryGetHotspot(seed ^ 0xA2C4B0, ArchCellSize, ArchChance, ArchMargin,
                worldX, worldZ, out hash, out dx, out dz))
        {
            return false;
        }

        int di = (int)((hash >> 20) & 0x7);
        double dirLen = System.Math.Sqrt((double)(ChainDirX[di] * ChainDirX[di] + ChainDirZ[di] * ChainDirZ[di]));
        double ux = ChainDirX[di] / dirLen, uz = ChainDirZ[di] / dirLen;
        along = dx * ux + dz * uz;
        across = -dx * uz + dz * ux;
        halfSpan = 8.0 + ((hash >> 24) & 0x7);         // 8..15
        abutR = 4.0 + ((hash >> 28) & 0x3);            // 4..7
        height = 14.0 + ((hash >> 32) & 0xF) * 0.8;    // 14..26
        barHalfW = 2.5 + ((hash >> 36) & 0x3) * 0.7;   // 2.5..4.6
        barThick = 2.0 + ((hash >> 38) & 0x1);         // 2..3
        return true;
    }

    /// <summary>The arch abutments' steep ground rise at a column (#706), 0 elsewhere.</summary>
    private double ArchGroundOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetArchGeometry(seed, worldX, worldZ, out double along, out double across,
                out double halfSpan, out double abutR, out double height, out _, out _, out _, out _, out _))
        {
            return 0.0;
        }

        double da = System.Math.Min(
            System.Math.Sqrt((along + halfSpan) * (along + halfSpan) + across * across),
            System.Math.Sqrt((along - halfSpan) * (along - halfSpan) + across * across));
        if (da > abutR)
        {
            return 0.0;
        }

        return height * System.Math.Pow(1.0 - da / abutR, 0.25); // near-vertical pillar, flat-ish top
    }

    /// <summary>The arch's bridging bar band at a column (#706): a solid rock beam spanning the two
    /// abutments at their top height, anchored to the pre-landmark ground under the feature centre.</summary>
    private bool TryGetArchBar(PlanetType planet, long seed, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetArchGeometry(seed, worldX, worldZ, out double along, out double across,
                out double halfSpan, out _, out double height, out double barHalfW, out double barThick, out _,
                out double dx, out double dz))
        {
            return false;
        }

        if (System.Math.Abs(along) > halfSpan || System.Math.Abs(across) > barHalfW)
        {
            return false;
        }

        // The bar sags slightly toward mid-span, like a real rock bridge; it anchors to the pre-landmark
        // ground under the feature CENTRE so the beam is level regardless of the terrain under each end.
        double sag = 1.5 * (1.0 - System.Math.Abs(along) / halfSpan);
        int anchor = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        top = anchor + (int)System.Math.Round(height - sag);
        bottom = top - (int)barThick + 1;
        return true;
    }

    // --- Sea stacks (#706): pillars standing in the surf just off low coasts, some mushroom-capped. ---
    private const double SeaStackCellSize = 260.0;
    private const double SeaStackChance = 0.25;    // visibility tuning 2026-08-03
    private const double SeaStackMargin = 16.0;

    private bool HasSeaStacks(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return hasAir && waterAb >= 0.5;
    }

    private bool TryGetSeaStack(PlanetType planet, long seed, int worldX, int worldZ,
        out double dist, out double stemR, out double rise, out int anchor, out ulong hash)
    {
        dist = stemR = rise = 0.0;
        anchor = 0;
        if (!TryGetHotspot(seed ^ 0x5EA57ACC, SeaStackCellSize, SeaStackChance, SeaStackMargin,
                worldX, worldZ, out hash, out double dx, out double dz))
        {
            return false;
        }

        // #712 perf: reject by DISTANCE before the expensive probes — every column of a stack-bearing
        // cell used to pay a 4-octave FBM plus a full RawSurfaceHeight anchor even far from the stack.
        // Callers only ever act within stemR + 2 (the cap), so this early-out is behaviour-preserving.
        dist = System.Math.Sqrt(dx * dx + dz * dz);
        stemR = 2.0 + ((hash >> 16) & 0x3);        // 2..5
        if (dist > stemR + 2.0)
        {
            return false;
        }

        // Stacks only rise from LOW ground (coasts + shallows): probe the base swell at the stack centre.
        int cx = worldX - (int)System.Math.Round(dx);
        int cz = worldZ - (int)System.Math.Round(dz);
        double hswell = (FbmT(seed, cx, cz, planet.TerrainScale, octaves: 4) - 0.5) * 2.0;
        if (hswell > -0.05)
        {
            return false;
        }

        rise = 14.0 + ((hash >> 20) & 0x7);        // 14..21
        anchor = RawSurfaceHeight(planet, cx, cz);
        return true;
    }

    /// <summary>The sea stack's stem rise at a column (#706), 0 elsewhere.</summary>
    private double SeaStackGroundOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetSeaStack(planet, seed, worldX, worldZ, out double dist, out double stemR, out double rise, out _, out _)
            || dist > stemR)
        {
            return 0.0;
        }

        return rise * System.Math.Pow(1.0 - dist / stemR, 0.2); // a sheer pillar
    }

    /// <summary>The sea stack's overhanging mushroom cap (#706): a rock disc two blocks wider than the
    /// stem, sitting on the pillar top. Only ~60 % of stacks carry one — the rest stay sheer.</summary>
    private bool TryGetSeaStackCap(PlanetType planet, long seed, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetSeaStack(planet, seed, worldX, worldZ, out double dist, out double stemR, out double rise,
                out int anchor, out ulong h)
            || ((h >> 28) & 0xFF) >= 154 // ~60 % capped
            || dist > stemR + 2.0)
        {
            return false;
        }

        top = anchor + (int)System.Math.Round(rise) + 1;
        bottom = top - 1 - (int)((h >> 36) & 0x1);
        return true;
    }

    // --- Hoodoos / fairy chimneys (#706): dense fields of thin spires balancing wider caprocks. ---
    private const double HoodooCellSize = 24.0;
    private const double HoodooChance = 0.5;
    private const double HoodooRegionThreshold = 0.55; // visibility tuning 2026-08-03: broader fields

    private bool HasHoodoos(PlanetType planet) // #1644: the `hoodoos` tag replaces the style list
        => planet.HasTag(TerrainTag.Hoodoos) && !planet.Void && !planet.Cratered && !_crateredWorld;

    private bool HoodooRegionAt(PlanetType planet, long seed, int worldX, int worldZ)
        => FbmT(seed + 0x400D00, worldX, worldZ, planet.TerrainScale * 1.6, octaves: 2) > HoodooRegionThreshold;

    private bool TryGetHoodoo(PlanetType planet, long seed, int worldX, int worldZ,
        out double dist, out double rise, out int anchor, out ulong hash)
    {
        dist = rise = 0.0;
        anchor = 0;
        hash = 0;
        // #712 perf: hotspot + distance first — in a hoodoo REGION every column used to pay the region
        // FBM plus a full RawSurfaceHeight anchor; callers only act within the cap radius 2.6, so the
        // cheap rejects go first and the anchor is computed last. Behaviour-preserving (conjunctive gates).
        if (!TryGetHotspot(seed ^ 0x400D01, HoodooCellSize, HoodooChance, 4.0,
                worldX, worldZ, out hash, out double dx, out double dz))
        {
            return false;
        }

        dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > 2.6 || !HoodooRegionAt(planet, seed, worldX, worldZ))
        {
            return false;
        }

        rise = 6.0 + ((hash >> 16) & 0x7);  // 6..13
        anchor = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        return true;
    }

    /// <summary>The hoodoo's thin stem rise at a column (#706), 0 elsewhere.</summary>
    private double HoodooGroundOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHoodoo(planet, seed, worldX, worldZ, out double dist, out double rise, out _, out _)
            || dist > 1.6)
        {
            return 0.0;
        }

        return rise * System.Math.Pow(1.0 - dist / 1.6, 0.2);
    }

    /// <summary>The hoodoo's caprock band (#706): a wider dark disc balanced on the stem.</summary>
    private bool TryGetHoodooCap(PlanetType planet, long seed, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetHoodoo(planet, seed, worldX, worldZ, out double dist, out double rise, out int anchor, out ulong h)
            || dist > 2.6)
        {
            return false;
        }

        top = anchor + (int)System.Math.Round(rise) + 1;
        bottom = top - (int)((h >> 20) & 0x1); // 1–2 thick
        return true;
    }

    /// <summary>Whether any overhang landmark's GROUND geometry (abutments, stems) can exist here (#706).</summary>
    private bool HasOverhangLandmarks(PlanetType planet)
        => HasArches(planet) || HasSeaStacks(planet) || HasHoodoos(planet);

    /// <summary>The overhang landmarks' ground rise at a column (#706): arch abutments, then sea-stack
    /// stems, then hoodoo stems — first hit wins (they share the one-landmark-per-column rule).</summary>
    private double OverhangGroundOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        double o = w.Arches ? ArchGroundOffset(w.Seed, worldX, worldZ) : 0.0;
        if (o == 0.0 && w.SeaStacks)
        {
            o = SeaStackGroundOffset(planet, w.Seed, worldX, worldZ);
        }

        if (o == 0.0 && w.Hoodoos)
        {
            o = HoodooGroundOffset(planet, w.Seed, worldX, worldZ);
        }

        return o;
    }

    // --- Cenotes (#707): sudden circular shafts dropping 30–80 blocks from green ground straight into
    // the cave layer — with an overhanging ring lip, and a turquoise pool where the world is wet. ---
    private const double CenoteCellSize = 800.0;
    private const double CenoteChance = 0.45;      // visibility tuning 2026-08-03
    private const double CenoteMaxRadius = 20.0;   // …and bigger, so a shaft reads as a place, not a pothole

    private bool HasCenotes(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands
            || planet.CaveThreshold <= 0.0)
        {
            return false;
        }

        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return hasAir && waterAb > 0.3;
    }

    private bool TryGetCenote(PlanetType planet, long seed, int worldX, int worldZ,
        out double dist, out double radius, out double depth, out int anchor, out ulong hash)
    {
        dist = radius = depth = 0.0;
        anchor = 0;
        if (!TryGetHotspot(seed ^ 0x0CE07E, CenoteCellSize, CenoteChance, CenoteMaxRadius + 6.0,
                worldX, worldZ, out hash, out double dx, out double dz))
        {
            return false;
        }

        dist = System.Math.Sqrt(dx * dx + dz * dz);
        radius = 9.0 + ((hash >> 16) & 0x7) * 1.5;  // 9..19.5
        depth = 30.0 + ((hash >> 20) & 0x3F) * 0.8; // 30..80
        if (dist > radius)
        {
            // #712 perf: no caller acts outside the shaft radius — skip the RawSurfaceHeight anchor for
            // the vast majority of columns in a cenote-bearing cell. Behaviour-preserving.
            return false;
        }

        anchor = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        return true;
    }

    /// <summary>The cenote shaft's (negative) offset at a column (#707): near-vertical walls to a flat
    /// floor. Caves open into the shaft walls for free — the mesher exposes every cave cell the shaft
    /// face touches.</summary>
    private double CenoteOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetCenote(planet, seed, worldX, worldZ, out double dist, out double radius, out double depth, out _, out _)
            || dist > radius)
        {
            return 0.0;
        }

        double t = System.Math.Min(1.0, (1.0 - dist / radius) / 0.15);
        return -depth * (t * t * (3.0 - 2.0 * t));
    }

    /// <summary>The cenote's overhanging ring lip (#707): a rock band at the original ground level
    /// reaching over the shaft's outer edge.</summary>
    private bool TryGetCenoteLip(PlanetType planet, long seed, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetCenote(planet, seed, worldX, worldZ, out double dist, out double radius, out _, out int anchor, out _)
            || dist < radius * 0.82 || dist > radius * 0.98)
        {
            return false;
        }

        top = anchor;
        bottom = anchor - 1;
        return true;
    }

    /// <summary>The cenote pool (#707): on wet worlds ~60 % of cenotes hold water over their floor.
    /// Outputs the pool's flat top Y (anchored to the feature centre), for Generate's fluid override.</summary>
    public bool TryGetCenotePool(PlanetType planet, int worldX, int worldZ, out int poolTopY)
    {
        poolTopY = 0;
        var w = WonderFor(planet); // #712
        if (!w.Cenotes)
        {
            return false;
        }

        long seed = w.Seed;
        if (!TryGetCenote(planet, seed, worldX, worldZ, out double dist, out double radius, out double depth,
                out int anchor, out ulong h)
            || dist >= radius || ((h >> 40) & 0xFF) >= 154)
        {
            return false;
        }

        double waterAb = planet.WaterAbundance ?? 0.55;
        if (waterAb < 0.45)
        {
            return false;
        }

        poolTopY = anchor - (int)depth + 4 + (int)((h >> 48) & 0x3); // 4..7 deep pool over the floor
        return true;
    }

    // --- Crevasse fields (#709): narrow deep slits across the ice of the coldest worlds. ---
    private const double CrevasseCellSize = 500.0;
    private const double CrevasseChance = 0.30;
    private const double CrevasseMaxHalfLen = 80.0;

    private bool HasCrevasses(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        return !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase)
            && planet.BaseTemperature <= -8.0;
    }

    /// <summary>The crevasse's (negative) offset at a column (#709): a thin, steep slit 8–18 deep.</summary>
    private double CrevasseOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x0C2EFA55, CrevasseCellSize, CrevasseChance,
                CrevasseMaxHalfLen + 8.0, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * (CrevasseMaxHalfLen - 30.0);
        double halfWidth = 1.5 + ((h >> 56) & 0xFF) / 255.0 * 2.0; // 1.5..3.5
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;
        if (System.Math.Abs(along) > halfLen || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        ulong h2 = h * 0x9E3779B97F4A7C15UL;
        double depth = 8.0 + ((h2 >> 20) & 0x3FF) / 1023.0 * 10.0; // 8..18
        double w = 1.0 - System.Math.Abs(across) / halfWidth;
        double wall = w >= 0.35 ? 1.0 : (w / 0.35) * (w / 0.35) * (3.0 - 2.0 * (w / 0.35));
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double taper = endT >= 0.2 ? 1.0 : (endT / 0.2) * (endT / 0.2) * (3.0 - 2.0 * (endT / 0.2));
        return -depth * wall * taper;
    }
}
