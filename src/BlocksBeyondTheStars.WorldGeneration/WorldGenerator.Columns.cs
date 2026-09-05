// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>chunk generation: Generate, the per-column profile and the y-loop (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>Whether this world vents steam/lava geysers for volcanic reasons (item 21 follow-up; #477 L-C:
    /// volcano worlds vent too; #1644: the `volcanic` tag replaces the lava/ashen key check).</summary>
    private static bool GeyserVolcanicFor(PlanetType planet, bool volcanoWorld)
        => (planet.LavaAbundance ?? 0.0) > 0.0 || planet.HasTag(TerrainTag.Volcanic) || volcanoWorld;

    /// <summary>Whether the set dressing scatters crystal shards here (W-R2; #1644: the `crystal` tag replaces
    /// the "key contains crystal" check — crystal ore veins and very cavy worlds still qualify).</summary>
    private static bool CrystalPropsFor(PlanetType planet)
        => planet.HasTag(TerrainTag.Crystal) || planet.Ores.Exists(o => o.Block == "crystal") || planet.CaveThreshold > 0.62;

    public ChunkData Generate(PlanetType planet, ChunkCoord coord)
    {
        var chunk = new ChunkData(coord);

        // Void worlds (orbital stations) are pure empty space — only their stamped structure exists.
        if (planet.Void)
        {
            return chunk; // all air
        }

        long seed = PlanetSeed(planet);

        var biomes = ResolveBiomes(planet);
        var deepId = ResolveBlock(planet.DeepBlock);
        var dataCacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;
        bool flora = planet.FloraDensity > 0;

        // Per-world flora richness (2026-06-10 — "belebte Planeten"): each world rolls its own seeded
        // multiplier (0.8..1.6, biased upward) on the planet type's flora + tree density, so the same type
        // can be sparse scrubland on one world and lush growth on the next. Deterministic from the world
        // seed (server + client preview agree); barren types (density 0) stay barren.
        double floraMul = (0.8 + 0.8 * Noise.Value01(seed + 0xF10A, 11, 23, 37)) * _floraFactor;
        double floraDensity = System.Math.Min(0.9, planet.FloraDensity * floraMul);

        // World floor (B46): an unmineable bedrock layer bounds the dig depth so a player can't fall forever.
        // On real planets a band of lava sits just above it; airless moons + asteroids get solid rock instead.
        var bedrockId = _content.GetBlock("bedrock")?.NumericId ?? deepId;
        var lavaFloorId = _content.GetBlock("lava")?.NumericId ?? bedrockId;
        var basaltFloorId = _content.GetBlock("basalt")?.NumericId ?? bedrockId;
        bool airlessBody = planet.Cratered || _crateredWorld;
        int floorDepth = FloorDepthFor(seed);
        var floorBandId = airlessBody ? basaltFloorId : lavaFloorId; // boundary band: basalt on airless, lava on planets

        // Per-world interior variety (item 21) + calibration (#472): cave threshold and ore CDF come from
        // the measured field distribution, richness and the mantle stay seeded rolls.
        var calib = CalibFor(planet);
        double caveThreshold = calib.CaveThreshold;
        int lavaTableDepth = calib.LavaTableDepth;
        double oreRichness = PerWorldOreRichness(seed) * _oreFactor;
        int mantleDepth = PerWorldMantle(seed, floorDepth, out var mantleId);

        // Altitude climate (#476): snow/ice above the world's snow line, tree/flora fades handled in the
        // stamps. Precomputed gate: hot flat worlds skip the per-column check entirely.
        var snowId = _content.GetBlock("snow")?.NumericId ?? BlockId.Air;
        var iceId = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
        bool hasAtmosphere = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        bool snowPossible = hasAtmosphere && !airlessBody && !snowId.IsAir
            && TempAt(calib, calib.MaxHeight) < SnowLineC + 2.0;
        bool freezeWater = CanFreezeWater(planet, calib); // #494: cold water columns freeze from the top

        // Volcanoes (#477): watery worlds may carry basalt cones with molten summit craters.
        bool volcanoWorld = HasVolcanoes(planet);
        var basaltId = _content.GetBlock("basalt")?.NumericId ?? BlockId.Air;
        var craterLavaId = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;

        // Surface seas: water fills terrain basins on worlds with an atmosphere; lava fills them on
        // volcanic / airless worlds (never both). A higher abundance raises the sea level so more low
        // ground floods — the basin's depth + any rises become shallow water / deep water / islands.
        var (fluidLevel, fluidId) = ResolveSeaFluid(planet);

        // Trees: multi-block trunk + leaf crown on grass/earth ground (a small auto density on flora worlds).
        double treeDensity = (planet.TreeDensity ?? (flora ? 0.012 : 0.0)) * floraMul;
        var logId = _content.GetBlock("wood_log")?.NumericId ?? BlockId.Air;
        var leafId = _content.GetBlock("tree_leaves")?.NumericId ?? BlockId.Air;
        bool trees = treeDensity > 0.0 && !logId.IsAir && !leafId.IsAir;

        // Giant mushrooms (item 21 V3): towering capped fungi on fungal (mycelium-surface) worlds.
        var stemId = _content.GetBlock("mushroom_stem")?.NumericId ?? BlockId.Air;
        var capId = _content.GetBlock("mushroom_cap")?.NumericId ?? BlockId.Air;
        var myceliumId = _content.GetBlock("mycelium")?.NumericId ?? BlockId.Air;
        bool giantMushrooms = !stemId.IsAir && !capId.IsAir && !myceliumId.IsAir
            && biomes.Exists(b => b.Surface == myceliumId);

        bool floatingIslands = planet.FloatingIslands; // item 21 V5: drifting sky-island slabs above the surface

        // Terrain wonders (#698–#709): per-world feature gates, resolved once per chunk so classic worlds
        // pay a handful of boolean checks and nothing else.
        bool anyBands = HasExtraBands(planet);        // #705: sky tiers, arch bars, caps, cenote lips, falls
        bool travertineWorld = HasTravertine(planet); // #701: white spring terraces with deck pools
        bool penitenteWorld = HasPenitentes(planet);  // #701: blade-ice fields (repainted ice)
        bool basaltFieldWorld = HasBasaltFields(planet); // #701: hex column fields (repainted basalt)
        bool cenoteWorld = HasCenotes(planet);        // #707: shaft pools
        bool cavernWorld = HasCaverns(planet);        // #707: underground mega-caverns
        bool tunnelWorld = HasTunnels(planet);        // #708: worm tunnels + cave mouths
        var saltBlockId = _content.GetBlock("salt")?.NumericId ?? BlockId.Air;
        var cavernCrystalId = _content.GetBlock("crystal")?.NumericId ?? BlockId.Air;

        // Generation-1 underground finds (#1646): crystal geodes (hollow crystal-lined spheres) and sediment
        // strata (tilted granite bands in the upper crust). Both gates are false on a generation-0 world.
        var wonderGates = WonderFor(planet);
        bool geodeWorld = wonderGates.Geodes && !cavernCrystalId.IsAir;
        var strataId = _content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        bool strataWorld = wonderGates.Strata && !strataId.IsAir;

        // Geysers / vents (item 21 follow-up): sparse erupting spouts — water geysers on reasonably wet worlds,
        // steam/lava vents on volcanic/ashen worlds. A marker block at the surface; the client attaches the
        // eruption VFX + hiss when the player is near. Deterministic, very sparse (landmark-rare).
        var geyserVentId = _content.GetBlock("geyser_vent")?.NumericId ?? BlockId.Air;
        double geyserWater = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        bool geysers = !geyserVentId.IsAir && (geyserWater > 0.25 || GeyserVolcanicFor(planet, volcanoWorld));

        // Aquatic flora: seabed plants (kelp stalks / coral reefs / seagrass) + lily pads on the surface, only
        // where the sea is water (never lava). World gen places them directly in the submerged columns below.
        var kelpId = _content.GetBlock("flora_kelp")?.NumericId ?? BlockId.Air;
        var lilyId = _content.GetBlock("flora_lily")?.NumericId ?? BlockId.Air;
        var coralId = _content.GetBlock("flora_coral")?.NumericId ?? BlockId.Air;
        var seagrassId = _content.GetBlock("flora_seagrass")?.NumericId ?? BlockId.Air;
        var seaWaterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        ResolveFlora(planet); // pick this world's active flora subset (sets the aquatic-archetype flags)
        // Each active seabed archetype contributes its block; nothing is planted if none of them grow here.
        bool seabedFlora = (_kelpActive && !kelpId.IsAir) || (_coralActive && !coralId.IsAir) || (_seagrassActive && !seagrassId.IsAir);
        bool waterFlora = flora && fluidId == seaWaterId && !seaWaterId.IsAir
            && (seabedFlora || (_lilyActive && !lilyId.IsAir));

        // Upland ponds/lakes (B7): scattered, swimmable water ABOVE the sea on flat ground. Frequency derives
        // from the world's WaterAbundance — the same property that sets the sea level — so wet worlds get more
        // (and larger) ponds, dry worlds almost none, and lava/airless worlds get none (their sea isn't water).
        double pondAbundance = planet.WaterAbundance
            ?? (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase) ? 0.0 : 0.55);
        bool ponds = pondAbundance > 0.15 && fluidId == seaWaterId && !seaWaterId.IsAir;
        // The mask is FBM noise (∈[0,1], clustered around 0.5), so the bar sits in its upper tail; a wetter
        // world lowers it for more/larger ponds. The flat-ground gate keeps them scattered (not everywhere).
        double pondThreshold = 0.70 - pondAbundance * 0.12;

        // Rivers (routed): a gefälle-aware network traced once per world (RiverFieldFor), guaranteed to flow
        // downhill into a sink (the sea or a self-formed lake). Empty on non-river worlds, so this is a cheap
        // O(1) lookup per column below. Replaces the old height-blind noise band + flat-ground gate.
        var riverField = RiverFieldFor(planet);

        // Beaches (#679): along a WATER shoreline (the sea, a large lake or a large pond) the ground turns
        // to the planet's beach block — lava seas keep their volcanic coasts, dry/airless worlds none.
        var beachId = BeachBlockFor(planet);
        bool beachPossible = !beachId.IsAir && !seaWaterId.IsAir
            && ((fluidId == seaWaterId && fluidLevel != int.MinValue) || riverField.FillFluid == seaWaterId);

        var origin = WorldConstants.ChunkOrigin(coord);

        // Scratch spans reused by every column (#705/#708) — allocated once per chunk (CA2014).
        System.Span<ColumnBand> bandScratch = stackalloc ColumnBand[MaxColumnBands];
        System.Span<(int Lo, int Hi)> tunnelScratch = stackalloc (int Lo, int Hi)[TunnelMaxSpans];

        // #1526: everything the column phase reads, bundled once per chunk for ComputeColumn.
        var ctx = new ColumnContext
        {
            Planet = planet,
            Seed = seed,
            Calib = calib,
            RiverField = riverField,
            Biomes = biomes,
            FluidLevel = fluidLevel,
            FluidId = fluidId,
            SeaWaterId = seaWaterId,
            CraterLavaId = craterLavaId,
            BeachId = beachId,
            SnowId = snowId,
            IceId = iceId,
            BasaltId = basaltId,
            SaltBlockId = saltBlockId,
            DeepId = deepId,
            Ponds = ponds,
            PondThreshold = pondThreshold,
            VolcanoWorld = volcanoWorld,
            TravertineWorld = travertineWorld,
            CenoteWorld = cenoteWorld,
            FreezeWater = freezeWater,
            BeachPossible = beachPossible,
            SnowPossible = snowPossible,
            PenitenteWorld = penitenteWorld,
            BasaltFieldWorld = basaltFieldWorld,
            AnyBands = anyBands,
            CavernWorld = cavernWorld,
            TunnelWorld = tunnelWorld,
            GeodeWorld = geodeWorld,
            StrataWorld = strataWorld,
        };

        // #1527: per-chunk ore invariants + one lazily built noise lattice per (column, field): slot 0 caves,
        // slot 1 the lava pockets, then a coarse + fine slot per vein.
        var oreSlots = BuildOreSlots(planet, seed, oreRichness);
        var samplers = new TorusColumnSampler[2 + oreSlots.Length * 2];

        for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
            for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
            {
                int worldX = origin.X + lx;
                int worldZ = origin.Z + lz;
                // #1526: the whole column phase is memoised per (planet, column) — every stacked chunk of a column
                // used to recompute it; now only the first one does (ComputeColumn holds the original code).
                var col = ColumnProfileFor(ctx, worldX, worldZ, bandScratch, tunnelScratch);
                TorusColumnSampler.ResetAll(samplers); // #1527: the per-column noise lattices rebuild lazily
                int surfaceY = col.SurfaceY;
                int seabedY = col.SeabedY;
                int waterTop = col.WaterTop;
                var columnFluid = col.ColumnFluid;
                int iceTop = col.IceTop;
                var biome = biomes[col.BiomeIndex];
                var surfaceId = col.SurfaceId;
                var subSurfaceId = col.SubSurfaceId;
                bool beachHere = col.BeachHere;
                var bands = col.Bands;
                int bandCount = bands.Length;
                int islandTop = col.IslandTop;
                bool cavernHere = col.CavernHere;
                int cavLo = col.CavLo, cavHi = col.CavHi, cavLakeY = col.CavLakeY;
                var tunnelSpans = col.Tunnels;
                int tunnelCount = tunnelSpans.Length;
                BlockId? craterMetal = col.CraterMetal;
                int effSurfaceDepth = col.EffSurfaceDepth;
                bool geodeHere = col.GeodeHere;
                int geoLo = col.GeoLo, geoHi = col.GeoHi, geoInLo = col.GeoInLo, geoInHi = col.GeoInHi;
                int strataShift = col.StrataShift;

                for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                {
                    int worldY = origin.Y + ly;
                    if (worldY > seabedY)
                    {
                        if (worldY <= waterTop)
                        {
                            // Sea fill in a basin, or an upland pond above it — the top of a cold column
                            // reads as solid ice instead of water (#494).
                            chunk.Set(lx, ly, lz, worldY > waterTop - iceTop ? iceId : columnFluid);
                        }
                        else
                        {
                            // Extra bands (#705): sky-island tiers, arch bars, caps, lips, waterfalls.
                            for (int b = 0; b < bandCount; b++)
                            {
                                if (worldY < bands[b].Bottom || worldY > bands[b].Top)
                                {
                                    continue;
                                }

                                switch (bands[b].Kind)
                                {
                                    case BandKind.Island:
                                        // Classic sky island: grass top, sub-surface under it, stone below.
                                        chunk.Set(lx, ly, lz, worldY == bands[b].Top ? surfaceId
                                            : worldY >= bands[b].Top - 2 ? subSurfaceId : deepId);
                                        break;
                                    case BandKind.IslandPond:
                                        // An island whose meadow top is a sunk pool (#707).
                                        chunk.Set(lx, ly, lz, worldY == bands[b].Top && !seaWaterId.IsAir
                                            ? seaWaterId
                                            : worldY >= bands[b].Top - 2 ? subSurfaceId : deepId);
                                        break;
                                    case BandKind.Cap:
                                        chunk.Set(lx, ly, lz, deepId); // bare rock: arch bars, caps, lips
                                        break;
                                    default: // BandKind.Waterfall (#707): a standing column of falling water
                                        if (!seaWaterId.IsAir)
                                        {
                                            chunk.Set(lx, ly, lz, seaWaterId);
                                        }

                                        break;
                                }

                                break; // first covering band wins
                            }
                        }

                        continue; // else air above the surface
                    }

                    int depth = seabedY - worldY;

                    // Unmineable world floor (B46/B?): solid bedrock at the very bottom of this world's deep
                    // foundation (no caves carved through it), with a boundary band just above — molten lava on real
                    // planets, basalt on airless moons/asteroids — so digging all the way down ends in lava/rock,
                    // never a void you can fall out of.
                    if (depth >= floorDepth)
                    {
                        chunk.Set(lx, ly, lz, bedrockId);
                        continue;
                    }

                    if (depth >= floorDepth - FloorBandThickness)
                    {
                        chunk.Set(lx, ly, lz, floorBandId);
                        continue;
                    }

                    // Underground mega-cavern (#707): a vast void with an optional still lake in its bowl.
                    if (cavernHere && worldY >= cavLo && worldY <= cavHi)
                    {
                        if (worldY <= cavLakeY && !airlessBody)
                        {
                            chunk.Set(lx, ly, lz, depth > lavaTableDepth ? lavaFloorId : seaWaterId);
                        }

                        continue; // void (or the lake fill above)
                    }

                    // Crystal geode (#1646): a hollow sphere lined with crystal — the shell is solid crystal, the
                    // interior air; nothing else (caves, tunnels, ores) touches the cells it claims.
                    if (geodeHere && worldY >= geoLo && worldY <= geoHi)
                    {
                        if (worldY >= geoInLo && worldY <= geoInHi)
                        {
                            continue; // the hollow
                        }

                        chunk.Set(lx, ly, lz, cavernCrystalId);
                        continue;
                    }

                    // Cavern shell glints (#707): sparse crystal studs the cell just under the cavern floor.
                    if (cavernHere && worldY == cavLo - 1 && !cavernCrystalId.IsAir
                        && Noise.Value01(seed + 0x0CAFE1, WorldConstants.WrapX(worldX, _circumference), worldY, Wz(worldZ)) < 0.10)
                    {
                        chunk.Set(lx, ly, lz, cavernCrystalId);
                        continue;
                    }

                    // Tunnel carver (#708): worm tunnels may reach all the way to the surface — cave MOUTHS.
                    if (tunnelCount > 0)
                    {
                        bool inTunnel = false;
                        for (int t = 0; t < tunnelCount; t++)
                        {
                            if (worldY >= tunnelSpans[t].Lo && worldY <= tunnelSpans[t].Hi)
                            {
                                inTunnel = true;
                                break;
                            }
                        }

                        if (inTunnel)
                        {
                            // Below the lava table the same molten-pocket rule as blob caves applies (#580).
                            if (!airlessBody && depth > lavaTableDepth
                                && SampleField(samplers, LavaSampler, seed + 0xDEE9, worldX, worldZ, 56.0, 40.0, 56.0, worldY) > 0.47)
                            {
                                chunk.Set(lx, ly, lz, lavaFloorId);
                            }

                            continue;
                        }
                    }

                    // Carve caves below the surface layer (quantile-calibrated per world, #472).
                    if (caveThreshold > 0.0 && depth > 1)
                    {
                        double cave = SampleField(samplers, CaveSampler, seed + 7777, worldX, worldZ, 22.0, 16.0, 22.0, worldY);
                        if (cave > caveThreshold)
                        {
                            // Below the world's lava table a carved cell fills with molten rock instead of
                            // air (#472/#477 L-A): the deep ore bands are now reachable — and dangerous.
                            // Airless bodies stay dry (their floor band is basalt for the same reason).
                            // #580: only MOLTEN REGIONS fill — a coarse pocket field leaves ~40 % of the
                            // deep caverns open, so the deep kilometre is explorable, not a uniform lava
                            // bath (mining down must stay rewarding, not frustrating). The pocket scale is
                            // large so each region reads as one coherent cave system, not salt-and-pepper.
                            if (!airlessBody && depth > lavaTableDepth
                                && SampleField(samplers, LavaSampler, seed + 0xDEE9, worldX, worldZ, 56.0, 40.0, 56.0, worldY) > 0.47)
                            {
                                chunk.Set(lx, ly, lz, lavaFloorId);
                            }

                            continue; // cave => air (or the lava pocket above)
                        }
                    }

                    BlockId block;
                    if (craterMetal.HasValue && depth <= 1)
                    {
                        block = craterMetal.Value; // a rare-metal clump on the crater floor (top two cells)
                    }
                    else if (depth < effSurfaceDepth)
                    {
                        block = depth == 0 ? surfaceId : subSurfaceId;
                    }
                    else
                    {
                        // Deep crust turns to a dark basalt mantle below this world's mantle depth (item 21), so the
                        // interior isn't one uniform stone column on every world. Ores still vein through it.
                        var rock = depth >= mantleDepth ? mantleId : deepId;
                        block = SelectOre(calib, oreSlots, samplers, worldX, worldZ, worldY, depth, fallback: rock);

                        // Sediment strata (#1646): inside a strata region the upper crust carries tilted granite
                        // bands (2 of every 7 cells) between the topsoil and 48 deep; ores keep their cells.
                        if (block == rock && strataShift != int.MinValue && depth < 48 && StrataBandAt(worldY, strataShift))
                        {
                            block = strataId;
                        }

                        if (block == rock && planet.DataCacheRarity > 0 && !dataCacheId.IsAir)
                        {
                            double r = Noise.Value01(seed + 4242, WorldConstants.WrapX(worldX, _circumference), worldY, Wz(worldZ));
                            if (r < planet.DataCacheRarity)
                            {
                                block = dataCacheId;
                            }
                        }
                    }

                    chunk.Set(lx, ly, lz, block);
                }

                // Surface flora: one plant in the air cell directly above the surface (bounded — one per column,
                // no spreading), chosen by biome surface + a density roll. Columns that lie under the sea grow
                // aquatic flora instead (kelp + lily pads); land plants don't grow underwater.
                if (flora && seabedY + 1 > waterTop)
                {
                    // On a beach the painted ground is the beach block, not the biome surface — grow that
                    // host's flora (sparse sand tufts), never grass plants standing in sand (#679).
                    var floraId = FloraForSurface(planet, biome, seed, worldX, worldZ,
                        beachHere ? surfaceId : (BlockId?)null);
                    int fy = seabedY + 1;
                    int fly = fy - origin.Y;
                    // Local density is modulated by a vegetation-richness mask (lush forest floors / meadows vs
                    // sparse open ground) + the per-biome density, so undergrowth gathers into thickets instead
                    // of an even sprinkle — and the same forest the trees cluster in is also carpeted with plants.
                    // The cold factor (#476) thins growth toward the snow line and stops it at the ice.
                    double localFloraDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ)
                        * ColdFloraFactor(calib, surfaceY);
                    if (beachHere)
                    {
                        localFloraDensity *= 0.35; // beaches read best mostly bare
                    }
                    if (!floraId.IsAir && fly >= 0 && fly < WorldConstants.ChunkSize
                        && Noise.Value01(seed + 9001, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < localFloraDensity)
                    {
                        chunk.Set(lx, fly, lz, floraId);
                    }
                }
                else if (waterFlora && columnFluid == seaWaterId && seabedY + 1 <= waterTop - iceTop)
                {
                    // Submerged WATER column — the sea or an upland pond grows seabed plants / lily pads.
                    // The column-fluid check keeps kelp out of lava rivers and volcano craters (#477).
                    // Plants stay below any ice sheet, and no lily pads float on a frozen surface (#494);
                    // frozen-through columns (guard above) grow nothing at all.
                    StampWaterFlora(chunk, origin, lx, lz, seed, worldX, worldZ, seabedY, waterTop - iceTop,
                        kelpId, iceTop > 0 ? BlockId.Air : lilyId, coralId, seagrassId, floraDensity);
                }

                // Sky islands grow their own surface flora on top — a floating meadow, not a bare slab.
                if (flora && islandTop != int.MinValue)
                {
                    var isleFlora = FloraForSurface(planet, biome, seed, worldX, worldZ);
                    int ify = islandTop + 1 - origin.Y;
                    double isleDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ);
                    if (!isleFlora.IsAir && ify >= 0 && ify < WorldConstants.ChunkSize
                        && Noise.Value01(seed + 9002, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < isleDensity)
                    {
                        chunk.Set(lx, ify, lz, isleFlora);
                    }
                }
            }

        if (trees)
        {
            StampTrees(planet, seed, chunk, coord, biomes, logId, leafId, treeDensity, fluidLevel);
        }

        if (giantMushrooms)
        {
            StampGiantMushrooms(planet, seed, chunk, coord, biomes, stemId, capId, myceliumId, fluidLevel);
        }

        if (geysers)
        {
            StampGeysers(planet, seed, chunk, coord, geyserVentId, fluidLevel);
        }

        // Set-dressing ("Welten reicher" W-R2): sparse scatter props that break the flat-grid monotony —
        // boulder clusters of the world's own rock, crystal shard outcrops on crystal-bearing worlds, and
        // bare dead trees on dry atmospheric worlds. Existing blocks only; nothing carves terrain.
        if (!planet.Void)
        {
            var boulderId = ResolveBlock(planet.DeepBlock);
            var crystalId = _content.GetBlock("crystal")?.NumericId ?? BlockId.Air;
            bool crystalWorld = !crystalId.IsAir && CrystalPropsFor(planet);
            bool dryWorld = (planet.WaterAbundance ?? 0.55) <= 0.15 && !planet.IsAirless && !logId.IsAir;
            StampSetDressing(planet, seed, chunk, coord, boulderId, crystalWorld ? crystalId : BlockId.Air,
                dryWorld ? logId : BlockId.Air, fluidLevel);
        }

        // Landing pads (ship-as-object): level + clear the planned pad areas so the placed ship structure
        // always sits on flat, solid, vegetation-free ground.
        FlattenLandingPads(planet, chunk, coord, biomes, seed);

        return chunk;
    }

    /// <summary>#1526: one column's terrain profile — everything Generate's y-loop needs that depends only on
    /// (planet, x, z). Immutable once built; shared by every stacked chunk of the column.</summary>
    private sealed class ColumnProfile
    {
        public int SurfaceY, SeabedY, WaterTop, IceTop, BiomeIndex, IslandTop, CavLo, CavHi, CavLakeY, EffSurfaceDepth;
        public int GeoLo, GeoHi, GeoInLo, GeoInHi, StrataShift = int.MinValue; // #1646
        public bool CavernHere, BeachHere, GeodeHere;
        public BlockId ColumnFluid, SurfaceId, SubSurfaceId;
        public BlockId? CraterMetal;
        public ColumnBand[] Bands = System.Array.Empty<ColumnBand>();
        public (int Lo, int Hi)[] Tunnels = System.Array.Empty<(int Lo, int Hi)>();
    }

    /// <summary>The per-chunk constants the column phase reads (resolved once per Generate call).</summary>
    private sealed class ColumnContext
    {
        public PlanetType Planet = null!;
        public long Seed;
        public WorldCalibration Calib = null!;
        public RiverField RiverField = null!;
        public List<BiomeResolved> Biomes = null!;
        public int FluidLevel;
        public BlockId FluidId, SeaWaterId, CraterLavaId, BeachId, SnowId, IceId, BasaltId, SaltBlockId, DeepId;
        public bool Ponds, VolcanoWorld, TravertineWorld, CenoteWorld, FreezeWater, BeachPossible, SnowPossible;
        public bool PenitenteWorld, BasaltFieldWorld, AnyBands, CavernWorld, TunnelWorld;
        public bool GeodeWorld, StrataWorld; // #1646
        public double PondThreshold;
    }

    private ColumnProfile ColumnProfileFor(ColumnContext c, int worldX, int worldZ,
        System.Span<ColumnBand> bands, System.Span<(int Lo, int Hi)> tunnelSpans)
    {
        var key = (c.Planet.Key, ColumnKey(worldX, worldZ));
        lock (_columnLock)
        {
            if (_columnProfiles.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var profile = ComputeColumn(c, worldX, worldZ, bands, tunnelSpans);
        lock (_columnLock)
        {
            if (_columnProfiles.Count >= ColumnProfileCap)
            {
                _columnProfiles.Clear();
            }

            _columnProfiles[key] = profile;
        }

        return profile;
    }

    /// <summary>The column phase of <see cref="Generate"/>, moved here verbatim (#1526) — the same calls in the
    /// same order on the same operands, so the profile is what the inline code computed.</summary>
    private ColumnProfile ComputeColumn(ColumnContext c, int worldX, int worldZ,
        System.Span<ColumnBand> bands, System.Span<(int Lo, int Hi)> tunnelSpans)
    {
        var planet = c.Planet;
        long seed = c.Seed;
        var calib = c.Calib;
        var riverField = c.RiverField;
        var biomes = c.Biomes;
        int fluidLevel = c.FluidLevel;
        var fluidId = c.FluidId;
        var seaWaterId = c.SeaWaterId;
        var craterLavaId = c.CraterLavaId;
        var beachId = c.BeachId;
        var snowId = c.SnowId;
        var iceId = c.IceId;
        var basaltId = c.BasaltId;
        var saltBlockId = c.SaltBlockId;
        var deepId = c.DeepId;
        bool ponds = c.Ponds;
        double pondThreshold = c.PondThreshold;
        bool volcanoWorld = c.VolcanoWorld;
        bool travertineWorld = c.TravertineWorld;
        bool cenoteWorld = c.CenoteWorld;
        bool freezeWater = c.FreezeWater;
        bool beachPossible = c.BeachPossible;
        bool snowPossible = c.SnowPossible;
        bool penitenteWorld = c.PenitenteWorld;
        bool basaltFieldWorld = c.BasaltFieldWorld;
        bool anyBands = c.AnyBands;
        bool cavernWorld = c.CavernWorld;
        bool tunnelWorld = c.TunnelWorld;

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);

        // An upland pond carves a shallow bowl here (seabed below the terrain) and fills it with water up to
        // the original surface (a pond flush with the surrounding ground), so the column reads as a swimmable
        // pool. Normal columns leave seabed=surface and fill the sea up to the global level, unchanged.
        int seabedY = surfaceY;
        int waterTop = fluidLevel;
        var columnFluid = fluidId;
        bool pondHere = false;
        double? pondMask = null; // computed at most once per column; shared with the beach rim test (#679)
        if (ponds && surfaceY > fluidLevel)
        {
            pondMask = PondMaskAt(planet, seed, worldX, worldZ);
            int pondDepth = PondDepthFromMask(planet, seed, worldX, worldZ, pondThreshold, pondMask.Value);
            if (pondDepth > 0)
            {
                seabedY = surfaceY - pondDepth;
                waterTop = surfaceY;
                columnFluid = seaWaterId;
                pondHere = true;
            }
        }

        // Volcano (#477): the summit crater overrides the column's fluid to a molten pool — the same
        // per-column mechanism ponds/rivers use — and the cone's flanks turn to basalt below.
        bool craterHere = false;
        double coneRise = 0.0;
        if (volcanoWorld && TryGetVolcano(planet, seed, worldX, worldZ, out var vCone, out double vDist))
        {
            coneRise = ConeOffsetOf(vCone, vDist);
            if (vDist < vCone.CraterR - 0.5)
            {
                craterHere = true;
                seabedY = surfaceY;
                waterTop = CraterLavaTop(planet, vCone);
                columnFluid = craterLavaId;
            }
        }

        // Rivers (routed): the RiverField places a channel whose water surface FOLLOWS the terrain — a
        // thin sheet on a flowing reach (no floating wall), the pooled level inside a capped lake, and at
        // a flagged step a vertical waterfall column poured into the lower reach. Skipped where a pond,
        // a volcano crater or the global sea already claims the column. The river bed is carved to BedY.
        bool riverHere = false;
        if (!pondHere && !craterHere && surfaceY > fluidLevel && riverField.TryGet(worldX, worldZ, out var river))
        {
            riverHere = true;
            seabedY = river.BedY;
            if (river.WaterfallDrop > 0)
            {
                // River incision (#709): the plunge pool under a waterfall cuts a slot into the bed —
                // the erosion look without simulating erosion.
                seabedY -= System.Math.Min(6, river.WaterfallDrop);
            }

            waterTop = river.WaterfallDrop > 0 ? river.WaterSurfaceY + river.WaterfallDrop : river.WaterSurfaceY;
            columnFluid = riverField.FillFluid; // water on watery worlds, lava on lava/ashen worlds (L2)
        }

        // Travertine deck pools (#701): shallow 1-deep water flush on the white terrace decks.
        bool travertineHere = false;
        if (travertineWorld && !pondHere && !craterHere && !riverHere && surfaceY > fluidLevel
            && TryGetTravertine(seed, worldX, worldZ, out double travDeck, out bool travPool))
        {
            travertineHere = travDeck > 0.0 || travPool;
            if (travPool && !seaWaterId.IsAir)
            {
                seabedY = surfaceY - 1;
                waterTop = surfaceY;
                columnFluid = seaWaterId;
            }
        }

        // Cenote pools (#707): a turquoise pool standing over the shaft floor.
        if (cenoteWorld && !pondHere && !craterHere && !riverHere && !seaWaterId.IsAir
            && TryGetCenotePool(planet, worldX, worldZ, out int cenotePoolTop)
            && cenotePoolTop > surfaceY)
        {
            seabedY = surfaceY;
            waterTop = cenotePoolTop;
            columnFluid = seaWaterId;
        }

        // Frozen water (#494): a cold column's water freezes from the waterline down — a walkable
        // ice sheet with liquid below on merely-cold bodies, frozen through to the seabed in the
        // deep cold or where the sheet reaches the bed anyway. Lava columns never freeze.
        int iceTop = 0;
        if (freezeWater && columnFluid == seaWaterId && !seaWaterId.IsAir && waterTop > seabedY)
        {
            iceTop = IceSheetThickness(calib, seed, worldX, worldZ, waterTop, waterTop - seabedY);
        }

        // Per-column biome → surface/sub-surface blocks (single-biome worlds use index 0).
        int biomeIndex = biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, surfaceY);
        var biome = biomes[biomeIndex];
        var surfaceId = biome.Surface;
        var subSurfaceId = biome.Sub;

        // Beaches (#679): near a water shoreline the ground turns to the beach block — surface AND
        // sub-surface, so the varied topsoil depth yields a real sand layer, and the shallow seabed
        // apron continues the beach under water. The coast mask alternates beach and bare shore;
        // the snow pass below still dusts cold coasts, and volcano basalt still wins near a cone.
        bool beachHere = false;
        if (beachPossible)
        {
            if (surfaceY < fluidLevel && fluidId == seaWaterId)
            {
                beachHere = fluidLevel - surfaceY <= BeachApronDepth
                    && CoastMaskAt(planet, seed, worldX, worldZ);
            }
            else if (!pondHere && !craterHere && !riverHere)
            {
                beachHere = DryBeachAt(planet, calib, seed, riverField, seaWaterId,
                    worldX, worldZ, surfaceY, pondMask);
            }

            if (beachHere)
            {
                surfaceId = beachId;
                subSurfaceId = beachId;
            }
        }

        // Altitude climate (#476): above the snow line the ground gets a snow cover, further up solid
        // ice. Dithered (±1.5 °C noise) so the line wanders naturally instead of cutting a contour.
        if (snowPossible && surfaceY > waterTop)
        {
            double surfT = TempAt(calib, surfaceY)
                + (FbmT(seed + 0x51F0, worldX, worldZ, 24.0, octaves: 2) - 0.5) * 3.0;
            if (surfT < IceLineC && !iceId.IsAir)
            {
                surfaceId = iceId;
                subSurfaceId = iceId;
            }
            else if (surfT < SnowLineC)
            {
                surfaceId = snowId;
            }
        }

        // Volcano flanks read as dark volcanic rock wherever the cone meaningfully rises (#477) —
        // after the snow pass, so the warm basalt wins over a snow cap near the vent.
        if (coneRise > 3.0 && !basaltId.IsAir)
        {
            surfaceId = basaltId;
            subSurfaceId = basaltId;
        }

        // Travertine terraces read blinding white (#701) — salt is the closest shipped block.
        if (travertineHere && !saltBlockId.IsAir)
        {
            surfaceId = saltBlockId;
            subSurfaceId = saltBlockId;
        }

        // Penitente blades freeze to solid ice (#701) where they rise meaningfully.
        if (penitenteWorld && !iceId.IsAir && PenitenteRise(planet, seed, worldX, worldZ) > 1.5)
        {
            surfaceId = iceId;
            subSurfaceId = iceId;
        }

        // Basalt column fields read as dark columnar rock (#701).
        if (basaltFieldWorld && !basaltId.IsAir && TryGetBasaltColumns(seed, worldX, worldZ, out _))
        {
            surfaceId = basaltId;
            subSurfaceId = basaltId;
        }

        // Landmark-table paints (#1644): the active rows' surface repaints, table order. Empty on every
        // classic world — the families above keep their inline paints because those interleave with the
        // beach/snow order; new families register a paint delegate instead of editing this method.
        var wonder = WonderFor(planet);
        var landmarkPaints = wonder.ActivePaints;
        for (int i = 0; i < landmarkPaints.Length; i++)
        {
            if (landmarkPaints[i](this, planet, wonder, worldX, worldZ, surfaceY) is { } painted)
            {
                surfaceId = painted;
                subSurfaceId = painted;
            }
        }

        // Ejecta rays (#699): bright/contrast streaks radiating from the primary chain crater —
        // repainted with the body's deep rock so the rays pop against the regolith.
        if ((planet.Cratered || _crateredWorld) && CraterRayAt(planet, worldX, worldZ))
        {
            surfaceId = deepId;
            subSurfaceId = deepId;
        }

        // Extra column bands (#705): sky-island tiers (with ponds + stalactites), arch bars,
        // sea-stack/hoodoo caps, cenote lips and island waterfalls. Tier 0 keeps the classic
        // sky-island fill; islandTop below feeds the island flora pass like before.
        int bandCount = anyBands ? GetExtraBands(planet, worldX, worldZ, bands) : 0;
        int islandTop = int.MinValue;
        for (int b = 0; b < bandCount; b++)
        {
            if (bands[b].Kind == BandKind.Island && bands[b].Top > islandTop)
            {
                islandTop = bands[b].Top;
            }
        }

        // Underground mega-cavern (#707): this column's void span, if a cavern covers it.
        int cavLo = 0, cavHi = -1, cavLakeY = int.MinValue;
        bool cavernHere = cavernWorld
            && TryGetCavernSpan(planet, worldX, worldZ, out cavLo, out cavHi, out cavLakeY);

        // Tunnel carver (#708): this column's worm-carve y-spans (empty on most columns).
        int tunnelCount = tunnelWorld ? TunnelSpans(planet, worldX, worldZ, tunnelSpans) : 0;

        // Generation-1 underground finds (#1646): the geode sphere covering this column, and the strata region.
        int geoLo = 0, geoHi = -1, geoInLo = 1, geoInHi = 0;
        bool geodeHere = c.GeodeWorld && TryGetGeodeSpan(planet, wonder, worldX, worldZ, out geoLo, out geoHi, out geoInLo, out geoInHi);
        int strataShift = c.StrataWorld ? StrataShiftAt(seed, worldX, worldZ) : int.MinValue;

        // Crater-floor metal clumps (item 33): on a cratered world, the top cells of a metal-bearing deep
        // crater floor are exposed rare ore instead of regolith (only some craters, a few clumps each).
        BlockId? craterMetal = (planet.Cratered || _crateredWorld)
            ? CraterFloorMetal(planet, seed, worldX, worldZ) : (BlockId?)null;

        // Non-uniform topsoil: this column's surface/sub-surface layer thickness (varies per column, not a
        // flat band) so the stone/ore boundary undulates and reaches close to the surface in the thin spots.
        int effSurfaceDepth = VariedSurfaceDepth(planet, seed, worldX, worldZ);

        return new ColumnProfile
        {
            SurfaceY = surfaceY,
            SeabedY = seabedY,
            WaterTop = waterTop,
            ColumnFluid = columnFluid,
            IceTop = iceTop,
            BiomeIndex = biomeIndex,
            SurfaceId = surfaceId,
            SubSurfaceId = subSurfaceId,
            BeachHere = beachHere,
            Bands = bandCount > 0 ? bands.Slice(0, bandCount).ToArray() : System.Array.Empty<ColumnBand>(),
            IslandTop = islandTop,
            CavernHere = cavernHere,
            CavLo = cavLo,
            CavHi = cavHi,
            CavLakeY = cavLakeY,
            Tunnels = tunnelCount > 0 ? tunnelSpans.Slice(0, tunnelCount).ToArray() : System.Array.Empty<(int Lo, int Hi)>(),
            CraterMetal = craterMetal,
            EffSurfaceDepth = effSurfaceDepth,
            GeodeHere = geodeHere,
            GeoLo = geoLo,
            GeoHi = geoHi,
            GeoInLo = geoInLo,
            GeoInHi = geoInHi,
            StrataShift = strataShift,
        };
    }
}
