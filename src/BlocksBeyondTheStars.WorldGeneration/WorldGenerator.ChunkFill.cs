// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>chunk generation: the fill stages <see cref="Generate"/> calls — the per-chunk <see cref="FillContext"/>, the
/// y-loop, the per-column flora and the chunk passes (partial of <see cref="WorldGenerator"/>, split out of .Columns by seam,
/// B9). Every stage body is the original Generate code moved verbatim: a short prologue copies the context fields it reads
/// into locals of the original names, so no expression, salt, literal or call order changed.</summary>
public sealed partial class WorldGenerator
{
    /// <summary>B9: the per-chunk constants of one <see cref="Generate"/> call, resolved once by <see cref="ResolveFillContext"/>
    /// (the sibling of <see cref="ColumnContext"/>). Created per call and never stored in a field — Generate runs
    /// concurrently on the chunk pool, and <see cref="Samplers"/> is per-chunk scratch the y-loop mutates.</summary>
    private sealed class FillContext
    {
        public PlanetType Planet = null!;
        public ChunkCoord Coord;
        public Vector3i Origin;
        public long Seed;
        public WorldCalibration Calib = null!;
        public WonderProfile WonderGates = null!;
        public RiverField RiverField = null!;
        public List<BiomeResolved> Biomes = null!;
        public BlockId DeepId, DataCacheId, BedrockId, LavaFloorId, FloorBandId, MantleId, SnowId, IceId, BasaltId;
        public BlockId CraterLavaId, FluidId, DenseGasId, LogId, LeafId, StemId, CapId, MyceliumId, SaltBlockId;
        public BlockId CavernCrystalId, DripstoneId, CoralRockId, GraniteId, SandstoneId, StrataId, BoneId, OilId;
        public BlockId TarShellId, GrassId, DirtId, MudId, SandId, SeaSandId, StoneId, MossStoneId, ScreeId, AshId;
        public BlockId GeyserVentId, KelpId, LilyId, CoralId, SeagrassId, SeaWaterId, BeachId, SeabedId;
        public int FloorDepth, LavaTableDepth, MantleDepth, FluidLevel, DenseGasTop;
        public double FloraDensity, CaveThreshold, OreRichness, TreeDensity, PondThreshold;
        public bool Flora, AirlessBody, SnowPossible, FreezeWater, VolcanoWorld, Trees, GiantMushrooms, AnyBands;
        public bool TravertineWorld, PenitenteWorld, BasaltFieldWorld, CenoteWorld, CavernWorld, TunnelWorld;
        public bool GeodeWorld, StrataWorld, BoneStrata, OilWorld, Gen1Paints, Geysers, WaterFlora, Ponds;
        public bool BeachPossible, UnderwaterForests, WaterSealed;
        /// <summary>The per-chunk ore slots and noise lattices (#1527), set by Generate after the column context.</summary>
        public OreSlot[] OreSlots = System.Array.Empty<OreSlot>();
        public TorusColumnSampler[] Samplers = System.Array.Empty<TorusColumnSampler>();
    }

    /// <summary>Stage 1 of <see cref="Generate"/>: every per-chunk constant, resolved once per call. ResolveFlora
    /// (which sets the active flora subset) runs here, before the column loop, as it always did.</summary>
    private FillContext ResolveFillContext(PlanetType planet, ChunkCoord coord)
    {
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

        // #2134: a gas giant has no ground under its sea — the light gas lies over dense gas that reaches down to the floor.
        // MinValue on every other world (and on content without the block), where nothing below changes.
        int denseGasTop = DenseGasTop(planet);
        var denseGasId = denseGasTop != int.MinValue ? _content.GetBlock("gas_dense")!.NumericId : BlockId.Air;

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
        // Dripstone (generation 3, part 3): white like the travertine repaint on limestone country, the deep rock
        // elsewhere. Read only where a column carries dripstone lengths, which no generation 0–2 column does.
        var dripstoneId = planet.HasTag(TerrainTag.Karst) && !saltBlockId.IsAir ? saltBlockId : deepId;
        var coralRockId = _content.GetBlock("coral_rock")?.NumericId ?? BlockId.Air; // part 6: the reef floor

        // Generation-1 underground finds (#1646): crystal geodes (hollow crystal-lined spheres) and sediment
        // strata (tilted granite bands in the upper crust). Both gates are false on a generation-0 world.
        var wonderGates = WonderFor(planet);
        bool geodeWorld = wonderGates.Geodes && !cavernCrystalId.IsAir;
        var graniteId = _content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        var sandstoneId = _content.GetBlock("sandstone")?.NumericId ?? BlockId.Air;
        var strataId = sandstoneId.IsAir ? graniteId : sandstoneId; // #1647: sandstone strata now that the block exists
        bool strataWorld = wonderGates.Strata && !strataId.IsAir;
        // Generation 21 (#2341): a fossil world lays one bone layer in every third strata period.
        var boneId = _content.GetBlock("bone")?.NumericId ?? BlockId.Air;
        bool boneStrata = strataWorld && wonderGates.BoneStrata && !boneId.IsAir;

        // Terrain generation 18 (#2106): the oil pockets — tar-shelled, oil-filled, only where plants and animals live.
        var oilId = _content.GetBlock("oil")?.NumericId ?? BlockId.Air;
        var tarShellId = _content.GetBlock("tar")?.NumericId ?? BlockId.Air;
        bool oilWorld = wonderGates.OilPockets && !oilId.IsAir && !tarShellId.IsAir;

        // Generation-1 bodies + paints (#1647): the block ids the paint chain writes, resolved once per chunk.
        bool gen1Paints = wonderGates.Generation >= 1 && !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands;
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;
        var sandId = _content.GetBlock("sand")?.NumericId ?? BlockId.Air;
        var seaSandId = _content.GetBlock(GiantRules.SeaSandBlock(planet))?.NumericId ?? sandId; // #2074: the sea's own sand
        var stoneId = _content.GetBlock("stone")?.NumericId ?? BlockId.Air;
        var mossStoneId = _content.GetBlock("moss_stone")?.NumericId ?? BlockId.Air;
        var screeId = _content.GetBlock("scree")?.NumericId ?? BlockId.Air;
        var ashId = _content.GetBlock("ash")?.NumericId ?? BlockId.Air;

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

        // #1757 (generation 5): a type may name the block of its whole seabed — the rainbow planet's diggable sand.
        var seabedId = _terrainGeneration >= WorldDescription.AuthoredContentGeneration && !string.IsNullOrEmpty(planet.SeabedBlock)
            ? _content.GetBlock(planet.SeabedBlock)?.NumericId ?? BlockId.Air
            : BlockId.Air;
        // #1757: the seabed forests — tall kelp stands in patches instead of the 2–4 cell stalks.
        bool underwaterForests = _terrainGeneration >= WorldDescription.AuthoredContentGeneration && planet.UnderwaterForests;

        var origin = WorldConstants.ChunkOrigin(coord);
        bool waterSealed = WaterSealed;

        return new FillContext
        {
            Planet = planet,
            Coord = coord,
            Seed = seed,
            Biomes = biomes,
            DeepId = deepId,
            DataCacheId = dataCacheId,
            Flora = flora,
            FloraDensity = floraDensity,
            BedrockId = bedrockId,
            LavaFloorId = lavaFloorId,
            AirlessBody = airlessBody,
            FloorDepth = floorDepth,
            FloorBandId = floorBandId,
            Calib = calib,
            CaveThreshold = caveThreshold,
            LavaTableDepth = lavaTableDepth,
            OreRichness = oreRichness,
            MantleDepth = mantleDepth,
            MantleId = mantleId,
            SnowId = snowId,
            IceId = iceId,
            SnowPossible = snowPossible,
            FreezeWater = freezeWater,
            VolcanoWorld = volcanoWorld,
            BasaltId = basaltId,
            CraterLavaId = craterLavaId,
            FluidLevel = fluidLevel,
            FluidId = fluidId,
            DenseGasTop = denseGasTop,
            DenseGasId = denseGasId,
            TreeDensity = treeDensity,
            LogId = logId,
            LeafId = leafId,
            Trees = trees,
            StemId = stemId,
            CapId = capId,
            MyceliumId = myceliumId,
            GiantMushrooms = giantMushrooms,
            AnyBands = anyBands,
            TravertineWorld = travertineWorld,
            PenitenteWorld = penitenteWorld,
            BasaltFieldWorld = basaltFieldWorld,
            CenoteWorld = cenoteWorld,
            CavernWorld = cavernWorld,
            TunnelWorld = tunnelWorld,
            SaltBlockId = saltBlockId,
            CavernCrystalId = cavernCrystalId,
            DripstoneId = dripstoneId,
            CoralRockId = coralRockId,
            WonderGates = wonderGates,
            GeodeWorld = geodeWorld,
            GraniteId = graniteId,
            SandstoneId = sandstoneId,
            StrataId = strataId,
            StrataWorld = strataWorld,
            BoneId = boneId,
            BoneStrata = boneStrata,
            OilId = oilId,
            TarShellId = tarShellId,
            OilWorld = oilWorld,
            Gen1Paints = gen1Paints,
            GrassId = grassId,
            DirtId = dirtId,
            MudId = mudId,
            SandId = sandId,
            SeaSandId = seaSandId,
            StoneId = stoneId,
            MossStoneId = mossStoneId,
            ScreeId = screeId,
            AshId = ashId,
            GeyserVentId = geyserVentId,
            Geysers = geysers,
            KelpId = kelpId,
            LilyId = lilyId,
            CoralId = coralId,
            SeagrassId = seagrassId,
            SeaWaterId = seaWaterId,
            WaterFlora = waterFlora,
            Ponds = ponds,
            PondThreshold = pondThreshold,
            RiverField = riverField,
            BeachId = beachId,
            BeachPossible = beachPossible,
            SeabedId = seabedId,
            UnderwaterForests = underwaterForests,
            Origin = origin,
            WaterSealed = waterSealed,
        };
    }

    /// <summary>Stage 2 (#1526): everything the column phase reads, bundled once per chunk for ComputeColumn.</summary>
    private ColumnContext BuildColumnContext(FillContext k)
    {
        return new ColumnContext
        {
            Planet = k.Planet,
            Seed = k.Seed,
            Calib = k.Calib,
            RiverField = k.RiverField,
            Biomes = k.Biomes,
            FluidLevel = k.FluidLevel,
            FluidId = k.FluidId,
            SeaWaterId = k.SeaWaterId,
            CraterLavaId = k.CraterLavaId,
            BeachId = k.BeachId,
            SnowId = k.SnowId,
            IceId = k.IceId,
            BasaltId = k.BasaltId,
            SaltBlockId = k.SaltBlockId,
            DeepId = k.DeepId,
            Ponds = k.Ponds,
            PondThreshold = k.PondThreshold,
            VolcanoWorld = k.VolcanoWorld,
            TravertineWorld = k.TravertineWorld,
            CenoteWorld = k.CenoteWorld,
            FreezeWater = k.FreezeWater,
            BeachPossible = k.BeachPossible,
            SnowPossible = k.SnowPossible,
            PenitenteWorld = k.PenitenteWorld,
            BasaltFieldWorld = k.BasaltFieldWorld,
            AnyBands = k.AnyBands,
            CavernWorld = k.CavernWorld,
            TunnelWorld = k.TunnelWorld,
            GeodeWorld = k.GeodeWorld,
            StrataWorld = k.StrataWorld,
            OilWorld = k.OilWorld,
            OilId = k.OilId,
            TarShellId = k.TarShellId,
            Gen1Paints = k.Gen1Paints,
            GrassId = k.GrassId,
            DirtId = k.DirtId,
            MudId = k.MudId,
            SandId = k.SandId,
            SeaSandId = k.SeaSandId,
            StoneId = k.StoneId,
            GraniteId = k.GraniteId,
            MossStoneId = k.MossStoneId,
            ScreeId = k.ScreeId,
            SandstoneId = k.SandstoneId,
            AshId = k.AshId,
            SeabedId = k.SeabedId,
            SnowCover = _terrainGeneration >= WorldDescription.ExtremePlanetsGeneration && !k.SnowId.IsAir ? k.Planet.SnowCoverDepth : 0,
        };
    }

    /// <summary>Stage 3 (generation 24, #2516): the fluid spans of the four neighbouring columns into
    /// <paramref name="sealSpans"/>; returns their count. <paramref name="ownFluid"/> = the column holds its own fluid.</summary>
    private int CollectSealSpans(ColumnContext ctx, FillContext k, ColumnProfile col, int worldX, int worldZ,
        System.Span<ColumnBand> bandScratch, System.Span<(int Lo, int Hi)> tunnelScratch, System.Span<(int Lo, int Hi)> sealSpans,
        out bool ownFluid)
    {
        bool waterSealed = k.WaterSealed;
        int seabedY = col.SeabedY;
        int waterTop = col.WaterTop;
        var columnFluid = col.ColumnFluid;

        // Generation 24 (#2516): the generated fluid of the four neighbouring columns. No cave or tunnel cell is
        // carved at a height where a neighbour holds it, and a tunnel never opens the bed under this column's own
        // water — an open side is exactly where the fluid automaton would let it go. Empty on every older world.
        int sealCount = 0;
        ownFluid = false;
        if (waterSealed)
        {
            ownFluid = waterTop > seabedY && !columnFluid.IsAir;
            for (int d = 0; d < 4; d++)
            {
                var nb = ColumnProfileFor(ctx, worldX + (d == 0 ? 1 : d == 1 ? -1 : 0), worldZ + (d == 2 ? 1 : d == 3 ? -1 : 0),
                    bandScratch, tunnelScratch);
                if (nb.WaterTop > nb.SeabedY && !nb.ColumnFluid.IsAir)
                {
                    sealSpans[sealCount++] = (nb.SeabedY + 1, nb.WaterTop);
                }
            }
        }

        return sealCount;
    }

    /// <summary>Stage 4: the y-loop — every cell of one column of the chunk, bottom to top.</summary>
    private void FillColumnCells(ChunkData chunk, FillContext k, ColumnProfile col, int lx, int lz, int worldX, int worldZ,
        System.Span<(int Lo, int Hi)> sealSpans, int sealCount, bool ownFluid)
    {
        var planet = k.Planet;
        long seed = k.Seed;
        var calib = k.Calib;
        var wonderGates = k.WonderGates;
        var origin = k.Origin;
        var oreSlots = k.OreSlots;
        var samplers = k.Samplers;
        var deepId = k.DeepId;
        var dataCacheId = k.DataCacheId;
        var bedrockId = k.BedrockId;
        var lavaFloorId = k.LavaFloorId;
        var floorBandId = k.FloorBandId;
        int floorDepth = k.FloorDepth;
        bool airlessBody = k.AirlessBody;
        double caveThreshold = k.CaveThreshold;
        int lavaTableDepth = k.LavaTableDepth;
        int mantleDepth = k.MantleDepth;
        var mantleId = k.MantleId;
        var iceId = k.IceId;
        bool snowPossible = k.SnowPossible;
        int fluidLevel = k.FluidLevel;
        var fluidId = k.FluidId;
        int denseGasTop = k.DenseGasTop;
        var denseGasId = k.DenseGasId;
        var seaWaterId = k.SeaWaterId;
        var mudId = k.MudId;
        var dripstoneId = k.DripstoneId;
        var cavernCrystalId = k.CavernCrystalId;
        var strataId = k.StrataId;
        var boneId = k.BoneId;
        bool boneStrata = k.BoneStrata;
        var oilId = k.OilId;
        var tarShellId = k.TarShellId;
        int seabedY = col.SeabedY;
        int waterTop = col.WaterTop;
        var columnFluid = col.ColumnFluid;
        int iceTop = col.IceTop;
        var biome = k.Biomes[col.BiomeIndex];
        var surfaceId = col.SurfaceId;
        var subSurfaceId = col.SubSurfaceId;
        var bands = col.Bands;
        int bandCount = bands.Length;
        bool cavernHere = col.CavernHere;
        int cavLo = col.CavLo, cavHi = col.CavHi, cavLakeY = col.CavLakeY;
        var tunnelSpans = col.Tunnels;
        int tunnelCount = tunnelSpans.Length;
        BlockId? craterMetal = col.CraterMetal;
        int effSurfaceDepth = col.EffSurfaceDepth;
        int coverDepth = col.CoverDepth;
        bool geodeHere = col.GeodeHere;
        int geoLo = col.GeoLo, geoHi = col.GeoHi, geoInLo = col.GeoInLo, geoInHi = col.GeoInHi;
        bool oilHere = col.OilHere; // generation 18 (#2106): false on every older column
        int oilLo = col.OilLo, oilHi = col.OilHi, oilInLo = col.OilInLo, oilInHi = col.OilInHi;
        int oilSeepLo = col.OilSeepLo; // generation 22 (#2371): int.MaxValue on every column without a seep cell
        bool oilPuddle = col.OilPuddle;
        int strataShift = col.StrataShift;
        // Terrain generation 3: the paint fill, the sub-surface fluid spans and the cave shield — all at
        // their classic no-op values (MinValue / empty / an empty range) on every generation 0–2 column.
        int paintFillToY = col.PaintFillToY;
        var paintCycle = col.PaintCycle;
        var subFluid = col.SubFluid;
        int subFluidCount = subFluid.Length;
        int shieldLo = col.ShieldLo, shieldHi = col.ShieldHi;
        bool materialBands = col.MaterialBands;
        int dripDown = col.DripDown, dripUp = col.DripUp;
        // Generation 21 (#2336): what a crown band wears — the biome of its own height (Air = the column's biome).
        var crownSurface = col.CrownSurface.IsAir ? biome.Surface : col.CrownSurface;
        var crownSub = col.CrownSub.IsAir ? biome.Sub : col.CrownSub;

        for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
        {
            int worldY = origin.Y + ly;
            if (denseGasTop != int.MinValue && worldY <= fluidLevel)
            {
                // #2134: the gas giant's whole column under the sea surface — the light gas, the dense gas under it,
                // and the unchanged bedrock floor. No heightfield rock, ore or cache is left under the gas to see
                // through it; the islands stand above the sea and take the ordinary path below.
                chunk.Set(lx, ly, lz, worldY > denseGasTop ? fluidId
                    : seabedY - worldY >= floorDepth ? bedrockId : denseGasId);
                continue;
            }

            if (worldY > seabedY)
            {
                // Material bands (generation 3) stand INSIDE the water span — an iceberg's hull below the
                // waterline — so they are consulted before the sea fill. Never present on a classic column.
                if (materialBands)
                {
                    bool bandHit = false;
                    for (int b = 0; b < bandCount && !bandHit; b++)
                    {
                        if (worldY < bands[b].Bottom || worldY > bands[b].Top)
                        {
                            continue;
                        }

                        if (bands[b].Kind == BandKind.Ice)
                        {
                            if (!iceId.IsAir)
                            {
                                chunk.Set(lx, ly, lz, iceId);
                            }

                            bandHit = true;
                        }
                        else if (bands[b].Kind == BandKind.Fluid)
                        {
                            if (!columnFluid.IsAir)
                            {
                                chunk.Set(lx, ly, lz, columnFluid);
                            }

                            bandHit = true;
                        }
                        else if (bands[b].Kind == BandKind.Mat)
                        {
                            // A floating mat (part 5): one cell of mud at the lake's water top.
                            chunk.Set(lx, ly, lz, mudId.IsAir ? subSurfaceId : mudId);
                            bandHit = true;
                        }
                        else if (bands[b].Kind == BandKind.Afloat)
                        {
                            // #1757: the keel of an island afloat — the biome's own ground, not the seabed
                            // sand the submerged column below it was given.
                            chunk.Set(lx, ly, lz, worldY == bands[b].Top ? biome.Surface
                                : worldY >= bands[b].Top - 2 ? biome.Sub : deepId);
                            bandHit = true;
                        }
                        else if (bands[b].Kind == BandKind.Rock)
                        {
                            // Generation 21 (#2332): solid rock of the band's own material — an arch bar through a
                            // bay stands in the water, a bridge deck over a fjord too.
                            chunk.Set(lx, ly, lz, bands[b].Material.IsAir ? deepId : bands[b].Material);
                            bandHit = true;
                        }
                        else if (bands[b].Kind == BandKind.Crown)
                        {
                            // Generation 21 (#2332): a crown or a balcony — the crown's ground on the band's rock.
                            chunk.Set(lx, ly, lz, worldY == bands[b].Top ? crownSurface
                                : worldY >= bands[b].Top - 2 ? crownSub
                                : bands[b].Material.IsAir ? deepId : bands[b].Material);
                            bandHit = true;
                        }
                    }

                    if (bandHit)
                    {
                        continue;
                    }
                }

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
                                // An island whose meadow top is a sunk pool (#707). Generation 21 (#2336): a pond
                                // on a crown that stands above the ice line is frozen — a pillar's crown 90 up is
                                // far colder than its foot; older sky worlds keep their water (the gate).
                                chunk.Set(lx, ly, lz, worldY == bands[b].Top && !seaWaterId.IsAir
                                    ? (snowPossible && !iceId.IsAir && wonderGates.Generation >= WorldDescription.SpectacleGeneration
                                        && TempAt(calib, worldY) < IceLineC ? iceId : seaWaterId)
                                    : worldY >= bands[b].Top - 2 ? subSurfaceId : deepId);
                                break;
                            case BandKind.Afloat:
                                // #1757: the deck of an island afloat (its keel was written before the sea fill).
                                chunk.Set(lx, ly, lz, worldY == bands[b].Top ? biome.Surface
                                    : worldY >= bands[b].Top - 2 ? biome.Sub : deepId);
                                break;
                            case BandKind.Cap:
                                chunk.Set(lx, ly, lz, deepId); // bare rock: arch bars, caps, lips
                                break;
                            case BandKind.Ice: // generation 3 (normally taken by the pre-scan above)
                                if (!iceId.IsAir)
                                {
                                    chunk.Set(lx, ly, lz, iceId);
                                }

                                break;
                            case BandKind.Fluid: // generation 3
                                if (!columnFluid.IsAir)
                                {
                                    chunk.Set(lx, ly, lz, columnFluid);
                                }

                                break;
                            case BandKind.Mat: // generation 3, part 5 (normally taken by the pre-scan above)
                                chunk.Set(lx, ly, lz, mudId.IsAir ? subSurfaceId : mudId);
                                break;
                            case BandKind.Rock: // generation 21 (#2332; normally taken by the pre-scan above)
                                chunk.Set(lx, ly, lz, bands[b].Material.IsAir ? deepId : bands[b].Material);
                                break;
                            case BandKind.Crown: // generation 21 (#2332; normally taken by the pre-scan above)
                                chunk.Set(lx, ly, lz, worldY == bands[b].Top ? crownSurface
                                    : worldY >= bands[b].Top - 2 ? crownSub
                                    : bands[b].Material.IsAir ? deepId : bands[b].Material);
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
                // Dripstone (generation 3): a spike from the roof, a spike from the floor, never a wall —
                // both keep an air cell between them. Zero-length on every generation 0–2 column.
                if ((dripDown | dripUp) != 0 && DripstoneFits(cavLo, cavHi, dripDown, dripUp, seabedY)
                    && (worldY > cavHi - dripDown || worldY < cavLo + dripUp))
                {
                    chunk.Set(lx, ly, lz, dripstoneId);
                    continue;
                }

                if (worldY <= cavLakeY && !airlessBody)
                {
                    chunk.Set(lx, ly, lz, depth > lavaTableDepth ? lavaFloorId : seaWaterId);
                }

                continue; // void (or the lake fill above)
            }

            // Sub-surface fluid spans (generation 3): an underground river's water on its passage floor —
            // the passage itself is a tunnel span, the cells around it are shielded from the cave carver,
            // so the water sits in a sealed pocket (generated fluid is a bottomless source to the automaton).
            if (subFluidCount > 0)
            {
                bool inFluid = false;
                for (int f = 0; f < subFluidCount; f++)
                {
                    if (worldY >= subFluid[f].Lo && worldY <= subFluid[f].Hi)
                    {
                        chunk.Set(lx, ly, lz, subFluid[f].Fluid);
                        inFluid = true;
                        break;
                    }
                }

                if (inFluid)
                {
                    continue;
                }
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

            // Oil pocket (#2106, generation 18): a tar shell around still oil — like the geode, claimed before
            // the tunnels and caves so nothing ever opens it; the oil is a finite liquid, it never flows.
            if (oilHere && worldY >= oilLo && worldY <= oilHi)
            {
                chunk.Set(lx, ly, lz, worldY >= oilInLo && worldY <= oilInHi ? oilId : tarShellId);
                continue;
            }

            // Oil seep (#2371, generation 22): the tar chimney from the pocket's shell up to the ground and the tar
            // patch round its mouth, with an oil puddle in the middle on dry land — claimed like the pocket, before
            // the topsoil, the tunnels and the caves, so the black trail runs unbroken down to the oil.
            if (worldY >= oilSeepLo)
            {
                chunk.Set(lx, ly, lz, oilPuddle && worldY == seabedY ? oilId : tarShellId);
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
                int spanLo = 0, spanHi = -1;
                for (int t = 0; t < tunnelCount; t++)
                {
                    if (worldY >= tunnelSpans[t].Lo && worldY <= tunnelSpans[t].Hi)
                    {
                        inTunnel = true;
                        spanLo = tunnelSpans[t].Lo;
                        spanHi = tunnelSpans[t].Hi;
                        break;
                    }
                }

                // Generation 24 (#2516): sealed water — the cell stays rock beside a neighbour's fluid, and under the
                // column's own fluid the bed stays shut.
                if (inTunnel && sealCount + (ownFluid ? 1 : 0) > 0
                    && ((ownFluid && worldY >= seabedY) || InSealSpans(sealSpans, sealCount, worldY)))
                {
                    inTunnel = false;
                }

                if (inTunnel)
                {
                    // Below the lava table the same molten-pocket rule as blob caves applies (#580).
                    if (!airlessBody && depth > lavaTableDepth
                        && SampleField(samplers, LavaSampler, seed + 0xDEE9, worldX, worldZ, 56.0, 40.0, 56.0, worldY) > 0.47)
                    {
                        chunk.Set(lx, ly, lz, lavaFloorId);
                    }
                    else if ((dripDown | dripUp) != 0 && DripstoneFits(spanLo, spanHi, dripDown, dripUp, seabedY)
                        && !(spanLo >= shieldLo && spanHi <= shieldHi)
                        && (worldY > spanHi - dripDown || worldY < spanLo + dripUp))
                    {
                        // Dripstone (generation 3) at the span's ends. An underground river's passage is the
                        // one span inside the cave shield and never drips: its floor is water or a bank
                        // ledge and its three cells of headroom are the promise the reach is passable.
                        // Zero-length on every generation 0–2 column.
                        chunk.Set(lx, ly, lz, dripstoneId);
                    }

                    continue;
                }
            }

            // Carve caves below the surface layer (quantile-calibrated per world, #472). The cave shield
            // (generation 3) is the rock around a sub-surface fluid span; empty on a classic column.
            if (caveThreshold > 0.0 && depth > 1 && (worldY < shieldLo || worldY > shieldHi)
                && (sealCount == 0 || !InSealSpans(sealSpans, sealCount, worldY)))
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
                block = depth < coverDepth ? surfaceId : subSurfaceId; // coverDepth 1 = the classic top cell
            }
            else if (paintFillToY != int.MinValue && worldY >= paintFillToY)
            {
                // Paint fill (generation 3): a landmark paint that claims the column down to fillToY — a
                // glacier that is ice through and through, or a rainbow cliff cycling its bands parallel
                // to the surface. No ore, strata or cache inside the fill; the carvers above have already
                // had their say.
                block = paintCycle is null ? subSurfaceId : CycleBlockAt(paintCycle, depth);
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

                // Bone stratum (#2341, generation 21): one bone layer in every third period of a fossil world's strata.
                if (boneStrata && block == rock && strataShift != int.MinValue && depth < 48 && StrataBoneBandAt(worldY, strataShift))
                {
                    block = boneId;
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
    }

    /// <summary>Stage 5: the column's plants — the surface or water flora, the sky island's meadow, the further band
    /// tops (generation 21) and the hanging kelp under an island (generation 5).</summary>
    private void PlantColumnFlora(ChunkData chunk, FillContext k, ColumnProfile col, int lx, int lz, int worldX, int worldZ)
    {
        var planet = k.Planet;
        long seed = k.Seed;
        var calib = k.Calib;
        var wonderGates = k.WonderGates;
        var origin = k.Origin;
        bool flora = k.Flora;
        double floraDensity = k.FloraDensity;
        var snowId = k.SnowId;
        var iceId = k.IceId;
        var seaWaterId = k.SeaWaterId;
        var kelpId = k.KelpId;
        var lilyId = k.LilyId;
        var coralId = k.CoralId;
        var seagrassId = k.SeagrassId;
        var coralRockId = k.CoralRockId;
        bool waterFlora = k.WaterFlora;
        bool underwaterForests = k.UnderwaterForests;
        int surfaceY = col.SurfaceY;
        int seabedY = col.SeabedY;
        int waterTop = col.WaterTop;
        var columnFluid = col.ColumnFluid;
        int iceTop = col.IceTop;
        var biome = k.Biomes[col.BiomeIndex];
        var surfaceId = col.SurfaceId;
        bool beachHere = col.BeachHere;
        var bands = col.Bands;
        int bandCount = bands.Length;
        int islandTop = col.IslandTop;
        int oilSeepLo = col.OilSeepLo; // generation 22 (#2371): int.MaxValue on every column without a seep cell
        bool materialBands = col.MaterialBands;
        bool paintedHost = col.PaintedHost;

        // Surface flora: one plant in the air cell directly above the surface (bounded — one per column,
        // no spreading), chosen by biome surface + a density roll. Columns that lie under the sea grow
        // aquatic flora instead (kelp + lily pads); land plants don't grow underwater.
        if (flora && seabedY + 1 > waterTop && oilSeepLo == int.MaxValue) // #2371: nothing grows out of a seep's tar
        {
            // On a beach the painted ground is the beach block, not the biome surface — grow that
            // host's flora (sparse sand tufts), never grass plants standing in sand (#679).
            // A generation-3 landmark paint likewise hosts its own flora (reeds on peat, lichen on a frost
            // ridge's stone, ember blooms on a lava flow's basalt) — never on an older world. Generation 11: altitude
            // snow and ice host their own flora too (frost flowers on a snow cap).
            var floraId = FloraForSurface(planet, biome, seed, worldX, worldZ,
                beachHere || paintedHost || FrozenFloraHost(surfaceId, snowId, iceId) ? surfaceId : (BlockId?)null);
            int fy = seabedY + 1;
            int fly = fy - origin.Y;
            // Local density is modulated by a vegetation-richness mask (lush forest floors / meadows vs
            // sparse open ground) + the per-biome density, so undergrowth gathers into thickets instead
            // of an even sprinkle — and the same forest the trees cluster in is also carpeted with plants.
            // The cold factor (#476) thins growth toward the snow line and stops it at the ice — except, from
            // generation 11, for the cold-adapted species, which grow on far into the frost.
            double localFloraDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ)
                * SurfaceFloraColdFactor(calib, surfaceY, floraId);
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
        else if (waterFlora && columnFluid == seaWaterId && seabedY + 1 <= waterTop - iceTop && !materialBands)
        {
            // (materialBands: an iceberg's hull stands in this water span — no kelp through solid ice.)
            // Submerged WATER column — the sea or an upland pond grows seabed plants / lily pads.
            // The column-fluid check keeps kelp out of lava rivers and volcano craters (#477).
            // Plants stay below any ice sheet, and no lily pads float on a frozen surface (#494);
            // frozen-through columns (guard above) grow nothing at all.
            // A coral-rock floor (a generation-3 reef) grows four times the seabed flora.
            StampWaterFlora(chunk, origin, lx, lz, seed, worldX, worldZ, seabedY, waterTop - iceTop,
                kelpId, iceTop > 0 ? BlockId.Air : lilyId, coralId, seagrassId,
                !coralRockId.IsAir && surfaceId == coralRockId ? floraDensity * 4.0 : floraDensity,
                underwaterForests);
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

        // Generation 21 (#2332): every further crown or island band grows its own meadow — a pillar's balconies,
        // a second-storey crown. Older worlds plant the highest band only (the pass above), so nothing moves there.
        if (flora && bandCount > 1 && wonderGates.Generation >= WorldDescription.SpectacleGeneration)
        {
            for (int b = 0; b < bandCount; b++)
            {
                if ((bands[b].Kind != BandKind.Island && bands[b].Kind != BandKind.Crown) || bands[b].Top == islandTop)
                {
                    continue;
                }

                int by = bands[b].Top + 1 - origin.Y;
                if (by < 0 || by >= WorldConstants.ChunkSize || !chunk.Get(lx, by, lz).IsAir)
                {
                    continue;
                }

                var bandFlora = FloraForSurface(planet, biome, seed, worldX, worldZ);
                double bandDensity = LocalFloraDensity(planet, biome, floraDensity, seed, worldX, worldZ);
                if (!bandFlora.IsAir
                    && Noise.Value01(seed + 9004, WorldConstants.WrapX(worldX, _circumference), 11 + b, Wz(worldZ)) < bandDensity)
                {
                    chunk.Set(lx, by, lz, bandFlora);
                }
            }
        }

        // #1759 (generation 5): hanging kelp under the island — roots in the lowest island cell, grows down
        // into the air (or, under an island afloat, the water) below it, in patches so the underside reads
        // as a fringe and not as stubble. The roster only activates the hanging species on a generation-5
        // world, so older islands stay bare below.
        if (flora && !_hangingFloraId.IsAir && col.IslandBottom != int.MinValue)
        {
            int hy = col.IslandBottom - 1 - origin.Y;
            if (hy >= 0 && hy < WorldConstants.ChunkSize
                && (chunk.Get(lx, hy, lz).IsAir || (!seaWaterId.IsAir && chunk.Get(lx, hy, lz) == seaWaterId))
                && FbmT(seed + 0x4A46, worldX, worldZ, 18.0, octaves: 2) > 0.45
                && Noise.Value01(seed + 9003, WorldConstants.WrapX(worldX, _circumference), 7, Wz(worldZ)) < System.Math.Min(0.6, floraDensity * 2.5))
            {
                chunk.Set(lx, hy, lz, _hangingFloraId);
            }
        }
    }

    /// <summary>Stage 6: the chunk-wide passes after the column loop — cave flora, trees, giant mushrooms and flora, the
    /// giant trees, the band tops, the Fifi plants, geysers, set dressing and the landing pads, in that order.</summary>
    private void StampChunkPasses(ChunkData chunk, FillContext k, System.ReadOnlySpan<int> groundTops,
        System.ReadOnlySpan<int> waterTops)
    {
        var planet = k.Planet;
        long seed = k.Seed;
        var coord = k.Coord;
        var origin = k.Origin;
        var calib = k.Calib;
        var biomes = k.Biomes;
        var wonderGates = k.WonderGates;
        bool flora = k.Flora;
        double floraDensity = k.FloraDensity;
        bool trees = k.Trees;
        double treeDensity = k.TreeDensity;
        var logId = k.LogId;
        var leafId = k.LeafId;
        bool giantMushrooms = k.GiantMushrooms;
        var stemId = k.StemId;
        var capId = k.CapId;
        var myceliumId = k.MyceliumId;
        bool anyBands = k.AnyBands;
        bool geysers = k.Geysers;
        var geyserVentId = k.GeyserVentId;
        int fluidLevel = k.FluidLevel;

        // Generation 11: plants in the caves (floors + ceilings) and the rainbow clusters on the surface.
        StampFloraGen11(planet, seed, chunk, origin, calib, groundTops, waterTops, flora);

        if (trees)
        {
            StampTrees(planet, seed, chunk, coord, biomes, logId, leafId, treeDensity, fluidLevel);
        }

        if (giantMushrooms)
        {
            StampGiantMushrooms(planet, seed, chunk, coord, biomes, stemId, capId, myceliumId, fluidLevel);
        }

        // Generation-1 giant flora (#1648): giant ferns on jungle mud, giant crystals on crystal ground, giant
        // cacti on sand — the same recipe as the mushrooms with their own salts; classic worlds never roll them.
        if (wonderGates.Generation >= 1)
        {
            StampGiantFloraGen1(planet, seed, chunk, coord, biomes, fluidLevel);
        }

        // Generation 6 (#1783): the giant trees — rare landmarks inside the woods, on their own block pair, in
        // the shape of the biome theme's own trees. A separate pass with its own margin and rise, so the
        // ordinary tree scan keeps its 4-cell margin; an older world never grows one.
        if (trees && wonderGates.Generation >= WorldDescription.NewKindsGeneration)
        {
            StampGiantTrees(planet, seed, chunk, coord, biomes, fluidLevel);
        }

        // Generation 21 (#2335): trees, boulders, ore clumps and data caches on the crowns and islands — the band-top
        // host pass. Never on an older world (its sky islands stay bare) and nothing on a world without bands.
        if (anyBands && wonderGates.Generation >= WorldDescription.SpectacleGeneration)
        {
            StampBandTops(planet, seed, chunk, coord, biomes, logId, leafId, treeDensity, fluidLevel);
        }

        // Generation 16 (#2085): Sophie's Fifi plants — groves of tree-sized plants with glowing blossoms and berries on
        // every world with plant life (the pass gates itself on the generation and the world's ground).
        if (flora && floraDensity > 0.0)
        {
            StampFifiPlants(planet, seed, chunk, coord, biomes, fluidLevel, trees);
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
    }
}
