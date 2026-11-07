// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3358 (the Sounders story pack): Soaniri's singing meadows — the one pass only a type with
/// <see cref="PlanetType.SingingMeadows"/> runs (the <c>sounder_world</c>), so no other world's chunk moves and no terrain
/// generation gate is needed. Three zones, read from the column's resolved biome: the <b>chime meadows</b> (chime moss over
/// soil) grow bell blooms, chime reeds and now and then an echo flower in patches; the <b>crystal slopes</b> (chime moss over
/// stone) grow fewer plants but carry the obsidian outcrops with their resonance-crystal caps, lone crystal shards and most of
/// the <b>harp arches</b> — two obsidian pillars under a lintel with harp vines hanging between them; the <b>reed marsh</b>
/// (mud) is dense chime reed. The plants stand on the surface cell like every flora pass (a pure function of seed and column);
/// the outcrops and arches are anchor props like the set dressing (an anchor column hashes the roll, the prop is written from
/// every chunk it reaches, air cells only), so a prop reads the same from both sides of a chunk edge. The singing plants are
/// authored catalog species (<see cref="FloraCatalog.SingingPlantKeys"/>): no roster, no world tint.
/// </summary>
public sealed partial class WorldGenerator
{
    private const long MeadowPatchSalt = 0x3358_0A01;
    private const long MeadowRollSalt = 0x3358_0A02;
    private const long MeadowPickSalt = 0x3358_0A03;
    private const long OutcropSalt = 0x3358_0A04;
    private const long HarpArchSalt = 0x3358_0A05;
    private const long ShardSalt = 0x3358_0A06;
    private const long SingingShapeSalt = 0x3358_0A07;

    /// <summary>Share of chime-meadow columns that grow a singing plant, before the patch field (which averages out near 1).</summary>
    internal const double MeadowPlantChance = 0.16;

    /// <summary>Share of crystal-slope columns that grow a singing plant.</summary>
    internal const double SlopePlantChance = 0.06;

    /// <summary>Share of reed-marsh columns that grow a chime reed.</summary>
    internal const double MarshReedChance = 0.32;

    /// <summary>Per-column anchor chance of an obsidian outcrop on the crystal slopes (and, far rarer, in the meadows).</summary>
    internal const double SlopeOutcropChance = 0.010;

    internal const double MeadowOutcropChance = 0.0025;

    /// <summary>Per-column anchor chance of a harp arch on the crystal slopes (and in the meadows).</summary>
    internal const double SlopeArchChance = 0.004;

    internal const double MeadowArchChance = 0.0012;

    /// <summary>Per-column chance of a lone resonance-crystal shard on the crystal slopes (each one is a light source).</summary>
    internal const double SlopeShardChance = 0.004;

    /// <summary>How far an anchor prop reaches from its anchor column (the arch: two pillars two cells either side).</summary>
    private const int SingingPropMargin = 3;

    /// <summary>The highest cell a prop writes above its anchor's ground (an arch of 5 plus its crystal).</summary>
    private const int SingingPropRise = 7;

    /// <summary>The zone of a singing-meadow column.</summary>
    internal enum SingingZone
    {
        None,
        Meadow,
        Slope,
        Marsh,
    }

    /// <summary>The block ids the pass writes, resolved once per chunk (air when a key is missing: that part stays off).</summary>
    private readonly struct SingingIds
    {
        public SingingIds(WorldGenerator g)
        {
            Moss = g.ResolveKey("chime_moss");
            Mud = g.ResolveKey("mud");
            Stone = g.ResolveKey("stone");
            Obsidian = g.ResolveKey("obsidian");
            Crystal = g.ResolveKey("resonance_crystal");
            Reed = g.ResolveKey(FloraCatalog.ChimeReedKey);
            Bloom = g.ResolveKey(FloraCatalog.BellBloomKey);
            Vine = g.ResolveKey(FloraCatalog.HarpVineKey);
            Echo = g.ResolveKey(FloraCatalog.EchoFlowerKey);
        }

        public BlockId Moss { get; }
        public BlockId Mud { get; }
        public BlockId Stone { get; }
        public BlockId Obsidian { get; }
        public BlockId Crystal { get; }
        public BlockId Reed { get; }
        public BlockId Bloom { get; }
        public BlockId Vine { get; }
        public BlockId Echo { get; }

        /// <summary>Whether <paramref name="id"/> is one of the singing plants the pass writes.</summary>
        public bool IsPlant(BlockId id) => !id.IsAir && (id == Reed || id == Bloom || id == Vine || id == Echo);
    }

    private BlockId ResolveKey(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;

    /// <summary>The singing meadows of one chunk (see the class summary). Called from <c>StampChunkPasses</c> for a
    /// <see cref="PlanetType.SingingMeadows"/> type only, before the landing pads are flattened.</summary>
    private void StampSingingMeadows(ChunkData chunk, FillContext k, System.ReadOnlySpan<int> groundTops,
        System.ReadOnlySpan<int> waterTops)
    {
        var ids = new SingingIds(this);
        if (ids.Moss.IsAir || ids.Obsidian.IsAir)
        {
            return;
        }

        if (k.Flora && _floraFactor > 0)
        {
            PlantSingingColumns(chunk, k, ids, groundTops, waterTops);
        }

        StampSingingProps(chunk, k, ids);
    }

    /// <summary>The zone of a column from its resolved biome: mud is the reed marsh, chime moss over stone the crystal
    /// slopes, every other chime-moss biome the meadows; anything else no zone.</summary>
    private SingingZone SingingZoneOf(FillContext k, in SingingIds ids, int worldX, int worldZ, int surfaceY)
        => SingingZoneOf(k.Biomes, k.Calib, k.Seed, ids, worldX, worldZ, surfaceY);

    private SingingZone SingingZoneOf(System.Collections.Generic.List<BiomeResolved> biomes, WorldCalibration calib, long seed,
        in SingingIds ids, int worldX, int worldZ, int surfaceY)
    {
        var b = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, worldX, worldZ, biomes.Count, surfaceY)];
        if (b.Surface == ids.Mud)
        {
            return SingingZone.Marsh;
        }

        if (b.Surface != ids.Moss)
        {
            return SingingZone.None;
        }

        return b.Sub == ids.Stone ? SingingZone.Slope : SingingZone.Meadow;
    }

    /// <summary>Public read of a column's singing zone (tests, the server's site search): <c>none</c>, <c>meadow</c>,
    /// <c>slope</c> or <c>marsh</c>.</summary>
    public string SingingZoneAt(PlanetType planet, int worldX, int worldZ)
    {
        if (!planet.SingingMeadows)
        {
            return "none";
        }

        var zone = SingingZoneOf(ResolveBiomes(planet), CalibFor(planet), PlanetSeed(planet), new SingingIds(this), worldX, worldZ,
            SurfaceHeight(planet, worldX, worldZ));
        return zone switch
        {
            SingingZone.Meadow => "meadow",
            SingingZone.Slope => "slope",
            SingingZone.Marsh => "marsh",
            _ => "none",
        };
    }

    /// <summary>One singing plant per lucky surface column: dry ground only, on the zone's own host (chime moss, or mud in
    /// the marsh), in patches.</summary>
    private void PlantSingingColumns(ChunkData chunk, FillContext k, in SingingIds ids, System.ReadOnlySpan<int> groundTops,
        System.ReadOnlySpan<int> waterTops)
    {
        const int n = WorldConstants.ChunkSize;
        var origin = k.Origin;
        for (int lx = 0; lx < n; lx++)
            for (int lz = 0; lz < n; lz++)
            {
                int ground = groundTops[lx * n + lz];
                int ly = ground + 1 - origin.Y;
                if (ly < 0 || ly >= n || ground + 1 <= waterTops[lx * n + lz] || !chunk.Get(lx, ly, lz).IsAir)
                {
                    continue; // the cell above the ground is in another chunk, under water, or taken
                }

                int worldX = origin.X + lx, worldZ = origin.Z + lz;
                var zone = SingingZoneOf(k, ids, worldX, worldZ, ground);
                var host = ly >= 1 ? chunk.Get(lx, ly - 1, lz) : (zone == SingingZone.Marsh ? ids.Mud : ids.Moss);
                if (zone == SingingZone.None || host != (zone == SingingZone.Marsh ? ids.Mud : ids.Moss))
                {
                    continue; // a beach, a paint or a stamped feature on top — no host for a singing plant
                }

                var plant = PickSingingPlant(k.Seed, ids, zone, worldX, worldZ);
                if (!plant.IsAir)
                {
                    chunk.Set(lx, ly, lz, plant);
                }
                else if (zone == SingingZone.Slope && !ids.Crystal.IsAir
                         && Noise.Value01(k.Seed + ShardSalt, WorldConstants.WrapX(worldX, _circumference), 5, Wz(worldZ)) < SlopeShardChance)
                {
                    chunk.Set(lx, ly, lz, ids.Crystal); // a lone resonance-crystal shard pushing out of the moss
                }
            }
    }

    /// <summary>The plant a column grows (air for none): the zone's chance, scaled by a patch field so plants gather in
    /// singing patches, then a weighted pick — meadows: bell bloom 5, chime reed 4, echo flower 1; slopes: chime reed 3,
    /// bell bloom 2, echo flower 1; the marsh: chime reed only.</summary>
    private BlockId PickSingingPlant(long seed, in SingingIds ids, SingingZone zone, int worldX, int worldZ)
    {
        int cx = WorldConstants.WrapX(worldX, _circumference), cz = Wz(worldZ);
        double patch = FbmT(seed + MeadowPatchSalt, worldX, worldZ, 14.0, octaves: 2);
        double patchMul = patch > 0.58 ? 2.2 : patch > 0.47 ? 1.0 : 0.3;
        double chance = zone switch
        {
            SingingZone.Meadow => MeadowPlantChance,
            SingingZone.Slope => SlopePlantChance,
            _ => MarshReedChance,
        } * patchMul * System.Math.Min(2.0, _floraFactor);
        if (Noise.Value01(seed + MeadowRollSalt, cx, 1, cz) >= chance)
        {
            return BlockId.Air;
        }

        if (zone == SingingZone.Marsh)
        {
            return ids.Reed;
        }

        double pick = Noise.Value01(seed + MeadowPickSalt, cx, 2, cz);
        return zone == SingingZone.Meadow
            ? (pick < 0.5 ? ids.Bloom : pick < 0.9 ? ids.Reed : ids.Echo)
            : (pick < 0.5 ? ids.Reed : pick < 0.83 ? ids.Bloom : ids.Echo);
    }
}
