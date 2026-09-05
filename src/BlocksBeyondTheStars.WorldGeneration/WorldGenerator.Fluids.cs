// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>surface water and lava: sea fluid, world floor, ponds, beaches, rivers and the fluid queries (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>The world's surface sea level (world Y) — the height water/lava fills basins to, or
    /// int.MinValue if the world has no surface fluid. Used to keep aquatic creatures in the water.</summary>
    public int SeaLevel(PlanetType planet) => ResolveSeaFluid(planet).Level;

    /// <summary>The world's surface sea: which fluid fills its basins and up to what world-Y level (#473 —
    /// percentile-based, see <see cref="BuildCalibration"/>). Returns (int.MinValue, Air) for a dry world.</summary>
    private (int Level, BlockId Fluid) ResolveSeaFluid(PlanetType planet)
    {
        var c = CalibFor(planet);
        return (c.SeaLevel, c.SeaFluid);
    }

    // World floor (B46/B?): every world has a DEEP solid foundation (a few hundred to a couple thousand blocks,
    // varied per world) ending in an unmineable bedrock layer, so caves never open a hole you can fall out of
    // the bottom through. Just above the bedrock sits a boundary band — molten lava on real planets, basalt on
    // airless moons/asteroids — so digging all the way down ends in lava/rock, never a void.
    private const int WorldFloorMinDepth = 256;   // the shallowest a world's foundation ever bottoms out
    private const int WorldFloorMaxDepth = 2048;  // …the deepest (per-world, deterministic)
    private const int FloorBandThickness = 6;     // thickness of the lava/basalt boundary band above the bedrock

    /// <summary>This world's solid-foundation depth below the surface (deterministic per world) — many hundreds
    /// to a couple thousand blocks, so there is always a deep foundation and no way to fall out the bottom.</summary>
    private static int FloorDepthFor(long seed)
        => WorldFloorMinDepth + (int)((ulong)(seed ^ 0x466C6F6F72L) % (ulong)(WorldFloorMaxDepth - WorldFloorMinDepth + 1));

    private const int PondMaxDepth = 5;     // deepest carve at a pond's centre (≥2 is swimmable)
    private const double PondBand = 0.10;   // mask range from "rim" (depth 0) to "centre" (full depth)
    private const int PondMaxSlope = 4;     // only carve on flat ground (Δheight over ±2 in x+z) so water sits level

    // Rivers no longer use a noise band + slope gate; they are routed downhill into a sink by RiverNetwork /
    // RiverField (see RiverFieldFor below and docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md).

    /// <summary>Local terrain steepness at a column: the summed |Δheight| over ±2 blocks in x and z. 0 on a flat
    /// plain, growing with the grade. Used to gate flush-filled water bodies (ponds, rivers) to ground level
    /// enough that the water surface doesn't step into free-standing walls.</summary>
    private int SurfaceSlope(PlanetType planet, int worldX, int worldZ)
        => System.Math.Abs(SurfaceHeight(planet, worldX + 2, worldZ) - SurfaceHeight(planet, worldX - 2, worldZ))
         + System.Math.Abs(SurfaceHeight(planet, worldX, worldZ + 2) - SurfaceHeight(planet, worldX, worldZ - 2));

    /// <summary>Carve depth (0 = none) for an upland pond at this column: a low-frequency mask scatters ponds
    /// (sized by its peaks → small pools + occasional lakes), gated to flat ground so the water surface stays
    /// level. Deterministic — pure noise. The caller fills the carved bowl with water up to the original
    /// surface, so a pond reads as a swimmable pool flush with the surrounding terrain (B7).</summary>
    private int PondDepthAt(PlanetType planet, long seed, int worldX, int worldZ, double threshold)
        => PondDepthFromMask(planet, seed, worldX, worldZ, threshold, PondMaskAt(planet, seed, worldX, worldZ));

    /// <summary>The raw pond placement mask at a column — split out so Generate can compute it once per
    /// column and share it between the pond carve and the beach rim test (#679).</summary>
    private double PondMaskAt(PlanetType planet, long seed, int worldX, int worldZ)
        => FbmT(seed + 0x7A11, worldX, worldZ, planet.TerrainScale * 4.0, octaves: 3);

    private int PondDepthFromMask(PlanetType planet, long seed, int worldX, int worldZ, double threshold, double mask)
    {
        double strength = (mask - threshold) / PondBand;
        if (strength <= 0.0)
        {
            return 0;
        }

        // No ponds anywhere on a volcano (#477): the crater is molten and the flanks are steep basalt —
        // checked here (the single source of truth) so Generate and SurfacePondDepth can never disagree.
        if (HasVolcanoes(planet) && TryGetVolcano(planet, seed, worldX, worldZ, out _, out _))
        {
            return 0;
        }

        // Flat-ground gate — sampled lazily, only inside the pond mask, so it doesn't cost on every column.
        if (SurfaceSlope(planet, worldX, worldZ) > PondMaxSlope)
        {
            return 0;
        }

        return (int)System.Math.Round(System.Math.Min(1.0, strength) * PondMaxDepth);
    }

    // --- Beaches (#679): sand along the waterline of the sea and of LARGE lakes/ponds ---
    private const int BeachApronDepth = 3;      // submerged shore: seabed this close under the sea line reads sandy
    private const int BeachMaxRise = 3;         // tallest dry beach strip above a waterline (per-column jitter 1..3)
    private const int BeachLargePondDepth = 3;  // a pond earns a beach rim only where its bowl gets this deep nearby
    private static readonly int[] BeachProbeRadii = { 4, 8, 12 };
    private static readonly int[] BeachDirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] BeachDirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>Coast-character mask (#679): long stretches of coast alternate between beach and bare
    /// (rocky/cliff) shore, so sand doesn't ring every waterline uniformly (~55–60 % of coast is beach).</summary>
    private bool CoastMaskAt(PlanetType planet, long seed, int worldX, int worldZ)
        => FbmT(seed + 0xBEAC50, worldX, worldZ, planet.TerrainScale * 3.0, octaves: 2) > 0.46;

    /// <summary>How high above its waterline this column's dry beach strip may reach (1..3) — jittered by
    /// a small noise so the sand edge wanders instead of following a contour line.</summary>
    private int BeachRiseAt(long seed, int worldX, int worldZ)
        => 1 + (int)(System.Math.Clamp(FbmT(seed + 0xBEAC51, worldX, worldZ, 13.0, octaves: 1), 0.0, 0.999) * BeachMaxRise);

    /// <summary>True when actual sea water lies within the probe ring of this column — the guard that keeps
    /// inland lowland at coastal ALTITUDE from sand-coating (#679). Early-outs on the first hit, and a real
    /// shore answers on the innermost ring, so the full 24 samples are only paid by the (rare) rejects.</summary>
    private bool SeaWithinBeachProbe(PlanetType planet, int worldX, int worldZ, int seaLevel)
    {
        for (int r = 0; r < BeachProbeRadii.Length; r++)
            for (int d = 0; d < 8; d++)
            {
                int radius = BeachProbeRadii[r];
                if (SurfaceHeight(planet, worldX + BeachDirX[d] * radius, worldZ + BeachDirZ[d] * radius) < seaLevel)
                {
                    return true;
                }
            }

        return false;
    }

    /// <summary>
    /// Dry-beach test (#679) for a column KNOWN to hold no water itself (no sea/pond/river/crater — the
    /// caller guarantees it). Three shorelines qualify, checked in rising cost order behind cheap band
    /// gates: the sea coast (band above the sea line + real-water probe), a large lake's shore ring
    /// (pre-marked by <see cref="RiverField"/>), and a large pond's rim (mask edge + depth probe). All of
    /// it is masked by <see cref="CoastMaskAt"/> and a jittered rise so the sand edge varies. Pure function
    /// of (seed, x, z) — Generate, tree stamping, tests and the client can never disagree.
    /// </summary>
    private bool DryBeachAt(PlanetType planet, WorldCalibration calib, long seed, RiverField riverField,
        BlockId waterId, int worldX, int worldZ, int surfaceY, double? pondMask = null)
    {
        if (waterId.IsAir)
        {
            return false;
        }

        bool seaIsWater = calib.SeaLevel != int.MinValue && calib.SeaFluid == waterId;
        bool riversAreWater = riverField.FillFluid == waterId;
        if (!seaIsWater && !riversAreWater)
        {
            return false; // no water shoreline anywhere on this world (dry, airless or lava-sea)
        }

        // Cheap candidacy gates first — the mask FBM and the probes only run on waterline-band columns.
        bool? coast = null;
        bool Coast() => coast ??= CoastMaskAt(planet, seed, worldX, worldZ);

        if (seaIsWater && surfaceY >= calib.SeaLevel && surfaceY - calib.SeaLevel <= BeachMaxRise
            && Coast()
            && surfaceY - calib.SeaLevel <= BeachRiseAt(seed, worldX, worldZ)
            && SeaWithinBeachProbe(planet, worldX, worldZ, calib.SeaLevel))
        {
            return true;
        }

        if (riversAreWater && riverField.TryGetLakeShore(worldX, worldZ, out int lakeLevel)
            && surfaceY >= lakeLevel && surfaceY - lakeLevel <= BeachMaxRise
            && Coast()
            && surfaceY - lakeLevel <= BeachRiseAt(seed, worldX, worldZ))
        {
            return true;
        }

        // Large-pond rim: just OUTSIDE the pond mask's waterline (depth 0 there), confirmed against a
        // nearby bowl that actually reaches lake depth — depth tracks the mask's excess, so only the big
        // ponds qualify and puddles get no rim. Ponds share the sea's water gate (they never form otherwise).
        if (!seaIsWater)
        {
            return false;
        }

        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        if (!(pondAbundance > 0.15))
        {
            return false;
        }

        double pondThreshold = 0.70 - pondAbundance * 0.12;
        double mask = pondMask ?? PondMaskAt(planet, seed, worldX, worldZ);
        if (mask <= pondThreshold - PondBand || mask > pondThreshold || !Coast())
        {
            return false;
        }

        for (int r = 0; r < BeachProbeRadii.Length; r++)
            for (int d = 0; d < 8; d++)
            {
                int radius = BeachProbeRadii[r];
                if (PondDepthAt(planet, seed, worldX + BeachDirX[d] * radius, worldZ + BeachDirZ[d] * radius,
                        pondThreshold) >= BeachLargePondDepth)
                {
                    return true;
                }
            }

        return false;
    }

    /// <summary>True when this dry surface column is a beach (#679): the shoreline band of the sea or of a
    /// large lake/pond, on a beach-masked stretch of coast. Water columns (sea/pond/river/crater) are never
    /// "beach" — the submerged sandy apron is Generate's detail, not part of this query. Deterministic;
    /// shared by Generate, tree stamping and tests so they can never disagree about the painted ground.</summary>
    public bool IsBeachColumn(PlanetType planet, int worldX, int worldZ)
    {
        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        var calib = CalibFor(planet);
        if (calib.SeaLevel != int.MinValue && surfaceY < calib.SeaLevel)
        {
            return false; // submerged under the sea
        }

        if (SurfacePondDepth(planet, worldX, worldZ) > 0 || SurfaceRiverDepth(planet, worldX, worldZ) > 0
            || TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return false; // a water column is never the beach
        }

        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        return DryBeachAt(planet, calib, PlanetSeed(planet), RiverFieldFor(planet), waterId, worldX, worldZ, surfaceY);
    }

    /// <summary>This planet's beach surface block (#679): <see cref="PlanetType.BeachBlock"/>, sand by default.</summary>
    private BlockId BeachBlockFor(PlanetType planet)
        => ResolveBlock(string.IsNullOrWhiteSpace(planet.BeachBlock) ? "sand" : planet.BeachBlock);

    // --- Routed rivers (Phase 1): per-world memoized network + block-resolution placement field ---
    // A river is no longer a height-blind noise band. RiverNetwork traces every river downhill (steepest
    // descent + fill-and-spill lakes) to a guaranteed sink (the sea or a self-formed lake); RiverField then
    // rasterizes that to block columns whose water surface FOLLOWS the terrain (no floating wall) and which
    // carry a waterfall drop at steep steps. The whole thing is integer + seed-deterministic, so the client
    // rebuilds the identical field — no network snapshot. See the plan doc.
    // STATIC like the calibration cache: the field is a pure function of (world seed, planet, size,
    // cratered, body salt), and fresh generator instances (tests, client preview bakes) would otherwise
    // re-run the ~300 ms network build per instance.
    private static readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool, bool, int), RiverField> _riverFields = new();
    private static readonly object _riverLock = new object();
    private static readonly System.Collections.Generic.Queue<(long, string, int, bool, long, bool, bool, int)> _riverOrder = new();

    /// <summary>This world's routed river placement (built once per world, then cached). Empty on worlds that
    /// get no rivers (no water sea, or WaterAbundance below the river threshold).</summary>
    public RiverField RiverFieldFor(PlanetType planet)
    {
        var key = (_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt, _continentsEnabled, _lavaCoreVolcanoes, _terrainGeneration);
        lock (_riverLock)
        {
            if (_riverFields.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var field = BuildRiverField(planet);
            EvictOldest(_riverFields, _riverOrder, 8); // #1527: a ship alternating past 8 bodies no longer re-runs the ~300 ms build
            _riverFields[key] = field;
            _riverOrder.Enqueue(key);
            return field;
        }
    }

    /// <summary>Whether this type routes LAVA rivers into its lava sea (#1644: the `volcanic` tag — formerly
    /// the `lava` / `ashen` key check).</summary>
    private static bool LavaRiversFor(PlanetType planet) => planet.HasTag(TerrainTag.Volcanic);

    private RiverField BuildRiverField(PlanetType planet)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        if (seaLevel == int.MinValue)
        {
            return RiverField.Empty(_circumference); // dry world: no sea, nothing to drain into
        }

        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);

        int period = WorldConstants.LatitudePeriodFor(_circumference);
        int Height(int x, int z) => SurfaceHeight(planet, x, z);
        long refArea = (long)(WorldConstants.Circumference / 16) * (WorldConstants.LatitudePeriodFor(WorldConstants.Circumference) / 16);
        long area = (long)(_circumference / 16) * (period / 16);
        double areaScale = area / (double)refArea;

        // WATER rivers: the wetter water worlds. Density scales with WaterAbundance + world area (Phase 4).
        // channelFlowThreshold 1 (#474): the headwaters (FlowAccum == 1) are stamped too, so a river has a
        // SOURCE instead of appearing abruptly at the first confluence; density is steered via sourceCount.
        if (seaFluid == waterId && !waterId.IsAir && pondAbundance >= 0.4)
        {
            double wetness = System.Math.Min(1.0, System.Math.Max(0.0, (pondAbundance - 0.4) / 0.6));
            int sources = System.Math.Max(8, (int)System.Math.Round((40 + 80 * wetness) * areaScale));
            var net = RiverNetwork.Build(PlanetSeed(planet), _circumference, period, seaLevel, Height, cellSize: 16, sourceCount: sources);
            return RiverField.Build(net, Height, _circumference, fillFluid: waterId,
                channelFlowThreshold: 1, fullWidthAccum: 8);
        }

        // LAVA rivers (L2): only the `lava` and `ashen` worlds (user decision). Magma is viscous, so the
        // channels are FEWER, WIDER and SHALLOWER than water brooks — thick flows creeping into the lava sea.
        if (LavaRiversFor(planet) && seaFluid == lavaId && !lavaId.IsAir)
        {
            int sources = System.Math.Max(6, (int)System.Math.Round(26 * areaScale));
            var net = RiverNetwork.Build(PlanetSeed(planet), _circumference, period, seaLevel, Height, cellSize: 16, sourceCount: sources);
            // channelFlowThreshold 1: magma flows are sparse, so every routed source path counts as a channel
            // (they rarely merge the way dense water tributaries do). fullWidthAccum 1 (#474): a lava flow
            // reaches full width without needing tributaries — the old absolute divisor kept every lava
            // channel at width 1 (FlowAccum never exceeds 1 on a lava world), making this tuning inert.
            return RiverField.Build(net, Height, _circumference, fillFluid: lavaId,
                channelFlowThreshold: 1, maxWidth: 9, fullWidthAccum: 1, maxLakeDepth: 4, estuaryWiden: 4);
        }

        return RiverField.Empty(_circumference);
    }

    /// <summary>Upland-pond carve depth (0 = none) at a surface column — the same scattered-water gate
    /// <see cref="Generate"/> applies (B7), but with this world's pond-enable, threshold and seed resolved
    /// internally so callers (tree placement, ship landing) can keep things out of the water without
    /// duplicating the rule. Returns 0 on worlds that have no water ponds (dry / lava / airless).</summary>
    public int SurfacePondDepth(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        if (!(pondAbundance > 0.15) || seaFluid != waterId || waterId.IsAir)
        {
            return 0; // ponds only on watery worlds (matches Generate)
        }

        if (SurfaceHeight(planet, worldX, worldZ) <= seaLevel)
        {
            return 0; // below the global sea — the sea fills this column, not a pond
        }

        double pondThreshold = 0.70 - pondAbundance * 0.12;
        return PondDepthAt(planet, PlanetSeed(planet), worldX, worldZ, pondThreshold);
    }

    /// <summary>River water depth (0 = none) at a surface column — resolved from the routed
    /// <see cref="RiverFieldFor"/> placement so callers (tree/prop placement, ship landing, aquatic life,
    /// client preview) and <see cref="Generate"/> can never disagree about where river water is. A pond takes
    /// precedence (matches Generate's pond-first order); the sea owns columns at/below sea level.</summary>
    public int SurfaceRiverDepth(PlanetType planet, int worldX, int worldZ)
    {
        if (SurfacePondDepth(planet, worldX, worldZ) > 0)
        {
            return 0; // a pond already claims this column (pond-first precedence)
        }

        // The global sea owns columns at/below sea level — Generate skips the river fill there, so we must
        // too — and a volcano crater's molten pool is never a river column (#477).
        int seaLevel = ResolveSeaFluid(planet).Level;
        if (SurfaceHeight(planet, worldX, worldZ) <= seaLevel || TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return 0;
        }

        if (RiverFieldFor(planet).TryGet(worldX, worldZ, out var col))
        {
            int depth = col.WaterSurfaceY - col.BedY;
            return depth >= 1 ? depth : 1;
        }

        return 0;
    }

    /// <summary>True if this surface column is under water — beneath the global water sea, inside an upland
    /// pond/lake (B7), or in a river channel. A lava sea is not "water" here. Used to keep ship landings out of
    /// the water (B36).</summary>
    public bool IsSurfaceWater(PlanetType planet, int worldX, int worldZ)
    {
        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        bool water = (seaFluid == waterId && !waterId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel)
            || SurfacePondDepth(planet, worldX, worldZ) > 0   // inside an upland pond
            || SurfaceRiverDepth(planet, worldX, worldZ) > 0; // …or a river channel
        if (!water)
        {
            return false;
        }

        // Frozen columns (#494): a body frozen to the seabed, or capped by a thick sheet, is walkable
        // land — ships may land on a frozen sea. Thin sheets (1–2 blocks) still count as water so
        // landings and chests don't sit on breakable crust.
        var calib = CalibFor(planet);
        if (CanFreezeWater(planet, calib) && TryGetRawWaterColumn(planet, worldX, worldZ, out int top, out int bed))
        {
            int ice = IceSheetThickness(calib, PlanetSeed(planet), worldX, worldZ, top, top - bed);
            if (ice >= top - bed || ice >= LandableIceSheet)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if this surface column is under a LAVA sea — or inside a volcano's molten summit
    /// crater (#477) — so a ship landing avoids it too (B54), the same way it avoids water.</summary>
    public bool IsSurfaceLava(PlanetType planet, int worldX, int worldZ)
    {
        if (TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return true;
        }

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        return seaFluid == lavaId && !lavaId.IsAir && SurfaceHeight(planet, worldX, worldZ) + 1 <= seaLevel;
    }

    /// <summary>The local LIQUID water column at a surface (x,z): true if water actually covers it — the
    /// global sea, an upland pond, or a river — returning the liquid-surface Y (topmost water cell, i.e.
    /// beneath any ice sheet, #494) and the seabed Y (last solid cell below the water). Mirrors what
    /// <see cref="Generate"/> fills, so the server can place and keep aquatic life in ANY water body, not
    /// just the deep global sea. False (with 0s) for dry/lava/frozen-through columns.</summary>
    public bool TryGetWaterSurface(PlanetType planet, int worldX, int worldZ, out int waterTopY, out int seabedY)
    {
        if (!TryGetRawWaterColumn(planet, worldX, worldZ, out waterTopY, out seabedY))
        {
            return false;
        }

        var calib = CalibFor(planet);
        if (CanFreezeWater(planet, calib))
        {
            // Fauna lives below the ice sheet (#494) — report the topmost LIQUID cell.
            waterTopY -= IceSheetThickness(calib, PlanetSeed(planet), worldX, worldZ, waterTopY, waterTopY - seabedY);
        }

        return waterTopY > seabedY; // frozen through → no water body left here
    }

    /// <summary>The water column at a surface (x,z) as generated BEFORE the freeze pass (#494) — surface Y
    /// of the topmost filled (water or ice) cell and the seabed Y. The ice-aware public queries
    /// (<see cref="TryGetWaterSurface"/>, <see cref="IsSurfaceWater"/>, <see cref="SurfaceIceThickness"/>)
    /// layer the sheet on top of this.</summary>
    private bool TryGetRawWaterColumn(PlanetType planet, int worldX, int worldZ, out int waterTopY, out int seabedY)
    {
        waterTopY = 0;
        seabedY = 0;

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;

        // Generation-1 water bodies (#1647) exist on DRY worlds too (oases, playas), so they are checked before
        // the "no water sea" exit — on a column no classic body (sea, pond, river, crater, travertine pool,
        // cenote pool) claims, exactly the column phase's order. Same function as the fill: agreement by construction.
        var wg = WonderFor(planet);
        if (wg.Generation >= 1 && !waterId.IsAir)
        {
            int sy = SurfaceHeight(planet, worldX, worldZ);
            bool classic = sy <= seaLevel
                || SurfacePondDepth(planet, worldX, worldZ) > 0
                || TryGetVolcanoCrater(planet, worldX, worldZ, out _)
                || RiverFieldFor(planet).TryGet(worldX, worldZ, out _)
                || (wg.Travertine && TryGetTravertine(wg.Seed, worldX, worldZ, out _, out bool travPool) && travPool)
                || (wg.Cenotes && TryGetCenotePool(planet, worldX, worldZ, out int cenoteTop) && cenoteTop > sy);
            if (!classic && TryGetGen1Water(planet, wg, worldX, worldZ, sy, out int g1Top, out int g1Bed, out var g1Fluid)
                && g1Fluid == waterId)
            {
                waterTopY = g1Top;
                seabedY = g1Bed;
                return true;
            }
        }

        if (seaFluid != waterId || waterId.IsAir)
        {
            return false; // a lava/dry world has no water bodies
        }

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);

        // Global sea: terrain sits at/below the sea level, so water fills surfaceY+1 .. seaLevel.
        if (surfaceY + 1 <= seaLevel)
        {
            waterTopY = seaLevel;
            seabedY = surfaceY;
            return true;
        }

        // Upland pond: a carved bowl filled flush to the original surface (pond-first precedence).
        int pond = SurfacePondDepth(planet, worldX, worldZ);
        if (pond > 0)
        {
            waterTopY = surfaceY;
            seabedY = surfaceY - pond;
            return true;
        }

        // River: read the routed field's ABSOLUTE surface/bed (#469). A pooled reach sits ABOVE the local
        // terrain by design (that is what makes it a pool), so reconstructing the band from surfaceY put
        // the reported water into solid rock — and aquatic creatures spawned inside it.
        if (surfaceY > seaLevel && !TryGetVolcanoCrater(planet, worldX, worldZ, out _)
            && RiverFieldFor(planet).TryGet(worldX, worldZ, out var col))
        {
            waterTopY = col.WaterfallDrop > 0 ? col.WaterSurfaceY + col.WaterfallDrop : col.WaterSurfaceY;
            seabedY = col.BedY;
            return true;
        }

        return false;
    }

    /// <summary>The local LAVA column at a surface (x,z): a volcano crater pool (#477), the global lava
    /// sea, or a lava river/flow — with the melt-surface Y and the bed Y. The molten counterpart of
    /// <see cref="TryGetWaterSurface"/>, so lava fauna can spawn and stay IN lava (#470 F4).</summary>
    public bool TryGetLavaSurface(PlanetType planet, int worldX, int worldZ, out int lavaTopY, out int bedY)
    {
        lavaTopY = 0;
        bedY = 0;
        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        if (TryGetVolcanoCrater(planet, worldX, worldZ, out int craterTop))
        {
            lavaTopY = craterTop;
            bedY = surfaceY;
            return true;
        }

        var lavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        if (lavaId.IsAir)
        {
            return false;
        }

        var (seaLevel, seaFluid) = ResolveSeaFluid(planet);
        if (seaFluid == lavaId && surfaceY + 1 <= seaLevel)
        {
            lavaTopY = seaLevel;
            bedY = surfaceY;
            return true;
        }

        if (surfaceY > seaLevel)
        {
            var field = RiverFieldFor(planet);
            if (field.FillFluid == lavaId && field.TryGet(worldX, worldZ, out var col))
            {
                lavaTopY = col.WaterfallDrop > 0 ? col.WaterSurfaceY + col.WaterfallDrop : col.WaterSurfaceY;
                bedY = col.BedY;
                return true;
            }

            // Generation-1 lava lakes (#1647): caldera and shield-volcano summit lakes on volcanic worlds.
            var wg = WonderFor(planet);
            if (wg.Generation >= 1 && !field.TryGet(worldX, worldZ, out _) && SurfacePondDepth(planet, worldX, worldZ) == 0
                && TryGetGen1Water(planet, wg, worldX, worldZ, surfaceY, out int g1Top, out int g1Bed, out var g1Fluid)
                && g1Fluid == lavaId)
            {
                lavaTopY = g1Top;
                bedY = g1Bed;
                return true;
            }
        }

        return false;
    }
}
