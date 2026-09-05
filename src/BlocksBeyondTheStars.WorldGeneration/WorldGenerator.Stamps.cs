// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>post-column stamps: set dressing, geysers, giant mushrooms, trees (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>What a set-dressing prop is built from — the world's own deep rock, crystal, or dead wood.
    /// Each is resolved per chunk; a prop whose material is Air on this world never rolls.</summary>
    private enum PropMaterial
    {
        Boulder,
        Crystal,
        DeadLog,
    }

    /// <summary>Everything a prop shape needs to stamp itself (#1644): the column, the surface height, the
    /// per-column shape hash, the resolved blocks and the air-only cell setter.</summary>
    private readonly struct PropStamp
    {
        public PropStamp(WorldGenerator generator, PlanetType planet, int wx, int sy, int wz, int shapeHash,
            BlockId material, BlockId cache, System.Action<int, int, int, BlockId> set)
        {
            Generator = generator;
            Planet = planet;
            Wx = wx;
            Sy = sy;
            Wz = wz;
            ShapeHash = shapeHash;
            Material = material;
            Cache = cache;
            Set = set;
        }

        public readonly WorldGenerator Generator;
        public readonly PlanetType Planet;
        public readonly int Wx, Sy, Wz;
        public readonly int ShapeHash;                          // per-column 0..996 (the former h1)
        public readonly BlockId Material;                       // the row's material, resolved for this world
        public readonly BlockId Cache;                          // data_cache (Air when the content lacks it)
        public readonly System.Action<int, int, int, BlockId> Set; // fills AIR cells only, never carves
    }

    /// <summary>One row of the set-dressing prop table (#1644): the per-column roll (its own salt + hash row +
    /// chance), the material whose absence disables the row, and the shape. Rows are tried in table order
    /// and the first hit wins — the order below is exactly the former if/else precedence (monolith &gt;
    /// circle &gt; boulder &gt; shard &gt; dead tree) with the same salts, so classic worlds are unchanged.
    /// Adding a prop = one row + one shape method.</summary>
    private readonly struct PropKind
    {
        public PropKind(string name, long salt, int row, double chance, PropMaterial material, System.Action<PropStamp> shape)
        {
            Name = name;
            Salt = salt;
            Row = row;
            Chance = chance;
            Material = material;
            Shape = shape;
        }

        public readonly string Name;
        public readonly long Salt;      // added to the world seed for this row's roll
        public readonly int Row;        // the middle hash coordinate (keeps every row's roll independent)
        public readonly double Chance;  // per-column probability
        public readonly PropMaterial Material;
        public readonly System.Action<PropStamp> Shape;
    }

    private static readonly PropKind[] PropKinds =
    {
        // Small POIs (W-R3, blocks-only): lone monoliths + broken stone circles, rarer than the props —
        // landmark finds with a data cache at the base/centre worth detouring for.
        new("monolith", 0x3057, 43, 0.00018, PropMaterial.Boulder, StampMonolith),
        new("stone-circle", 0xC1AC, 47, 0.00007, PropMaterial.Boulder, StampStoneCircle),
        // One roll per column per prop kind (distinct salts), all rare — these are scattered accents.
        new("boulder", 0xB01D, 29, 0.0012, PropMaterial.Boulder, StampBoulder),
        new("crystal-shard", 0xC57A, 31, 0.0008, PropMaterial.Crystal, StampCrystalShard),
        new("dead-tree", 0xDEAD, 37, 0.0009, PropMaterial.DeadLog, StampDeadTree),
    };

    /// <summary>The prop table's row names in precedence order (tests).</summary>
    internal static string[] PropOrderForTest()
    {
        var names = new string[PropKinds.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = PropKinds[i].Name;
        }

        return names;
    }

    /// <summary>Stamps sparse scatter props ("Welten reicher" W-R2): boulder clusters (the world's deep rock),
    /// crystal shard outcrops, and bare dead trees — per-column deterministic rolls with a margin scan so a
    /// prop straddling a chunk edge generates identically from either side. Props sit ON the surface
    /// (air cells only) and never spawn in seas/ponds. Driven by the <see cref="PropKinds"/> table (#1644).</summary>
    private void StampSetDressing(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        BlockId boulderId, BlockId crystalId, BlockId deadLogId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;

        void SetCell(int wx, int wy, int wz, BlockId block)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (chunk.Get(lx, ly, lz).IsAir)
            {
                chunk.Set(lx, ly, lz, block); // props fill air only — never carve terrain/other features
            }
        }

        System.Action<int, int, int, BlockId> set = SetCell;
        var cacheId = _content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;

        BlockId MaterialOf(PropMaterial m) => m switch
        {
            PropMaterial.Crystal => crystalId,
            PropMaterial.DeadLog => deadLogId,
            _ => boulderId,
        };

        // Margin 6 so the widest feature (a stone circle, radius ~4) generates identically from either side
        // of a chunk edge.
        for (int wx = origin.X - 6; wx < origin.X + cs + 6; wx++)
            for (int wz = origin.Z - 6; wz < origin.Z + cs + 6; wz++)
            {
                int cx = WorldConstants.WrapX(wx, _circumference);
                int cz = Wz(wz);

                // Table order = precedence: the first row whose material exists here and whose roll hits wins.
                int hit = -1;
                for (int k = 0; k < PropKinds.Length; k++)
                {
                    ref readonly var kind = ref PropKinds[k];
                    if (MaterialOf(kind.Material).IsAir)
                    {
                        continue;
                    }

                    if (Noise.Value01(seed + kind.Salt, cx, kind.Row, cz) < kind.Chance)
                    {
                        hit = k;
                        break;
                    }
                }

                if (hit < 0)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + 1 > origin.Y + cs - 1 || sy + MaxStampRise < origin.Y)
                {
                    continue; // #1527: props write sy+1 .. sy+7 — none of it lands in this chunk
                }

                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                    || SurfaceGen1WaterDepth(planet, wx, wz) > 0)
                {
                    continue; // dry ground only
                }

                int h1 = (int)(Noise.Value01(seed + 0x5E7D, cx, 41, cz) * 997); // per-column shape hash
                ref readonly var row = ref PropKinds[hit];
                row.Shape(new PropStamp(this, planet, wx, sy, wz, h1, MaterialOf(row.Material), cacheId, set));
            }
    }

    /// <summary>A lone weathered monolith, 5–7 tall, with a data cache leaning at its base.</summary>
    private static void StampMonolith(PropStamp s)
    {
        int height = 5 + s.ShapeHash % 3;
        for (int dy = 1; dy <= height; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }

        if (!s.Cache.IsAir)
        {
            s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Cache);
        }
    }

    /// <summary>A broken stone circle: pillars on a radius-4 ring (some collapsed), a data cache at the
    /// centre. Each pillar grounds on its own column so the ring follows the terrain.</summary>
    private static void StampStoneCircle(PropStamp s)
    {
        (int X, int Z)[] ring = { (4, 0), (3, 3), (0, 4), (-3, 3), (-4, 0), (-3, -3), (0, -4), (3, -3) };
        for (int r = 0; r < ring.Length; r++)
        {
            if (((s.ShapeHash >> r) & 1) == 0 && r % 3 == 2)
            {
                continue; // the odd collapsed pillar
            }

            int px = s.Wx + ring[r].X, pz = s.Wz + ring[r].Z;
            int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
            int ph = 2 + ((s.ShapeHash >> r) & 1);
            for (int dy = 1; dy <= ph; dy++)
            {
                s.Set(px, py + dy, pz, s.Material);
            }
        }

        if (!s.Cache.IsAir)
        {
            s.Set(s.Wx, s.Sy + 1, s.Wz, s.Cache);
        }
    }

    /// <summary>An irregular 2–4 block boulder cluster of the world's own rock.</summary>
    private static void StampBoulder(PropStamp s)
    {
        int h1 = s.ShapeHash;
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        if ((h1 & 1) == 0) s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Material);
        if ((h1 & 2) == 0) s.Set(s.Wx, s.Sy + 1, s.Wz + 1, s.Material);
        if ((h1 & 12) == 0) s.Set(s.Wx, s.Sy + 2, s.Wz, s.Material); // the odd two-tall rock
    }

    /// <summary>A jutting crystal shard, 1–3 blocks tall (taller ones rarer).</summary>
    private static void StampCrystalShard(PropStamp s)
    {
        int height = 1 + s.ShapeHash % 3;
        for (int dy = 1; dy <= height; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }
    }

    /// <summary>A bare dead trunk (3–5 tall) with a single stub branch near the top — no leaves.</summary>
    private static void StampDeadTree(PropStamp s)
    {
        int height = 3 + s.ShapeHash % 3;
        for (int dy = 1; dy <= height; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }

        int bx = (s.ShapeHash & 4) == 0 ? 1 : -1;
        s.Set(s.Wx + bx, s.Sy + height - 1, s.Wz, s.Material);
    }

    /// <summary>Stamps sparse geyser/vent marker blocks on the surface (item 21 follow-up): the topmost ground
    /// cell of a rare column becomes a <c>geyser_vent</c> with open air above, which the client detects to play
    /// the eruption VFX + hiss. Never under water/ponds. Deterministic from the seed; very rare (a landmark).</summary>
    private void StampGeysers(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord, BlockId ventId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const double density = 0.0015; // per-column chance (rare — geysers are scattered landmarks)

        for (int wx = origin.X; wx < origin.X + cs; wx++)
            for (int wz = origin.Z; wz < origin.Z + cs; wz++)
            {
                if (Noise.Value01(seed + 0x6E7A, WorldConstants.WrapX(wx, _circumference), 23, Wz(wz)) >= density)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy < origin.Y || sy >= origin.Y + cs)
                {
                    continue; // #1527: the vent is the surface cell itself — not in this chunk
                }

                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                    || SurfaceGen1WaterDepth(planet, wx, wz) > 0)
                {
                    continue; // a vent needs open ground (not a sea/pond column)
                }

                int ly = sy - origin.Y;
                if (ly >= 0 && ly < cs)
                {
                    chunk.Set(wx - origin.X, ly, wz - origin.Z, ventId); // the surface cell becomes a vent
                }
            }
    }

    /// <summary>Stamps towering giant mushrooms (a fibrous stem + a domed cap) on a fungal world's mycelium
    /// ground (item 21 V3). Mirrors <see cref="StampTrees"/>: scans a margin so a mushroom straddling a chunk
    /// edge generates identically from either chunk, and the per-column roll wraps in X. Deterministic.</summary>
    private void StampGiantMushrooms(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, BlockId stemId, BlockId capId, BlockId myceliumId, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int maxCapR = 4;       // the widest a cap can grow — the chunk-edge scan margin must cover it
        const double density = 0.012; // per-column chance on mycelium ground

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return;
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        for (int wx = origin.X - maxCapR; wx < origin.X + cs + maxCapR; wx++)
            for (int wz = origin.Z - maxCapR; wz < origin.Z + cs + maxCapR; wz++)
            {
                if (Noise.Value01(seed + 0x5340, WorldConstants.WrapX(wx, _circumference), 17, Wz(wz)) >= density)
                {
                    continue;
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + 1 > origin.Y + cs - 1 || sy + MaxStampRise < origin.Y)
                {
                    continue; // #1527: a mushroom writes sy+1 .. sy+15 — none of it lands in this chunk
                }

                var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)].Surface;
                if (surf != myceliumId)
                {
                    continue; // only on mycelium ground
                }

                if (TempAt(calib, sy) < TreeLineC)
                {
                    continue; // above the tree line (#476)
                }

                if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                    || SurfaceGen1WaterDepth(planet, wx, wz) > 0)
                {
                    continue; // not in water
                }

                if (DryBeachAt(planet, calib, seed, RiverFieldFor(planet),
                        _content.GetBlock("water")?.NumericId ?? BlockId.Air, wx, wz, sy))
                {
                    continue; // #679: the painted ground here is beach sand — no giant fungi on the beach
                }

                // Per-mushroom size (loosely-coupled stem height + cap): a shared bell factor with independent
                // jitter on each, so a fungal grove reads as a mix of small and towering capped fungi.
                double sizeF = SizeFactor(seed + 0x53410, wx, wz, 0.30);  // overall size, ±30% (bell)
                double hJit = SizeFactor(seed + 0x53411, wx, wz, 0.12);  // independent stem-height jitter
                double cJit = SizeFactor(seed + 0x53412, wx, wz, 0.12);  // independent cap jitter
                int height = System.Math.Clamp((int)System.Math.Round(7.0 * sizeF * hJit), 4, 12);   // ~5..9 before
                int capR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF * cJit), 2, maxCapR); // 2..4
                int topY = sy + height;
                for (int ty = sy + 1; ty <= topY; ty++)
                {
                    SetCell(wx, ty, wz, stemId, overwrite: true);
                }

                // A domed cap: shrinking discs stacked above the stem top (taller dome for bigger caps).
                int capLayers = capR - 1;
                for (int dy = 0; dy <= capLayers; dy++)
                {
                    int rr = capR - dy;
                    for (int dx = -rr; dx <= rr; dx++)
                        for (int dz = -rr; dz <= rr; dz++)
                        {
                            if (dx * dx + dz * dz <= rr * rr + 1)
                            {
                                SetCell(wx + dx, topY + dy, wz + dz, capId, overwrite: false);
                            }
                        }
                }
            }
    }

    /// <summary>A deterministic per-instance size factor centred on 1.0 (a "bell" — the average of two
    /// uniform samples is triangular, so most individuals sit near the species size and extremes are rare).
    /// <paramref name="amp"/> is the half-range (0.30 = ±30%). Pure function of the world column, so it is
    /// identical on the server and every client (vegetation is meshed from the same blocks).</summary>
    private double SizeFactor(long salt, int wx, int wz, double amp)
    {
        int cx = WorldConstants.WrapX(wx, _circumference);
        double u = (Noise.Value01(salt, cx, 23, Wz(wz)) + Noise.Value01(salt ^ 0x9E3779B9, cx, 41, Wz(wz))) * 0.5;
        return 1.0 + (u - 0.5) * 2.0 * amp;
    }

    /// <summary>Stamps multi-block trees on grass/earth columns. Each biome's flora theme dictates the tree
    /// ARCHETYPES its woods are made of (broadleaf / conifer / palm / jungle / dead); a low-frequency grove
    /// mask picks one kind per patch so a wood is all conifers OR all palms, not a jumble of shapes. Each tree
    /// also gets its own size (loosely-coupled trunk + crown). Scans a margin (the MAX crown) so a tree
    /// straddling a chunk edge generates identically from either chunk; the per-column roll wraps in X.
    /// Deterministic from the seed.</summary>
    private void StampTrees(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, BlockId logId, BlockId leafId, double density, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int maxCrown = 4; // the widest a crown can grow (jungle canopy) — the chunk-edge scan margin must cover it
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;
        var sandId = _content.GetBlock("sand")?.NumericId ?? BlockId.Air;
        // Distinct foliage for needled / fronded crowns; fall back to the generic leaf if not in this content.
        var pineId = _content.GetBlock("pine_needles")?.NumericId ?? leafId;
        var palmId = _content.GetBlock("palm_frond")?.NumericId ?? leafId;

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return; // outside this chunk (a neighbour chunk stamps that part of the tree)
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return; // leaves only fill air, never carve the trunk or terrain
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var riverField = RiverFieldFor(planet); // cached — needed for the beach ground check (#679)

        // #1527: the density roll is tested against a conservative UPPER BOUND of every biome's multiplier first,
        // so the ~99 % of margin columns the exact test rejects never pay SurfaceHeight / BiomeIndex. The exact
        // test still runs for the survivors: roll >= bound >= localDensity, and the bound carries a 1e-9 slack
        // so float association can never put it below a biome's own product.
        double maxTreeMul = 0.0;
        foreach (var b in biomes)
        {
            maxTreeMul = System.Math.Max(maxTreeMul, b.TreeMul * b.Theme.TreeMul);
        }

        maxTreeMul *= 1.0 + 1e-9;
        for (int wx = origin.X - maxCrown; wx < origin.X + cs + maxCrown; wx++)
            for (int wz = origin.Z - maxCrown; wz < origin.Z + cs + maxCrown; wz++)
            {
                // FORESTS: a low-frequency mask gathers trees into real groves/woods. Inside a forest patch the
                // density is ~9x, on the fringe ~2x, the open land between almost bare — scaled by the biome's
                // (and its theme's) tree density so savanna stays sparse, jungle dense, fungal/crystal treeless.
                double forest = ForestMaskAt(planet, seed, wx, wz);
                double forestFactor = forest > 0.62 ? 9.0 : forest > 0.52 ? 2.0 : 0.15;
                double roll = Noise.Value01(seed + 5150, WorldConstants.WrapX(wx, _circumference), 11, Wz(wz));
                double densityBound = density * maxTreeMul * forestFactor;
                if (densityBound <= 0.0 || roll >= densityBound)
                {
                    continue; // the exact test below cannot pass either
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + 1 > origin.Y + cs - 1 || sy + MaxStampRise < origin.Y)
                {
                    continue; // every cell a tree here could write lies outside this chunk — SetCell would clip them all
                }

                var biome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)];
                double localDensity = density * biome.TreeMul * biome.Theme.TreeMul * forestFactor;

                // Oasis palm fringe (#1647): the ring around a desert oasis grows dense, and grows palms.
                bool oasisFringe = OasisPalmFringeAt(planet, wx, wz);
                if (oasisFringe)
                {
                    localDensity = System.Math.Max(localDensity * 8.0, 0.08);
                }

                if (localDensity <= 0.0 || roll >= localDensity)
                {
                    continue;
                }

                if (TempAt(calib, sy) < TreeLineC)
                {
                    continue; // above the tree line (#476): woods stop before the snow does
                }

                // Pick a grove kind from the biome theme's tree palette (one kind per low-frequency patch).
                var kind = PickTreeKind(biome.Theme.Trees, seed, wx, wz, planet.TerrainScale);
                if (kind == TreeKind.None)
                {
                    continue; // this theme grows no trees here (e.g. fungal → giant mushrooms instead)
                }

                if (oasisFringe && System.Array.IndexOf(biome.Theme.Trees, TreeKind.Palm) >= 0)
                {
                    kind = TreeKind.Palm;
                }

                if (sy + 1 <= fluidLevel)
                {
                    continue; // not in the sea
                }

                if (SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                    || SurfaceGen1WaterDepth(planet, wx, wz) > 0)
                {
                    continue; // B35: an upland pond/lake, a river or a generation-1 body here — a tree would stand in the water
                }

                // Beaches (#679): on a beach column the painted ground is the beach block, NOT the biome
                // surface (StampTrees can't see Generate's override, so it must ask the shared helper).
                // Only palms / dead snags belong in the sand — themes that grow either get palm-fringed
                // shores, themes with neither leave the beach bare.
                if (DryBeachAt(planet, calib, seed, riverField, waterId, wx, wz, sy))
                {
                    if (System.Array.IndexOf(biome.Theme.Trees, TreeKind.Palm) >= 0)
                    {
                        kind = TreeKind.Palm;
                    }
                    else if (System.Array.IndexOf(biome.Theme.Trees, TreeKind.Dead) >= 0)
                    {
                        kind = TreeKind.Dead;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    var surf = biome.Surface;
                    bool earthy = surf == grassId || surf == dirtId || surf == mudId;
                    bool sandyOk = surf == sandId && (kind == TreeKind.Palm || kind == TreeKind.Dead); // palms/dead snags on sand
                    if (!earthy && !sandyOk)
                    {
                        continue;
                    }
                }

                // Per-tree size (loosely-coupled height + crown): a shared bell factor sets the overall scale,
                // with a smaller independent jitter on each so trunk height and crown width still vary apart.
                double sizeF = SizeFactor(seed + 0x71EE5, wx, wz, 0.30);              // overall tree size, ±30% (bell)
                double hJit = SizeFactor(seed + 0x71EE6, wx, wz, 0.12);              // independent height jitter
                double cJit = SizeFactor(seed + 0x71EE7, wx, wz, 0.12);              // independent crown jitter

                switch (kind)
                {
                    case TreeKind.Conifer: BuildConifer(wx, sy, wz, sizeF, hJit, cJit, logId, pineId, SetCell); break;
                    case TreeKind.Palm: BuildPalm(wx, sy, wz, sizeF, hJit, cJit, logId, palmId, SetCell); break;
                    case TreeKind.Jungle: BuildJungle(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
                    case TreeKind.Dead: BuildDead(wx, sy, wz, sizeF, hJit, logId, SetCell); break;
                    default: BuildBroadleaf(wx, sy, wz, sizeF, hJit, cJit, logId, leafId, SetCell); break;
                }
            }
    }

    /// <summary>The highest cell any surface stamp writes above its column's surface: the jungle crown
    /// (trunk ≤ 14 + crown radius ≤ 4 = 18); conifers reach 14, giant mushrooms 15, props 7. A chunk whose
    /// cells all lie above surface + 18 (or below surface + 1) cannot receive a single write from that
    /// column, so the stamps skip it (#1527) — SetCell clipped those writes before, one by one.</summary>
    private const int MaxStampRise = 18;

    /// <summary>The forest mask of a column (#1527: memoised — StampTrees asks for the 576 margin columns of every
    /// stacked chunk).</summary>
    private double ForestMaskAt(PlanetType planet, long seed, int wx, int wz)
    {
        var key = (planet.Key, ColumnKey(wx, wz));
        lock (_columnLock)
        {
            if (_forestCache.TryGetValue(key, out double cached))
            {
                return cached;
            }
        }

        double forest = FbmT(seed + 0xF07E57, wx, wz, planet.TerrainScale * 2.0, octaves: 3);
        lock (_columnLock)
        {
            if (_forestCache.Count >= SurfaceCacheCap)
            {
                _forestCache.Clear();
            }

            _forestCache[key] = forest;
        }

        return forest;
    }

    /// <summary>Picks one tree archetype for this column from the theme's palette. A low-frequency grove mask
    /// keeps a whole patch to a single kind (a pine wood, a palm grove), not a per-tree jumble.</summary>
    private TreeKind PickTreeKind(TreeKind[] palette, long seed, int wx, int wz, double terrainScale)
    {
        int valid = 0;
        foreach (var k in palette)
        {
            if (k != TreeKind.None)
            {
                valid++;
            }
        }

        if (valid == 0)
        {
            return TreeKind.None;
        }

        if (valid == 1)
        {
            foreach (var k in palette)
            {
                if (k != TreeKind.None)
                {
                    return k;
                }
            }
        }

        double grove = FbmT(seed + 0x70EE17, wx, wz, terrainScale * 3.0, octaves: 2);
        int pick = (int)(grove * valid);
        if (pick >= valid)
        {
            pick = valid - 1;
        }

        int n = 0;
        foreach (var k in palette)
        {
            if (k == TreeKind.None)
            {
                continue;
            }

            if (n++ == pick)
            {
                return k;
            }
        }

        return TreeKind.Broadleaf;
    }

    /// <summary>The classic deciduous tree: a straight trunk under a roughly spherical leaf crown.</summary>
    private static void BuildBroadleaf(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(5.5 * sizeF * hJit), 3, 10);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.0 * sizeF * cJit), 1, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = -1; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 1)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
    }

    /// <summary>A rainforest giant: very tall trunk under a broad, deep canopy.</summary>
    private static void BuildJungle(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(8.0 * sizeF * hJit), 7, 14);
        int crownR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF * cJit), 2, 4);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = -2; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 2)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
    }

    /// <summary>A boreal conifer: tall narrow trunk under a layered conical needle crown tapering to a tip.</summary>
    private static void BuildConifer(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(7.0 * sizeF * hJit), 5, 13);
        int baseR = System.Math.Clamp((int)System.Math.Round(2.0 * sizeF * cJit), 1, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        int crownStart = sy + System.Math.Max(2, height / 3);
        int tip = topY + 1;
        for (int y = crownStart; y <= topY; y++)
        {
            double f = (double)(tip - y) / (tip - crownStart); // wide at the base, ~0 near the tip
            int r = System.Math.Clamp((int)System.Math.Round(baseR * f), 0, baseR);
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz <= r * r + 1)
                    {
                        set(wx + dx, y, wz + dz, leafId, false);
                    }
                }
        }

        set(wx, tip, wz, leafId, false); // pointed tip
    }

    /// <summary>A palm: a bare slender trunk topped by a burst of drooping fronds.</summary>
    private static void BuildPalm(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 5, 11);
        int fr = System.Math.Clamp((int)System.Math.Round(2.0 * cJit), 2, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        set(wx, topY + 1, wz, leafId, false); // crown core
        set(wx, topY, wz, leafId, false);
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 }, { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } };
        for (int i = 0; i < dirs.GetLength(0); i++)
        {
            for (int d = 1; d <= fr; d++)
            {
                int y = topY - (d == fr ? 1 : 0); // the frond tips droop one cell
                set(wx + dirs[i, 0] * d, y, wz + dirs[i, 1] * d, leafId, false);
            }
        }
    }

    /// <summary>A bare dead snag: a trunk with a couple of stub branches and no leaves.</summary>
    private static void BuildDead(int wx, int sy, int wz, double sizeF, double hJit,
        BlockId logId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(4.5 * sizeF * hJit), 3, 8);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        set(wx + 1, topY - 1, wz, logId, false);
        set(wx - 1, topY - 2, wz, logId, false);
        set(wx, topY - 1, wz + 1, logId, false);
        set(wx, topY - 2, wz - 1, logId, false);
    }
}
