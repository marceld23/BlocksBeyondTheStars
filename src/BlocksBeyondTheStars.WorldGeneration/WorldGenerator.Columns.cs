// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>chunk generation: Generate and the per-column profile — the column phase; the fill stages Generate calls are in
/// .ChunkFill (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
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

        var k = ResolveFillContext(planet, coord);

        // Scratch spans reused by every column (#705/#708) — allocated once per chunk (CA2014).
        System.Span<ColumnBand> bandScratch = stackalloc ColumnBand[MaxColumnBands];
        System.Span<(int Lo, int Hi)> tunnelScratch = stackalloc (int Lo, int Hi)[TunnelMaxSpans];
        // Generation 24 (#2516): the neighbouring columns' fluid spans, refilled per column.
        System.Span<(int Lo, int Hi)> sealSpans = stackalloc (int Lo, int Hi)[4];
        // Generation 11: each column's ground top + water top, for the cave flora pass after the column loop.
        System.Span<int> groundTops = stackalloc int[WorldConstants.ChunkSize * WorldConstants.ChunkSize];
        System.Span<int> waterTops = stackalloc int[WorldConstants.ChunkSize * WorldConstants.ChunkSize];

        // #1526: everything the column phase reads, bundled once per chunk for ComputeColumn.
        var ctx = BuildColumnContext(k);

        // #1527: per-chunk ore invariants + one lazily built noise lattice per (column, field): slot 0 caves,
        // slot 1 the lava pockets, then a coarse + fine slot per vein.
        k.OreSlots = BuildOreSlots(planet, k.Seed, k.OreRichness);
        k.Samplers = new TorusColumnSampler[2 + k.OreSlots.Length * 2];

        for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
            for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
            {
                int worldX = k.Origin.X + lx;
                int worldZ = k.Origin.Z + lz;
                // #1526: the whole column phase is memoised per (planet, column) — every stacked chunk of a column
                // used to recompute it; now only the first one does (ComputeColumn holds the original code).
                var col = ColumnProfileFor(ctx, worldX, worldZ, bandScratch, tunnelScratch);
                TorusColumnSampler.ResetAll(k.Samplers); // #1527: the per-column noise lattices rebuild lazily
                groundTops[lx * WorldConstants.ChunkSize + lz] = col.SeabedY;
                waterTops[lx * WorldConstants.ChunkSize + lz] = col.WaterTop;
                int sealCount = CollectSealSpans(ctx, k, col, worldX, worldZ, bandScratch, tunnelScratch, sealSpans, out bool ownFluid);
                FillColumnCells(chunk, k, col, lx, lz, worldX, worldZ, sealSpans, sealCount, ownFluid);
                PlantColumnFlora(chunk, k, col, lx, lz, worldX, worldZ);
            }

        StampChunkPasses(chunk, k, groundTops, waterTops);

        return chunk;
    }

    /// <summary>#1526: one column's terrain profile — everything the y-loop (FillColumnCells) needs that depends only on
    /// (planet, x, z). Immutable once built; shared by every stacked chunk of the column.</summary>
    private sealed class ColumnProfile
    {
        public int SurfaceY, SeabedY, WaterTop, IceTop, BiomeIndex, IslandTop, CavLo, CavHi, CavLakeY, EffSurfaceDepth;
        public int GeoLo, GeoHi, GeoInLo, GeoInHi, StrataShift = int.MinValue; // #1646
        public bool CavernHere, BeachHere, GeodeHere;
        /// <summary>Generation 18 (#2106): the oil pocket's shell span and inner (oil) span; OilHere is false on every
        /// older column.</summary>
        public int OilLo, OilHi = -1, OilInLo = 1, OilInHi;
        public bool OilHere;
        /// <summary>Generation 22 (#2371): the lowest cell of the seep on this column (every cell from it up to the ground is
        /// the seep's tar), int.MaxValue on every column without one; OilPuddle = the ground cell is the oil puddle.</summary>
        public int OilSeepLo = int.MaxValue;
        public bool OilPuddle;
        public BlockId ColumnFluid, SurfaceId, SubSurfaceId;
        public BlockId? CraterMetal;
        public ColumnBand[] Bands = System.Array.Empty<ColumnBand>();
        public (int Lo, int Hi)[] Tunnels = System.Array.Empty<(int Lo, int Hi)>();

        // Terrain generation 3 — every field at its classic no-op value on a generation 0–2 column.
        /// <summary>The lowest Y a landmark paint claims below the topsoil (MinValue = topsoil only).</summary>
        public int PaintFillToY = int.MinValue;
        /// <summary>The blocks the paint fill lays down in bands parallel to the surface, top first (null = the
        /// single paint block). Rainbow strata are the reference consumer.</summary>
        public BlockId[]? PaintCycle;
        /// <summary>Fluid spans below the seabed (an underground river's water); at most two.</summary>
        public (int Lo, int Hi, BlockId Fluid)[] SubFluid = System.Array.Empty<(int Lo, int Hi, BlockId Fluid)>();
        /// <summary>[ShieldLo, ShieldHi] is never cave-carved — the rock around a sub-surface fluid span. Empty when Lo &gt; Hi.</summary>
        public int ShieldLo = 1, ShieldHi = 0;
        /// <summary>True when a band of kind Ice or Fluid covers the column (they must be written before the sea fill).</summary>
        public bool MaterialBands;
        /// <summary>Dripstone lengths (part 3): cells hanging from a carve span's roof / rising from its floor; 0/0 unless
        /// the world drips and the column carries a tunnel or a cavern.</summary>
        public byte DripDown, DripUp;
        /// <summary>A generation-3 landmark paint set the surface block, so the surface flora follows the painted
        /// block instead of the biome's (part 5). Never true on a generation 0–2 column.</summary>
        public bool PaintedHost;

        /// <summary>School club wave 3 (#1759): the lowest cell of the column's sky-island bands (the underside a
        /// hanging plant roots in), or MinValue when no island stands over this column.</summary>
        public int IslandBottom = int.MinValue;

        /// <summary>Generation 8 (Titas): how many top cells take the surface block (a snow blanket); 1 = classic.</summary>
        public int CoverDepth = 1;

        /// <summary>Generation 21 (#2336): the ground a crown band wears — the biome at the CROWN's height (a crown 90 up
        /// may be alpine where its foot is lowland, and snow or ice past the snow line), not the column's own. Air =
        /// no crown over the column; the y-loop then falls back to the column's biome.</summary>
        public BlockId CrownSurface, CrownSub;
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
        public bool OilWorld; // #2106 (generation 18): this world carries oil pockets and has both blocks
        public BlockId OilId, TarShellId; // #2106
        public bool Gen1Paints; // #1647
        public BlockId GrassId, DirtId, MudId, SandId, StoneId, GraniteId, MossStoneId, ScreeId, SandstoneId, AshId; // #1647
        public BlockId SeaSandId; // #2074: the sand of this type's sand sea (= SandId on every classic type)
        public double PondThreshold;
        public BlockId SeabedId; // #1757: the floor of every submerged sea column beyond the beach apron (Air = classic)
        public int SnowCover; // generation 8 (Titas): a fixed snow blanket this deep on dry land (0 = the classic snow pass)
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
        var wonder = WonderFor(planet); // #712/#1644/#1647: the per-world profile (gates, rows, generation)

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);

        // Generation 21 (#2340): a daylight hall under this column. Under its skylight the column's GROUND is the hall
        // floor — the surface, the biome, the flora and the bodies below all work on the floor; the roofed rim is a
        // mega-cavern span (taken up where the classic cavern is). Nothing on any older column.
        bool hallOpen = false, hallRoofed = false;
        int hallLo = 0, hallHi = -1, hallLake = int.MinValue;
        if (wonder.DaylightHalls && TryGetDaylightHallSpan(planet, wonder, worldX, worldZ, surfaceY, out hallLo, out hallHi, out hallLake, out hallOpen))
        {
            if (hallOpen)
            {
                surfaceY = hallLo - 1;
            }
            else
            {
                hallRoofed = true;
            }
        }

        // Generation 8 (Titas): a hot-zone column — its ponds hold lava, it never snows or freezes. False elsewhere.
        bool hotHere = calib.HotBiome >= 0 && HotZoneAt(calib, seed, worldX, worldZ);

        // An upland pond carves a shallow bowl here (seabed below the terrain) and fills it with water up to
        // the original surface (a pond flush with the surrounding ground), so the column reads as a swimmable
        // pool. Normal columns leave seabed=surface and fill the sea up to the global level, unchanged.
        int seabedY = surfaceY;
        int waterTop = fluidLevel;
        var columnFluid = fluidId;
        if (hallOpen)
        {
            waterTop = seabedY; // #2340: a hall floor under the sea line is a sealed bowl, not a sea column — dry unless its lake fills it
        }

        bool pondHere = false;
        double? pondMask = null; // computed at most once per column; shared with the beach rim test (#679)
        if (ponds && surfaceY > fluidLevel)
        {
            pondMask = PondMaskAt(planet, seed, worldX, worldZ);
            // Below generation 24 the pond fills flush to this column's own ground; from 24 (#2516) to the pond's one level.
            if (TryGetPondWater(planet, seed, worldX, worldZ, surfaceY, fluidLevel, pondThreshold, pondMask.Value, out int pondTop, out int pondBed))
            {
                seabedY = pondBed;
                waterTop = pondTop;
                columnFluid = hotHere && !craterLavaId.IsAir ? craterLavaId : seaWaterId;
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
        // Underground reach (generation 3): the surface stays; the passage and its water are carved below it.
        bool passageHere = false;
        int passageLo = 0, passageHi = -1, subLo = 0, subHi = -1, shieldLo = 1, shieldHi = 0;
        if (!pondHere && !craterHere && !hallOpen && surfaceY > fluidLevel && riverField.TryGet(worldX, worldZ, out var river))
        {
            if (river.Underground)
            {
                // The roof is the CENTERLINE's value, so on a slope it may sit above this column's own ground —
                // keep the carve under the surface unless this is the mouth (the shaft through the ground).
                int roof = river.Mouth ? river.RoofY : System.Math.Min(river.RoofY, surfaceY - 1);
                bool water = river.WaterSurfaceY > river.BedY; // a bank column carries air only
                passageLo = water ? river.BedY + 1 : river.WaterSurfaceY + 1;
                passageHi = roof;
                passageHere = passageHi >= passageLo;
                if (water)
                {
                    subLo = river.BedY + 1;
                    subHi = System.Math.Min(river.WaterSurfaceY, roof);
                }

                shieldLo = river.BedY - 2;
                shieldHi = roof + 2;
            }
            else
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

        // Generation 21 (#2340): the daylight hall's lake fills the floor's bowl under the skylight (the roofed rim's
        // share is the cavern branch's); the floor near the rim rises above the lake and stays dry.
        if (hallOpen && hallLake > seabedY && seabedY == surfaceY && !seaWaterId.IsAir)
        {
            waterTop = hallLake;
            columnFluid = seaWaterId;
        }

        // Generation-1 bodies (#1647): marsh sheets, oases, hot springs, caldera / shield / maar lakes, playas,
        // tarns — only on a column no classic body claimed (still plain: bed = surface, water = the sea line).
        // TryGetGen1Water is the same function the surface-water helpers read, so they agree by construction.
        if (c.Gen1Paints && !hallOpen && seabedY == surfaceY && waterTop == fluidLevel && surfaceY > fluidLevel
            && TryGetGen1Water(planet, wonder, worldX, worldZ, surfaceY, out int g1Top, out int g1Bed, out var g1Fluid))
        {
            seabedY = g1Bed;
            waterTop = g1Top;
            columnFluid = g1Fluid;
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
        bool sandSeaHere = calib.SandBiome >= 0 && biomeIndex == calib.SandBiome; // generation 9 (#2000)
        var surfaceId = biome.Surface;
        var subSurfaceId = biome.Sub;

        // Beaches (#679): near a water shoreline the ground turns to the beach block — surface AND
        // sub-surface, so the varied topsoil depth yields a real sand layer, and the shallow seabed
        // apron continues the beach under water. The coast mask alternates beach and bare shore;
        // the snow pass below still dusts cold coasts, and volcano basalt still wins near a cone.
        bool beachHere = false;
        if (beachPossible && !hallOpen)
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

        // #1757 (generation 5): beyond the beach apron the whole sea floor takes the type's seabed block — sand you
        // can dig in on the rainbow planet. Never on a river, a pond or a dry column; Air (every other type) = classic.
        if (!c.SeabedId.IsAir && !beachHere && !hallOpen && surfaceY < fluidLevel && fluidId == seaWaterId && seabedY == surfaceY)
        {
            surfaceId = c.SeabedId;
            subSurfaceId = c.SeabedId;
        }

        // Generation-1 paints (#1647) on plain dry land (no body on the column, no beach): marsh mud, oasis
        // ring, spring crust, playa salt, scree slopes, ash fall, dry beds, deck bands, soil patches, moss.
        if (c.Gen1Paints && !beachHere && seabedY == surfaceY && waterTop <= surfaceY && surfaceY > fluidLevel
            && Gen1SurfacePaint(planet, wonder, c, worldX, worldZ, surfaceY, biome.Surface, out var g1Sub) is { } g1Painted)
        {
            surfaceId = g1Painted;
            if (g1Sub is { } g1SubId)
            {
                subSurfaceId = g1SubId;
            }
        }

        // Generation 8 (Titas): a fixed snow blanket over the sub-surface rock on dry land instead of the altitude rule — no
        // ice ground. The beds under water show the rock; beaches and hot zones stay bare.
        int coverDepth = 1;
        if (c.SnowCover > 0)
        {
            if (!hotHere && !beachHere && surfaceY > waterTop)
            {
                surfaceId = snowId;
                subSurfaceId = biome.Sub; // under the blanket lies the type's rock, never a scree or soil paint
                coverDepth = c.SnowCover;
            }
            else if (!hotHere && !beachHere)
            {
                surfaceId = subSurfaceId;
            }
        }

        // Altitude climate (#476): above the snow line the ground gets a snow cover, further up solid
        // ice. Dithered (±1.5 °C noise) so the line wanders naturally instead of cutting a contour.
        else if (snowPossible && surfaceY > waterTop)
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
        var landmarkPaints = wonder.ActivePaints;
        var landmarkCycles = wonder.ActivePaintCycles;
        int paintFillToY = int.MinValue; // generation 3: the last painter that hit decides the fill, like the block
        BlockId[]? paintCycle = null;
        bool paintedHost = false;
        for (int i = 0; i < landmarkPaints.Length; i++)
        {
            if (landmarkPaints[i](this, planet, wonder, worldX, worldZ, surfaceY, out int fillToY) is { } painted)
            {
                surfaceId = painted;
                subSurfaceId = painted;
                paintFillToY = fillToY;
                paintCycle = landmarkCycles[i]?.Invoke(this, planet, wonder, worldX, worldZ);
                paintedHost = wonder.Generation >= 3; // part 5: the flora follows a generation-3 paint
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
        int islandBottom = int.MinValue; // #1759: the lowest island cell — where a hanging plant roots
        bool materialBands = false; // generation 3: Ice / Fluid bands are written before the sea fill
        for (int b = 0; b < bandCount; b++)
        {
            // A crown (generation 21, #2332) is an island for the flora passes: a meadow on top, hanging plants below.
            if (bands[b].Kind == BandKind.Island || bands[b].Kind == BandKind.Afloat || bands[b].Kind == BandKind.Crown)
            {
                if (bands[b].Top > islandTop)
                {
                    islandTop = bands[b].Top;
                }

                if (islandBottom == int.MinValue || bands[b].Bottom < islandBottom)
                {
                    islandBottom = bands[b].Bottom;
                }

                if (bands[b].Kind != BandKind.Island)
                {
                    materialBands = true; // #1757: the keel stands inside the sea span, written before the fill; a crown too
                }
            }
            else if (bands[b].Kind == BandKind.Ice || bands[b].Kind == BandKind.Fluid || bands[b].Kind == BandKind.Mat
                || bands[b].Kind == BandKind.Rock)
            {
                materialBands = true;
            }
        }

        // Generation 21 (#2336): a crown wears the biome of ITS height — alpine meadow, snow or ice on a crown 90 up —
        // never the lowland (or seabed) its stem rises from. Resolved once per column for every crown band over it.
        BlockId crownSurface = BlockId.Air, crownSub = BlockId.Air;
        int crownTop = int.MinValue;
        for (int b = 0; b < bandCount; b++)
        {
            if (bands[b].Kind == BandKind.Crown && bands[b].Top > crownTop)
            {
                crownTop = bands[b].Top;
            }
        }

        if (crownTop != int.MinValue)
        {
            var crownBiome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, crownTop)];
            crownSurface = crownBiome.Surface;
            crownSub = crownBiome.Sub;
            if (snowPossible)
            {
                double crownT = TempAt(calib, crownTop) + (FbmT(seed + 0x51F0, worldX, worldZ, 24.0, octaves: 2) - 0.5) * 3.0;
                if (crownT < IceLineC && !iceId.IsAir)
                {
                    crownSurface = iceId;
                    crownSub = iceId;
                }
                else if (crownT < SnowLineC && !snowId.IsAir)
                {
                    crownSurface = snowId;
                }
            }
        }

        // Underground mega-cavern (#707): this column's void span, if a cavern covers it.
        int cavLo = 0, cavHi = -1, cavLakeY = int.MinValue;
        bool cavernHere = cavernWorld
            && TryGetCavernSpan(planet, worldX, worldZ, out cavLo, out cavHi, out cavLakeY);

        // Generation 21 (#2340): the daylight hall's roofed rim is written like a mega-cavern (a classic cavern on the
        // same column keeps precedence; both are rare).
        if (!cavernHere && hallRoofed)
        {
            cavernHere = true;
            cavLo = hallLo;
            cavHi = System.Math.Min(hallHi, seabedY - 1);
            cavLakeY = hallLake;
        }

        // Generation 9 (#2000): nothing opens under the sand sea — its deep sand band is shielded from every carver
        // (caves, the worm tunnels below, and the mega-caverns), so the sandworm's buried body never shows through.
        if (sandSeaHere)
        {
            int seaLo = surfaceY - System.Math.Max(1, planet.SandSeaDepth) - 3, seaHi = surfaceY + 1;
            if (shieldHi >= shieldLo)
            {
                shieldLo = System.Math.Min(shieldLo, seaLo);
                shieldHi = System.Math.Max(shieldHi, seaHi);
            }
            else
            {
                shieldLo = seaLo;
                shieldHi = seaHi;
            }

            if (cavernHere && cavHi >= seaLo)
            {
                cavernHere = false;
            }
        }

        // Tunnel carver (#708): this column's worm-carve y-spans (empty on most columns). An underground river's
        // passage (generation 3) is appended as one more span: the same carve, one more source.
        int tunnelCount = tunnelWorld ? TunnelSpans(planet, worldX, worldZ, tunnelSpans) : 0;
        if (shieldHi >= shieldLo)
        {
            // The cave shield also keeps the worms out: a classic span overlapping the shielded rock around the
            // passage is clipped to what lies outside it (at most one extra piece when it straddles the shield).
            System.Span<(int Lo, int Hi)> original = stackalloc (int Lo, int Hi)[TunnelMaxSpans];
            tunnelSpans.Slice(0, tunnelCount).CopyTo(original);
            int kept = 0;
            for (int t = 0; t < tunnelCount; t++)
            {
                var (lo, hi) = original[t];
                if (hi < shieldLo || lo > shieldHi)
                {
                    tunnelSpans[kept++] = (lo, hi);
                    continue;
                }

                if (lo < shieldLo)
                {
                    tunnelSpans[kept++] = (lo, shieldLo - 1);
                }

                if (hi > shieldHi && kept < tunnelSpans.Length - 1)
                {
                    tunnelSpans[kept++] = (shieldHi + 1, hi); // leave one slot for the passage itself
                }
            }

            tunnelCount = kept;
        }

        if (passageHere && tunnelCount < tunnelSpans.Length)
        {
            tunnelSpans[tunnelCount++] = (passageLo, passageHi);
        }

        // Sub-surface fluid spans (generation 3): the underground river's water, and — part 6 — a blowhole's shaft:
        // water from the sea line up to the cell under the vent, sealed by the vent block above and the cave
        // shield around it (generated fluid is a bottomless source; a dug-into shaft gushes, which is the point).
        var subFluidSpans = System.Array.Empty<(int Lo, int Hi, BlockId Fluid)>();
        if (subHi >= subLo)
        {
            subFluidSpans = new[] { (subLo, subHi, riverField.FillFluid) };
        }

        if (wonder.Blowholes && !seaWaterId.IsAir && seabedY == surfaceY && surfaceY > fluidLevel + 2
            && BlowholeAt(planet, wonder, worldX, worldZ))
        {
            int shaftLo = fluidLevel + 1, shaftHi = surfaceY - 1;
            subFluidSpans = subFluidSpans.Length == 0
                ? new[] { (shaftLo, shaftHi, seaWaterId) }
                : new[] { subFluidSpans[0], (shaftLo, shaftHi, seaWaterId) };
            shieldLo = shieldHi >= shieldLo ? System.Math.Min(shieldLo, shaftLo - 2) : shaftLo - 2;
            shieldHi = shieldHi >= shieldLo ? System.Math.Max(shieldHi, surfaceY + 1) : surfaceY + 1;
        }

        // Dripstone (generation 3, part 3): resolved once per column, and only where a carve exists to hang it in.
        byte dripDown = 0, dripUp = 0;
        if (wonder.Dripstone && (tunnelCount > 0 || cavernHere))
        {
            var (down, up) = DripstoneAt(wonder, worldX, worldZ);
            dripDown = (byte)down;
            dripUp = (byte)up;
        }

        // Generation-1 underground finds (#1646): the geode sphere covering this column, and the strata region.
        int geoLo = 0, geoHi = -1, geoInLo = 1, geoInHi = 0;
        bool geodeHere = c.GeodeWorld && TryGetGeodeSpan(planet, wonder, worldX, worldZ, out geoLo, out geoHi, out geoInLo, out geoInHi);
        int strataShift = c.StrataWorld ? StrataShiftAt(seed, worldX, worldZ) : int.MinValue;

        // Generation 18 (#2106): the oil pocket covering this column (clamped under the local ground).
        // Generation 22 (#2371): and its seep, if it has one — the puddle only where no water stands over the ground.
        int oilLo = 0, oilHi = -1, oilInLo = 1, oilInHi = 0, oilSeepLo = int.MaxValue;
        bool oilPuddle = false;
        bool oilHere = c.OilWorld && TryGetOilPocketSpan(planet, wonder, worldX, worldZ, seabedY,
            out oilLo, out oilHi, out oilInLo, out oilInHi, out oilSeepLo, out oilPuddle);
        oilPuddle = oilPuddle && waterTop <= seabedY;

        // Crater-floor metal clumps (item 33): on a cratered world, the top cells of a metal-bearing deep
        // crater floor are exposed rare ore instead of regolith (only some craters, a few clumps each).
        BlockId? craterMetal = (planet.Cratered || _crateredWorld)
            ? CraterFloorMetal(planet, seed, worldX, worldZ) : (BlockId?)null;

        // Non-uniform topsoil: this column's surface/sub-surface layer thickness (varies per column, not a
        // flat band) so the stone/ore boundary undulates and reaches close to the surface in the thin spots.
        int effSurfaceDepth = VariedSurfaceDepth(planet, seed, worldX, worldZ) + coverDepth - 1; // the blanket sits on top
        if (sandSeaHere)
        {
            effSurfaceDepth = System.Math.Max(effSurfaceDepth, planet.SandSeaDepth); // #2000: the sea is deep sand
        }

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
            IslandBottom = islandBottom,
            CavernHere = cavernHere,
            CavLo = cavLo,
            CavHi = cavHi,
            CavLakeY = cavLakeY,
            Tunnels = tunnelCount > 0 ? tunnelSpans.Slice(0, tunnelCount).ToArray() : System.Array.Empty<(int Lo, int Hi)>(),
            CraterMetal = craterMetal,
            EffSurfaceDepth = effSurfaceDepth,
            CoverDepth = coverDepth,
            GeodeHere = geodeHere,
            GeoLo = geoLo,
            GeoHi = geoHi,
            GeoInLo = geoInLo,
            GeoInHi = geoInHi,
            OilHere = oilHere,
            OilLo = oilLo,
            OilHi = oilHi,
            OilInLo = oilInLo,
            OilInHi = oilInHi,
            OilSeepLo = oilHere ? oilSeepLo : int.MaxValue,
            OilPuddle = oilHere && oilPuddle,
            StrataShift = strataShift,
            // Terrain generation 3
            PaintFillToY = paintFillToY,
            PaintCycle = paintCycle,
            SubFluid = subFluidSpans,
            ShieldLo = shieldLo,
            ShieldHi = shieldHi,
            MaterialBands = materialBands,
            DripDown = dripDown,
            DripUp = dripUp,
            PaintedHost = paintedHost,
            CrownSurface = crownSurface,
            CrownSub = crownSub,
        };
    }
}
