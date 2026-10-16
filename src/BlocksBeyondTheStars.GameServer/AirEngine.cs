// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>What one cell is for the air fill (#2650). The per-place policy (a base on a planet, a player station in the
/// void) decides; the engine only walks.</summary>
internal enum AirCell : byte
{
    /// <summary>Air, or a block that does not seal (dirt, sand, a shaped cell, flora, a torch …) — air flows through.</summary>
    Open,

    /// <summary>Holds air: an airtight full cube, a force field, a fluid where the place says so.</summary>
    Wall,

    /// <summary>A door cell: a wall for air, open or closed, that also links the rooms on its two sides (a bulkhead).</summary>
    Door,

    /// <summary>Not loaded: bounds the fill like a wall and never makes a pocket leak — unknown is not a leak.</summary>
    Unknown,

    /// <summary>The outside: past the sky line of a base, or beyond the box of a station (the void).</summary>
    Sky,
}

/// <summary>
/// The world as the air fill sees it (#2650). Coordinates may be unwrapped (the fill walks x ± 1 across the wrap seam);
/// the space canonicalizes where the world needs it, and <see cref="ColumnKey"/> is what identifies a column, so one
/// column reached from both sides of the seam is the same column. Implemented by the server over a resident world and by
/// the tests over an array.
/// </summary>
internal interface IAirSpace
{
    /// <summary>What the cell is for air. Must return <see cref="AirCell.Sky"/> or a bounding kind at some height in every
    /// column, so a vertical scan always ends.</summary>
    AirCell Classify(int x, int y, int z);

    /// <summary>The identity of the column holding (x, z) — equal for two unwrapped positions of the same column.</summary>
    long ColumnKey(int x, int z);

    /// <summary>The canonical form of a cell (where a player would read its coordinates).</summary>
    Vector3i Canonical(int x, int y, int z);

    /// <summary>Whether an <see cref="AirCell.Unknown"/> cell met on the way UP a column is the open sky the server simply
    /// has not loaded (a base: nothing any player built lies there or above). Asked only for the first unknown cell above an
    /// open run.</summary>
    bool UnknownAboveIsSky(int x, int y, int z);

    /// <summary>Whether an <see cref="AirCell.Open"/> cell holds a block (sand, a slab …) rather than plain air — what a
    /// leak is named after.</summary>
    bool HoldsBlock(int x, int y, int z);

    /// <summary>The door a <see cref="AirCell.Door"/> cell belongs to, or −1 when the place does not link rooms through it.</summary>
    int DoorAt(int x, int y, int z);

    /// <summary>Adds the cells on both sides of a door (every neighbour of its cells) — the seeds that chain the rooms a
    /// sealed room's doors lead to.</summary>
    void AddDoorSeeds(int door, List<Vector3i> into);

    /// <summary>The shortest signed distance for a longitude difference (identity in a space without a seam).</summary>
    int WrapDx(int dx);

    /// <summary>The shortest signed distance for a latitude difference (identity in a space without a seam).</summary>
    int WrapDz(int dz);
}

/// <summary>
/// The emergency brake of a fill (#2650) — not a room size limit, far beyond anything a player builds. Per <b>room</b>: a
/// pocket that grows past <see cref="MaxCells"/> or <see cref="MaxSpans"/> stops as <see cref="AirPocket.Braked"/> (its
/// state is unknown — the owner keeps what it knew) while the fill goes on with the other rooms. Per <b>run</b>: a run whose
/// rooms together pass <see cref="MaxRunCells"/> or <see cref="MaxRunSpans"/> stops as a whole (<see cref="AirFill.Braked"/>)
/// — the bound on memory, about 48 bytes per span (the span record plus its column's dictionary entry).
/// </summary>
internal readonly struct AirFillLimits
{
    /// <summary>The same limit for one room and for the whole run.</summary>
    public AirFillLimits(long maxCells, int maxSpans)
        : this(maxCells, maxSpans, maxCells, maxSpans)
    {
    }

    public AirFillLimits(long maxCells, int maxSpans, long maxRunCells, int maxRunSpans)
    {
        MaxCells = maxCells;
        MaxSpans = maxSpans;
        MaxRunCells = maxRunCells;
        MaxRunSpans = maxRunSpans;
    }

    /// <summary>Most cells one room may hold before it is braked.</summary>
    public long MaxCells { get; }

    /// <summary>Most spans one room may hold before it is braked.</summary>
    public int MaxSpans { get; }

    /// <summary>Most cells one run may visit over all its rooms.</summary>
    public long MaxRunCells { get; }

    /// <summary>Most spans one run may hold over all its rooms.</summary>
    public int MaxRunSpans { get; }
}

/// <summary>One pocket of a fill (#2650): a maximal region of open cells bounded by walls, doors and unknown cells. Sealed
/// when the fill finished it without reaching the outside.</summary>
internal sealed class AirPocket
{
    private static int s_nextId;

    public AirPocket(int index, Vector3i seed)
        : this(index, seed, System.Threading.Interlocked.Increment(ref s_nextId))
    {
    }

    private AirPocket(int index, Vector3i seed, int id)
    {
        Index = index;
        Seed = seed;
        Id = id;
    }

    /// <summary>A pocket of an earlier fill carried into a new one (see <see cref="AirFill.AdoptSealedFrom"/>), keeping its
    /// id and its state (sealed, or braked) — the room is the same room.</summary>
    internal static AirPocket CarriedFrom(AirPocket old, int index, int chainedFrom)
        => new(index, old.Seed, old.Id)
        {
            Complete = old.Complete,
            Leaky = false,
            Braked = old.Braked,
            Carried = true,
            ChainedFrom = chainedFrom,
        };

    /// <summary>Carried over from an earlier fill because unloaded cells (or a braked room) kept the new fill from looking
    /// at it (#2650).</summary>
    public bool Carried { get; private init; }

    /// <summary>The emergency brake stopped this room (#2650): incomplete, neither sealed nor leaky — its state is unknown.</summary>
    public bool Braked { get; internal set; }

    /// <summary>The pocket whose door chained this one (its index in the same fill), or −1 for a seed from outside.</summary>
    public int ChainedFrom { get; internal set; } = -1;

    /// <summary>Unique across every fill of the process — the room id the bed assignment compares (#2649).</summary>
    public int Id { get; }

    /// <summary>Position in its fill's pocket list.</summary>
    public int Index { get; }

    /// <summary>The cell the pocket was filled from (canonical).</summary>
    public Vector3i Seed { get; }

    /// <summary>The fill finished this pocket (sealed, or stopped at a leak); false while in progress or after a brake.</summary>
    public bool Complete { get; internal set; }

    /// <summary>The pocket reaches the outside.</summary>
    public bool Leaky { get; internal set; }

    /// <summary>Finished without reaching the outside: the room holds air.</summary>
    public bool Sealed => Complete && !Leaky;

    /// <summary>Open cells in the pocket's spans (for a leaky pocket: those visited before the leak was found).</summary>
    public long Cells { get; internal set; }

    /// <summary>Spans the pocket holds.</summary>
    public int Spans { get; internal set; }

    /// <summary>The doors bounding the pocket (as <see cref="IAirSpace.DoorAt"/> numbers them), or null when none.</summary>
    public List<int>? Doors { get; internal set; }

    /// <summary>The pocket borders unloaded cells somewhere (they bounded it like walls).</summary>
    public bool TouchesUnknown { get; internal set; }

    /// <summary>For a leaky pocket: where the room meets the outside, at the opening's height (canonical) — the hole in
    /// the wall, the hole in the roof, the sand block of a sand roof. Null when sealed.</summary>
    public Vector3i? Opening { get; internal set; }
}

/// <summary>
/// The shared air engine of bases and player stations (#2650). Air is filled in vertical <b>spans</b> — maximal runs of
/// open cells in one column — instead of single cells: the fill walks a span's four neighbour columns over its height
/// and keeps, per column, the spans it found. A 100 × 100 × 100 hall is 10 000 spans, not a million set entries, and
/// "is there air here?" is a column lookup plus a short list search.
/// <para>
/// A fill runs from seeds; every seed in an open cell no earlier pocket holds starts a pocket. A pocket that reaches
/// <see cref="AirCell.Sky"/> is leaky and stops at once (its opening is recorded); one that runs out of spans to expand is
/// sealed. With door chaining, a sealed pocket's doors seed the rooms behind them — a leaky pocket never continues the
/// chain. <see cref="Step"/> works for a time budget and returns, so a large fill runs over several ticks while its
/// owner keeps answering from the previous result; a run that grows past its <see cref="AirFillLimits"/> is braked.
/// </para>
/// </summary>
internal sealed class AirFill
{
    private struct Span
    {
        public int X;      // as walked (unwrapped)
        public int Z;
        public int Lo;
        public int Hi;
        public int Pocket;
        public int Next;   // next span of the same column, −1 at the end
    }

    private static readonly int[] NeighbourDx = { 1, -1, 0, 0 };
    private static readonly int[] NeighbourDz = { 0, 0, 1, -1 };

    private readonly IAirSpace _space;
    private readonly AirFillLimits _limits;
    private readonly bool _chainDoors;
    private readonly int _originX;
    private readonly int _originZ;
    private Span[] _spans = new Span[64];
    private int _spanCount;
    private readonly Dictionary<long, int> _heads = new();
    private readonly List<AirPocket> _pockets = new();
    private readonly Queue<(Vector3i Cell, int Parent)> _seeds = new();
    private readonly Queue<int> _frontier = new();
    private readonly HashSet<int> _doorsChained = new();
    private readonly List<Vector3i> _scratch = new();
    private AirPocket? _current;
    private int _runSpans;
    private long _runCells;
    private int _minDx = int.MaxValue, _maxDx = int.MinValue, _minY = int.MaxValue, _maxY = int.MinValue;
    private int _minDz = int.MaxValue, _maxDz = int.MinValue;

    /// <param name="space">The world as air sees it.</param>
    /// <param name="limits">The emergency brake of one run.</param>
    /// <param name="chainDoors">Whether a sealed pocket's doors seed the rooms behind them (bases).</param>
    /// <param name="origin">A canonical cell the extent is measured from (a base core); any cell for a space without a seam.</param>
    public AirFill(IAirSpace space, AirFillLimits limits, bool chainDoors, Vector3i origin)
    {
        _space = space;
        _limits = limits;
        _chainDoors = chainDoors;
        _originX = origin.X;
        _originZ = origin.Z;
    }

    /// <summary>The fill has nothing left to do (finished or braked) until a new seed arrives.</summary>
    public bool Done { get; private set; } = true;

    /// <summary>The last run hit the emergency brake as a whole (<see cref="AirFillLimits.MaxRunSpans"/>): its pockets are
    /// incomplete and must not replace a previous result.</summary>
    public bool Braked { get; private set; }

    /// <summary>Rooms the per-room brake stopped (<see cref="AirPocket.Braked"/>) over every run.</summary>
    public int BrakedPockets { get; private set; }

    /// <summary>Spans held over every run.</summary>
    public int SpanCount => _spanCount;

    /// <summary>Open cells held over every run.</summary>
    public long CellCount { get; private set; }

    /// <summary>Column scans' cell reads over every run (the measurement of #2650).</summary>
    public long CellsRead { get; private set; }

    /// <summary>The pockets found, in the order their seeds started them.</summary>
    public IReadOnlyList<AirPocket> Pockets => _pockets;

    /// <summary>Seeds that fell on unloaded cells — rooms behind them could not be looked at.</summary>
    public int UnknownSeeds { get; private set; }

    /// <summary>Whether unloaded cells may have hidden rooms from this fill: a seed fell on one, a sealed pocket borders one
    /// (a door chain could have gone on there), or a room was braked. Leaky pockets do not count — they never chain.</summary>
    public bool SawUnknown
    {
        get
        {
            if (UnknownSeeds > 0 || BrakedPockets > 0)
            {
                return true;
            }

            foreach (var p in _pockets)
            {
                if (p.Sealed && p.TouchesUnknown)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// "Unknown is not a leak" across fills (#2650): carries the rooms of <paramref name="old"/> this fill could not look at
    /// into this fill, whole and unchanged, under their old room ids. Room by room, never span by span (#2649 review): an old
    /// room that ANY span of a finished room of this fill overlaps was looked at again and is never carried — a breached
    /// hall whose leaky fill stopped at its first sky contact must lose all its air, not keep the part the fill did not
    /// walk. An old sealed room this fill did not touch at all is carried only when something unknown stood in the way:
    /// its seed now lies in an unloaded chunk, it borders a room the brake stopped, or the room whose door chained it is
    /// carried too (a player far along a door chain whose core's chunks unloaded). Its seed loaded and nothing hidden — its
    /// door was removed, its chain cut — means it is gone. With <paramref name="carryBraked"/> old braked rooms nothing
    /// looked at again are carried as braked too, so a backstop refill does not fill a pathological room again.
    /// </summary>
    public void AdoptSealedFrom(AirFill old, bool carryBraked = false)
    {
        int count = old._pockets.Count;
        var lookedAt = new bool[count];   // a span overlaps a finished room of this fill
        var nearBraked = new bool[count]; // a span overlaps a room this fill braked
        for (int i = 0; i < old._spanCount; i++)
        {
            var s = old._spans[i];
            Overlap(_space.ColumnKey(s.X, s.Z), s.Lo, s.Hi, out bool finished, out bool braked);
            lookedAt[s.Pocket] |= finished;
            nearBraked[s.Pocket] |= braked;
        }

        var carried = new AirPocket?[count];
        for (int i = 0; i < count; i++)
        {
            var was = old._pockets[i];
            if (lookedAt[i] || !(was.Sealed || (carryBraked && was.Braked)))
            {
                continue;
            }

            // A braked room is carried only where no room of this fill covers it at all (a new braked room re-states it —
            // carrying both would add a copy every round).
            bool hidden = was.Braked
                ? !nearBraked[i]
                : nearBraked[i]
                  || Classify(was.Seed.X, was.Seed.Y, was.Seed.Z) == AirCell.Unknown
                  || (was.ChainedFrom >= 0 && was.ChainedFrom < i && carried[was.ChainedFrom] is not null);
            if (!hidden)
            {
                continue;
            }

            int parent = was.ChainedFrom >= 0 && was.ChainedFrom < i && carried[was.ChainedFrom] is { } p ? p.Index : -1;
            var pocket = AirPocket.CarriedFrom(was, _pockets.Count, parent);
            _pockets.Add(pocket);
            carried[i] = pocket;
        }

        for (int i = 0; i < old._spanCount; i++)
        {
            var s = old._spans[i];
            if (carried[s.Pocket] is { } pocket)
            {
                Append(_space.ColumnKey(s.X, s.Z), s.X, s.Z, s.Lo, s.Hi, pocket); // at the column head: it shadows a braked span
            }
        }
    }

    /// <summary>Whether this fill holds a span overlapping [lo, hi] in a column — of a finished room, of a braked room.</summary>
    private void Overlap(long key, int lo, int hi, out bool finished, out bool braked)
    {
        finished = braked = false;
        if (!_heads.TryGetValue(key, out int index))
        {
            return;
        }

        while (index >= 0)
        {
            ref var s = ref _spans[index];
            if (s.Lo <= hi && s.Hi >= lo)
            {
                var p = _pockets[s.Pocket];
                finished |= p.Complete;
                braked |= p.Braked;
            }

            index = s.Next;
        }
    }

    /// <summary>Queues a seed cell. A seed in a wall, a door or a cell some pocket already holds starts nothing.</summary>
    public void AddSeed(Vector3i cell)
    {
        if (Done)
        {
            Done = false;
            Braked = false;
            _runSpans = 0;
            _runCells = 0;
        }

        _seeds.Enqueue((cell, -1));
    }

    /// <summary>
    /// Works through seeds and pockets until done or until <paramref name="budgetTicks"/> (Stopwatch ticks; negative =
    /// no limit) have passed, but always for at least <paramref name="minSpans"/> span expansions so a starved fill still
    /// moves. Returns <see cref="Done"/>.
    /// </summary>
    public bool Step(long budgetTicks, int minSpans = 0)
    {
        if (Done)
        {
            return true;
        }

        long start = budgetTicks >= 0 ? Stopwatch.GetTimestamp() : 0;
        int work = 0;
        while (true)
        {
            if (_current != null)
            {
                if (_frontier.Count == 0)
                {
                    FinishPocket(leaky: false);
                }
                else
                {
                    Expand(_frontier.Dequeue());
                }
            }
            else if (_seeds.Count > 0)
            {
                var (cell, parent) = _seeds.Dequeue();
                Start(cell, parent);
            }
            else
            {
                Done = true;
                return true;
            }

            if (_current is { } room && (room.Cells > _limits.MaxCells || room.Spans > _limits.MaxSpans))
            {
                // The per-room emergency brake (#2650): this room's state is unknown — the owner keeps what it knew about
                // it — and the fill goes on with the other rooms.
                room.Braked = true;
                BrakedPockets++;
                _current = null;
                _frontier.Clear();
            }

            if (_runSpans > _limits.MaxRunSpans || _runCells > _limits.MaxRunCells)
            {
                // The run's emergency brake (#2650): stop here; the owner keeps its previous result.
                Braked = true;
                Done = true;
                _current = null;
                _frontier.Clear();
                _seeds.Clear();
                return true;
            }

            work++;
            if (budgetTicks >= 0 && work >= minSpans && (work & 15) == 0 && Stopwatch.GetTimestamp() - start >= budgetTicks)
            {
                return false;
            }
        }
    }

    /// <summary>The pocket holding the cell, or null when no span of this fill holds it.</summary>
    public AirPocket? PocketAt(int x, int y, int z)
    {
        int index = FindSpan(_space.ColumnKey(x, z), y);
        return index < 0 ? null : _pockets[_spans[index].Pocket];
    }

    /// <summary><see cref="PocketAt(int,int,int)"/> for a cell.</summary>
    public AirPocket? PocketAt(Vector3i c) => PocketAt(c.X, c.Y, c.Z);

    /// <summary>Whether the canonical cell lies within the box of every span this fill holds, grown by
    /// <paramref name="margin"/> — the area a block change can affect the result in.</summary>
    public bool InExtent(Vector3i canonical, int margin)
    {
        if (_spanCount == 0)
        {
            return false;
        }

        int dx = _space.WrapDx(canonical.X - _originX);
        int dz = _space.WrapDz(canonical.Z - _originZ);
        return dx >= _minDx - margin && dx <= _maxDx + margin
            && dz >= _minDz - margin && dz <= _maxDz + margin
            && canonical.Y >= _minY - margin && canonical.Y <= _maxY + margin;
    }

    /// <summary>The bottom cell of every span of a sealed pocket (as walked) — the seeds that refill the known sealed rooms:
    /// a room a new wall split is found again from both halves.</summary>
    public IEnumerable<Vector3i> SealedSpanBottoms()
    {
        for (int i = 0; i < _spanCount; i++)
        {
            if (_pockets[_spans[i].Pocket].Sealed)
            {
                yield return new Vector3i(_spans[i].X, _spans[i].Lo, _spans[i].Z);
            }
        }
    }

    private void Start(Vector3i seed, int parent)
    {
        var kind = Classify(seed.X, seed.Y, seed.Z);
        if (kind == AirCell.Unknown)
        {
            UnknownSeeds++;
        }

        if (kind != AirCell.Open || FindSpan(_space.ColumnKey(seed.X, seed.Z), seed.Y) >= 0)
        {
            return;
        }

        var pocket = new AirPocket(_pockets.Count, _space.Canonical(seed.X, seed.Y, seed.Z)) { ChainedFrom = parent };
        _pockets.Add(pocket);
        _current = pocket;
        var outcome = BuildSpan(seed.X, seed.Z, seed.Y, out int lo, out int hi, full: true);
        AddSpan(seed.X, seed.Z, lo, hi); // kept even when open to the outside: a later seed in this column is answered at once
        if (outcome != SpanEnd.Bounded)
        {
            pocket.Opening = SeedOpening(seed.X, seed.Z, seed.Y, lo, hi, outcome == SpanEnd.SkyUp);
            FinishPocket(leaky: true);
        }
    }

    private void Expand(int spanIndex)
    {
        var s = _spans[spanIndex];
        var pocket = _current!;
        for (int d = 0; d < 4; d++)
        {
            int nx = s.X + NeighbourDx[d], nz = s.Z + NeighbourDz[d];
            long key = _space.ColumnKey(nx, nz);
            int y = s.Lo;
            while (y <= s.Hi)
            {
                var kind = Classify(nx, y, nz);
                if (kind == AirCell.Open)
                {
                    int known = FindSpan(key, y);
                    if (known >= 0)
                    {
                        var other = _pockets[_spans[known].Pocket];
                        if (other != pocket && other.Leaky)
                        {
                            // Joined to a pocket already known to leak: this one leaks through the same opening.
                            pocket.Opening = other.Opening;
                            FinishPocket(leaky: true);
                            return;
                        }

                        if (other != pocket && other.Braked)
                        {
                            // Joined to a room the brake stopped: this is the same room, and its state is just as unknown.
                            pocket.Braked = true;
                            BrakedPockets++;
                            _current = null;
                            _frontier.Clear();
                            return;
                        }

                        y = _spans[known].Hi + 1;
                        continue;
                    }

                    var outcome = BuildSpan(nx, nz, y, out int lo, out int hi);
                    if (outcome != SpanEnd.Bounded)
                    {
                        pocket.Opening = Opening(s, nx, nz, y, lo, hi, outcome == SpanEnd.SkyUp);
                        FinishPocket(leaky: true);
                        return;
                    }

                    AddSpan(nx, nz, lo, hi);
                    y = hi + 1;
                    continue;
                }

                if (kind == AirCell.Sky)
                {
                    pocket.Opening = _space.Canonical(s.X, y, s.Z); // the room meets the outside right here
                    FinishPocket(leaky: true);
                    return;
                }

                Bound(kind, nx, y, nz);
                y++;
            }
        }
    }

    private enum SpanEnd : byte
    {
        Bounded,
        SkyUp,
        SkyDown,
    }

    /// <summary>The span through the open cell (x, y, z): up first — a column open to the sky decides the pocket, so the
    /// way down is skipped unless <paramref name="full"/> asks for the whole run — then down. Records the doors and
    /// unknown cells that bound it on the current pocket.</summary>
    private SpanEnd BuildSpan(int x, int z, int y, out int lo, out int hi, bool full = false)
    {
        var end = SpanEnd.Bounded;
        hi = y;
        while (true)
        {
            var kind = Classify(x, hi + 1, z);
            if (kind == AirCell.Open)
            {
                hi++;
                continue;
            }

            if (kind == AirCell.Sky || (kind == AirCell.Unknown && _space.UnknownAboveIsSky(x, hi + 1, z)))
            {
                end = SpanEnd.SkyUp;
                if (!full)
                {
                    lo = y;
                    return end;
                }

                break;
            }

            Bound(kind, x, hi + 1, z);
            break;
        }

        lo = y;
        while (true)
        {
            var kind = Classify(x, lo - 1, z);
            if (kind == AirCell.Open)
            {
                lo--;
                continue;
            }

            if (kind == AirCell.Sky)
            {
                return end == SpanEnd.SkyUp ? end : SpanEnd.SkyDown;
            }

            Bound(kind, x, lo - 1, z);
            return end;
        }
    }

    /// <summary>Notes a bounding cell on the current pocket: a door it links through, an unknown cell it borders.</summary>
    private void Bound(AirCell kind, int x, int y, int z)
    {
        var pocket = _current!;
        if (kind == AirCell.Door)
        {
            int door = _space.DoorAt(x, y, z);
            if (door >= 0)
            {
                pocket.Doors ??= new List<int>();
                if (!pocket.Doors.Contains(door))
                {
                    pocket.Doors.Add(door);
                }
            }
        }
        else if (kind == AirCell.Unknown)
        {
            pocket.TouchesUnknown = true;
        }
    }

    private void AddSpan(int x, int z, int lo, int hi)
    {
        int index = Append(_space.ColumnKey(x, z), x, z, lo, hi, _current!);
        _runCells += hi - lo + 1;
        _runSpans++;
        _frontier.Enqueue(index);
    }

    /// <summary>Stores a span for a pocket: the column's list, the counts, the extent.</summary>
    private int Append(long key, int x, int z, int lo, int hi, AirPocket pocket)
    {
        if (_spanCount == _spans.Length)
        {
            System.Array.Resize(ref _spans, _spans.Length * 2);
        }

        int index = _spanCount++;
        _spans[index] = new Span
        {
            X = x,
            Z = z,
            Lo = lo,
            Hi = hi,
            Pocket = pocket.Index,
            Next = _heads.TryGetValue(key, out int head) ? head : -1,
        };
        _heads[key] = index;

        int cells = hi - lo + 1;
        CellCount += cells;
        pocket.Cells += cells;
        pocket.Spans++;
        Grow(x, z, lo, hi);
        return index;
    }

    private void Grow(int x, int z, int lo, int hi)
    {
        int dx = _space.WrapDx(x - _originX), dz = _space.WrapDz(z - _originZ);
        _minDx = System.Math.Min(_minDx, dx);
        _maxDx = System.Math.Max(_maxDx, dx);
        _minDz = System.Math.Min(_minDz, dz);
        _maxDz = System.Math.Max(_maxDz, dz);
        _minY = System.Math.Min(_minY, lo);
        _maxY = System.Math.Max(_maxY, hi);
    }

    private int FindSpan(long key, int y)
    {
        if (!_heads.TryGetValue(key, out int index))
        {
            return -1;
        }

        while (index >= 0)
        {
            ref var s = ref _spans[index];
            if (y >= s.Lo && y <= s.Hi)
            {
                return index;
            }

            index = s.Next;
        }

        return -1;
    }

    private void FinishPocket(bool leaky)
    {
        var pocket = _current!;
        pocket.Complete = true;
        pocket.Leaky = leaky;
        _frontier.Clear();
        _current = null;
        if (leaky || !_chainDoors || pocket.Doors is null)
        {
            return;
        }

        // A sealed room's doors lead on (#2651): the rooms behind them are rooms of the same base, however far.
        foreach (int door in pocket.Doors)
        {
            if (!_doorsChained.Add(door))
            {
                continue;
            }

            _scratch.Clear();
            _space.AddDoorSeeds(door, _scratch);
            foreach (var seed in _scratch)
            {
                _seeds.Enqueue((seed, pocket.Index));
            }
        }
    }

    private AirCell Classify(int x, int y, int z)
    {
        CellsRead++;
        return _space.Classify(x, y, z);
    }

    // ---------------- where a room meets the outside (#2654) ----------------

    /// <summary>
    /// The opening of a pocket whose span <paramref name="parent"/> met a column open to the outside (#2654), entered at
    /// <paramref name="entryY"/>. Two shapes:
    /// <list type="bullet">
    /// <item>The outside column runs on past the parent's floor (or ceiling, for a way out downwards): it is a column
    /// outside the room, and the room meets it at the parent's cell — the hole in the wall, at the height nearest the
    /// seed (a doorway is named at the floor the player stands on).</item>
    /// <item>It starts within the parent's height: it is a column of the room itself that rises through the ceiling. Above
    /// the parent's top, the first cell holding a block that does not seal is the opening (a sand roof), else the first air
    /// cell with walls on two sides (a hole in a roof). When neither shows, the parent's cell at its top (or, for a
    /// doorway-sized parent, at the seed's height) stands in.</item>
    /// </list>
    /// </summary>
    private Vector3i Opening(Span parent, int nx, int nz, int entryY, int runLo, int runHi, bool up)
    {
        int seedY = _current!.Seed.Y;
        bool shortParent = parent.Hi - parent.Lo <= 3;
        if (up)
        {
            int eLo = entryY, eHi = parent.Hi;
            bool runsBelow = entryY == parent.Lo && Classify(nx, entryY - 1, nz) is AirCell.Open or AirCell.Sky;
            if (!runsBelow)
            {
                for (int y = eHi + 1; y <= runHi; y++)
                {
                    if (OpeningCell(nx, y, nz))
                    {
                        return _space.Canonical(nx, y, nz);
                    }
                }
            }

            int at = runsBelow || shortParent ? System.Math.Clamp(seedY, eLo, eHi) : eHi;
            return _space.Canonical(parent.X, at, parent.Z);
        }
        else
        {
            int eLo = parent.Lo, eHi = System.Math.Min(parent.Hi, runHi);
            bool runsAbove = runHi > parent.Hi;
            if (!runsAbove)
            {
                for (int y = eLo - 1; y >= runLo; y--)
                {
                    if (OpeningCell(nx, y, nz))
                    {
                        return _space.Canonical(nx, y, nz);
                    }
                }
            }

            int at = runsAbove || shortParent ? System.Math.Clamp(seedY, eLo, eHi) : eLo;
            return _space.Canonical(parent.X, at, parent.Z);
        }
    }

    /// <summary>The opening when the seed's own column reaches the outside: the first cell above (below) the seed that
    /// holds a non-sealing block or is a hole in a ceiling (floor); else the last open cell before the outside.</summary>
    private Vector3i SeedOpening(int x, int z, int seedY, int runLo, int runHi, bool up)
    {
        if (up)
        {
            for (int y = seedY + 1; y <= runHi; y++)
            {
                if (OpeningCell(x, y, z))
                {
                    return _space.Canonical(x, y, z);
                }
            }

            return _space.Canonical(x, runHi, z);
        }

        for (int y = seedY - 1; y >= runLo; y--)
        {
            if (OpeningCell(x, y, z))
            {
                return _space.Canonical(x, y, z);
            }
        }

        return _space.Canonical(x, runLo, z);
    }

    /// <summary>An open cell that reads as the opening itself: it holds a block that does not seal, or air with walls on
    /// at least two of its four sides (a hole in a roof plane or in a wall).</summary>
    private bool OpeningCell(int x, int y, int z)
    {
        if (_space.HoldsBlock(x, y, z))
        {
            return true;
        }

        int walls = 0;
        for (int d = 0; d < 4; d++)
        {
            if (Classify(x + NeighbourDx[d], y, z + NeighbourDz[d]) is AirCell.Wall or AirCell.Door && ++walls >= 2)
            {
                return true;
            }
        }

        return false;
    }
}
