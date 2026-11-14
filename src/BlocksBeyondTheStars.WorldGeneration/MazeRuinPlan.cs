// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>An opening in a maze ruin's outer wall (#3645): the side it lies on (<see cref="MazeRuinPlan.North"/> …
/// <see cref="MazeRuinPlan.West"/>) and the cell index along that side.</summary>
internal readonly struct MazeEntrance
{
    public MazeEntrance(int side, int index)
    {
        Side = side;
        Index = index;
    }

    public int Side { get; }

    public int Index { get; }
}

/// <summary>
/// #3645: the ground plan of a Labyrinth Builders' maze ruin (<see cref="MazeRuinGenerator"/>) — pure 2D and seed-pure.
/// An n × n grid of cells (n odd, by size) whose every column and every row has a passage width of its own drawn from a
/// palette — narrow 2–3, medium 4–6, wide 8–10 — so wide avenues and narrow alleys alternate. Inner walls are two blocks
/// thick, the outer wall three. Over the cells runs a perfect maze (growing tree, mixing the newest and a random cell); the
/// centre's 3 × 3 cells merge into a plaza, a few 2 × 2 blocks into halls; 2–4 entrances open the outer wall on different
/// sides; then a share of the dead ends is opened into a neighbour (braid), which makes loops.
/// <para>X runs along the columns (index i), Z along the rows (index j); a cell's index is <c>j · n + i</c>. A wall line
/// has the index of the cell after it: line 0 is the west (north) outer wall, line n the east (south) one. The size draws
/// as many cells as the footprint (≈ 80 / 120 / 180 blocks across) needs with this palette, then nudges the widths within
/// their bands toward the footprint.</para>
/// </summary>
internal sealed class MazeRuinPlan
{
    public const int OuterWall = 3;
    public const int InnerWall = 2;

    /// <summary>Directions and outer-wall sides: north = toward low z, east = high x, south = high z, west = low x.</summary>
    public const int North = 0;
    public const int East = 1;
    public const int South = 2;
    public const int West = 3;

    /// <summary>The group of the plaza's cells; a hall's cells carry <see cref="FirstHallGroup"/> + its index.</summary>
    public const int PlazaGroup = 1;
    public const int FirstHallGroup = 2;

    public const string Small = "S";
    public const string Medium = "M";
    public const string Large = "L";

    /// <summary>The sizes, smallest first.</summary>
    public static readonly string[] Sizes = { Small, Medium, Large };

    /// <summary>The palette: the share of narrow and of medium passages (the rest is wide) and each band's widths.</summary>
    private const double NarrowShare = 0.45;
    private const double MediumShare = 0.35;
    private static readonly (int Min, int Max)[] Bands = { (2, 3), (4, 6), (8, 10) };

    /// <summary>How often the growing tree continues from its newest cell (else from a random one).</summary>
    private const double NewestShare = 0.5;

    private const int HallTriesPerHall = 12;

    private readonly bool[] _openX;
    private readonly bool[] _openZ;
    private readonly bool[] _openPost;
    private readonly int[] _group;
    private readonly int[] _xCell;
    private readonly int[] _xLine;
    private readonly int[] _zCell;
    private readonly int[] _zLine;
    private readonly List<MazeEntrance> _entrances = new();
    private readonly List<(int I, int J)> _halls = new();

    private MazeRuinPlan(int[] widthX, int[] widthZ)
    {
        N = widthX.Length;
        WidthX = widthX;
        WidthZ = widthZ;
        StartX = Lay(widthX, out int width, out _xCell, out _xLine);
        StartZ = Lay(widthZ, out int length, out _zCell, out _zLine);
        Width = width;
        Length = length;
        _openX = new bool[N * N];
        _openZ = new bool[N * N];
        _openPost = new bool[N * N];
        _group = new int[N * N];
    }

    /// <summary>Cells per side (odd).</summary>
    public int N { get; }

    /// <summary>The footprint along X.</summary>
    public int Width { get; }

    /// <summary>The footprint along Z.</summary>
    public int Length { get; }

    /// <summary>The passage width of each column (along X).</summary>
    public IReadOnlyList<int> WidthX { get; }

    /// <summary>The passage width of each row (along Z).</summary>
    public IReadOnlyList<int> WidthZ { get; }

    /// <summary>The first x of each column.</summary>
    public IReadOnlyList<int> StartX { get; }

    /// <summary>The first z of each row.</summary>
    public IReadOnlyList<int> StartZ { get; }

    public IReadOnlyList<MazeEntrance> Entrances => _entrances;

    /// <summary>The halls by their north-west cell; hall k has the group <see cref="FirstHallGroup"/> + k.</summary>
    public IReadOnlyList<(int I, int J)> Halls => _halls;

    /// <summary>The cells per side of a size (unknown = medium).</summary>
    public static int CellsFor(string size) => size switch
    {
        Small => 11,
        Large => 25,
        _ => 17,
    };

    /// <summary>The footprint a size aims at, blocks per side (unknown = medium).</summary>
    public static int FootprintFor(string size) => size switch
    {
        Small => 80,
        Large => 180,
        _ => 120,
    };

    /// <summary>The plan of a maze: the seed and the size decide the grid and the maze; <paramref name="braidPercent"/> of
    /// the dead ends open into loops, up to <paramref name="halls"/> halls are merged (as many as fit). Every decision has a
    /// lane of its own, so the braid share or the hall count never reshapes the grid or the perfect maze under it.</summary>
    public static MazeRuinPlan Create(long seed, string size, int braidPercent, int halls)
    {
        int n = CellsFor(size);
        int passages = FootprintFor(size) - (2 * OuterWall) - ((n - 1) * InnerWall);
        var widths = Rng(seed, "widths");
        var plan = new MazeRuinPlan(DrawWidths(widths, n, passages), DrawWidths(widths, n, passages));
        plan.CarvePerfectMaze(Rng(seed, "maze"));
        plan.Merge((n / 2) - 1, (n / 2) - 1, 3, PlazaGroup);
        plan.PlaceHalls(Rng(seed, "halls"), halls);
        plan.OpenEntrances(Rng(seed, "entrances"));
        plan.Braid(Rng(seed, "braid"), braidPercent);
        return plan;
    }

    /// <summary>The rng of one decision lane of a maze.</summary>
    internal static DeterministicRandom Rng(long seed, string lane)
        => new(unchecked((long)StableHashing.Mixed("maze_ruin:" + lane, seed)));

    /// <summary>The column whose passage x lies in, or -1 in a wall.</summary>
    public int CellX(int x) => _xCell[x];

    /// <summary>The wall line x lies in, or -1 in a passage.</summary>
    public int LineX(int x) => _xLine[x];

    public int CellZ(int z) => _zCell[z];

    public int LineZ(int z) => _zLine[z];

    /// <summary>The cell's group: 0 for a plain maze cell, <see cref="PlazaGroup"/>, or a hall's.</summary>
    public int Group(int i, int j) => _group[(j * N) + i];

    /// <summary>Whether cell (i, j) opens toward <paramref name="dir"/>: into its neighbour, or out through an entrance.</summary>
    public bool IsOpen(int i, int j, int dir) => dir switch
    {
        North => j == 0 ? HasEntrance(North, i) : _openZ[((j - 1) * N) + i],
        East => i == N - 1 ? HasEntrance(East, j) : _openX[(j * N) + i],
        South => j == N - 1 ? HasEntrance(South, i) : _openZ[(j * N) + i],
        _ => i == 0 ? HasEntrance(West, j) : _openX[(j * N) + i - 1],
    };

    /// <summary>How many sides of the cell are open.</summary>
    public int Degree(int i, int j)
    {
        int d = 0;
        for (int dir = North; dir <= West; dir++)
        {
            d += IsOpen(i, j, dir) ? 1 : 0;
        }

        return d;
    }

    /// <summary>A plain cell with a single way in or out (plaza and hall cells never are).</summary>
    public bool IsDeadEnd(int i, int j) => _group[(j * N) + i] == 0 && Degree(i, j) == 1;

    /// <summary>Whether the wall segment of line <paramref name="line"/> (running along Z) beside row <paramref name="j"/> is open.</summary>
    public bool SegmentAlongZOpen(int line, int j) => line == N ? IsOpen(N - 1, j, East) : IsOpen(line, j, West);

    /// <summary>Whether the wall segment of line <paramref name="line"/> (running along X) beside column <paramref name="i"/> is open.</summary>
    public bool SegmentAlongXOpen(int line, int i) => line == N ? IsOpen(i, N - 1, South) : IsOpen(i, line, North);

    /// <summary>Whether the post where wall lines <paramref name="lineX"/> and <paramref name="lineZ"/> cross is carved away
    /// (inside the plaza or a hall); an outer-wall post never is.</summary>
    public bool PostOpen(int lineX, int lineZ)
        => lineX > 0 && lineX < N && lineZ > 0 && lineZ < N && _openPost[((lineZ - 1) * N) + lineX - 1];

    /// <summary>The passages between cells (entrances not counted) — a perfect maze has n² − 1.</summary>
    public int OpenPassages()
    {
        int n = 0;
        for (int c = 0; c < N * N; c++)
        {
            n += (_openX[c] ? 1 : 0) + (_openZ[c] ? 1 : 0);
        }

        return n;
    }

    /// <summary>The block box (inclusive) of the cells from (i0, j0) to (i1, j1).</summary>
    public (int X0, int Z0, int X1, int Z1) Box(int i0, int j0, int i1, int j1)
        => (StartX[i0], StartZ[j0], StartX[i1] + WidthX[i1] - 1, StartZ[j1] + WidthZ[j1] - 1);

    /// <summary>The plaza's block box.</summary>
    public (int X0, int Z0, int X1, int Z1) PlazaBox() => Box((N / 2) - 1, (N / 2) - 1, (N / 2) + 1, (N / 2) + 1);

    private bool HasEntrance(int side, int index)
    {
        foreach (var e in _entrances)
        {
            if (e.Side == side && e.Index == index)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A side's passage widths: one band per column from the palette (the centre column wide, its neighbours never
    /// narrow — the plaza carries the big statue), then nudged within the bands toward <paramref name="passageSum"/>.</summary>
    private static int[] DrawWidths(DeterministicRandom rng, int n, int passageSum)
    {
        var widths = new int[n];
        var bands = new int[n];
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            bands[i] = BandOf(rng.NextDouble(), Math.Abs(i - (n / 2)));
            widths[i] = rng.Range(Bands[bands[i]].Min, Bands[bands[i]].Max);
            sum += widths[i];
        }

        int diff = passageSum - sum;
        for (int tries = 0; diff != 0 && tries < n * 8; tries++)
        {
            int i = rng.Range(0, n - 1);
            if (diff > 0 && widths[i] < Bands[bands[i]].Max)
            {
                widths[i]++;
                diff--;
            }
            else if (diff < 0 && widths[i] > Bands[bands[i]].Min)
            {
                widths[i]--;
                diff++;
            }
        }

        return widths;
    }

    /// <summary>The band (0 narrow, 1 medium, 2 wide) of a column <paramref name="fromCentre"/> columns from the centre for
    /// the roll <paramref name="u"/>: the centre column is always wide, its two neighbours never narrow.</summary>
    private static int BandOf(double u, int fromCentre)
    {
        if (fromCentre == 0)
        {
            return 2;
        }

        if (u < NarrowShare)
        {
            return fromCentre > 1 ? 0 : 1;
        }

        return u < NarrowShare + MediumShare ? 1 : 2;
    }

    /// <summary>Lays one side out: the outer wall, then each passage followed by its wall line. Returns each passage's start.</summary>
    private static int[] Lay(int[] widths, out int total, out int[] cellOf, out int[] lineOf)
    {
        int n = widths.Length;
        total = (2 * OuterWall) + ((n - 1) * InnerWall);
        foreach (int w in widths)
        {
            total += w;
        }

        var starts = new int[n];
        cellOf = new int[total];
        lineOf = new int[total];
        int p = 0;
        for (int i = 0; i <= n; i++)
        {
            int wall = i == 0 || i == n ? OuterWall : InnerWall;
            for (int k = 0; k < wall; k++, p++)
            {
                cellOf[p] = -1;
                lineOf[p] = i;
            }

            if (i == n)
            {
                break;
            }

            starts[i] = p;
            for (int k = 0; k < widths[i]; k++, p++)
            {
                cellOf[p] = i;
                lineOf[p] = -1;
            }
        }

        return starts;
    }

    /// <summary>The perfect maze: a growing tree from a random cell, continuing from its newest cell or a random one.</summary>
    private void CarvePerfectMaze(DeterministicRandom rng)
    {
        var visited = new bool[N * N];
        var active = new List<int>(N * N);
        var options = new int[4];
        var around = new int[4];
        int start = rng.Range(0, (N * N) - 1);
        visited[start] = true;
        active.Add(start);
        while (active.Count > 0)
        {
            int k = rng.NextDouble() < NewestShare ? active.Count - 1 : rng.Range(0, active.Count - 1);
            int cell = active[k];
            int count = 0;
            foreach (int next in Neighbours(cell, around))
            {
                if (next >= 0 && !visited[next])
                {
                    options[count++] = next;
                }
            }

            if (count == 0)
            {
                active.RemoveAt(k);
                continue;
            }

            int pick = options[rng.Range(0, count - 1)];
            Connect(cell, pick);
            visited[pick] = true;
            active.Add(pick);
        }
    }

    /// <summary>Fills <paramref name="into"/> with the four neighbours of a cell (north, east, south, west), -1 outside the
    /// grid.</summary>
    private int[] Neighbours(int cell, int[] into)
    {
        int i = cell % N, j = cell / N;
        into[North] = j > 0 ? cell - N : -1;
        into[East] = i < N - 1 ? cell + 1 : -1;
        into[South] = j < N - 1 ? cell + N : -1;
        into[West] = i > 0 ? cell - 1 : -1;
        return into;
    }

    /// <summary>Opens the wall between two neighbouring cells.</summary>
    private void Connect(int a, int b)
    {
        int lo = Math.Min(a, b);
        if (Math.Abs(a - b) == 1)
        {
            _openX[lo] = true;
        }
        else
        {
            _openZ[lo] = true;
        }
    }

    private bool Connected(int a, int b) => Math.Abs(a - b) == 1 ? _openX[Math.Min(a, b)] : _openZ[Math.Min(a, b)];

    /// <summary>Merges a <paramref name="size"/> × <paramref name="size"/> block of cells into one open room: every wall and
    /// post inside it opens.</summary>
    private void Merge(int i0, int j0, int size, int group)
    {
        for (int j = j0; j < j0 + size; j++)
        {
            for (int i = i0; i < i0 + size; i++)
            {
                int c = (j * N) + i;
                _group[c] = group;
                _openX[c] |= i < i0 + size - 1;
                _openZ[c] |= j < j0 + size - 1;
                _openPost[c] |= i < i0 + size - 1 && j < j0 + size - 1;
            }
        }
    }

    /// <summary>Up to <paramref name="count"/> 2 × 2 halls, each with a ring of plain cells around it (never touching the
    /// plaza or another hall); a try that does not fit is dropped.</summary>
    private void PlaceHalls(DeterministicRandom rng, int count)
    {
        for (int tries = 0; _halls.Count < count && tries < count * HallTriesPerHall; tries++)
        {
            int i = rng.Range(0, N - 2), j = rng.Range(0, N - 2);
            if (IsPlain(i - 1, j - 1, i + 2, j + 2))
            {
                Merge(i, j, 2, FirstHallGroup + _halls.Count);
                _halls.Add((i, j));
            }
        }
    }

    /// <summary>Whether every cell of the box (clipped to the grid) is a plain maze cell.</summary>
    private bool IsPlain(int i0, int j0, int i1, int j1)
    {
        for (int j = Math.Max(0, j0); j <= Math.Min(N - 1, j1); j++)
        {
            for (int i = Math.Max(0, i0); i <= Math.Min(N - 1, i1); i++)
            {
                if (_group[(j * N) + i] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>2–4 entrances, each on a side of its own, never in a corner cell.</summary>
    private void OpenEntrances(DeterministicRandom rng)
    {
        var sides = new List<int> { North, East, South, West };
        Shuffle(sides, rng);
        int count = rng.Range(2, 4);
        for (int k = 0; k < count; k++)
        {
            _entrances.Add(new MazeEntrance(sides[k], rng.Range(1, N - 2)));
        }
    }

    /// <summary>Opens <paramref name="percent"/> % of the dead ends into a neighbour they are not joined to — another dead end
    /// when one is beside it — so the maze gets loops.</summary>
    private void Braid(DeterministicRandom rng, int percent)
    {
        var ends = new List<int>();
        for (int c = 0; c < N * N; c++)
        {
            if (IsDeadEnd(c % N, c / N))
            {
                ends.Add(c);
            }
        }

        Shuffle(ends, rng);
        var options = new List<int>(4);
        var around = new int[4];
        foreach (int c in ends)
        {
            bool open = rng.NextDouble() * 100.0 < percent; // drawn for every dead end: a stable stream
            if (!open || !IsDeadEnd(c % N, c / N))
            {
                continue;
            }

            ClosedNeighbours(c, options, around);
            if (options.Count > 0)
            {
                Connect(c, options[rng.Range(0, options.Count - 1)]);
            }
        }
    }

    /// <summary>The neighbours a cell is not joined to — only the dead ends among them when there are any.</summary>
    private void ClosedNeighbours(int cell, List<int> options, int[] around)
    {
        options.Clear();
        bool deadEndSeen = false;
        foreach (int next in Neighbours(cell, around))
        {
            if (next < 0 || Connected(cell, next))
            {
                continue;
            }

            bool deadEnd = IsDeadEnd(next % N, next / N);
            if (deadEnd && !deadEndSeen)
            {
                options.Clear();
                deadEndSeen = true;
            }

            if (deadEnd || !deadEndSeen)
            {
                options.Add(next);
            }
        }
    }

    private static void Shuffle(List<int> list, DeterministicRandom rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = rng.Range(0, i);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }
}
