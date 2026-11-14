// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration.Sources;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>How a maze ruin is built over its plan (#3645): the wall height above the floor, the statue share of the roomy
/// dead ends and the three blocks (walls, the accent of the band and the pillar caps, the paving).</summary>
internal readonly struct MazeRuinLook
{
    public MazeRuinLook(int height, int statues, ushort wall, ushort accent, ushort floor)
    {
        Height = height;
        Statues = statues;
        Wall = wall;
        Accent = accent;
        Floor = floor;
    }

    /// <summary>The walls' height above the floor row (each wall section ±1).</summary>
    public int Height { get; }

    /// <summary>The percent of the dead ends with room for one (<see cref="MazeRuinGenerator.StatueRoom"/>) that carry a
    /// statue; 0 = no statue but the centre's (the halls carry one whenever this is above 0).</summary>
    public int Statues { get; }

    public ushort Wall { get; }

    public ushort Accent { get; }

    public ushort Floor { get; }
}

/// <summary>
/// #3645: a maze ruin of the Labyrinth Builders, built over its <see cref="MazeRuinPlan"/> as a structure that stands intact —
/// the ruin pass is the template's (a generator entry with <c>ruin: always</c>, profile <c>masonry</c>). Row 0 is the floor:
/// the paving under every passage, the wall stone under every wall. Every wall section (a wall between two cells, one post
/// where wall lines cross) stands <see cref="MazeRuinLook.Height"/> ±1 high, a ragged skyline; some posts where three or four
/// walls meet — and the four corners of the outer wall — rise as pillars a few blocks higher with an accent cap; an accent
/// band runs along the walls a few rows under their tops. The lowest <see cref="KeepRows"/> wall rows above the floor are
/// kept (<see cref="BuiltStructure.Keep"/>): whatever the ruin pass brings down, the maze's plan stays readable.
/// <para>Markers, all on the floor (row 1): a <c>prop</c> <c>pool=statue_large</c> in the plaza's middle with a rich chest
/// before it, a <c>prop</c> <c>pool=statue</c> in every hall and in a share of the roomy dead ends, and a common chest in a
/// fifth of the dead ends (every second of them only by chance — <c>chance=0.5</c>). Deterministic from the seed; the walls'
/// heights and the markers have lanes of their own, so neither the height nor the statue share moves anything else.</para>
/// </summary>
internal static class MazeRuinGenerator
{
    /// <summary>The prop pool of the statues in the halls and the dead ends.</summary>
    public const string StatuePool = "statue";

    /// <summary>The prop pool of the big statue in the plaza.</summary>
    public const string LargeStatuePool = "statue_large";

    /// <summary>The loot table of the plaza's chest.</summary>
    public const string RichTable = "civ_labyrinth_builders_rich";

    /// <summary>The loot table of the dead ends' chests.</summary>
    public const string CommonTable = "civ_labyrinth_builders_common";

    /// <summary>The chance a hidden dead-end chest is there at all (the stamper's <c>chance</c> setting).</summary>
    public const string HiddenChestChance = "0.5";

    /// <summary>The wall rows above the floor the ruin pass leaves standing.</summary>
    public const int KeepRows = 3;

    /// <summary>A dead end carries a statue only when both its widths are at least this.</summary>
    public const int StatueRoom = 4;

    /// <summary>How much higher than <see cref="MazeRuinLook.Height"/> a pillar rises: this plus 0–2.</summary>
    public const int PillarRise = 3;

    private const int PillarRiseSpread = 3;
    private const double PillarChance = 0.3;
    private const double ChestShare = 0.2;
    private const double HiddenChestShare = 0.5;

    /// <summary>Hash kinds of the wall sections (each one a stream of its own).</summary>
    private const int KindAlongZ = 1;
    private const int KindAlongX = 2;
    private const int KindPillar = 3;
    private const int KindPillarRise = 4;

    /// <summary>The maze of <paramref name="plan"/> as a structure of the source <paramref name="key"/>.</summary>
    public static BuiltStructure Build(string key, MazeRuinPlan plan, long seed, MazeRuinLook look)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var walls = new Walls(plan, seed, look.Height);
        int top = 0;
        for (int x = 0; x < plan.Width; x++)
        {
            for (int z = 0; z < plan.Length; z++)
            {
                top = Math.Max(top, walls.Top(x, z, out _));
            }
        }

        var b = new BuiltStructure(key, plan.Width, top + 1, plan.Length);
        int band = Math.Max(KeepRows + 2, look.Height - 3);
        for (int x = 0; x < plan.Width; x++)
        {
            for (int z = 0; z < plan.Length; z++)
            {
                WriteColumn(b, x, z, walls.Top(x, z, out bool cap), cap, band, look);
            }
        }

        AddMarkers(b, plan, seed, look.Statues);
        return b;
    }

    /// <summary>One column: the paving where <paramref name="top"/> is -1, else the wall from the floor row up to
    /// <paramref name="top"/> — the band row and a pillar's cap in the accent, the lowest wall rows kept.</summary>
    private static void WriteColumn(BuiltStructure b, int x, int z, int top, bool cap, int band, MazeRuinLook look)
    {
        if (top < 0)
        {
            b.Blocks[b.Index(x, 0, z)] = look.Floor;
            return;
        }

        for (int y = 0; y <= top; y++)
        {
            int i = b.Index(x, y, z);
            b.Blocks[i] = (cap && y == top) || (y == band && y < top) ? look.Accent : look.Wall;
            if (y >= 1 && y <= KeepRows)
            {
                b.Keep.Add(i);
            }
        }
    }

    private static void AddMarkers(BuiltStructure b, MazeRuinPlan plan, long seed, int statues)
    {
        var (x0, z0, x1, z1) = plan.PlazaBox();
        int cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
        b.Markers.Add(Prop(cx, cz, LargeStatuePool));
        b.Markers.Add(Chest(cx, z0 + 1, RichTable, hidden: false));
        for (int k = 0; k < plan.Halls.Count && statues > 0; k++)
        {
            var (i, j) = plan.Halls[k];
            var (hx0, hz0, hx1, hz1) = plan.Box(i, j, i + 1, j + 1);
            b.Markers.Add(Prop((hx0 + hx1) / 2, (hz0 + hz1) / 2, StatuePool));
        }

        var rng = MazeRuinPlan.Rng(seed, "markers");
        for (int j = 0; j < plan.N; j++)
        {
            for (int i = 0; i < plan.N; i++)
            {
                if (plan.IsDeadEnd(i, j))
                {
                    AddDeadEndMarkers(b, plan, rng, i, j, statues);
                }
            }
        }
    }

    /// <summary>A dead end's statue (in its middle, when it has room) and chest (at its far end — in a far corner beside a
    /// statue). Three rolls per dead end, drawn whatever they decide.</summary>
    private static void AddDeadEndMarkers(BuiltStructure b, MazeRuinPlan plan, DeterministicRandom rng, int i, int j, int statues)
    {
        double statueRoll = rng.NextDouble(), chestRoll = rng.NextDouble(), hiddenRoll = rng.NextDouble();
        var (x0, z0, x1, z1) = plan.Box(i, j, i, j);
        bool statue = Math.Min(plan.WidthX[i], plan.WidthZ[j]) >= StatueRoom && statueRoll * 100.0 < statues;
        if (statue)
        {
            b.Markers.Add(Prop((x0 + x1) / 2, (z0 + z1) / 2, StatuePool));
        }

        if (chestRoll >= ChestShare)
        {
            return;
        }

        // The far end lies opposite the one opening; beside a statue the chest moves into that end's corner.
        int mx = statue ? x1 : (x0 + x1) / 2, mz = statue ? z1 : (z0 + z1) / 2;
        var (cx, cz) = plan.IsOpen(i, j, MazeRuinPlan.North) ? (mx, z1)
            : plan.IsOpen(i, j, MazeRuinPlan.East) ? (x0, mz)
            : plan.IsOpen(i, j, MazeRuinPlan.South) ? (statue ? x0 : mx, z0)
            : (x1, statue ? z0 : mz);
        b.Markers.Add(Chest(cx, cz, CommonTable, hiddenRoll < HiddenChestShare));
    }

    private static BuiltMarker Prop(int x, int z, string pool) => new(CivilizationRules.PropMarker, x, 1, z, "pool=" + pool);

    private static BuiltMarker Chest(int x, int z, string table, bool hidden)
        => new(LandmarkRules.ChestMarker, x, 1, z, "table=" + table + (hidden ? ";chance=" + HiddenChestChance : string.Empty));

    /// <summary>The height of every column of the plan: -1 on the open floor, else the top row of its wall section.</summary>
    private sealed class Walls
    {
        private readonly MazeRuinPlan _plan;
        private readonly ulong _lane;
        private readonly int _height;

        public Walls(MazeRuinPlan plan, long seed, int height)
        {
            _plan = plan;
            _lane = StableHashing.Mixed("maze_ruin:walls", seed);
            _height = height;
        }

        /// <summary>The top row of the wall at (x, z), -1 where the floor is open; <paramref name="cap"/> for a pillar.</summary>
        public int Top(int x, int z, out bool cap)
        {
            cap = false;
            int i = _plan.CellX(x), j = _plan.CellZ(z), a = _plan.LineX(x), c = _plan.LineZ(z);
            if (i >= 0 && j >= 0)
            {
                return -1;
            }

            if (a >= 0 && j >= 0)
            {
                return _plan.SegmentAlongZOpen(a, j) ? -1 : Section(KindAlongZ, a, j);
            }

            if (i >= 0)
            {
                return _plan.SegmentAlongXOpen(c, i) ? -1 : Section(KindAlongX, c, i);
            }

            return _plan.PostOpen(a, c) ? -1 : Post(a, c, out cap);
        }

        /// <summary>A wall section's top: the height, one up or down by its hash.</summary>
        private int Section(int kind, int line, int index) => _height + StableHashing.Index(Hash(kind, line, index), 3) - 1;

        /// <summary>The post where wall lines <paramref name="a"/> (along Z) and <paramref name="c"/> (along X) cross: as
        /// high as the highest wall that meets it — or a pillar (the outer wall's corners, some posts where three or four
        /// walls meet).</summary>
        private int Post(int a, int c, out bool cap)
        {
            int top = -1, meeting = 0;
            for (int k = c - 1; k <= c; k++)
            {
                if (k >= 0 && k < _plan.N && !_plan.SegmentAlongZOpen(a, k))
                {
                    meeting++;
                    top = Math.Max(top, Section(KindAlongZ, a, k));
                }
            }

            for (int k = a - 1; k <= a; k++)
            {
                if (k >= 0 && k < _plan.N && !_plan.SegmentAlongXOpen(c, k))
                {
                    meeting++;
                    top = Math.Max(top, Section(KindAlongX, c, k));
                }
            }

            bool corner = (a == 0 || a == _plan.N) && (c == 0 || c == _plan.N);
            cap = corner || (meeting >= 3 && StableHashing.Unit(Hash(KindPillar, a, c)) < PillarChance);
            if (cap)
            {
                return _height + PillarRise + StableHashing.Index(Hash(KindPillarRise, a, c), PillarRiseSpread);
            }

            return top >= 0 ? top : _height;
        }

        private ulong Hash(int kind, int a, int b)
            => StableHashing.Mix64(_lane ^ ((ulong)(uint)kind << 48) ^ ((ulong)(uint)a << 24) ^ (uint)b);
    }
}
