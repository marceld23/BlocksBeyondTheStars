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
    /// lerps computed OFFSETS, not parameters. Entries 8–10 exist on generation-1 worlds only (#1645).</summary>
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
                        + OrientedRidge(seed, w.Grain, worldX, worldZ, w.Scale * 0.9) * 0.30)
                    * amp * 1.9;
            case 4: // canyons (strongly ridged)
                return (h * 0.35 + Ridge(h) * 0.65) * amp * 1.3;
            case 5: // plateau decks (#576): terraced mesa country as a REGION, not a whole-world style
                {
                    double raw = h * amp * 1.05;
                    double step = System.Math.Max(5.0, amp * 0.5);
                    double deck = System.Math.Floor(raw / step) * step;
                    double roll = FbmT(seed + 0x9D3C, worldX, worldZ, w.Scale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 2.0; // ±1-block texture so decks read as rock, not glass
                }

            case 6: // extreme peaks (#576): the far tail of relief, well above the mountains archetype
                {
                    // #700: the extreme crests follow the world's grain too — the tallest ranges chain.
                    double or6 = OrientedRidge(seed, w.Grain, worldX, worldZ, w.Scale * 0.9);
                    double r = h * 0.25 + Ridge(h) * 0.45 + or6 * 0.30;
                    if (r > 0)
                    {
                        r = System.Math.Pow(r, 1.6); // flatter mid-slopes, prouder crests
                    }

                    return r * amp * 3.4;
                }

            case 7: // rift gorges (#576): gentle ground gashed by deep ridged canyons
                {
                    double g = Ridge(h);
                    double swell = h * amp * 0.3;
                    return g > 0 ? swell - System.Math.Pow(g, 2.2) * amp * 3.0 : swell;
                }

            case 8: // moorland (#1645): near-flat heath dimpled with shallow pans — the pond mask fills them
                {
                    double p = FbmT(seed + 0x3003, worldX, worldZ, w.Scale * 0.35, octaves: 2);
                    double pan = p < 0.3 ? -(0.3 - p) / 0.3 * amp * 0.35 : 0.0;
                    return h * amp * 0.3 + pan;
                }

            case 9: // knob-and-kettle (#1645): dense small domes and pits, glacial-drift country
                {
                    double k = FbmT(seed + 0x4E0B, worldX, worldZ, w.Scale * 0.3, octaves: 3);
                    double c = k * 2.0 - 1.0;
                    double sharp = System.Math.Sign(c) * System.Math.Pow(System.Math.Abs(c), 0.7);
                    return h * amp * 0.4 + sharp * amp * 0.9;
                }

            default: // 10: coastal cliffs (#1645): a relief step where the swell crosses zero — cliff lines, not ramps
                {
                    double s = System.Math.Clamp((h + 0.05) / 0.10, 0.0, 1.0);
                    s = s * s * (3.0 - 2.0 * s);
                    return h * amp * 0.6 + (s - 0.5) * amp * 1.2;
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
        double n = FbmT(seed, worldX, worldZ, w.Scale, octaves: 4);
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

        // Generation-1 baseline regimes (#1645): a second escarpment (three storeys), a tilted world, an
        // equatorial ridge. Rare rolls, all part of the baseline like the escarpment.
        if (w.Stepped)
        {
            baseline += EscarpmentOffset(seed ^ SteppedSeedSalt, worldX, worldZ);
        }

        if (w.Tilted)
        {
            baseline += TiltOffset(seed, worldZ);
        }

        if (w.EquatorRidge)
        {
            baseline += EquatorialRidgeOffset(seed, worldX, worldZ);
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
        // plains between its dune seas instead of being dunes from pole to pole. Generation-1 worlds (#1645)
        // roll 1–3 styles from the type's pool and lay them out as REGIONS (see StyleOffset).
        double relief;
        if (w.Styles.Length != 0)
        {
            double styled = StyleOffset(planet, w, seed, h, worldX, worldZ);
            if (w.HybridEligible)
            {
                double fade = FbmT(seed + 0x57FAD1, worldX, worldZ, w.Scale * 6.0, octaves: 2);
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

            relief = styled;
        }
        else
        {
            // Regional terrain character: a large-scale field selects how rugged this area is (a blend across
            // the world's archetype subset), so the surface varies between flat plains, hills, mountains — and,
            // where the subset drew the #576 archetypes, terraced decks, extreme crests or rift gorges.
            relief = BlendedArchetypeOffset(planet, w, seed, h, worldX, worldZ);
        }

        // Biome relief (#1645, generation 1): a biome may damp or boost the relief under it (a mud marsh lies
        // flatter than the stone country next to it). Read through the REGION field only — never the
        // altitude share of the biome pick — so relief cannot feed back into its own multiplier.
        if (w.ReliefMuls is { } muls)
        {
            relief *= ReliefMulAt(muls, seed, worldX, worldZ);
        }

        return planet.BaseHeight + (int)System.Math.Round(baseline + relief * drama);
    }

    /// <summary>Styles that hand a rolled 20–40 % of their surface to the archetype blend (#703). The
    /// identity styles stay pure: flats IS the world (ocean floor, salt pan, sky-world ground) and spires
    /// is the crystal identity. Expects an ALREADY-LOWERED style (#712 — no per-column allocations).</summary>
    private static bool StyleHybridEligible(string loweredStyle) => loweredStyle switch
    {
        "mountains" or "canyons" or "mesa" or "dunes" or "hills" or "tablelands" or "badlands" or "karst" => true,
        "fjordlands" or "downs" or "shattered" or "terraces" or "drumlins" or "glacial" => true, // #1645
        _ => false,
    };

    /// <summary>Every style name <see cref="StyledHeightOffset"/> knows (content validation + tests).</summary>
    public static readonly string[] KnownTerrainStyles =
    {
        "flats", "hills", "mountains", "canyons", "mesa", "dunes", "spires", "tablelands", "badlands", "karst",
        "archipelago", "fjordlands", "downs", "shattered", "terraces", "drumlins", "glacial", // #1645
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

    // --- Regional style pools (#1645, generation 1): a world rolls 1–3 styles from its type's pool and a
    // broad field (Scale × 8) partitions the surface into style regions. Inside a region the style is pure;
    // across a boundary band the two neighbours' OFFSETS blend with a smoothstep (the archetype blend's
    // method — shapes like decks or spires cannot be blended as parameters). ---
    private const double StyleBlendHalf = 0.15; // half-width of the boundary band in region units (70 % pure)

    /// <summary>The styled offset at a column: the single style directly (every generation-0 styled world,
    /// and generation-1 worlds that rolled one style), else the regional blend of the rolled styles.</summary>
    private double StyleOffset(PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        var styles = w.Styles;
        if (styles.Length == 1)
        {
            return StyledHeightOffset(styles[0], planet, w, seed, h, worldX, worldZ);
        }

        double n = FbmT(seed + 0x57F1E5, worldX, worldZ, w.Scale * 8.0, octaves: 2);
        double spread = System.Math.Clamp((n - 0.5) * 2.4 + 0.5, 0.0, 0.9999);
        double pos = spread * styles.Length;
        int i0 = (int)pos;
        double t = pos - i0;
        double o0 = StyledHeightOffset(styles[i0], planet, w, seed, h, worldX, worldZ);

        // Boundary bands: the last 15 % of a region fades toward the next style, the first 15 % from the
        // previous one — continuous across the integer boundary (0.5 on both sides).
        if (t > 1.0 - StyleBlendHalf && i0 + 1 < styles.Length)
        {
            double f = (t - (1.0 - StyleBlendHalf)) / (2.0 * StyleBlendHalf);
            f = f * f * (3.0 - 2.0 * f);
            double o1 = StyledHeightOffset(styles[i0 + 1], planet, w, seed, h, worldX, worldZ);
            return o0 + (o1 - o0) * f;
        }

        if (t < StyleBlendHalf && i0 > 0)
        {
            double f = (t + StyleBlendHalf) / (2.0 * StyleBlendHalf);
            f = f * f * (3.0 - 2.0 * f);
            double oPrev = StyledHeightOffset(styles[i0 - 1], planet, w, seed, h, worldX, worldZ);
            return oPrev + (o0 - oPrev) * f;
        }

        return o0;
    }

    /// <summary>Which style region a column falls in on a multi-style world (tests): the index into
    /// <see cref="WonderProfile.Styles"/> whose offset dominates here.</summary>
    internal string StyleAtForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (w.Styles.Length == 0)
        {
            return string.Empty;
        }

        if (w.Styles.Length == 1)
        {
            return w.Styles[0];
        }

        double n = FbmT(w.Seed + 0x57F1E5, worldX, worldZ, w.Scale * 8.0, octaves: 2);
        double spread = System.Math.Clamp((n - 0.5) * 2.4 + 0.5, 0.0, 0.9999);
        return w.Styles[(int)(spread * w.Styles.Length)];
    }

    /// <summary>The biome relief multiplier at a column (#1645): the biome REGION field alone (the same field
    /// <see cref="BiomeIndex"/> spreads, without its altitude share) picks the resolved biome whose
    /// <c>ReliefMul</c> applies; a ±10 % band around each region boundary lerps the two multipliers so relief
    /// never steps at a biome edge.</summary>
    private double ReliefMulAt(double[] muls, long seed, int worldX, int worldZ)
    {
        double n = FbmT(seed ^ 0x0B10E, worldX, worldZ, 360.0, octaves: 3);
        double spread = System.Math.Clamp((n - 0.5) * 2.4 + 0.5, 0.0, 0.9999);
        double pos = spread * muls.Length;
        int i0 = (int)pos;
        double t = pos - i0;
        const double band = 0.10;
        if (t > 1.0 - band && i0 + 1 < muls.Length)
        {
            double f = (t - (1.0 - band)) / (2.0 * band);
            return muls[i0] + (muls[i0 + 1] - muls[i0]) * f;
        }

        if (t < band && i0 > 0)
        {
            double f = (t + band) / (2.0 * band);
            return muls[i0 - 1] + (muls[i0] - muls[i0 - 1]) * f;
        }

        return muls[i0];
    }

    /// <summary>The biome relief multiplier applied at a column (tests; 1.0 where the feature is off).</summary>
    internal double ReliefMulAtForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.ReliefMuls is { } muls ? ReliefMulAt(muls, w.Seed, worldX, worldZ) : 1.0;
    }

    /// <summary>One style's raw offset at a column (tests).</summary>
    internal double StyledOffsetForTest(string style, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        double n = FbmT(w.Seed, worldX, worldZ, w.Scale, octaves: 4);
        return StyledHeightOffset(style, planet, w, w.Seed, (n - 0.5) * 2.0, worldX, worldZ);
    }

    /// <summary>Height offset (blocks, added to BaseHeight) for a planet with an explicit <see cref="PlanetType.TerrainStyle"/>
    /// (item 21 V2). <paramref name="h"/> is the base FBM swell in [-1,1]. Each style reshapes it into a distinct
    /// landform so worlds look structurally different. Deterministic + seam-safe (all noise wraps on X).
    /// Dispatches on a pre-lowered style and the profile's pre-rolled grain (#712 — no per-column strings).
    /// The wavelength is the profile's <see cref="WonderProfile.Scale"/> (the type's TerrainScale on
    /// generation-0 worlds, jittered per body from generation 1 — #1645).</summary>
    private double StyledHeightOffset(string style, PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        double amp = planet.Amplitude;
        double Ridge(double v) => (1.0 - System.Math.Abs(v)) * 2.0 - 1.0; // smooth swell → sharp ridge/valley

        switch (style)
        {
            case "flats":
                return h * amp * 0.22; // near-flat plains (salt flats, ocean floor, low islands)

            case "hills":
                return h * amp * 0.75; // gentle rolling hills

            case "mountains":
                {
                    // #700: part of the ridging comes from an oriented field, so ranges form CHAINS along
                    // the world's grain instead of isotropic knots.
                    double or = OrientedRidge(seed, w.Grain, worldX, worldZ, w.Scale * 0.9);
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
                    double roll = FbmT(seed + 0x3E5A, worldX, worldZ, w.Scale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 2.0; // ±2-block texture on each deck
                }

            case "dunes":
                {
                    // Parallel wind-blown ridges: a ridged mid-frequency field laid over a gentle base.
                    // Since #700 the field is sampled in the world's GRAIN — every desert rolls a wind
                    // direction and its dune crests march that way instead of blobbing isotropically.
                    double d = GrainFbm(seed + 0x0D0E, w.Grain, worldX, worldZ, w.Scale * 0.45, octaves: 2);
                    double ridged = 1.0 - System.Math.Abs(d * 2.0 - 1.0); // 0..1 dune crests
                    return h * amp * 0.25 + ridged * amp * 0.85;
                }

            case "spires":
                {
                    // Mostly flat ground studded with sparse tall thin spikes (crystal needles / alien towers).
                    double basep = h * amp * 0.22;
                    double mask = FbmT(seed + 0x591E, worldX, worldZ, w.Scale * 0.4, octaves: 2);
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
                    double roll = FbmT(seed + 0x7B1D, worldX, worldZ, w.Scale * 0.5, octaves: 2);
                    return deck + (roll - 0.5) * 3.0; // rough rock texture on each deck
                }

            case "badlands":
                {
                    // Fine-ridged gully country (#579): dense sharp crests + broad eroded floors over a
                    // modest base swell — canyon geology at a smaller, busier wavelength.
                    double fine = FbmT(seed + 0x0BAD, worldX, worldZ, w.Scale * 0.35, octaves: 3);
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
                    double mask = FbmT(seed + 0x4A85, worldX, worldZ, w.Scale * 0.5, octaves: 2);
                    if (mask > 0.62)
                    {
                        double t = System.Math.Min(1.0, (mask - 0.62) / 0.22); // capped → flat tower top
                        return basep + t * t * (3.0 - 2.0 * t) * amp * 1.9;
                    }

                    return basep;
                }

            // ---- generation-1 styles (#1645) ----

            case "archipelago":
                {
                    // Flats studded with a dense field of island domes: with the sea calibrated high, hundreds
                    // of islets; on dry land, a knoll country. One dome per hotspot cell (cell ≈120, 60 % of cells).
                    double basep = h * amp * 0.22;
                    if (!TryGetHotspot(seed ^ 0x0A9C1, ArchipelagoCellSize, 0.6, 40.0, worldX, worldZ,
                            out ulong hh, out double dx, out double dz))
                    {
                        return basep;
                    }

                    double radius = 25.0 + ((hh >> 16) & 0x3FF) / 1023.0 * 35.0;   // 25..60
                    double height = amp * (1.2 + ((hh >> 26) & 0x3FF) / 1023.0 * 1.0); // 1.2..2.2 × amp
                    double dist = System.Math.Sqrt(dx * dx + dz * dz);
                    if (dist >= radius)
                    {
                        return basep;
                    }

                    double t = 1.0 - dist / radius;
                    return basep + height * (t * t * (3.0 - 2.0 * t));
                }

            case "fjordlands":
                {
                    // Mountains whose valleys plunge steep and deep below the base: once the sea percentile
                    // calibrates, the troughs flood into fjords between the ridges.
                    double or = OrientedRidge(seed, w.Grain, worldX, worldZ, w.Scale * 0.9);
                    double r = h * 0.25 + Ridge(h) * 0.30 + or * 0.45;
                    r = r > 0 ? System.Math.Pow(r, 1.35) : -System.Math.Pow(-r, 0.7); // proud crests, broad deep troughs
                    return r * amp * 2.1;
                }

            case "downs":
                {
                    // Chalk downs: very smooth, very long-wavelength rolling country.
                    double d = FbmT(seed + 0xD0E5, worldX, worldZ, w.Scale * 2.5, octaves: 2);
                    return (d - 0.5) * 2.0 * amp * 0.9;
                }

            case "shattered":
                {
                    // A crossing network of straight rifts to great depth, shards of upland between them: every
                    // ~900-block cell carries 2–3 linear features through its hotspot at fixed angles.
                    double basep = h * amp * 0.35;
                    if (!TryGetHotspot(seed ^ 0x5A77E, ShatteredCellSize, 1.0, 60.0, worldX, worldZ,
                            out ulong hh, out double dx, out double dz))
                    {
                        return basep;
                    }

                    int lines = 2 + (int)((hh >> 8) & 1UL);
                    double deepest = 0.0;
                    for (int k = 0; k < lines; k++)
                    {
                        double angle = ((hh >> (10 + 6 * k)) & 0x3F) / 63.0 * System.Math.PI;
                        double halfWidth = 10.0 + ((hh >> (30 + 4 * k)) & 0xF) / 15.0 * 8.0; // 10..18
                        double cos = System.Math.Cos(angle);
                        double sin = System.Math.Sin(angle);
                        double along = dx * cos + dz * sin;
                        double across = -dx * sin + dz * cos;
                        if (System.Math.Abs(across) > halfWidth || System.Math.Abs(along) > 400.0)
                        {
                            continue;
                        }

                        double wv = 1.0 - System.Math.Abs(across) / halfWidth;
                        double wall = wv >= 0.45 ? 1.0 : (wv / 0.45) * (wv / 0.45) * (3.0 - 2.0 * (wv / 0.45));
                        double endT = 1.0 - System.Math.Abs(along) / 400.0;
                        double taper = endT >= 0.15 ? 1.0 : (endT / 0.15) * (endT / 0.15) * (3.0 - 2.0 * (endT / 0.15));
                        deepest = System.Math.Max(deepest, wall * taper);
                    }

                    return basep - deepest * amp * 2.2;
                }

            case "terraces":
                {
                    // Rice-terrace country: the mesa quantisation with a fine step and no deck roll.
                    double raw = h * amp * 1.0;
                    double step = System.Math.Max(2.0, amp * 0.12);
                    return System.Math.Floor(raw / step) * step;
                }

            case "drumlins":
                {
                    // Glacial drift: smoothed whaleback ridges all elongated along the world's grain.
                    double d = GrainFbm(seed + 0xD8B1, w.Grain, worldX, worldZ, w.Scale * 0.5, octaves: 2);
                    double ridged = 1.0 - System.Math.Abs(d * 2.0 - 1.0);
                    ridged = ridged * ridged * (3.0 - 2.0 * ridged); // rounded, not sharp
                    return h * amp * 0.45 + ridged * amp * 0.5;
                }

            case "glacial":
                {
                    // Broad U-shaped troughs along the grain between rounded ridges — an ice-carved highland;
                    // the trough heads hold tarns once the sea/pond percentiles calibrate.
                    double v = GrainFbm(seed + 0x61AC, w.Grain, worldX, worldZ, w.Scale * 1.6, octaves: 2);
                    double u = System.Math.Abs(v * 2.0 - 1.0);          // 0 on the trough axis, 1 on the ridges
                    double valley = u * u * (3.0 - 2.0 * u);            // flat floor, steep walls
                    return h * amp * 0.5 + (valley - 0.5) * amp * 1.6;
                }

            default:
                return h * amp; // unknown style → plain base swell
        }
    }

    private const double ArchipelagoCellSize = 120.0;
    private const double ShatteredCellSize = 900.0;

    /// <summary>The blended archetype height offset for a column: a large-scale region field picks among
    /// the world's seed-chosen subset of archetypes (deterministic, seam-free across the X wrap) and
    /// smoothstep-blends the two neighbours' computed OFFSETS (#576 — shapes like quantised decks or
    /// asymmetric gorges cannot be blended as parameters). Generation-1 worlds draw from the larger pool
    /// (#1645: moorland, knob-and-kettle, coastal cliffs).</summary>
    private double BlendedArchetypeOffset(PlanetType planet, WonderProfile w, long seed, double h, int worldX, int worldZ)
    {
        int pool = ArchetypePoolFor(w);
        long s = seed ^ 0x7E44A1;
        ulong us = (ulong)(s < 0 ? -s : s);
        int count = 2 + (int)(us % (ulong)(pool - 1)); // this world uses 2..pool archetypes
        int rot = (int)((us >> 8) % (ulong)pool);       // …starting at a seed-rotated offset in the list

        // A broad field (much larger than the base terrain) picks a position across the subset + blends it.
        double rug = FbmT(s, worldX, worldZ, w.Scale * 6.0, octaves: 3);
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

    /// <summary>The archetype pool a world draws from: the classic eight, or eleven from generation 1 (#1645).</summary>
    private static int ArchetypePoolFor(WonderProfile w) => w.Generation >= 1 ? TerrainArchetypeCountGen1 : TerrainArchetypeCount;

    /// <summary>The archetype pool size of this world (tests).</summary>
    internal int ArchetypePoolForTest(PlanetType planet) => ArchetypePoolFor(WonderFor(planet));
}
