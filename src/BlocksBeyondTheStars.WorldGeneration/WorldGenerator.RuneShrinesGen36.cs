// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>A generated rune shrine (#3416): where it stands and whether its ring of eight is whole.</summary>
public readonly struct RuneShrine
{
    public RuneShrine(int x, int floorY, int z, bool complete, int stones)
    {
        X = x;
        FloorY = floorY;
        Z = z;
        Complete = complete;
        Stones = stones;
    }

    /// <summary>The centre column's X (canonical, 0 … circumference − 1).</summary>
    public int X { get; }

    /// <summary>The row of the worn brick floor; the altar stands on it.</summary>
    public int FloorY { get; }

    /// <summary>The centre column's Z (canonical latitude).</summary>
    public int Z { get; }

    /// <summary>All eight ring stones stand — the ring the Sounders' shrine steps accept.</summary>
    public bool Complete { get; }

    /// <summary>How many of the eight ring spots hold a standing rune stone (3–8).</summary>
    public int Stones { get; }

    /// <summary>The altar: one ancient brick on the floor in the middle of the ring.</summary>
    public Vector3i Altar => new(X, FloorY + 1, Z);
}

/// <summary>
/// Rune shrines (#3416, terrain generation 36 — Justus' F1 reports 2026-11-08): the rune stones no longer stand alone on the
/// hills (the generation-1 <c>rune-stone</c> prop row is off from this generation); they stand in small shrine ruins. The world
/// is cut into cells of about <see cref="RuneShrineCellSize"/> blocks; a cell rolls a shrine with <see cref="RuneShrineChance"/>
/// and tries up to <see cref="RuneShrineSpotTries"/> seeded spots in it until the ground holds one (dry — no sea, pond, river,
/// lake, seep or lava —, a relief of at most <see cref="RuneShrineMaxRelief"/>, no arch over it, no hall or tunnel under it,
/// clear of every landing pad). A shrine is a worn floor of ancient brick (a disc of radius ≈ 5.5, levelled to the median
/// ground: brick fill on the low side, the hill cut away on the high side), an altar brick in the middle, rune stones (two or
/// three tall) on a ring of radius 4 — all eight in a <b>complete</b> shrine, which is exactly the ring the Sounders' shrine
/// steps accept (<c>MelodySiteGeometry.FindRing</c> around an ancient brick), three to six in a partial one — broken brick
/// pillars on the rim, half the time an arch over the way in, loose bricks and now and then a data cache. Each chunk writes its
/// own columns from the world seed alone, so a shrine is the same from either side of a chunk edge; the vegetation and prop
/// passes skip anchors whose spread would reach into a shrine (<see cref="RuneShrineClaims"/>). (partial of
/// <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The cell pitch: one shrine candidate per cell this wide (a default 6000-block world has 54 × 27 cells).</summary>
    internal const double RuneShrineCellSize = 112.0;

    /// <summary>The share of cells that roll a shrine (the ground then refuses some of them).</summary>
    internal const double RuneShrineChance = 0.8;

    /// <summary>How many seeded spots of a rolled cell are tried before the cell stays empty.</summary>
    internal const int RuneShrineSpotTries = 4;

    /// <summary>The share of shrines whose ring of eight is whole.</summary>
    internal const double RuneShrineCompleteChance = 0.5;

    /// <summary>The floor disc: every column with dx² + dz² at most this (radius ≈ 5.5, eleven across).</summary>
    internal const int RuneShrineDiskR2 = 30;

    /// <summary>The highest and lowest sampled ground under a shrine may differ this much (fill and cut stay within three).</summary>
    internal const int RuneShrineMaxRelief = 5;

    /// <summary>The widest spread a vegetation or prop pass asks <see cref="RuneShrineClaims"/> about (a giant tree's crown).</summary>
    private const int RuneShrineMaxReach = 16;

    /// <summary>A shrine's centre keeps this far from its cell's edges, so every column it or a guarded anchor reaches lies in
    /// the same cell — a column only ever asks its own cell.</summary>
    private const double RuneShrineMargin = 6.0 + RuneShrineMaxReach;

    /// <summary>The clear air above the floor the shrine keeps (its arch's lintel is the highest block, five up).</summary>
    private const int RuneShrineClearRise = 6;

    private const long RuneShrineSalt = 0x3416_5A1E;
    private const long RuneShrineWearSalt = 0x3416_3EA2;

    /// <summary>The ring of rune stones in the shrine's own frame, clockwise from north: radius 4 (4 and 4.24 from the altar —
    /// one stone per 45° sector, well inside the ring rule's 1.9–5.8 and its 1.5 roundness).</summary>
    private static readonly (int U, int V)[] RuneShrineRing =
    {
        (0, 4), (3, 3), (4, 0), (3, -3), (0, -4), (-3, -3), (-4, 0), (-3, 3),
    };

    /// <summary>The rim pillars in the shrine's own frame (between the ring stones' bearings, 5.4 out). Pillars 3 and 4 flank
    /// the way in (the frame's south) and carry the arch.</summary>
    private static readonly (int U, int V)[] RuneShrinePillars =
    {
        (2, 5), (5, 2), (5, -2), (2, -5), (-2, -5), (-5, -2), (-5, 2), (-2, 5),
    };

    private const int RuneShrineGateRight = 3;
    private const int RuneShrineGateLeft = 4;

    private const int SpotOutside = -1, SpotFloor = 0, SpotAltar = 1, SpotCache = 2, SpotRing = 10, SpotPillar = 20;

    /// <summary>What stands on each cell of the shrine's frame (u, v in −6..6): outside, floor, altar, cache, ring i, pillar j.</summary>
    private static readonly sbyte[] RuneShrineSpots = BuildRuneShrineSpots();

    /// <summary>One cell's rolled shrine — <see cref="Valid"/> false when the cell rolled none or the ground refused every spot.</summary>
    private sealed class RuneShrineSite
    {
        public static readonly RuneShrineSite None = new();

        public bool Valid;
        public int X, Z, FloorY, Turn;
        public bool Complete, Arch, ArchBroken, Cache;
        public int Stones, Tall, Stumps;
        public readonly int[] PillarHeights = new int[8];
        public ulong Hash;

        public int StoneCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < 8; i++)
                {
                    n += (Stones >> i) & 1;
                }

                return n;
            }
        }
    }

    /// <summary>The blocks a shrine is built from; no shrine without the brick and the rune stone.</summary>
    private readonly struct RuneShrineIds
    {
        public RuneShrineIds(GameContent content)
        {
            Brick = content.GetBlock("ancient_brick")?.NumericId ?? BlockId.Air;
            Rune = content.GetBlock("rune_stone")?.NumericId ?? BlockId.Air;
            Cache = content.GetBlock("data_cache")?.NumericId ?? BlockId.Air;
        }

        public readonly BlockId Brick, Rune, Cache;

        public bool Usable => !Brick.IsAir && !Rune.IsAir;
    }

    /// <summary>The shrine cells of the current world: X wraps with the circumference, Z with the latitude period.</summary>
    private readonly struct RuneShrineGrid
    {
        public RuneShrineGrid(int circumference)
        {
            Circumference = circumference;
            Period = WorldConstants.LatitudePeriodFor(circumference);
            Nx = System.Math.Max(1, (int)System.Math.Round(circumference / RuneShrineCellSize));
            Nz = System.Math.Max(1, (int)System.Math.Round(Period / RuneShrineCellSize));
            Cw = circumference / (double)Nx;
            Ch = Period / (double)Nz;
        }

        public readonly int Circumference, Period, Nx, Nz;
        public readonly double Cw, Ch;

        /// <summary>A world too small for a cell to hold a shrine and its margin raises none.</summary>
        public bool Roomy => Cw > (2.0 * RuneShrineMargin) + 2.0 && Ch > (2.0 * RuneShrineMargin) + 2.0;

        public int CellX(int worldX) => System.Math.Min(Nx - 1, (int)(WorldConstants.WrapX(worldX, Circumference) / Cw));

        public int CellZ(int worldZ) => System.Math.Min(Nz - 1, (int)((((worldZ + (Period / 2)) % Period) + Period) % Period / Ch));
    }

    private readonly Dictionary<(string Planet, int A, int B), RuneShrineSite> _runeShrines = new();
    private readonly object _runeShrineLock = new();

    /// <summary>The generation-1 lone rune stone's gate: the ruins worlds of a generation before the shrines — and, from
    /// generation 36 on, the ruins worlds whose type can never raise a shrine (a structure whitelist without "monuments":
    /// Titas, Valuma), so they keep their lone stones as before.</summary>
    private static bool PropLoneRuneStone(WonderProfile w, PlanetType p)
        => PropRuins(w, p) && (w.Generation < WorldDescription.RuneShrineGeneration || !RuneShrinesAllowedOn(p));

    /// <summary>A type may raise rune shrines unless its structure whitelist leaves out its rune relics ("monuments" — Soaniri
    /// keeps them, Titas and Valuma do not).</summary>
    private static bool RuneShrinesAllowedOn(PlanetType p) => !p.RestrictStructures || p.AllowedStructures.Contains("monuments");

    /// <summary>Whether this world raises rune shrines: generation 36 or later, the lone stone's own world rule (solid ground,
    /// air, not cratered), and a type with a structure whitelist only when it keeps its rune relics ("monuments", Soaniri).</summary>
    private bool RuneShrinesOn(PlanetType planet)
        => _terrainGeneration >= WorldDescription.RuneShrineGeneration && PropRuins(WonderFor(planet), planet)
           && RuneShrinesAllowedOn(planet) && new RuneShrineGrid(_circumference).Roomy;

    /// <summary>The shrine of this column's cell when the column lies within √<paramref name="reachR2"/> of its centre, with
    /// the column's offset from that centre. False when the cell has none or the column is farther out.</summary>
    private bool TryGetRuneShrine(PlanetType planet, long seed, int worldX, int worldZ, long reachR2,
        out RuneShrineSite site, out int dx, out int dz)
    {
        var grid = new RuneShrineGrid(_circumference);
        site = RuneShrineAtCell(planet, seed, grid, grid.CellX(worldX), grid.CellZ(worldZ));
        dx = WorldConstants.WrapDeltaX(worldX - site.X, _circumference);
        dz = WorldConstants.WrapDeltaZ(worldZ - site.Z, _circumference);
        return site.Valid && ((long)dx * dx) + ((long)dz * dz) <= reachR2;
    }

    /// <summary>The memoised shrine of a cell (per generator, dropped with the column memos on a mode change).</summary>
    private RuneShrineSite RuneShrineAtCell(PlanetType planet, long seed, in RuneShrineGrid grid, int a, int b)
    {
        var key = (planet.Key, a, b);
        lock (_runeShrineLock)
        {
            if (_runeShrines.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var site = BuildRuneShrine(planet, seed ^ RuneShrineSalt, grid, a, b);
        lock (_runeShrineLock)
        {
            if (_runeShrines.Count >= 4096)
            {
                _runeShrines.Clear();
            }

            _runeShrines[key] = site;
        }

        return site;
    }

    /// <summary>Drops the memoised shrine sites (the column memos' mode change).</summary>
    private void InvalidateRuneShrines()
    {
        lock (_runeShrineLock)
        {
            _runeShrines.Clear();
        }
    }

    /// <summary>Rolls a cell: whether it holds a shrine, then its seeded spots in turn until the ground holds one, then the
    /// layout (from the cell's own hash, so it does not depend on which spot was taken).</summary>
    private RuneShrineSite BuildRuneShrine(PlanetType planet, long salt, in RuneShrineGrid grid, int a, int b)
    {
        ulong h = Noise.Hash(salt, a, 0, b);
        if ((h & 0xFFFF) / 65536.0 >= RuneShrineChance)
        {
            return RuneShrineSite.None;
        }

        for (int attempt = 1; attempt <= RuneShrineSpotTries; attempt++)
        {
            ulong spot = Noise.Hash(salt, a, attempt, b);
            double ox = RuneShrineMargin + (((spot >> 20) & 0x3FF) / 1023.0 * (grid.Cw - (2.0 * RuneShrineMargin)));
            double oz = RuneShrineMargin + (((spot >> 36) & 0x3FF) / 1023.0 * (grid.Ch - (2.0 * RuneShrineMargin)));
            int cx = (int)((a * grid.Cw) + ox);
            int cz = (int)((b * grid.Ch) + oz) - (grid.Period / 2);
            if (!RuneShrineNearPad(cx, cz) && RuneShrineFloor(planet, cx, cz, out int floorY))
            {
                var site = new RuneShrineSite { Valid = true, X = cx, Z = cz, FloorY = floorY, Hash = h };
                RollRuneShrineLayout(site, h);
                return site;
            }
        }

        return RuneShrineSite.None;
    }

    /// <summary>A shrine keeps clear of every landing pad's levelled ground (its islet included), with a few blocks to spare.</summary>
    private bool RuneShrineNearPad(int cx, int cz)
    {
        if (_sites.Length != 0 && SiteClaims(cx, cz, 16))
        {
            return true; // generation 40 (#3568): nor does a shrine seat in (or next to) a reserved site
        }

        foreach (var p in _landingPads)
        {
            int dx = WorldConstants.WrapDeltaX(cx - p.CenterX, _circumference);
            int dz = WorldConstants.WrapDeltaZ(cz - p.CenterZ, _circumference);
            double keep = System.Math.Max(p.Radius, p.IsletRadius) + IsletRimWobble + 6.0 + 4.0;
            if ((dx * (double)dx) + (dz * (double)dz) < keep * keep)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The samples under a shrine (its centre, the ring and the rim): a relief of at most
    /// <see cref="RuneShrineMaxRelief"/>, every one on dry land (no sea, pond, river, lake, seep or lava), no band over and no
    /// void under the centre and four rim points. The floor is the median sample. The cheap relief test runs first.</summary>
    private bool RuneShrineFloor(PlanetType planet, int cx, int cz, out int floorY)
    {
        floorY = 0;
        var heights = new int[1 + RuneShrineRing.Length + RuneShrinePillars.Length];
        for (int i = 0; i < heights.Length; i++)
        {
            var (u, v) = RuneShrineSample(i);
            heights[i] = SurfaceHeight(planet, cx + u, cz + v);
        }

        var sorted = (int[])heights.Clone();
        System.Array.Sort(sorted);
        if (sorted[sorted.Length - 1] - sorted[0] > RuneShrineMaxRelief)
        {
            return false;
        }

        int sea = SeaLevel(planet);
        for (int i = 0; i < heights.Length; i++)
        {
            var (u, v) = RuneShrineSample(i);
            int x = cx + u, z = cz + v;
            if (heights[i] + 1 <= sea || SurfacePondDepth(planet, x, z) > 0 || SurfaceRiverDepth(planet, x, z) > 0
                || SurfaceGen1WaterDepth(planet, x, z) > 0 || OilSeepNear(planet, x, z) || IsSurfaceLava(planet, x, z))
            {
                return false; // the shrine stands on dry land only
            }
        }

        if (!RuneShrineOpenGround(planet, cx, cz))
        {
            return false;
        }

        floorY = sorted[sorted.Length / 2];
        return true;
    }

    /// <summary>Sample <paramref name="i"/> under a shrine: the centre, then the eight ring spots, then the eight rim pillars.</summary>
    private static (int U, int V) RuneShrineSample(int i)
        => i == 0 ? (0, 0) : i <= RuneShrineRing.Length ? RuneShrineRing[i - 1] : RuneShrinePillars[i - 1 - RuneShrineRing.Length];

    /// <summary>No arch, crown or sky island over, no cavern, hall or tunnel under the centre and the four rim points.</summary>
    private bool RuneShrineOpenGround(PlanetType planet, int cx, int cz)
    {
        for (int i = 0; i < 5; i++)
        {
            int x = cx + (i == 1 ? 5 : i == 2 ? -5 : 0), z = cz + (i == 3 ? 5 : i == 4 ? -5 : 0);
            if (ColumnHasBandAbove(planet, x, z) || ColumnHasVoidBelow(planet, x, z, 4))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The layout of one shrine, a pure function of its cell hash: its turn, whole or partial ring (and which stones),
    /// tall stones, stumps of the missing ones, the rim pillars' heights, the arch, the cache.</summary>
    private static void RollRuneShrineLayout(RuneShrineSite s, ulong h)
    {
        ulong state = (h ^ 0x9E3779B97F4A7C15UL) | 1UL;
        double Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return (state & 0xFFFFF) / 1048576.0;
        }

        s.Turn = (int)(Next() * 4.0) & 3;
        s.Complete = Next() < RuneShrineCompleteChance;
        int count = s.Complete ? 8 : 3 + (int)(Next() * 4.0); // a partial ring keeps three to six stones
        var order = new int[8];
        for (int i = 0; i < 8; i++)
        {
            order[i] = i;
        }

        for (int i = 7; i > 0; i--)
        {
            int j = (int)(Next() * (i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int i = 0; i < count; i++)
        {
            s.Stones |= 1 << order[i];
        }

        for (int i = 0; i < 8; i++)
        {
            s.Tall |= Next() < 0.35 ? 1 << i : 0;
            s.Stumps |= Next() < 0.5 ? 1 << i : 0;
            s.PillarHeights[i] = Next() < 0.6 ? 1 + (int)(Next() * 4.0) : 0;
        }

        s.Arch = Next() < 0.5;
        s.ArchBroken = Next() < 0.5;
        if (s.Arch)
        {
            s.PillarHeights[RuneShrineGateLeft] = 4;
            s.PillarHeights[RuneShrineGateRight] = s.ArchBroken ? 2 : 4;
        }

        s.Cache = Next() < 0.4;
    }

    // ---------------- the chunk pass ----------------

    /// <summary>Writes the shrine columns of this chunk — after every vegetation and prop pass, before the pads are levelled.</summary>
    private void StampRuneShrines(ChunkData chunk, FillContext k)
    {
        var planet = k.Planet;
        var ids = new RuneShrineIds(_content);
        if (!RuneShrinesOn(planet) || !ids.Usable)
        {
            return;
        }

        long seed = WonderFor(planet).Seed;
        int cs = WorldConstants.ChunkSize;
        if (!RuneShrineTouchesChunk(planet, seed, k.Origin))
        {
            return; // the common case: no shrine floor reaches into this chunk's columns
        }

        for (int lx = 0; lx < cs; lx++)
            for (int lz = 0; lz < cs; lz++)
            {
                int wx = k.Origin.X + lx, wz = k.Origin.Z + lz;
                if (TryGetRuneShrine(planet, seed, wx, wz, RuneShrineDiskR2, out var site, out int dx, out int dz))
                {
                    WriteRuneShrineColumn(chunk, k.Origin, ids, site, SurfaceHeight(planet, wx, wz), lx, lz, dx, dz);
                }
            }
    }

    /// <summary>Whether the shrine of a cell under one of the chunk's four corner columns reaches into the chunk's columns (a
    /// chunk is narrower than a cell, so these are all the cells it touches).</summary>
    private bool RuneShrineTouchesChunk(PlanetType planet, long seed, Vector3i origin)
    {
        var grid = new RuneShrineGrid(_circumference);
        int cs = WorldConstants.ChunkSize, half = cs / 2, reach = half + 6;
        for (int corner = 0; corner < 4; corner++)
        {
            int x = origin.X + ((corner & 1) * (cs - 1)), z = origin.Z + ((corner >> 1) * (cs - 1));
            var s = RuneShrineAtCell(planet, seed, grid, grid.CellX(x), grid.CellZ(z));
            if (s.Valid
                && System.Math.Abs(WorldConstants.WrapDeltaX(s.X - (origin.X + half), _circumference)) <= reach
                && System.Math.Abs(WorldConstants.WrapDeltaZ(s.Z - (origin.Z + half), _circumference)) <= reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One floor column: brick fill from the ground up to the floor on the low side, the hill cut away above the
    /// floor on the high side, the floor itself (a worn cell keeps the ground it lies on) and the shrine above it, clear air
    /// up to <see cref="RuneShrineClearRise"/> over the floor (and three over the column's own ground).</summary>
    private static void WriteRuneShrineColumn(ChunkData chunk, Vector3i origin, in RuneShrineIds ids, RuneShrineSite s,
        int ground, int lx, int lz, int dx, int dz)
    {
        int f = s.FloorY;
        var (u, v) = RuneShrineLocal(s.Turn, dx, dz);
        int cs = WorldConstants.ChunkSize;
        int y0 = System.Math.Max(System.Math.Min(ground + 1, f), origin.Y);
        int y1 = System.Math.Min(System.Math.Max(f + RuneShrineClearRise, ground + 3), origin.Y + cs - 1);
        bool worn = ground >= f && RuneShrineSpot(u, v) == SpotFloor && RuneShrineRoll(s, u, v, 0) < 0.14;

        // A cave right under the floor is plugged three deep, so the floor never hangs over a hole.
        for (int y = System.Math.Max(f - 3, origin.Y); y <= System.Math.Min(System.Math.Min(f - 1, ground), origin.Y + cs - 1); y++)
        {
            if (chunk.Get(lx, y - origin.Y, lz).IsAir)
            {
                chunk.Set(lx, y - origin.Y, lz, ids.Brick);
            }
        }

        for (int y = y0; y <= y1; y++)
        {
            if (y == f && worn)
            {
                continue; // a missing floor brick: the ground shows through
            }

            var block = y <= f ? ids.Brick : RuneShrineBlockAt(s, ids, u, v, y - f);
            int ly = y - origin.Y;
            chunk.Set(lx, ly, lz, block);
            if (!block.IsAir)
            {
                chunk.SetModifier(lx, ly, lz, 0, 0); // the shrine's own blocks carry no tint or shape of what stood here
                chunk.SetShape(lx, ly, lz, 0);
            }
        }
    }

    /// <summary>The shrine's block <paramref name="h"/> rows above the floor at (u, v) of its own frame — air where nothing stands.</summary>
    private static BlockId RuneShrineBlockAt(RuneShrineSite s, in RuneShrineIds ids, int u, int v, int h)
    {
        int spot = RuneShrineSpot(u, v);
        if (spot == SpotAltar)
        {
            return h == 1 ? ids.Brick : BlockId.Air;
        }

        if (spot >= SpotRing && spot < SpotRing + 8)
        {
            int i = spot - SpotRing;
            if (((s.Stones >> i) & 1) != 0)
            {
                return h <= (((s.Tall >> i) & 1) != 0 ? 3 : 2) ? ids.Rune : BlockId.Air;
            }

            return h == 1 && ((s.Stumps >> i) & 1) != 0 ? ids.Brick : BlockId.Air; // the stump of a fallen stone
        }

        if (spot >= SpotPillar && h <= s.PillarHeights[spot - SpotPillar])
        {
            return ids.Brick;
        }

        if (s.Arch && h == 5 && v == -5 && u >= -2 && u <= (s.ArchBroken ? -1 : 2))
        {
            return ids.Brick; // the lintel over the way in (a broken arch keeps its left end)
        }

        if (h != 1)
        {
            return BlockId.Air;
        }

        if (spot == SpotCache && s.Cache && !ids.Cache.IsAir)
        {
            return ids.Cache;
        }

        bool inTheWayIn = v <= -3 && u >= -1 && u <= 1;
        return spot == SpotFloor && !inTheWayIn && RuneShrineRoll(s, u, v, 16) < 0.05 ? ids.Brick : BlockId.Air; // loose bricks
    }

    /// <summary>A per-cell roll of the shrine in [0, 1): <paramref name="shift"/> 0 = floor wear, 16 = loose bricks.</summary>
    private static double RuneShrineRoll(RuneShrineSite s, int u, int v, int shift)
        => ((Noise.Hash(RuneShrineWearSalt ^ (long)s.Hash, u, 0, v) >> shift) & 0xFFFF) / 65536.0;

    /// <summary>A world offset from the shrine's centre in the shrine's own frame (<paramref name="turn"/> quarter turns).</summary>
    private static (int U, int V) RuneShrineLocal(int turn, int dx, int dz) => turn switch
    {
        1 => (dz, -dx),
        2 => (-dx, -dz),
        3 => (-dz, dx),
        _ => (dx, dz),
    };

    /// <summary>The inverse of <see cref="RuneShrineLocal"/>: a cell of the shrine's frame as a world offset from its centre.</summary>
    private static (int Dx, int Dz) RuneShrineWorld(int turn, int u, int v) => turn switch
    {
        1 => (-v, u),
        2 => (-u, -v),
        3 => (v, -u),
        _ => (u, v),
    };

    private static int RuneShrineSpot(int u, int v)
        => u < -6 || u > 6 || v < -6 || v > 6 ? SpotOutside : RuneShrineSpots[((u + 6) * 13) + v + 6];

    private static sbyte[] BuildRuneShrineSpots()
    {
        var spots = new sbyte[13 * 13];
        for (int u = -6; u <= 6; u++)
            for (int v = -6; v <= 6; v++)
            {
                spots[((u + 6) * 13) + v + 6] = (sbyte)((u * u) + (v * v) <= RuneShrineDiskR2 ? SpotFloor : SpotOutside);
            }

        void Put(int u, int v, int spot) => spots[((u + 6) * 13) + v + 6] = (sbyte)spot;
        Put(0, 0, SpotAltar);
        Put(1, 1, SpotCache);
        for (int i = 0; i < 8; i++)
        {
            Put(RuneShrineRing[i].U, RuneShrineRing[i].V, SpotRing + i);
            Put(RuneShrinePillars[i].U, RuneShrinePillars[i].V, SpotPillar + i);
        }

        return spots;
    }

    // ---------------- the other passes keep out ----------------

    /// <summary>
    /// True when a rune shrine (generation 36) stands within <paramref name="reach"/> blocks of its floor from this column — a
    /// tree, a giant plant, a geyser or a prop anchored here would grow into the ruin (or lose its trunk to the shrine's clear
    /// air and float), so the passes skip such an anchor. <paramref name="reach"/> is the pass's own spread (a crown's radius);
    /// at most <see cref="RuneShrineMaxReach"/>. Always false on an older world.
    /// </summary>
    private bool RuneShrineClaims(PlanetType planet, int worldX, int worldZ, int reach)
    {
        // Generation 40 (#3568): a reserved site claims its box the same way — every vegetation and prop pass asks here
        // before it anchors, so a town's ground stays clear without the stamper carving trees out of it.
        if (_sites.Length != 0 && SiteClaims(worldX, worldZ, reach))
        {
            return true;
        }

        if (!RuneShrinesOn(planet))
        {
            return false;
        }

        double r = System.Math.Sqrt(RuneShrineDiskR2) + System.Math.Min(reach, RuneShrineMaxReach) + 1.0;
        return TryGetRuneShrine(planet, WonderFor(planet).Seed, worldX, worldZ, (long)System.Math.Ceiling(r * r), out _, out _, out _);
    }

    // ---------------- queries ----------------

    /// <summary>
    /// The rune shrines of this world whose centre lies within <paramref name="radius"/> blocks of (<paramref name="worldX"/>,
    /// <paramref name="worldZ"/>), horizontally and across both seams, nearest first — as generated (a mined stone or a stamp
    /// built over one is not seen here). Empty on a world without shrines. Cost: one site per cell in reach, memoised.
    /// </summary>
    public List<RuneShrine> RuneShrinesNear(PlanetType planet, int worldX, int worldZ, int radius)
    {
        var found = new List<(long D2, RuneShrine Shrine)>();
        if (RuneShrinesOn(planet) && new RuneShrineIds(_content).Usable)
        {
            CollectRuneShrines(planet, worldX, worldZ, radius, found);
        }

        found.Sort((a, b) => a.D2.CompareTo(b.D2));
        var list = new List<RuneShrine>(found.Count);
        foreach (var f in found)
        {
            list.Add(f.Shrine);
        }

        return list;
    }

    private void CollectRuneShrines(PlanetType planet, int worldX, int worldZ, int radius, List<(long D2, RuneShrine Shrine)> found)
    {
        long seed = WonderFor(planet).Seed;
        var grid = new RuneShrineGrid(_circumference);
        int cxI = grid.CellX(worldX), czI = grid.CellZ(worldZ);
        int kx = System.Math.Min(grid.Nx / 2, (int)System.Math.Ceiling(radius / grid.Cw) + 1);
        int kz = System.Math.Min(grid.Nz / 2, (int)System.Math.Ceiling(radius / grid.Ch) + 1);
        var seen = new HashSet<(int, int)>();
        long r2 = (long)radius * radius;
        for (int i = -kx; i <= kx; i++)
            for (int j = -kz; j <= kz; j++)
            {
                int a = (((cxI + i) % grid.Nx) + grid.Nx) % grid.Nx, b = (((czI + j) % grid.Nz) + grid.Nz) % grid.Nz;
                if (!seen.Add((a, b)) || RuneShrineAtCell(planet, seed, grid, a, b) is not { Valid: true } s)
                {
                    continue;
                }

                long ddx = WorldConstants.WrapDeltaX(s.X - worldX, _circumference);
                long ddz = WorldConstants.WrapDeltaZ(s.Z - worldZ, _circumference);
                long d2 = (ddx * ddx) + (ddz * ddz);
                if (d2 <= r2)
                {
                    found.Add((d2, new RuneShrine(s.X, s.FloorY, s.Z, s.Complete, s.StoneCount)));
                }
            }
    }

    /// <summary>
    /// The nearest rune shrine within <paramref name="radius"/> blocks of (<paramref name="worldX"/>, <paramref name="worldZ"/>)
    /// that <paramref name="accept"/> takes (the server asks for a complete one no stamp was built over). Walks the cells ring by
    /// ring outward and stops as soon as no farther ring can hold a nearer shrine, so a near find costs a handful of cells.
    /// </summary>
    public bool TryFindNearestRuneShrine(PlanetType planet, int worldX, int worldZ, int radius,
        System.Func<RuneShrine, bool> accept, out RuneShrine shrine)
    {
        shrine = default;
        if (!RuneShrinesOn(planet) || !new RuneShrineIds(_content).Usable)
        {
            return false;
        }

        long seed = WonderFor(planet).Seed;
        var grid = new RuneShrineGrid(_circumference);
        int cxI = grid.CellX(worldX), czI = grid.CellZ(worldZ);
        double step = System.Math.Min(grid.Cw, grid.Ch);
        int rings = System.Math.Min((System.Math.Max(grid.Nx, grid.Nz) / 2) + 1, (int)System.Math.Ceiling(radius / step) + 1);
        var seen = new HashSet<(int, int)>();
        long best = long.MaxValue, r2 = (long)radius * radius;
        for (int k = 0; k <= rings && (best == long.MaxValue || System.Math.Sqrt(best) > (k - 1) * step); k++)
        {
            for (int i = -k; i <= k; i++)
                for (int j = -k; j <= k; j++)
                {
                    int a = (((cxI + i) % grid.Nx) + grid.Nx) % grid.Nx, b = (((czI + j) % grid.Nz) + grid.Nz) % grid.Nz;
                    if (System.Math.Max(System.Math.Abs(i), System.Math.Abs(j)) != k || !seen.Add((a, b))
                        || RuneShrineAtCell(planet, seed, grid, a, b) is not { Valid: true } s)
                    {
                        continue;
                    }

                    long ddx = WorldConstants.WrapDeltaX(s.X - worldX, _circumference);
                    long ddz = WorldConstants.WrapDeltaZ(s.Z - worldZ, _circumference);
                    long d2 = (ddx * ddx) + (ddz * ddz);
                    var candidate = new RuneShrine(s.X, s.FloorY, s.Z, s.Complete, s.StoneCount);
                    if (d2 <= r2 && d2 < best && accept(candidate))
                    {
                        best = d2;
                        shrine = candidate;
                    }
                }
        }

        return best != long.MaxValue;
    }

    /// <summary>The base cells (the lowest stone) of the standing ring stones of the shrine centred at (x, z), in world
    /// coordinates near that centre, clockwise from the shrine's own north. Empty when no shrine is centred there (tests).</summary>
    internal List<Vector3i> RuneShrineStoneBasesForTest(PlanetType planet, int x, int z)
    {
        var list = new List<Vector3i>();
        if (!RuneShrinesOn(planet) || !TryGetRuneShrine(planet, WonderFor(planet).Seed, x, z, 0, out var s, out _, out _))
        {
            return list;
        }

        for (int i = 0; i < 8; i++)
        {
            if (((s.Stones >> i) & 1) != 0)
            {
                var (dx, dz) = RuneShrineWorld(s.Turn, RuneShrineRing[i].U, RuneShrineRing[i].V);
                list.Add(new Vector3i(x + dx, s.FloorY + 1, z + dz));
            }
        }

        return list;
    }

    /// <summary>Whether a vegetation or prop anchor at this column would be kept out by a shrine (tests).</summary>
    internal bool RuneShrineClaimsForTest(PlanetType planet, int x, int z, int reach) => RuneShrineClaims(planet, x, z, reach);
}
