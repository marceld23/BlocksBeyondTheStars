// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Fixed, pre-planned landing pads (item 38). Each body has a deterministic set of landing pads — a
/// seeded-random count within its size-class range (asteroids fewest, moons more, planets most) scattered
/// across BOTH longitude and latitude, nudged onto dry land. Pads are <b>communal</b>: occupancy is
/// <b>live</b> — a pad counts as taken
/// only while a player is standing on the body (not flown off to space), so it frees the moment they leave.
/// Landing lets the player pick a free pad; a body whose pads are all occupied is <b>full</b> and refuses
/// landing. No one may build on a pad (the pad is reserved); the ship is PLACED on the player's pad as a
/// structure object (ship-as-object) and the pad terrain is levelled by worldgen.
/// </summary>
public sealed partial class GameServer
{
    private const int LandingPadRadius = 8;       // one generous size — clears the largest ship (hauler 7×9)
    private const int PadClearanceHeight = 16;    // reserve only the landing volume above the pad, not the whole sky

    /// <summary>A fixed landing pad on a body. Deterministic from the body seed; no owner (communal).</summary>
    internal sealed class LandingPad
    {
        public int Index;
        public int CenterX;
        public int CenterZ;
        public int CenterY;                       // ground height at the pad (the reserved volume sits just above it)
        public int Radius = LandingPadRadius;

        /// <summary>The pad footprint lies under water — the seabed fallback (#1454): the ship parks in a dry
        /// shaft on the sea floor. The chooser marks these so a player can pick a dry pad instead.</summary>
        public bool Wet;

        /// <summary>Blocks of water above a <see cref="Wet"/> pad's ground (0 when dry) — the chooser shows
        /// it (#1622) so a 4-block wade and an 80-block shaft read differently.</summary>
        public int Depth;

        /// <summary>The generator raises an islet under this pad instead of sinking it to the seabed
        /// (#1453/#1619): every all-water pad in water deeper than <see cref="ShallowSeabedDepth"/>, on any
        /// world with a water sea. Only shallow water still parks the ship on the seabed.</summary>
        public bool Islet;

        /// <summary>A pad planned by the pre-generation-2 rules (#1665): the longitude-only march, the rolled
        /// ocean-world islet two blocks over the sea with the plain sand-mound shape. Saves created before the
        /// ocean-pad wave keep these, so their pads — and the ships and bases beside them — never move.</summary>
        public bool Classic;
    }

    /// <summary>How far above the sea an islet pad's surface sits (a dry beach, not a tidal flat).</summary>
    private const int IsletRise = 3;

    /// <summary>The pre-generation-2 islet (#1453): two blocks over the sea, a 1:1 sand slope out to
    /// <see cref="ClassicIsletRadius"/>. Frozen for the saves created with it (#1665).</summary>
    private const int ClassicIsletRise = 2;
    private const int ClassicIsletRadius = LandingPadRadius + 8;

    /// <summary>Whether the active save plans its pads by the ocean-pad rules (#1618–#1622) — only worlds created
    /// with terrain generation 2 or later (#1665). The generator carries the save's generation from start-up.</summary>
    private bool OceanPadRules => _generator.TerrainGeneration >= WorldDescription.OceanPadsGeneration;

    /// <summary>Roughly three of five all-water pads on a classic ocean-class world get an islet; the rest keep
    /// the seabed shaft (#1453, frozen for pre-generation-2 saves by #1665).</summary>
    private static bool ClassicIsletRoll(string locationId, int padIndex)
        => (WorldGenerator.StableHash("islet:" + locationId + ":" + padIndex) & 0xFF) < 154;

    /// <summary>Radius of the islet's level top (#1620) — wider than the reserved pad, so there is room to
    /// walk, build and dig beside the ship.</summary>
    private const int IsletPlateauRadius = LandingPadRadius + 4;

    /// <summary>Outer radius of the islet's beach slope (2:1, one block down per two blocks out).</summary>
    private const int IsletRadius = IsletPlateauRadius + 16;

    /// <summary>The deepest water a pad may still be sunk into as a seabed shaft (#1619): a wade with daylight
    /// above, never a well. Deeper all-water pads always get an islet, so seabed landings stay possible but
    /// rare (decision 2026-09-05, after Marie's 88-block shaft on the school playtest).</summary>
    private const int ShallowSeabedDepth = 8;

    /// <summary>How far the pad nudge searches for dry, flat ground around the planned position (#1618),
    /// in blocks, in BOTH X and Z. Ocean-class worlds get a larger budget: land is scarce there, and the
    /// probe (12 seeds) found real land within reach for 92 % of the all-water pads.</summary>
    private const int PadSearchBudget = 180;
    private const int PadSearchBudgetOcean = 300;

    private List<LandingPad> _landingPads => _worlds.Active.LandingPads;

    /// <summary>Pads computed per body (#1618): the 2-D nudge on an all-water pad walks tens of thousands of
    /// columns, and the chooser asks for a remote body's pads on every approach. Deterministic per body,
    /// so the first computation is the only one. Cleared with the galaxy (server start).</summary>
    private readonly Dictionary<string, List<LandingPad>> _padCache = new(System.StringComparer.Ordinal);

    // --- deterministic pad set ---

    /// <summary>The seeded RNG for a body's pads — stable per body, so the pad count + positions are the same
    /// every load and can be queried cheaply (e.g. for the star-map "full" signal) without loading the world.</summary>
    private System.Random PadRng(string locationId)
    {
        long seed = _meta.Seed ^ WorldGenerator.StableHash("landingpads:" + locationId);
        return new System.Random(unchecked((int)(seed ^ (seed >> 32))));
    }

    /// <summary>How many pads a body has: a seeded-random base count varying within its size-class range,
    /// DOUBLED so each body offers twice as many landing spots — asteroids 2–4, moons 4–8, planets 8–16
    /// (fewest → most by world size). The ×2 is applied AFTER the single deterministic draw, so the same
    /// seed still yields the same base count (determinism preserved) — only the multiplier scales it.
    /// This is the single source of truth for the pad count: BOTH BuildLandingPads (the in-world placement)
    /// and HandleRequestLandingPads (the approach landing map / pad chooser) derive their count from here,
    /// so the map always shows exactly the spots that exist in the world.</summary>
    private int PadCountFor(string locationId, string planetKey, CelestialKind kind)
    {
        var cls = WorldConstants.SizeClassFor(kind, planetKey);
        (int lo, int hi) = cls switch
        {
            WorldConstants.WorldSizeClass.Asteroid => (1, 2),
            WorldConstants.WorldSizeClass.Moon => (2, 4),
            _ => (4, 8),
        };
        int baseCount = lo + PadRng(locationId).Next(hi - lo + 1);
        int count = baseCount * 2; // double the landing spots per world (kept consistent across both consumers)

        // Full-size planets must offer at least one pad per player, so a busy server with PersonalLandingZones
        // doesn't run out of free pads and cluster everyone onto pad 0 (spawn fallback). Moons/asteroids keep
        // their deliberately small set — players don't start there and en-masse landings on tiny bodies are rare.
        if (cls != WorldConstants.WorldSizeClass.Asteroid && cls != WorldConstants.WorldSizeClass.Moon)
        {
            count = System.Math.Max(count, _config.MaxPlayers);
        }
        return count;
    }

    /// <summary>(Re)builds the active world's deterministic pad set and hands it to worldgen for terrain
    /// levelling. Idempotent — called on every world load (the pads aren't persisted; they're recomputed from
    /// the body seed).</summary>
    private void BuildLandingPads()
    {
        var pads = _landingPads;
        pads.Clear();

        var body = _galaxy?.FindBody(_world.LocationId);
        var kind = body?.Kind ?? CelestialKind.Planet;
        pads.AddRange(ComputeLandingPads(_world.Planet, kind, _world.LocationId, _world.Circumference));

        // Hand the planned pads to worldgen so their terrain is levelled at generation time (ship-as-object:
        // the landed ship is a placed structure that needs flat, clear ground). Must run before any pad-area
        // chunk generates — ComputeLandingPads only needs noise queries, so it is safe this early.
        var flats = _world.LandingPadFlats;
        flats.Clear();
        foreach (var pad in pads)
        {
            flats.Add(pad.Classic
                ? new BlocksBeyondTheStars.WorldGeneration.LandingPadFlatten(pad.CenterX, pad.CenterZ, pad.CenterY, pad.Radius, pad.Islet, pad.Radius, ClassicIsletRadius, classicShape: true)
                : new BlocksBeyondTheStars.WorldGeneration.LandingPadFlatten(pad.CenterX, pad.CenterZ, pad.CenterY, pad.Radius, pad.Islet, IsletPlateauRadius, IsletRadius));
        }
    }

    /// <summary>The single source of truth for a body's landing pads — usable for ANY body, loaded or not, so
    /// the in-world placement and the pre-landing pad-chooser map agree exactly. Pads are spread across BOTH
    /// longitude (X) and latitude (Z) — pad 0 is the prime-meridian/equator home touchdown, the rest are
    /// scattered with a golden-ratio latitude sequence + an even longitude spread — each nudged onto dry,
    /// reasonably flat ground. Deterministic from the body seed. Configures the shared generator for the target
    /// body (circumference + airless-moon cratering) and restores it afterwards.</summary>
    private List<LandingPad> ComputeLandingPads(PlanetType planet, CelestialKind kind, string locationId, int circ)
    {
        if (_padCache.TryGetValue(locationId, out var cached))
        {
            return cached;
        }

        var computed = ComputeLandingPadsUncached(planet, kind, locationId, circ);
        _padCache[locationId] = computed;
        return computed;
    }

    private List<LandingPad> ComputeLandingPadsUncached(PlanetType planet, CelestialKind kind, string locationId, int circ)
    {
        int savedCirc = _generator.Circumference;
        bool savedCratered = _generator.Cratered;
        var savedPads = _generator.LandingPads;
        string savedLocation = _generator.LocationId;
        double savedOreBoost = _generator.FrontierOreBoost;
        bool airlessMoon = kind == CelestialKind.Moon
            && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        // Full mode swap for the target body (#424 S13) — no pads: this computes WHERE the pads go, so
        // flattening must not apply, and the active world's pads must not leak into the noise queries.
        // The target's location id rides along (#478) so pad nudging sees the target's OWN terrain.
        _generator.SetWorldMode(circ, airlessMoon, null, locationId);

        try
        {
            int count = PadCountFor(locationId, planet.Key, kind);
            int latP = WorldConstants.LatitudePeriodFor(circ);
            // Keep pads inside a navigable mid-latitude band (so they spread well on the map without touching
            // the latitude wrap), and ensure the footprint fits.
            int latBand = System.Math.Min((int)(latP * 0.38), latP / 2 - LandingPadRadius - 8);
            if (latBand < 0)
            {
                latBand = 0;
            }

            // A stable per-body latitude offset so different bodies don't share the same scatter pattern.
            double latOffset = (WorldGenerator.StableHash("padlat:" + locationId) & 0x3FF) / 1024.0;

            var pads = new List<LandingPad>(count);
            for (int i = 0; i < count; i++)
            {
                int baseX, baseZ;
                if (i == 0)
                {
                    baseX = 0;
                    baseZ = 0; // home touchdown: prime meridian, equator
                }
                else
                {
                    baseX = WorldConstants.WrapX((int)((i / (double)count) * circ), circ);
                    double gz = (i * 0.61803398875 + latOffset) % 1.0; // golden-ratio stratified latitude
                    baseZ = (int)System.Math.Round((gz - 0.5) * 2.0 * latBand);
                }

                bool oceanClass = (planet.WaterAbundance ?? 0.0) >= 1.0;
                if (!OceanPadRules)
                {
                    // A save from before the ocean-pad wave (#1665): the longitude-only march and the rolled
                    // ocean-world islet it was created with, so its pads stay exactly where its ships and bases
                    // are. Pads are not persisted — the rule that re-derives them is the only thing holding
                    // them in place.
                    int classicX = ClassicNudgePadToDryAndFlat(planet, baseX, baseZ);
                    pads.Add(DecideClassicPad(planet, locationId, i, classicX, baseZ, oceanClass));
                    continue;
                }

                // Search around the planned position — longitude AND latitude (#1618) — for the nearest dry
                // + reasonably flat column, so a ship never lands in water (B36) or perches on a terrain
                // spike (dramatic-terrain worlds). Ocean-class worlds search further: land is scarce there.
                var (cx, cz) = NudgePadToDryAndFlat(planet, baseX, baseZ, latBand, oceanClass ? PadSearchBudgetOcean : PadSearchBudget);
                pads.Add(DecidePad(planet, i, cx, cz));
            }

            return pads;
        }
        finally
        {
            _generator.SetWorldMode(savedCirc, savedCratered, savedPads, savedLocation, savedOreBoost);
        }
    }

    /// <summary>What a pad at its final column becomes (#1619): dry ground as it is; an all-water footprint in
    /// water deeper than <see cref="ShallowSeabedDepth"/> gets an islet raised to sea level + <see cref="IsletRise"/>
    /// (any world whose sea is water — not lava); shallow water, ponds and lava keep the seabed shaft and are
    /// flagged <see cref="LandingPad.Wet"/> with their depth so the chooser can say so (#1454/#1622).
    /// The generator must be configured for the pad's body.</summary>
    private LandingPad DecidePad(PlanetType planet, int index, int cx, int cz)
    {
        bool wet = LandingFootprintWet(planet, cx, cz);
        int groundY = PadGroundY(planet, cx, cz);
        int seaLevel = _generator.SeaLevel(planet);
        int seaDepth = wet && seaLevel != int.MinValue ? seaLevel - groundY : 0;
        bool islet = wet && seaDepth > ShallowSeabedDepth && _generator.SeaIsWater(planet);
        int depth = 0;
        if (wet && !islet)
        {
            // A pond or river pad reports the water standing over its own ground, not the sea's.
            depth = _generator.TryGetWaterSurface(planet, cx, cz, out int waterTop, out _)
                ? System.Math.Max(0, waterTop - groundY)
                : System.Math.Max(0, seaDepth);
        }

        return new LandingPad
        {
            Index = index,
            CenterX = cx,
            CenterZ = cz,
            CenterY = islet ? seaLevel + IsletRise : groundY,
            Wet = wet && !islet,
            Depth = depth,
            Islet = islet,
        };
    }

    /// <summary>What a classic pad (pre-generation-2 save, #1665) becomes — the #1453/#1454 rule, frozen: still
    /// wet after the march = an all-sea band; an ocean-class world rolls an islet two blocks over the sea for
    /// roughly three pads in five, everything else keeps the seabed shaft and is flagged wet with its depth so
    /// the chooser can say so.</summary>
    private LandingPad DecideClassicPad(PlanetType planet, string locationId, int index, int cx, int cz, bool oceanClass)
    {
        bool wet = LandingFootprintWet(planet, cx, cz);
        int seaLevel = _generator.SeaLevel(planet);
        bool islet = wet && seaLevel != int.MinValue && oceanClass && ClassicIsletRoll(locationId, index);
        int groundY = PadGroundY(planet, cx, cz);
        int depth = 0;
        if (wet && !islet)
        {
            depth = _generator.TryGetWaterSurface(planet, cx, cz, out int waterTop, out _)
                ? System.Math.Max(0, waterTop - groundY)
                : System.Math.Max(0, seaLevel != int.MinValue ? seaLevel - groundY : 0);
        }

        return new LandingPad
        {
            Index = index,
            CenterX = cx,
            CenterZ = cz,
            CenterY = islet ? seaLevel + ClassicIsletRise : groundY,
            Wet = wet && !islet,
            Depth = depth,
            Islet = islet,
            Classic = true,
        };
    }

    /// <summary>The pre-#1618 pad nudge, frozen for pre-generation-2 saves (#1665): marches the pad LONGITUDE
    /// (at a fixed latitude) to the nearest column that is both dry and reasonably flat (footprint spread ≤ 5),
    /// preferring green ground where the world offers it. Falls back to the flattest dry candidate seen, then to
    /// the plain dry march. Byte-for-byte the rule those saves' pads were derived with.</summary>
    private int ClassicNudgePadToDryAndFlat(PlanetType planet, int baseX, int baseZ)
    {
        int circ = _generator.Circumference;
        bool seekEarthy = _generator.HasEarthySurfaceBiome(planet);
        int bestX = ClassicNudgePadToDry(planet, baseX, baseZ);
        int bestSpread = int.MaxValue;
        int earthyX = int.MinValue, earthySpread = int.MaxValue;

        void Consider(int x)
        {
            if (LandingFootprintWet(planet, x, baseZ))
            {
                return;
            }

            int spread = PadFootprintSpread(planet, x, baseZ);
            if (spread < bestSpread)
            {
                bestSpread = spread;
                bestX = x;
            }

            if (seekEarthy && spread < earthySpread && _generator.IsEarthySurface(planet, x, baseZ))
            {
                earthySpread = spread;
                earthyX = x;
            }
        }

        Consider(bestX);
        if (bestSpread <= 5 && (!seekEarthy || earthyX == bestX))
        {
            return bestX;
        }

        for (int step = 1; step <= 60; step++)
        {
            foreach (int x in new[] { WorldConstants.WrapX(baseX + step * 3, circ), WorldConstants.WrapX(baseX - step * 3, circ) })
            {
                Consider(x);
                if (earthySpread <= 5)
                {
                    return earthyX;
                }
            }
        }

        return earthyX != int.MinValue && earthySpread <= 10 ? earthyX : bestX;
    }

    /// <summary>The pre-#1618 dry nudge, frozen for pre-generation-2 saves (#1665): the nearest dry column along
    /// the latitude, ±120 blocks in steps of 3; the planned column itself on an all-ocean band.</summary>
    private int ClassicNudgePadToDry(PlanetType planet, int baseX, int baseZ)
    {
        int circ = _generator.Circumference;
        if (!LandingFootprintWet(planet, baseX, baseZ))
        {
            return baseX;
        }

        for (int step = 1; step <= 40; step++)
        {
            int xp = WorldConstants.WrapX(baseX + step * 3, circ);
            if (!LandingFootprintWet(planet, xp, baseZ))
            {
                return xp;
            }

            int xm = WorldConstants.WrapX(baseX - step * 3, circ);
            if (!LandingFootprintWet(planet, xm, baseZ))
            {
                return xm;
            }
        }

        return baseX;
    }

    /// <summary>The pad/ship ground height on the ACTIVE world: the MEDIAN surface height over the landing
    /// footprint (centre + four corners), not the centre column alone — one rocky spike no longer hoists the
    /// whole ship. Used by every ship-placement consumer.</summary>
    private int PadGroundY(int cx, int cz)
    {
        // An islet pad's ground is the raised mound the generator built, not the noise seabed under it (#1453).
        foreach (var pad in _landingPads)
        {
            if (pad.Islet && pad.CenterX == cx && pad.CenterZ == cz)
            {
                return pad.CenterY;
            }
        }

        return PadGroundY(_world.Planet, cx, cz);
    }

    /// <summary>How far above the generated median the touchdown height looks for player-built ground: a
    /// paved yard or a raised platform over the pad, not a tower beside it.</summary>
    private const int PadRaiseScan = 8;

    /// <summary>The height a ship parks on and a player touches down at (#1318): the pad's generated median
    /// (<see cref="PadGroundY(int,int)"/>), RAISED to whatever has been built over the footprint since — the
    /// real blocks at the centre and the four corner columns, scanned from the median up to
    /// <see cref="PadRaiseScan"/> cells. Never lowered: a pit dug at the centre must not sink the hull, and the
    /// median stays the floor the pad was levelled to at generation. Lyxette paved his landing site with
    /// concrete walkways; the median ignored them, so every landing put the ship — and him — inside his own
    /// floor until the entombment rescue dug him out a second later.</summary>
    private int PadSurfaceY(int cx, int cz)
    {
        const int r = 4;
        int median = PadGroundY(cx, cz);
        int top = median;
        foreach (var (dx, dz) in new[] { (0, 0), (-r, -r), (r, -r), (-r, r), (r, r) })
        {
            for (int dy = PadRaiseScan; dy >= 1; dy--)
            {
                if (IsBodyBlockingCell(cx + dx, median + dy, cz + dz))
                {
                    top = System.Math.Max(top, median + dy);
                    break;
                }
            }
        }

        return top;
    }

    /// <summary>Test seam: a pad's centre column and generated ground height on the active world.</summary>
    public (int X, int Y, int Z) LandingPadForTest(int index)
    {
        if (_landingPads.Count == 0)
        {
            BuildLandingPads();
        }

        var pad = _landingPads[index];
        return (pad.CenterX, pad.CenterY, pad.CenterZ);
    }

    /// <summary>As <see cref="PadGroundY(int,int)"/> but for an explicit planet (the generator must already be
    /// configured for that body's circumference) — so pads can be computed for any body, loaded or not.</summary>
    private int PadGroundY(PlanetType planet, int cx, int cz)
    {
        const int r = 4;
        var h = new[]
        {
            _generator.SurfaceHeight(planet, cx, cz),
            _generator.SurfaceHeight(planet, cx - r, cz - r),
            _generator.SurfaceHeight(planet, cx + r, cz - r),
            _generator.SurfaceHeight(planet, cx - r, cz + r),
            _generator.SurfaceHeight(planet, cx + r, cz + r),
        };
        System.Array.Sort(h);
        return h[2];
    }

    /// <summary>Height spread over the landing footprint — small = flat enough to set a ship down on.</summary>
    private int PadFootprintSpread(PlanetType planet, int cx, int cz)
    {
        const int r = 4;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var (dx, dz) in new[] { (0, 0), (-r, -r), (r, -r), (-r, r), (r, r), (-r, 0), (r, 0), (0, -r), (0, r) })
        {
            int y = _generator.SurfaceHeight(planet, cx + dx, cz + dz);
            min = System.Math.Min(min, y);
            max = System.Math.Max(max, y);
        }

        return max - min;
    }

    /// <summary>Moves a pad from its planned column to the nearest column that is both DRY and reasonably
    /// FLAT (footprint spread ≤ 5), searching outward in rings over X AND Z (#1618; step 3, up to
    /// <paramref name="budget"/> blocks, |Z| kept inside <paramref name="latBand"/>). Falls back to the
    /// flattest dry candidate seen, then to the nearest dry one, then to the planned column itself (an
    /// all-sea neighbourhood — the caller sinks or islets it). Uses the generator's currently-configured
    /// circumference for the wrap. Deterministic: fixed ring order, no randomness.</summary>
    private (int X, int Z) NudgePadToDryAndFlat(PlanetType planet, int baseX, int baseZ, int latBand, int budget)
    {
        int circ = _generator.Circumference;
        // Prefer WELCOMING ground: on worlds that have grass/dirt biomes at all, keep searching for a green
        // column instead of settling on the first flat one — since the altitude-biome pass, "flat + dry"
        // is often the mud marsh just above the sea, where a new player's first dig finds no visible
        // topsoil ore windows (user playtest 2026-07-26). Preference only: a desert world, or a world
        // whose whole neighbourhood is marsh, still gets the flattest dry spot as before.
        bool seekEarthy = _generator.HasEarthySurfaceBiome(planet);
        int bestX = baseX, bestZ = baseZ, bestSpread = int.MaxValue;
        int earthyX = int.MinValue, earthyZ = 0, earthySpread = int.MaxValue;
        bool anyDry = false;

        void Consider(int x, int z)
        {
            if (LandingFootprintWet(planet, x, z))
            {
                return;
            }

            anyDry = true;
            int spread = PadFootprintSpread(planet, x, z);
            if (spread < bestSpread)
            {
                bestSpread = spread;
                bestX = x;
                bestZ = z;
            }

            if (seekEarthy && spread < earthySpread && _generator.IsEarthySurface(planet, x, z))
            {
                earthySpread = spread;
                earthyX = x;
                earthyZ = z;
            }
        }

        Consider(baseX, baseZ);
        if (anyDry && bestSpread <= 5 && (!seekEarthy || earthyX == bestX))
        {
            return (bestX, bestZ); // already flat + dry (+ green where the world offers green)
        }

        // Rings of step 3 (the old ±180 X march found its green column at 138 on the 2026-07-26 playtest
        // world; the 2-D probe of 2026-09-05 found land within 30–90 blocks for most all-water pads).
        // Cells on a ring are visited east/west first, then the rest of the perimeter, so a tie between
        // equidistant land keeps the pad on its planned latitude where possible.
        int rings = budget / 3;
        for (int ring = 1; ring <= rings; ring++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                int z = baseZ + dz * 3;
                if (System.Math.Abs(z) > latBand && latBand > 0)
                {
                    continue; // keep the pad inside the navigable latitude band (map + wrap safety)
                }

                if (latBand == 0 && dz != 0)
                {
                    continue;
                }

                if (dz == -ring || dz == ring)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        Consider(WorldConstants.WrapX(baseX + dx * 3, circ), z);
                    }
                }
                else
                {
                    Consider(WorldConstants.WrapX(baseX + ring * 3, circ), z);
                    Consider(WorldConstants.WrapX(baseX - ring * 3, circ), z);
                }

                if (earthySpread <= 5)
                {
                    return (earthyX, earthyZ); // green + flat + dry — done
                }
            }

            if (!seekEarthy && bestSpread <= 5)
            {
                return (bestX, bestZ); // flat + dry on a world without green — done
            }
        }

        // Green ground wins while it is still reasonably level; else the flattest dry spot found; else the
        // planned column (all sea within the budget).
        return earthyX != int.MinValue && earthySpread <= 10 ? (earthyX, earthyZ) : (bestX, bestZ);
    }

    /// <summary>True if the pad (its centre or any radius edge) sits over surface water/lava on the ACTIVE
    /// world.</summary>
    private bool LandingFootprintWet(int cx, int cz) => LandingFootprintWet(_world.Planet, cx, cz);

    /// <summary>As above but for an explicit planet (the generator must already be configured for that body's
    /// circumference) — so a ship never touches down in a sea or pond, on any body (B36/B54).</summary>
    private bool LandingFootprintWet(PlanetType planet, int cx, int cz)
    {
        int r = LandingPadRadius;
        bool Wet(int x, int z) => _generator.IsSurfaceWater(planet, x, z) || _generator.IsSurfaceLava(planet, x, z);
        return Wet(cx, cz) || Wet(cx - r, cz) || Wet(cx + r, cz) || Wet(cx, cz - r) || Wet(cx, cz + r);
    }

    // --- live occupancy (derived from sessions, never persisted) ---

    /// <summary>True if another player currently holds this pad on this body. A holder who is merely up in
    /// space still RESERVES their pad (#957): their session is alive and their AssignedPadIndex still points
    /// here, so treating the pad as free let a second player be assigned the same pad — two ships stamped on
    /// one origin when the holder came back. The exception is the player being served.</summary>
    private bool PadOccupiedByOther(string locationId, int padIndex, string exceptPlayerId)
    {
        if (PadReservedByTrader(locationId, padIndex))
        {
            return true; // a landed NPC trader holds this pad (P3) — never assign it to a player
        }

        foreach (var s in _sessions.Values)
        {
            // An observer never HOLDS a pad (#487/#996): occupancy is derived live from AssignedPadIndex,
            // and the relocate path may stamp one on a spectating session — an invisible admin must not
            // block a communal pad for the players.
            if (!s.Joined || s.Spectating || s.State.PlayerId == exceptPlayerId)
            {
                continue;
            }

            if (s.AssignedPadIndex == padIndex && s.CurrentLocationId == locationId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The lowest free pad index on a body, or -1 if every pad is currently taken (the body is full).
    /// The plain rule — NPC traders use it. Players go through <see cref="PreferredFreePadIndex"/>.</summary>
    private int FirstFreePadIndex(string locationId, int total, string exceptPlayerId)
    {
        for (int i = 0; i < total; i++)
        {
            if (!PadOccupiedByOther(locationId, i, exceptPlayerId))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The free pad a PLAYER gets when they do not choose one (#1621) — first spawn, the safety-net
    /// assignment and an auto landing: natural dry ground first, then an islet, a seabed shaft last; ties by
    /// index, so pad 0 stays the home touchdown whenever it is dry. -1 when the body is full.</summary>
    private int PreferredFreePadIndex(string locationId, IReadOnlyList<LandingPad> pads, string exceptPlayerId)
    {
        int best = -1, bestRank = int.MaxValue;
        for (int i = 0; i < pads.Count; i++)
        {
            if (PadOccupiedByOther(locationId, pads[i].Index, exceptPlayerId))
            {
                continue;
            }

            int rank = PadRank(pads[i]);
            if (rank < bestRank)
            {
                bestRank = rank;
                best = pads[i].Index;
            }
        }

        return best;
    }

    /// <summary>0 = natural dry ground, 1 = generated islet, 2 = seabed shaft.</summary>
    private static int PadRank(LandingPad pad) => pad.Wet ? 2 : pad.Islet ? 1 : 0;

    /// <summary>A body's pads: the active world's set, or the deterministic (cached) computation for a body
    /// that is not loaded — the auto-landing preference needs the Wet/Islet flags before the world exists.</summary>
    private IReadOnlyList<LandingPad> PadsForBody(string locationId)
    {
        if (_world != null && string.Equals(locationId, _world.LocationId, System.StringComparison.Ordinal) && _landingPads.Count > 0)
        {
            return _landingPads;
        }

        var body = _galaxy?.FindBody(locationId);
        return body != null ? ComputeLandingPadsForBody(body) : System.Array.Empty<LandingPad>();
    }

    /// <summary>How many of a body's pads are currently free (live occupancy). <paramref name="exceptPlayerId"/>
    /// is the player the count is FOR (#999): their own in-space reservation doesn't block them (#977), so it
    /// must not read as "occupied" either — or the star map said "pads full" while the chooser one screen
    /// later happily offered their own pad. Pass <see cref="string.Empty"/> for a neutral count (NPC traders).</summary>
    private int FreePadCount(string locationId, int total, string exceptPlayerId)
    {
        int free = 0;
        for (int i = 0; i < total; i++)
        {
            if (!PadOccupiedByOther(locationId, i, exceptPlayerId))
            {
                free++;
            }
        }

        return free;
    }

    /// <summary>The name of the player currently holding a pad on a body, or null if it's free.</summary>
    private string? PadOccupantName(string locationId, int padIndex)
    {
        if (TraderPadOccupant(locationId, padIndex) is { } traderName)
        {
            return traderName; // a landed NPC trader is parked on this pad (P3)
        }

        foreach (var s in _sessions.Values)
        {
            // A holder up in space still reserves the pad (#957) — the chooser shows it as taken.
            // Observers hold nothing (#487/#996), so their name must never label a pad.
            if (s.Joined && !s.Spectating && s.AssignedPadIndex == padIndex && s.CurrentLocationId == locationId)
            {
                return s.State.Name;
            }
        }

        return null;
    }

    /// <summary>The occupancy of a pad AS SEEN BY one player: who holds it, whether that blocks them, and
    /// whether the holder is themselves. The distinction matters because a player keeps their pad reserved
    /// while they are up in space (#957) — sending that back as plain "occupied" made the chooser grey out
    /// the very pad the player was trying to return to, labelled with their own name (#977). Blocking is
    /// therefore computed exactly like the landing itself does it, excluding the player being served.</summary>
    private NetLandingPad PadStatusFor(string locationId, int padIndex, PlayerSession receiver)
    {
        string occupant = PadOccupantName(locationId, padIndex) ?? string.Empty;
        bool mine = occupant.Length > 0 && !PadOccupiedByOther(locationId, padIndex, receiver.State.PlayerId);
        return new NetLandingPad
        {
            Index = padIndex,
            Occupied = occupant.Length > 0 && !mine,
            Occupant = occupant,
            Mine = mine,
        };
    }

    /// <summary>Picks the pad a landing player will touch down on: their requested pad if it's free, else (for an
    /// auto request, index &lt; 0) the first free pad. Returns -1 and a reason if the pad is taken or the body is
    /// full. Validates against the destination body's deterministic pad count (works before the world is loaded).</summary>
    private int TryClaimPad(PlayerSession session, string locationId, int total, int requestedIndex, out string reason)
    {
        reason = string.Empty;
        if (total <= 0)
        {
            return 0; // a body with no pads (shouldn't happen) — touch down at the origin pad
        }

        if (requestedIndex >= 0)
        {
            if (requestedIndex >= total)
            {
                reason = "@srv.land.no_pad";
                return -1;
            }

            if (PadOccupiedByOther(locationId, requestedIndex, session.State.PlayerId))
            {
                reason = "@srv.land.pad_taken";
                return -1;
            }

            return requestedIndex;
        }

        // An auto request prefers dry ground over an islet over the seabed (#1621); a body whose pads could
        // not be computed (no galaxy body) falls back to the plain lowest-free rule.
        var pads = PadsForBody(locationId);
        int free = pads.Count == total
            ? PreferredFreePadIndex(locationId, pads, session.State.PlayerId)
            : FirstFreePadIndex(locationId, total, session.State.PlayerId);
        if (free < 0)
        {
            reason = "@srv.land.full";
        }

        return free;
    }

    /// <summary>The active world's pad for a player (their assigned one; auto-assigns the first free pad as a
    /// safety net if somehow unassigned, e.g. an initial spawn). Used to stamp the ship + place the player.</summary>
    private LandingPad PlayerPad(PlayerSession session)
    {
        if (_landingPads.Count == 0)
        {
            BuildLandingPads();
        }

        int idx = session.AssignedPadIndex;
        // An in-range index is NOT blindly trusted (#957): a stored/default index (fresh joiners carry 0)
        // may meanwhile be held by someone else — stamping there put two ships on one origin.
        if (idx < 0 || idx >= _landingPads.Count
            || PadOccupiedByOther(_world.LocationId, idx, session.State.PlayerId))
        {
            idx = PreferredFreePadIndex(_world.LocationId, _landingPads, session.State.PlayerId); // dry first (#1621)
            if (idx < 0)
            {
                idx = 0; // overflow: the body is full but an initial spawn must still place the player
            }

            session.AssignedPadIndex = idx;
        }

        return _landingPads[idx];
    }

    /// <summary>True if a cell falls within a pad's footprint columns (longitude-wrap aware).</summary>
    private bool OnPadColumn(LandingPad pad, int x, int z)
        => System.Math.Abs(WorldConstants.WrapDeltaX(x - pad.CenterX, _world.Circumference)) <= pad.Radius
            && System.Math.Abs(z - pad.CenterZ) <= pad.Radius;

    /// <summary>True if a cell lies in a landing pad's reserved <b>landing volume</b> — its footprint, from just
    /// below the ground up to the ship-clearance height. No one may build there (the pad is kept clear for ships);
    /// building high above the pad is fine. Mining is unaffected (only placing is blocked).</summary>
    private bool IsOnLandingPad(Vector3i pos)
    {
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, pos.X, pos.Z) && pos.Y >= pad.CenterY - 2 && pos.Y <= pad.CenterY + PadClearanceHeight)
            {
                return true;
            }
        }

        return false;
    }

    // --- landing flow + networking ---

    /// <summary>Claims the player's chosen (or first free) pad on a body before committing a landing. Sends a
    /// reject + returns false if the pad is taken or the body is full (so the caller leaves the player in flight),
    /// else records the pad on the session and returns true. Validates against the body's deterministic pad count,
    /// so it works whether or not the destination world is loaded yet.</summary>
    private bool ClaimPadOrReject(PlayerSession session, string bodyId, int padIndex)
    {
        var body = _galaxy?.FindBody(bodyId);
        int total = body != null ? PadCountFor(bodyId, body.PlanetType ?? string.Empty, body.Kind) : 1;
        int chosen = TryClaimPad(session, bodyId, total, padIndex, out string reason);
        if (chosen < 0)
        {
            Reject(session, "land", reason);
            return false;
        }

        session.AssignedPadIndex = chosen;
        return true;
    }

    /// <summary>Sets a player (and re-stamps their ship) down on the pad they claimed, on the body they're already
    /// on — used when landing back on the current body (the cross-body case goes through travel).</summary>
    private void RelocateToAssignedPad(PlayerSession session)
    {
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);
        MarkArrivedOnBody(session, session.CurrentLocationId); // touched down here → a quick-travel target
        if (_ship is not null)
        {
            _ship.CurrentLocationId = session.CurrentLocationId; // keep the ship's body in sync so a later launch rises off THIS body (mirrors HandleTravel; B48) — fixes launching off an asteroid landing you adrift in the wrong orbit
        }

        // An observer arrives with no ship, no pad and no respawn anchor (#487/#996) — mirrors HandleTravel,
        // which has exempted spectators from the pad/ship half of a landing since day one.
        if (_config.PlaceStarterShip && !session.Spectating)
        {
            PlaceLandedShip();
        }

        var pad = PlayerPad(session);
        int surfaceY = PadSurfaceY(pad.CenterX, pad.CenterZ); // the ship placement's height: median, raised over player builds (#1318)
        var spawn = _shipPlaced ? _healTank : new Vector3f(pad.CenterX + 0.5f, surfaceY + 2f, pad.CenterZ + 0.5f);
        session.State.Position = spawn;
        if (!session.Spectating)
        {
            session.State.RespawnPoint = _shipPlaced ? _healTank : spawn;
            session.State.AboardShip = true;
        }

        session.AwaitingSpawnAdopt = true; // #865: the client keeps streaming its pre-launch pose for a beat

        // While this player was away (space / a station world), block changes on THIS body were only
        // broadcast to the players present on it — their client's chunk view is stale now (the #957
        // "ghost blocks" / invisible-ship desync). Re-stream everything: chunk delivery is idempotent
        // client-side, so this self-heals whatever drifted, exactly like the cross-body travel path.
        session.SentChunks.Clear();

        // The touchdown must ride the RespawnNotice snap channel (Died=false → no death feedback): the
        // client DISCARDS a position that arrives on PlayerStateUpdate (same rule as the suit teleporter,
        // #414 N17), and unlike the cross-body travel path there is no WorldReset here to re-arm its spawn
        // snap. Without this the body stayed at the pad it launched from while the ship parked on the pad
        // the player picked in the chooser — "I landed and my ship isn't there" (#971).
        SendLandedShips(session); // the landing world's parked ship objects (incl. the player's own) — before the snap (#1450)
        StreamFootingNow(session, spawn);
        Send(session, new RespawnNotice { X = spawn.X, Y = spawn.Y, Z = spawn.Z, Reason = "@srv.land.touchdown" });
        SendPlayerState(session);
        BroadcastLandingPads(session); // the touchdown claimed a pad — everyone's map must show it (#1020)
        SyncAppearance(session); // faces + body paintings both ways — the launch dropped them (#982)
        // Parity with the cross-body travel path (#957): without these the HUD compass ship blip and the
        // world-map marker kept pointing at the pad of the PREVIOUS landing.
        SendShipPlacement(session);
        SendShipStations(session);
        SendShipCombatStatus(session);
        SendEnvironment(session);
        SendDoors(session);
        SendShipRepairStatus(session); // the repair panel follows the ship, not the last console press (#1561)
        BroadcastShipTransit(session, session.CurrentLocationId, pad.CenterX + 0.5f, surfaceY, pad.CenterZ + 0.5f, landing: true); // others see the descent
    }

    /// <summary>Sends the active body's pads + live occupancy to a player (on world entry) — drives the pad
    /// markers on the world map.</summary>
    private void SendLandingPads(PlayerSession session)
    {
        var pads = new NetLandingPad[_landingPads.Count];
        for (int i = 0; i < _landingPads.Count; i++)
        {
            var p = _landingPads[i];
            pads[i] = PadStatusFor(_world.LocationId, p.Index, session);
            pads[i].X = p.CenterX;
            pads[i].Z = p.CenterZ;
            pads[i].Wet = p.Wet;
            pads[i].Depth = p.Depth;
        }

        // This is the active body, so its day fraction is live (drives the world-map terminator client-side).
        Send(session, new LandingPadList { BodyId = _world.LocationId, Pads = pads, TimeOfDay = (float)_dayFraction });
    }

    /// <summary>Re-sends the active body's pad list to everyone on it. The list is otherwise a world-entry
    /// snapshot: a pad claimed or released AFTER a bystander arrived stayed free/anonymous on their world
    /// map forever (#1020). Occupancy (<see cref="NetLandingPad.Mine"/>) is receiver-relative, so this must
    /// send per session rather than broadcast one message. Players up in space are skipped — their pad
    /// chooser owns the client's single pad-list slot and a fresh list arrives with their touchdown —
    /// except <paramref name="always"/>, the player whose arrival triggered the update.</summary>
    private void BroadcastLandingPads(PlayerSession? always = null)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (s != always && InSpace(s.State.PlayerId))
            {
                continue;
            }

            SendLandingPads(s);
        }
    }

    /// <summary>The day fraction the player will arrive at when landing on <paramref name="bodyId"/>: the live time
    /// if it's the active body, else the activation default every world resets to. Lets the pad chooser draw the
    /// day/night terminator exactly where the surface will be on touchdown.</summary>
    private float BodyArrivalTimeOfDay(string bodyId)
        => string.Equals(bodyId, _world?.LocationId, System.StringComparison.Ordinal)
            ? (float)_dayFraction
            : (float)InitialDayFraction;

    /// <summary>Tells the players already on a body that another player's ship is arriving/departing at a pad, so
    /// they see a landing/launch animation (item 38). Sent only to the others on that body (not the mover, not
    /// anyone in space).</summary>
    private void BroadcastShipTransit(PlayerSession mover, string bodyId, float x, float y, float z, bool landing)
    {
        ShipTransitFx? msg = null;
        SpaceStructure? design = null;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s == mover || s.CurrentLocationId != bodyId || InSpace(s.State.PlayerId))
            {
                continue;
            }

            msg ??= new ShipTransitFx
            {
                PlayerId = mover.State.PlayerId,
                Name = mover.State.Name,
                X = x,
                Y = y,
                Z = z,
                Landing = landing,
                Hull = mover.HullColor,
            };

            // The mover's REAL voxel ship design rides ahead of the FX, so the watcher's animation
            // shows the actual ship that is landing/launching, not a generic silhouette.
            design ??= BuildShipStructure(mover.State.PlayerId);
            SendShipDesign(s, design, "ship_remote");
            Send(s, msg);
        }
    }

    /// <summary>Replies to a client's request for a body's pads + occupancy (the pad chooser shown before landing).
    /// The body may be remote (not loaded), so positions are the deterministic base longitudes — exact dry-land
    /// positions arrive once the player is actually on the body; the chooser only needs index + occupancy.</summary>
    private void HandleRequestLandingPads(PlayerSession session, RequestLandingPadsIntent intent)
    {
        // Empty id = the body the player launched from (same convention HandleLeaveSpace resolves).
        // The reply must echo the REQUESTED id: the client gates its chooser on that exact string, so
        // answering with the resolved id (or not answering at all) froze the flight forever (#956).
        string requestedId = intent.BodyId ?? string.Empty;
        string resolvedId = requestedId.Length == 0 ? (session.CurrentLocationId ?? string.Empty) : requestedId;
        var body = _galaxy?.FindBody(resolvedId);
        if (body is null)
        {
            Send(session, new LandingPadList { BodyId = requestedId, Pads = System.Array.Empty<NetLandingPad>() });
            return;
        }

        // Compute the body's REAL pads (same source of truth as the in-world placement), so the chooser map
        // shows each pad exactly where the ship will touch down — including its true latitude (Z), not a line.
        var computed = ComputeLandingPadsForBody(body);
        var pads = new NetLandingPad[computed.Count];
        for (int i = 0; i < computed.Count; i++)
        {
            var p = computed[i];
            pads[i] = PadStatusFor(body.Id, p.Index, session);
            pads[i].X = p.CenterX;
            pads[i].Z = p.CenterZ;
            pads[i].Wet = p.Wet; // seabed pad (#1454) — the chooser says so before the player commits
            pads[i].Depth = p.Depth; // …and how deep (#1622)
        }

        Send(session, new LandingPadList { BodyId = requestedId, Pads = pads, TimeOfDay = BodyArrivalTimeOfDay(body.Id) });
    }

    /// <summary>The real pad set for a body (the chooser path). Resolves the body's planet type + circumference,
    /// then delegates to the shared <see cref="ComputeLandingPads"/>. Empty for a body with no surface (a
    /// station/wreck you dock with rather than land on).</summary>
    private List<LandingPad> ComputeLandingPadsForBody(CelestialBody body)
    {
        var planet = _content.GetPlanet(body.PlanetType ?? string.Empty);
        if (planet is null)
        {
            return new List<LandingPad>();
        }

        int circ = WorldConstants.CircumferenceFor(body.Id, WorldConstants.SizeClassFor(body.Kind, body.PlanetType ?? string.Empty), body.SizeBias);
        return ComputeLandingPads(planet, body.Kind, body.Id, circ);
    }

    // --- test hooks ---

    /// <summary>Number of pads on the active world.</summary>
    public int LandingPadCount => _landingPads.Count;

    /// <summary>Test hook: the pad centres (index, x, z) the approach landing map / pad chooser would advertise
    /// for the active body — i.e. what <see cref="HandleRequestLandingPads"/> derives. It MUST equal
    /// <see cref="LandingPadCenters"/>: the chooser map shows each pad exactly where the ship lands.</summary>
    public IReadOnlyList<(int Index, int X, int Z)> ApproachMapPadsForTest()
    {
        var body = _galaxy?.FindBody(_world.LocationId);
        if (body is null)
        {
            return System.Array.Empty<(int, int, int)>();
        }

        return ComputeLandingPadsForBody(body).ConvertAll(p => (p.Index, p.CenterX, p.CenterZ));
    }

    /// <summary>Test hook: the number of pads the approach landing map advertises for the active body.</summary>
    public int ApproachMapPadCountForTest() => ApproachMapPadsForTest().Count;

    /// <summary>Pad centres (index, x, z) on the active world, for tests/inspection.</summary>
    public IReadOnlyList<(int Index, int X, int Z)> LandingPadCenters
        => _landingPads.ConvertAll(p => (p.Index, p.CenterX, p.CenterZ));

    /// <summary>Test hook: the active world's sea level (int.MinValue on a dry world).</summary>
    public int SeaLevelForTest() => _generator.SeaLevel(_world.Planet);

    /// <summary>Test hook: the 2-D dry-and-flat nudge from a planned column on the active world (#1618) —
    /// where the pad would end up, and whether that footprint is dry.</summary>
    public (int X, int Z, bool Dry) NudgePadForTest(int baseX, int baseZ, int budget)
    {
        int latP = WorldConstants.LatitudePeriodFor(_world.Circumference);
        int latBand = System.Math.Max(0, System.Math.Min((int)(latP * 0.38), latP / 2 - LandingPadRadius - 8));
        var (x, z) = NudgePadToDryAndFlat(_world.Planet, baseX, baseZ, latBand, budget);
        return (x, z, !LandingFootprintWet(_world.Planet, x, z));
    }

    /// <summary>Test hook: the player pad preference (#1621) over synthetic pads (wet, islet) with the given
    /// occupied indices — dry &gt; islet &gt; seabed, ties by index.</summary>
    public static int PreferredPadIndexForTest(IReadOnlyList<(bool Wet, bool Islet)> pads, IReadOnlyCollection<int> occupied)
    {
        int best = -1, bestRank = int.MaxValue;
        for (int i = 0; i < pads.Count; i++)
        {
            if (occupied.Contains(i))
            {
                continue;
            }

            int rank = PadRank(new LandingPad { Index = i, Wet = pads[i].Wet, Islet = pads[i].Islet });
            if (rank < bestRank)
            {
                bestRank = rank;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Test hook: a pad's centre, levelled ground height and its seabed/islet flags (#1453/#1454)
    /// plus the water depth over a seabed pad (#1622).</summary>
    public (int X, int Y, int Z, bool Wet, bool Islet, int Depth) LandingPadInfoForTest(int index)
    {
        if (_landingPads.Count == 0)
        {
            BuildLandingPads();
        }

        var pad = _landingPads[index];
        return (pad.CenterX, pad.CenterY, pad.CenterZ, pad.Wet, pad.Islet, pad.Depth);
    }

    /// <summary>Test hook: true if the active world's pad at this index sits on dry land (B36).</summary>
    public bool LandingPadIsDry(int index)
        => index >= 0 && index < _landingPads.Count && !LandingFootprintWet(_landingPads[index].CenterX, _landingPads[index].CenterZ);

    /// <summary>Test hook: true if a cell column lies on a reserved pad (checked at the pad's ground level).</summary>
    public bool IsOnLandingPadForTest(int x, int z)
    {
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, x, z))
            {
                return IsOnLandingPad(new Vector3i(x, pad.CenterY + 1, z));
            }
        }

        return false;
    }

    /// <summary>Test hook: how many of the active world's pads are currently free.</summary>
    public int FreePadCountForTest(string exceptPlayerId = "") => FreePadCount(_world.LocationId, _landingPads.Count, exceptPlayerId);

    /// <summary>Test hook: runs the landing-pad claim for a player (mirrors a landing). Returns the chosen pad
    /// index (or -1 with a reason if the pad is taken / the body is full).</summary>
    public (int Chosen, string Reason) TryClaimPadForTest(PlayerSession session, int padIndex)
    {
        int chosen = TryClaimPad(session, _world.LocationId, _landingPads.Count, padIndex, out string reason);
        return (chosen, reason);
    }

    /// <summary>Test hook: routes a pad-list request through the real chooser handler (the E-landing path, #956).</summary>
    public void RequestLandingPadsForTest(PlayerSession session, string bodyId)
    {
        Serve(session);
        HandleRequestLandingPads(session, new RequestLandingPadsIntent { BodyId = bodyId });
    }

    /// <summary>Test hook: lands a pilot back on the body they launched from (the same-body chooser path, #957).</summary>
    public void LandOnCurrentBodyForTest(PlayerSession session, int padIndex = -1)
    {
        Serve(session);
        HandleLeaveSpace(session, new LeaveSpaceIntent { DestinationBodyId = string.Empty, PadIndex = padIndex });
    }
}
