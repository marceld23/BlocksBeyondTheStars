// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>terrain shape: archetypes, styles, drama, grain, continents, the regional blend (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>One archetype's height offset (blocks relative to BaseHeight, before drama) at a column.
    /// <paramref name="h"/> is the base FBM swell in [-1,1]. Archetypes are explicit shapes rather than
    /// (amplitude, ridged) parameter pairs because the #576 additions — quantised decks, asymmetric
    /// gorges — cannot be expressed as parameters of one shared formula; the regional blend therefore
    /// lerps computed OFFSETS, not parameters.</summary>
    private double ArchetypeOffset(int archetype, PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        double amp = planet.Amplitude;
        double Ridge(double v) => (1.0 - System.Math.Abs(v)) * 2.0 - 1.0; // smooth swell → sharp ridge/valley

        switch (archetype)
        {
            case 0: return h * amp * 0.18; // flats
            case 1: return h * amp * 0.55; // rolling plains
            case 2: return h * amp * 1.00; // hills
            case 3: // mountains (lightly ridged; #700 — a directional share so ranges chain along the grain)
                return (h * 0.58 + Ridge(h) * 0.12
                        + OrientedRidge(seed, w.Grain, worldX, worldZ, planet.TerrainScale * 0.9) * 0.30)
                    * amp * 1.9;
            case 4: // canyons (strongly ridged)
                return (h * 0.35 + Ridge(h) * 0.65) * amp * 1.3;
            case 5: // plateau decks (#576): terraced mesa country as a REGION, not a whole-world style
                {
                    double raw = h * amp * 1.05;
                    double step = System.Math.Max(5.0, amp * 0.5);
                    double deck = System.Math.Floor(raw / step) * step;
                    double roll = FbmT(seed + 0x9D3C, worldX, worldZ, planet.TerrainScale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 2.0; // ±1-block texture so decks read as rock, not glass
                }

            case 6: // extreme peaks (#576): the far tail of relief, well above the mountains archetype
                {
                    // #700: the extreme crests follow the world's grain too — the tallest ranges chain.
                    double or6 = OrientedRidge(seed, w.Grain, worldX, worldZ, planet.TerrainScale * 0.9);
                    double r = h * 0.25 + Ridge(h) * 0.45 + or6 * 0.30;
                    if (r > 0)
                    {
                        r = System.Math.Pow(r, 1.6); // flatter mid-slopes, prouder crests
                    }

                    return r * amp * 3.4;
                }

            default: // 7: rift gorges (#576): gentle ground gashed by deep ridged canyons
                {
                    double g = Ridge(h);
                    double swell = h * amp * 0.3;
                    return g > 0 ? swell - System.Math.Pow(g, 2.2) * amp * 3.0 : swell;
                }
        }
    }

    /// <summary>Per-world terrain drama ("Welten reicher" W-R1): a seeded ~0.9–1.5× multiplier on the relief,
    /// so the same planet type rolls gentle on one world and jagged/dramatic on the next. A small tail of
    /// bodies (~6 %, #576) instead rolls 1.9–2.6× — the rare outlier world whose relief reads genuinely
    /// extreme. Craters (airless regolith) stay flat by design.</summary>
    private static double DramaFor(long seed)
    {
        ulong u = (ulong)(seed * 2654435761L);
        if (((u >> 40) & 0xFF) < 16)
        {
            return 1.9 + 0.7 * ((u >> 26 & 0x3FF) / 1023.0);
        }

        return 0.9 + 0.6 * ((u >> 16 & 0x3FF) / 1023.0);
    }

    /// <summary>Highest world Y natural terrain may reach — safely under the atmosphere line (~Y 320), so
    /// no peak ever pokes a player "into space" on foot (#577/#578). Landmark height rolls clamp against
    /// it; the final clamp here is the safety net for freak archetype × drama × landmark stacks.</summary>
    private const int MaxNaturalSurfaceY = 288;

    /// <summary>The terrain height WITHOUT the volcano overlay — the base field volcano geometry itself is
    /// anchored to (the crater's lava level derives from the pre-cone ground under the cone's centre).</summary>
    private int RawSurfaceHeight(PlanetType planet, int worldX, int worldZ)
        => RawSurfaceHeight(planet, WonderFor(planet), worldX, worldZ);

    private int RawSurfaceHeight(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        long seed = w.Seed;
        double n = FbmT(seed, worldX, worldZ, planet.TerrainScale, octaves: 4);
        double h = (n - 0.5) * 2.0; // [-1, 1] base rolling terrain

        // Airless moons + landable asteroids (item 33): mostly flat regolith (a gentle undulation only — no
        // hills/mountains/canyons) pocked with round impact craters carved on top. How rolling that regolith
        // is, and how dense/deep/sharp the craters are, is this BODY's own character (#518). Crater CHAINS
        // (#699) — aligned secondary-impact strings — carve on top of the primary field.
        if (w.Cratered)
        {
            double flat = h * CraterProfileFor(seed).Flatness * planet.Amplitude;
            return planet.BaseHeight + (int)System.Math.Round(
                flat + CraterCarve(seed, worldX, worldZ, planet) + CraterChainCarve(seed, worldX, worldZ));
        }

        double drama = DramaFor(seed); // W-R1: per-world relief multiplier (gentle ↔ dramatic)

        // Continents (#704, new worlds only): a bimodal platform/basin offset UNDER everything else, so
        // styles, archetypes and landmarks simply ride on the continent or drown in the ocean basin.
        double baseline = w.Continent.Active ? ContinentOffset(w.Continent, seed, worldX, worldZ) : 0.0;

        // Whole-planet escarpment (#702): a rare two-storey world — the step is part of the baseline too.
        if (w.Escarpment)
        {
            baseline += EscarpmentOffset(seed, worldX, worldZ);
        }

        // Salt polygons (#701): the cracked-plate ridge network of salt pans.
        if (w.SaltPolygons)
        {
            baseline += SaltPolygonRidge(seed, worldX, worldZ);
        }

        // Basalt column fields (#701): stepped hex prisms on volcanic-reading worlds.
        if (w.BasaltFields && TryGetBasaltColumns(seed, worldX, worldZ, out double hexRise))
        {
            baseline += hexRise;
        }

        // A planet may dictate an overall terrain SHAPE (item 21 V2) so worlds read structurally different —
        // mesas, dunes, spires, etc. — instead of every world using the same mixed blend. Since #703 a broad
        // fade field hands 20–40 % of most styled worlds to the archetype blend, so a dunes world has gravel
        // plains between its dune seas instead of being dunes from pole to pole.
        if (w.Style.Length != 0)
        {
            double styled = StyledHeightOffset(planet, w, seed, h, worldX, worldZ);
            if (w.HybridEligible)
            {
                double fade = FbmT(seed + 0x57FAD1, worldX, worldZ, planet.TerrainScale * 6.0, octaves: 2);
                if (fade < w.HybridB)
                {
                    double arch = BlendedArchetypeOffset(planet, w, seed, h, worldX, worldZ);
                    if (fade <= w.HybridA)
                    {
                        styled = arch;
                    }
                    else
                    {
                        double f = (fade - w.HybridA) / (w.HybridB - w.HybridA);
                        styled = arch + (styled - arch) * (f * f * (3.0 - 2.0 * f));
                    }
                }
            }

            return planet.BaseHeight + (int)System.Math.Round(baseline + styled * drama);
        }

        // Regional terrain character: a large-scale field selects how rugged this area is (a blend across
        // the world's archetype subset), so the surface varies between flat plains, hills, mountains — and,
        // where the subset drew the #576 archetypes, terraced decks, extreme crests or rift gorges.
        return planet.BaseHeight + (int)System.Math.Round(baseline + BlendedArchetypeOffset(planet, w, seed, h, worldX, worldZ) * drama);
    }

    /// <summary>Styles that hand a rolled 20–40 % of their surface to the archetype blend (#703). The
    /// identity styles stay pure: flats IS the world (ocean floor, salt pan, sky-world ground) and spires
    /// is the crystal identity. Expects an ALREADY-LOWERED style (#712 — no per-column allocations).</summary>
    private static bool StyleHybridEligible(string loweredStyle) => loweredStyle switch
    {
        "mountains" or "canyons" or "mesa" or "dunes" or "hills" or "tablelands" or "badlands" or "karst" => true,
        _ => false,
    };

    /// <summary>The vertical band [<paramref name="bottom"/>..<paramref name="top"/>] of a floating sky island at
    /// this column (item 21 V5), or <c>false</c> if no island covers it. The single source of truth for the
    /// island mask — used by chunk generation AND by settlement placement, so both agree on island heights.</summary>
    public bool FloatingIslandBand(PlanetType planet, int worldX, int worldZ, out int top, out int bottom)
    {
        top = int.MinValue;
        bottom = int.MaxValue;
        if (!planet.FloatingIslands)
        {
            return false;
        }

        // Tier 0 of the (#707) multi-tier sky: same seeds and shaping as the classic single band, so
        // existing sky worlds keep their islands; upper tiers are GetExtraBands' business.
        return FloatingIslandTier(planet, WonderFor(planet).Seed, 0, worldX, worldZ, out top, out bottom, out _);
    }

    /// <summary>The TOP world-Y of a floating sky island at this column, or <see cref="int.MinValue"/> if none.</summary>
    public int FloatingIslandTop(PlanetType planet, int worldX, int worldZ)
        => FloatingIslandBand(planet, worldX, worldZ, out int top, out _) ? top : int.MinValue;

    // --- Terrain grain (#700): a per-world direction for dunes and mountain chains, expressed torus-safely
    // as integer stretch + period-normalised shear of the noise input. Rotation would break the wrap
    // periods; these two provably preserve them: X' = x·m advances the X circle a whole m laps per wrap,
    // and the shear couples the axes in UNITS OF THE TARGET AXIS'S PERIOD — Z' = z + k·(P/C)·x shifts by
    // exactly k·P under an X wrap (k whole Z periods) for ANY integers k, m, and any C/P ratio (the
    // latitude period is rounded to 32-block chunks, so it is NOT exactly C/2 — a naive x-coefficient
    // shear would tear the seam). ---
    private readonly struct TerrainGrain
    {
        public readonly bool Swap;    // false: compress X, shear Z; true: compress Z, shear X
        public readonly int Stretch;  // integer compression of the primary axis
        public readonly int Shear;    // integer shear coefficient (any int is wrap-safe in both families)

        public TerrainGrain(bool swap, int stretch, int shear)
        {
            Swap = swap;
            Stretch = stretch;
            Shear = shear;
        }
    }

    private static TerrainGrain GrainFor(long seed)
    {
        ulong u = Noise.Hash(seed ^ 0x06172A17, 5, 9, 2);
        bool swap = (u & 1UL) != 0;
        int stretch = 3 + (int)((u >> 1) & 0x3) % 3;          // 3..5
        int shear = (int)((u >> 3) & 0x3) - 1;                // −1..2
        if (shear == 0 && ((u >> 5) & 1UL) != 0)
        {
            shear = 2; // keep a healthy share of visibly diagonal worlds
        }

        return new TerrainGrain(swap, stretch, shear);
    }

    /// <summary>Samples an FBM field in this world's grain direction (#700): the primary axis is integer-
    /// stretched (whole extra wrap laps → seam-exact) and the cross axis sheared in units of its own
    /// period, so the torus wrap stays seamless on both axes for any world size.</summary>
    private double GrainFbm(long seed, in TerrainGrain g, int worldX, int worldZ, double scale, int octaves)
    {
        double period = LatPeriod;
        return g.Swap
            ? FbmT(seed, worldX + g.Shear * (_circumference / period) * worldZ, (double)worldZ * g.Stretch, scale, octaves)
            : FbmT(seed, (double)worldX * g.Stretch, worldZ + g.Shear * (period / _circumference) * worldX, scale, octaves);
    }

    /// <summary>An oriented ridge value in [−1,1] for mountain-chain shaping (#700): crests elongate along
    /// the world's grain instead of blobbing isotropically.</summary>
    private double OrientedRidge(long seed, in TerrainGrain g, int worldX, int worldZ, double scale)
    {
        double o = GrainFbm(seed + 0x06172A18, g, worldX, worldZ, scale, octaves: 2);
        return (1.0 - System.Math.Abs(2.0 * o - 1.0)) * 2.0 - 1.0;
    }

    // --- Continents & real oceans (#704, new worlds only): a very-low-frequency continentalness field
    // pushes eligible large planets into a BIMODAL height regime — continental platform vs ocean basin,
    // joined by a smoothstep shelf. Everything else (styles, archetypes, landmarks, sea percentile,
    // rivers, beaches) simply rides on top. Domain-warped so coastlines are ragged, not blobby. ---
    // The size gate: planets only (moons are 2500–4000). Lowered 8000 → 6000 (user decision 2026-08-03)
    // so the START world (body key "varied" hashes to circumference 6064 in every save) is eligible —
    // at 8000 no player ever saw a continent without flying to a big planet first.
    public const int ContinentMinCircumference = 6000;

    private readonly struct ContinentProfile
    {
        public readonly bool Active;
        public readonly double Wavelength;  // continent pitch (blocks): circumference / 4..7
        public readonly double Threshold;   // continentalness above this is land
        public readonly double Shelf;       // half-width of the shelf smoothstep, in field units
        public readonly double BasinDepth;  // ocean basin floor below base (35..60)
        public readonly double Lift;        // platform lift above base (6..14)
        public readonly double LandFrac;    // rolled target land fraction (0.25..0.55)

        public ContinentProfile(bool active, double wavelength, double threshold, double shelf,
            double basinDepth, double lift, double landFrac)
        {
            Active = active;
            Wavelength = wavelength;
            Threshold = threshold;
            Shelf = shelf;
            BasinDepth = basinDepth;
            Lift = lift;
            LandFrac = landFrac;
        }
    }

    /// <summary>This body's continent roll (#704). Inactive unless the world was created with continents,
    /// the body is a large planet (≥ <see cref="ContinentMinCircumference"/>), carries a sea to relocate
    /// (water — or lava on the lava/ashen worlds: basalt continents in a lava ocean), and wins the ~50 %
    /// per-body roll. The ocean type keeps its 78–97 %-flooded identity and never rolls continents.</summary>
    /// <summary>Whether a continental roll on this type means basalt continents in a LAVA ocean (#704; #1644:
    /// the `volcanic` tag replaces the lava/ashen key check).</summary>
    private static bool LavaOceanContinentsFor(PlanetType planet)
    {
        bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
        return (planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0)) > 0.0 && planet.HasTag(TerrainTag.Volcanic);
    }

    private ContinentProfile ContinentProfileFor(PlanetType planet, long seed)
    {
        if (!_continentsEnabled || _circumference < ContinentMinCircumference
            || planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return default;
        }

        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        if (!hasAir)
        {
            return default;
        }

        double waterAb = planet.WaterAbundance ?? 0.55;
        bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
        bool lavaOcean = LavaOceanContinentsFor(planet);
        if (waterAb >= 1.0 || (waterAb < 0.3 && !lavaOcean))
        {
            return default;
        }

        ulong u = Noise.Hash(seed ^ 0x0C047A9E, 7, 7, 7);
        if ((u & 0xFF) >= 128)
        {
            return default; // ~50 % of eligible bodies stay classic noise-coast worlds
        }

        double k = 4.0 + ((u >> 8) & 0x3);                       // 4..7 → 1–2 supercontinents .. many landmasses
        double landFrac = 0.25 + ((u >> 12) & 0x3FF) / 1023.0 * 0.30; // 25..55 % land
        double threshold = 0.62 - landFrac * 0.35;               // rough map; the sea percentile makes it exact
        double shelf = 0.02 + ((u >> 22) & 0xFF) / 255.0 * 0.025;
        double basin = 35.0 + ((u >> 30) & 0x3FF) / 1023.0 * 25.0; // 35..60
        double lift = 6.0 + ((u >> 40) & 0xFF) / 255.0 * 8.0;       // 6..14
        return new ContinentProfile(true, _circumference / k, threshold, shelf, basin, lift, landFrac);
    }

    /// <summary>The continental platform/basin offset at a column (#704). Domain-warped continentalness →
    /// smoothstep between −BasinDepth and +Lift across the shelf band. Where the field hovers near the
    /// threshold the shelf yields shallow seas and island arcs. Callers gate on <c>p.Active</c> (#712).</summary>
    private double ContinentOffset(in ContinentProfile p, long seed, int worldX, int worldZ)
    {
        double warpAmp = p.Wavelength * 0.1;
        double wx = worldX + (FbmT(seed + 0x0C047AA0, worldX, worldZ, p.Wavelength / 3.0, octaves: 2) - 0.5) * 2.0 * warpAmp;
        double wz = worldZ + (FbmT(seed + 0x0C047AA1, worldX, worldZ, p.Wavelength / 3.0, octaves: 2) - 0.5) * 2.0 * warpAmp;
        double c = FbmT(seed + 0x0C047AA2, wx, wz, p.Wavelength, octaves: 3);
        double m = System.Math.Clamp((c - (p.Threshold - p.Shelf)) / (2.0 * p.Shelf), 0.0, 1.0);
        m = m * m * (3.0 - 2.0 * m);
        return -p.BasinDepth + m * (p.BasinDepth + p.Lift);
    }

    /// <summary>Height offset (blocks, added to BaseHeight) for a planet with an explicit <see cref="PlanetType.TerrainStyle"/>
    /// (item 21 V2). <paramref name="h"/> is the base FBM swell in [-1,1]. Each style reshapes it into a distinct
    /// landform so worlds look structurally different. Deterministic + seam-safe (all noise wraps on X).
    /// Dispatches on the profile's pre-lowered style and pre-rolled grain (#712 — no per-column strings).</summary>
    private double StyledHeightOffset(PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        double amp = planet.Amplitude;
        double Ridge(double v) => (1.0 - System.Math.Abs(v)) * 2.0 - 1.0; // smooth swell → sharp ridge/valley

        switch (w.Style)
        {
            case "flats":
                return h * amp * 0.22; // near-flat plains (salt flats, ocean floor, low islands)

            case "hills":
                return h * amp * 0.75; // gentle rolling hills

            case "mountains":
                {
                    // #700: part of the ridging comes from an oriented field, so ranges form CHAINS along
                    // the world's grain instead of isotropic knots.
                    double or = OrientedRidge(seed, w.Grain, worldX, worldZ, planet.TerrainScale * 0.9);
                    double r = h * 0.25 + Ridge(h) * 0.30 + or * 0.45; // sharp, rugged, directional
                    if (r > 0)
                    {
                        r = System.Math.Pow(r, 1.35); // W-R1 crest sharpening: flatter mid-slopes, prouder peaks
                    }

                    return r * amp * 1.9;
                }

            case "canyons":
                {
                    double r = h * 0.35 + Ridge(h) * 0.65;
                    if (r < 0)
                    {
                        r = -System.Math.Pow(-r, 0.8); // W-R1: broader, deeper canyon floors below the mesatops
                    }

                    return r * amp * 1.4; // deep ridged canyons + mesatops
                }

            case "mesa":
                {
                    // Terraced plateaus: quantise the height into flat decks separated by sharp cliffs, with a little
                    // roll on each deck so the tops aren't dead flat.
                    double raw = h * amp * 1.15;
                    double step = System.Math.Max(3.0, amp * 0.30);
                    double deck = System.Math.Floor(raw / step) * step;
                    double roll = FbmT(seed + 0x3E5A, worldX, worldZ, planet.TerrainScale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 2.0; // ±2-block texture on each deck
                }

            case "dunes":
                {
                    // Parallel wind-blown ridges: a ridged mid-frequency field laid over a gentle base.
                    // Since #700 the field is sampled in the world's GRAIN — every desert rolls a wind
                    // direction and its dune crests march that way instead of blobbing isotropically.
                    double d = GrainFbm(seed + 0x0D0E, w.Grain, worldX, worldZ, planet.TerrainScale * 0.45, octaves: 2);
                    double ridged = 1.0 - System.Math.Abs(d * 2.0 - 1.0); // 0..1 dune crests
                    return h * amp * 0.25 + ridged * amp * 0.85;
                }

            case "spires":
                {
                    // Mostly flat ground studded with sparse tall thin spikes (crystal needles / alien towers).
                    double basep = h * amp * 0.22;
                    double mask = FbmT(seed + 0x591E, worldX, worldZ, planet.TerrainScale * 0.4, octaves: 2);
                    if (mask > 0.72)
                    {
                        double t = (mask - 0.72) / 0.28; // 0..1 toward the spike centre
                        return basep + t * t * amp * 2.6;
                    }

                    return basep;
                }

            case "tablelands":
                {
                    // Grand mesa country (#579): a few MONUMENTAL terrace decks with escarpment cliffs —
                    // the mesa style scaled up (decks of 8+ blocks instead of ~4).
                    double raw = h * amp * 1.2;
                    double step = System.Math.Max(8.0, amp * 0.45);
                    double deck = System.Math.Floor(raw / step) * step;
                    double roll = FbmT(seed + 0x7B1D, worldX, worldZ, planet.TerrainScale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 3.0; // rough rock texture on each deck
                }

            case "badlands":
                {
                    // Fine-ridged gully country (#579): dense sharp crests + broad eroded floors over a
                    // modest base swell — canyon geology at a smaller, busier wavelength.
                    double fine = FbmT(seed + 0x0BAD, worldX, worldZ, planet.TerrainScale * 0.35, octaves: 3);
                    double r = h * 0.3 + (1.0 - System.Math.Abs(fine * 2.0 - 1.0)) * 1.4 - 0.7;
                    if (r < 0)
                    {
                        r = -System.Math.Pow(-r, 0.85); // broaden the gully floors
                    }

                    return r * amp * 1.1;
                }

            case "karst":
                {
                    // Karst tower country (#579): steep stone towers with flat, walkable tops rising from
                    // rolling green ground — denser than spires, and capped instead of needle-pointed.
                    double basep = h * amp * 0.35;
                    double mask = FbmT(seed + 0x4A85, worldX, worldZ, planet.TerrainScale * 0.5, octaves: 2);
                    if (mask > 0.62)
                    {
                        double t = System.Math.Min(1.0, (mask - 0.62) / 0.22); // capped → flat tower top
                        return basep + t * t * (3.0 - 2.0 * t) * amp * 1.9;
                    }

                    return basep;
                }

            default:
                return h * amp; // unknown style → plain base swell
        }
    }

    /// <summary>The blended archetype height offset for a column: a large-scale region field picks among
    /// the world's seed-chosen subset of archetypes (deterministic, seam-free across the X wrap) and
    /// smoothstep-blends the two neighbours' computed OFFSETS (#576 — shapes like quantised decks or
    /// asymmetric gorges cannot be blended as parameters).</summary>
    private double BlendedArchetypeOffset(PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        int pool = TerrainArchetypeCount;
        long s = seed ^ 0x7E44A1;
        ulong us = (ulong)(s < 0 ? -s : s);
        int count = 2 + (int)(us % (ulong)(pool - 1)); // this world uses 2..pool archetypes
        int rot = (int)((us >> 8) % (ulong)pool);       // …starting at a seed-rotated offset in the list

        // A broad field (much larger than the base terrain) picks a position across the subset + blends it.
        double rug = FbmT(s, worldX, worldZ, planet.TerrainScale * 6.0, octaves: 3);
        double pos = (rug < 0 ? 0 : (rug > 0.9999 ? 0.9999 : rug)) * count; // [0, count)
        int i0 = (int)pos;
        int i1 = i0 + 1 < count ? i0 + 1 : count - 1;
        double t = pos - i0;
        double f = t * t * (3.0 - 2.0 * t); // smoothstep blend between adjacent archetypes

        int a0 = (rot + i0) % pool;
        int a1 = (rot + i1) % pool;
        double o0 = ArchetypeOffset(a0, planet, w, seed, h, worldX, worldZ);
        if (a1 == a0 || f <= 0.0)
        {
            return o0;
        }

        return o0 + (ArchetypeOffset(a1, planet, w, seed, h, worldX, worldZ) - o0) * f;
    }
}
