// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The world as the shared air engine (<see cref="AirFill"/>, #2650) reads it: one reader for a base on a planet, one for
/// a player-built station or a derelict in the void, plus the per-tick time budget every air fill shares.
/// <list type="bullet">
/// <item><b>Walls</b> are the place's own rule — on a planet airtight full cubes, every door cell of every kind (open or
/// closed) and the door blocks; on a station the old <c>IsStationAirtightCell</c> rule (airtight cubes, door blocks, a
/// door built into an air cell, the station core). Fluids follow their block's <c>Airtight</c> flag as before.</item>
/// <item><b>Unloaded cells are unknown</b>, not a leak: they bound the fill like walls. One exception keeps a hole in a
/// roof a hole: the server streams only two chunks above a player, so an open column that climbs into an unloaded chunk
/// with no player edit at or above that height in its chunk column is the open sky the server has not loaded.</item>
/// <item><b>Outside</b> is the sky line of a base (<c>max(core + reach, highest build + margin)</c>) or, on a station,
/// anything beyond its build box plus <see cref="StationAirMargin"/> (and an unloaded cell outside the build box).</item>
/// </list>
/// Every fill shares one time budget per server tick (<see cref="AirTickBudgetMs"/>): a query may work on a pending fill
/// only while budget is left; the tick driver (<see cref="TickAirFills"/>) always moves each pending fill a little.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Milliseconds of air filling per server tick, over every pending fill (#2650). A fill that needs more
    /// continues on the next tick; until it is done, its previous result keeps answering.</summary>
    private const double AirTickBudgetMs = 2.0;

    /// <summary>Span expansions each pending fill gets per tick from the tick driver even with the budget spent, so a fill
    /// always finishes.</summary>
    private const int AirMinSpansPerTick = 64;

    /// <summary>The emergency brake of an on-demand diagnosis fill (a player walking out of base air, <c>/basewalls</c>,
    /// a leak warning): it runs at once, so it is held to a few milliseconds. Past it the room reads unknown.</summary>
    private static readonly AirFillLimits AirLocateLimits = new(400_000, 40_000);

    /// <summary>Blocks above a base's highest build where its sky line starts at the least (#2651) — per chunk column too.</summary>
    private const int BaseAirSkyMargin = 4;

    /// <summary>Milliseconds a group's FIRST fill may work at once (#2649 review): it has no previous result to answer with,
    /// so a typical base gets its answer right away, while a huge one goes on sliced instead of stalling the tick.</summary>
    private const double AirFirstFillBudgetMs = 20.0;

    private static readonly AirFillLimits AirDefaultLimits = new(WorldConstants.AirFillBrakeCells, WorldConstants.AirFillBrakeSpans,
        WorldConstants.AirFillRunBrakeCells, WorldConstants.AirFillRunBrakeSpans);

    private double _airTickBudgetMs = AirTickBudgetMs;
    private double _airFirstFillBudgetMs = AirFirstFillBudgetMs;
    private AirFillLimits _airLimits = AirDefaultLimits;
    private double _airBudgetStamp = double.NaN;
    private long _airBudgetLeft;
    private bool[]? _airtightById;
    private bool[]? _airDoorBlockById;

    /// <summary>(body, chunk x, chunk z) → the highest player-edited Y in that chunk column (int.MinValue: none), from the
    /// block-edit store and grown live (<see cref="GrowBaseAirTop"/>). What tells an unloaded chunk over a roof hole (open
    /// sky) from the unloaded ceiling chunk of a tall hall (unknown).</summary>
    private readonly Dictionary<(string Body, int Cx, int Cz), int> _airColumnTop = new();

    private const int AirColumnTopCacheCap = 100_000;

    /// <summary>Scratch list of the tick driver (reused, so a tick allocates nothing).</summary>
    private readonly List<BaseAirVolume> _airTickGroups = new();

    /// <summary>Test seam (#2650): the per-tick air budget in milliseconds.</summary>
    internal double AirTickBudgetMsForTest
    {
        get => _airTickBudgetMs;
        set => _airTickBudgetMs = value;
    }

    /// <summary>Test seam (#2649 review): how long a group's first fill may work at once, in milliseconds.</summary>
    internal double AirFirstFillBudgetMsForTest
    {
        get => _airFirstFillBudgetMs;
        set => _airFirstFillBudgetMs = value;
    }

    /// <summary>Test seam (#2650): the per-room emergency brake of the group and station fills (the run's brake keeps its
    /// default unless given).</summary>
    internal void SetAirBrakeForTest(long maxCells, int maxSpans, long maxRunCells = 0, int maxRunSpans = 0)
        => _airLimits = new AirFillLimits(maxCells, maxSpans,
            maxRunCells > 0 ? maxRunCells : AirDefaultLimits.MaxRunCells, maxRunSpans > 0 ? maxRunSpans : AirDefaultLimits.MaxRunSpans);

    /// <summary>What is left of this tick's air budget, in Stopwatch ticks (renewed when the uptime moves on).</summary>
    private long AirBudgetLeft()
    {
        if (_uptime != _airBudgetStamp)
        {
            _airBudgetStamp = _uptime;
            _airBudgetLeft = (long)(_airTickBudgetMs * Stopwatch.Frequency / 1000.0);
        }

        return _airBudgetLeft;
    }

    /// <summary>Works on a fill: to the end with <paramref name="complete"/> (test seams, a never-seen station room),
    /// else within what is left of this tick's budget — with <paramref name="guaranteeProgress"/> (the tick driver) at
    /// least <see cref="AirMinSpansPerTick"/> span expansions even when the budget is spent.</summary>
    private void StepAirFill(AirFill fill, bool guaranteeProgress, bool complete = false)
    {
        if (fill.Done)
        {
            return;
        }

        long left = AirBudgetLeft();
        if (!complete && left <= 0 && !guaranteeProgress)
        {
            return;
        }

        long t0 = Stopwatch.GetTimestamp();
        fill.Step(complete ? -1 : System.Math.Max(0, left), guaranteeProgress ? AirMinSpansPerTick : 0);
        _airBudgetLeft -= Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>The first step of a group's first fill (#2649 review): up to <see cref="AirFirstFillBudgetMs"/> at once,
    /// whatever is left of the tick's budget — then the fill goes on sliced like any other.</summary>
    private void StepFirstAirFill(AirFill fill)
    {
        long t0 = Stopwatch.GetTimestamp();
        AirBudgetLeft();
        fill.Step((long)(_airFirstFillBudgetMs * Stopwatch.Frequency / 1000.0));
        _airBudgetLeft -= Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>Per-tick driver (#2650): moves every pending air fill of the active world — the base groups standing on it,
    /// the boarded station — and sends the base list where a finished fill changed a core's readout.</summary>
    private void TickAirFills(double dt)
    {
        _ = dt;
        string body = _world.LocationId;

        // A group on a world that ticks is in use, asked or not (a player standing in the core zone never asks about rooms):
        // only a body nobody occupies any more lets its groups go (#2649 review). Marked before the idle sweep runs.
        foreach (var group in _baseAirGroups.Values)
        {
            if (group.Body == body)
            {
                group.LastUsed = _uptime;
            }
        }

        DropIdleBaseAir();

        // A snapshot: a commit or a broadcast below may resolve a base to a new group.
        _airTickGroups.Clear();
        _airTickGroups.AddRange(_baseAirGroups.Values);
        foreach (var group in _airTickGroups)
        {
            if (group.Body != body)
            {
                continue;
            }

            if (group.Job is { } job)
            {
                StepAirFill(job, guaranteeProgress: true);
                if (job.Done)
                {
                    CommitBaseAir(group, broadcastOnChange: false);
                }
            }

            if (group.BroadcastPending)
            {
                group.BroadcastPending = false;
                BroadcastBasesOn(body);
            }
        }

        if (IsSealedAirStationWorld(body) && _stationAir.TryGetValue(body.Substring("station:".Length), out var station)
            && station.Job is { } stationJob)
        {
            StepAirFill(stationJob, guaranteeProgress: true);
            if (stationJob.Done)
            {
                CommitStationAir(station);
            }
        }
    }

    /// <summary>Airtight block ids (the <see cref="Shared.Definitions.BlockDefinition.Airtight"/> flag), as a table.</summary>
    private bool[] AirtightTable()
    {
        if (_airtightById is null)
        {
            var table = new bool[ushort.MaxValue + 1];
            for (int i = 1; i < table.Length; i++)
            {
                table[i] = _content.BlockById(new BlockId((ushort)i)) is { Airtight: true };
            }

            _airtightById = table;
        }

        return _airtightById;
    }

    /// <summary>The door blocks (every kind) as a table — on a planet every door holds air (#2651).</summary>
    private bool[] AirDoorBlockTable()
    {
        if (_airDoorBlockById is null)
        {
            var table = new bool[ushort.MaxValue + 1];
            foreach (var key in new[] { "door_slide", "door_hinge", "door_wood", "door_energy" })
            {
                var id = _content.GetBlock(key)?.NumericId ?? BlockId.Air;
                if (!id.IsAir)
                {
                    table[id.Value] = true;
                }
            }

            _airDoorBlockById = table;
        }

        return _airDoorBlockById;
    }

    /// <summary>The highest player-edited Y in a chunk column of a body (int.MinValue when nobody built there).</summary>
    private int AirColumnTop(string body, int cx, int cz)
    {
        var key = (body, cx, cz);
        if (_airColumnTop.TryGetValue(key, out int top))
        {
            return top;
        }

        if (_airColumnTop.Count >= AirColumnTopCacheCap)
        {
            _airColumnTop.Clear(); // a long trip over many bases — keep the table bounded; entries are re-read on demand
        }

        int s = WorldConstants.ChunkSize;
        top = _repo.TryGetPlayerBlockEditBounds(body, new Vector3i(cx * s, MinBuildY, cz * s),
            new Vector3i(cx * s + s - 1, MaxBuildY, cz * s + s - 1), out _, out var hi)
            ? hi.Y
            : int.MinValue;
        _airColumnTop[key] = top;
        return top;
    }

    /// <summary>Every door of the active world (every kind, open or closed) as canonical cells → door index, plus each
    /// door's cells: the door's width along its wall × the 3-tall doorway column (#2651: every door holds air).</summary>
    private (Dictionary<Vector3i, int> Cells, List<List<Vector3i>> PerDoor) AllDoorCells()
    {
        var cells = new Dictionary<Vector3i, int>();
        var perDoor = new List<List<Vector3i>>(_doors.Count);
        int circ = _world.Circumference;
        for (int i = 0; i < _doors.Count; i++)
        {
            var d = _doors[i];
            var list = new List<Vector3i>();
            perDoor.Add(list);

            // Reconstruct the covered cells from the gap centre + width (see MakeDoor): the lowest cell sits (Width-1)/2
            // left of the centre. Round kills the float error in the exact .5 centres.
            int by = (int)System.Math.Floor(d.Pos.Y);
            int low = (int)System.Math.Round((d.AxisX ? d.Pos.X : d.Pos.Z) - 0.5f - (d.Width - 1f) * 0.5f);
            int fixedAxis = d.AxisX ? (int)System.Math.Floor(d.Pos.Z) : (int)System.Math.Floor(d.Pos.X);
            for (int w = 0; w < (int)d.Width; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    var c = WorldConstants.CanonicalBlock(d.AxisX
                        ? new Vector3i(low + w, by + dy, fixedAxis)
                        : new Vector3i(fixedAxis, by + dy, low + w), circ);
                    cells[c] = i;
                    list.Add(c);
                }
            }
        }

        return (cells, perDoor);
    }

    /// <summary>
    /// A base's world for the air engine (#2651): canonical columns across both seams, every door a linking wall, the sky
    /// line of the group, unknown cells for unloaded chunks except the unbuilt sky above (see the class doc). Reads chunks
    /// directly, one at a time, so a column scan costs an array read per cell.
    /// </summary>
    private sealed class PlanetAirSpace : IAirSpace
    {
        private readonly ServerWorld _world;
        private readonly int _circ;
        private readonly bool[] _airtight;
        private readonly bool[] _doorBlock;
        private readonly Dictionary<Vector3i, int> _doorCells;
        private readonly List<List<Vector3i>> _doorLists;
        private readonly HashSet<ChunkCoord> _doorChunks = new();
        private readonly int _skyY;
        private readonly int _skyMargin;
        private readonly int _floorY;
        private readonly System.Func<int, int, int> _columnTop;
        private int _colX = int.MinValue, _colZ = int.MinValue, _canonX, _canonZ;
        private int _colSky;
        private bool _colSkyValid;
        private ChunkCoord _chunkCoord;
        private ChunkData? _chunk;
        private bool _chunkValid, _chunkHasDoors;

        public PlanetAirSpace(ServerWorld world, bool[] airtight, bool[] doorBlock,
            (Dictionary<Vector3i, int> Cells, List<List<Vector3i>> PerDoor) doors, int skyY, int floorY,
            System.Func<int, int, int> columnTop, int skyMargin)
        {
            _skyMargin = skyMargin;
            _world = world;
            _circ = world.Circumference;
            _airtight = airtight;
            _doorBlock = doorBlock;
            _doorCells = doors.Cells;
            _doorLists = doors.PerDoor;
            foreach (var c in _doorCells.Keys)
            {
                _doorChunks.Add(WorldConstants.WorldToChunk(c));
            }

            _skyY = skyY;
            _floorY = floorY;
            _columnTop = columnTop;
        }

        /// <summary>The group's sky line: an open cell at or above it — and at or above its own column's line — is the outside.</summary>
        public int SkyY => _skyY;

        /// <summary>Whether an open cell at height <paramref name="y"/> of the selected column is the outside: at or above
        /// the group's sky line AND its chunk column's own line, the highest player edit there plus the margin (#2649
        /// review) — a door-chained room past the reach cap or above the group's line is no sky to itself.</summary>
        private bool AboveSkyLine(int y)
        {
            if (y < _skyY)
            {
                return false;
            }

            if (!_colSkyValid)
            {
                int top = _columnTop(_canonX >> 4, _canonZ >> 4);
                _colSky = top == int.MinValue ? _skyY : System.Math.Max(_skyY, top + _skyMargin);
                _colSkyValid = true;
            }

            return y >= _colSky;
        }

        public AirCell Classify(int x, int y, int z)
        {
            if (y < _floorY)
            {
                return AirCell.Wall;
            }

            Select(x, z);
            var chunk = ChunkAt(y);
            if (chunk is null)
            {
                return AboveSkyLine(y) ? AirCell.Sky : AirCell.Unknown;
            }

            int lx = _canonX & 15, ly = y & 15, lz = _canonZ & 15;
            if (_chunkHasDoors && _doorCells.ContainsKey(new Vector3i(_canonX, y, _canonZ)))
            {
                return AirCell.Door;
            }

            ushort id = chunk.Get(lx, ly, lz).Value;
            if (id != 0)
            {
                if (_doorBlock[id])
                {
                    return AirCell.Door;
                }

                if (_airtight[id] && ShapeCode.IsCube(chunk.HasShapes ? chunk.GetShape(lx, ly, lz) : 0))
                {
                    return AirCell.Wall;
                }
            }

            return AboveSkyLine(y) ? AirCell.Sky : AirCell.Open;
        }

        public long ColumnKey(int x, int z)
        {
            Select(x, z);
            return ((long)_canonX << 32) | (uint)_canonZ;
        }

        public Vector3i Canonical(int x, int y, int z) => new(WorldConstants.WrapX(x, _circ), y, WorldConstants.WrapZ(z, _circ));

        public bool UnknownAboveIsSky(int x, int y, int z)
        {
            Select(x, z);
            return _columnTop(_canonX >> 4, _canonZ >> 4) < y;
        }

        public bool HoldsBlock(int x, int y, int z)
        {
            Select(x, z);
            var chunk = ChunkAt(y);
            return chunk != null && !chunk.Get(_canonX & 15, y & 15, _canonZ & 15).IsAir;
        }

        public int DoorAt(int x, int y, int z) => _doorCells.TryGetValue(Canonical(x, y, z), out int door) ? door : -1;

        public void AddDoorSeeds(int door, List<Vector3i> into)
        {
            if (door < 0 || door >= _doorLists.Count)
            {
                return;
            }

            foreach (var c in _doorLists[door])
            {
                into.Add(new Vector3i(c.X + 1, c.Y, c.Z));
                into.Add(new Vector3i(c.X - 1, c.Y, c.Z));
                into.Add(new Vector3i(c.X, c.Y + 1, c.Z));
                into.Add(new Vector3i(c.X, c.Y - 1, c.Z));
                into.Add(new Vector3i(c.X, c.Y, c.Z + 1));
                into.Add(new Vector3i(c.X, c.Y, c.Z - 1));
            }
        }

        /// <summary>The cells of a door (for the doorway cells that breathe beside a supplied room).</summary>
        public IReadOnlyList<Vector3i> DoorCells(int door)
            => door >= 0 && door < _doorLists.Count ? _doorLists[door] : System.Array.Empty<Vector3i>();

        public int WrapDx(int dx) => WorldConstants.WrapDeltaX(dx, _circ);

        public int WrapDz(int dz) => WorldConstants.WrapDeltaZ(dz, _circ);

        private void Select(int x, int z)
        {
            if (x != _colX || z != _colZ)
            {
                _colX = x;
                _colZ = z;
                int cx = WorldConstants.WrapX(x, _circ), cz = WorldConstants.WrapZ(z, _circ);
                if ((cx >> 4) != (_canonX >> 4) || (cz >> 4) != (_canonZ >> 4))
                {
                    _colSkyValid = false; // a new chunk column: its own sky line is read again
                }

                _canonX = cx;
                _canonZ = cz;
            }
        }

        private ChunkData? ChunkAt(int y)
        {
            var coord = new ChunkCoord(_canonX >> 4, y >> 4, _canonZ >> 4);
            if (!_chunkValid || !coord.Equals(_chunkCoord))
            {
                _chunkCoord = coord;
                _chunk = _world.LoadedChunkOrNull(coord);
                _chunkHasDoors = _doorChunks.Contains(coord);
                _chunkValid = true;
            }

            return _chunk;
        }
    }

    /// <summary>
    /// A player station's (or derelict's) interior for the air engine (#2652): plain unwrapped coordinates (the box bounds
    /// every fill), the station rule for walls, the void beyond the build box plus <see cref="StationAirMargin"/> as the
    /// outside — and an unloaded cell outside the build box is that void too, since nothing was built there.
    /// </summary>
    private sealed class StationAirSpace : IAirSpace
    {
        private readonly ServerWorld _world;
        private readonly int _circ;
        private readonly bool[] _airtight;
        private readonly HashSet<ushort> _doorBlocks;
        private readonly ushort _core;
        private readonly HashSet<Vector3i> _doorCells;
        private readonly Vector3i _buildMin, _buildMax, _min, _max;
        private ChunkCoord _chunkCoord;
        private ChunkData? _chunk;
        private bool _chunkValid;

        public StationAirSpace(ServerWorld world, bool[] airtight, HashSet<ushort> doorBlocks, ushort core,
            HashSet<Vector3i> doorCells, Vector3i buildMin, Vector3i buildMax, int margin)
        {
            _world = world;
            _circ = world.Circumference;
            _airtight = airtight;
            _doorBlocks = doorBlocks;
            _core = core;
            _doorCells = doorCells;
            _buildMin = buildMin;
            _buildMax = buildMax;
            _min = new Vector3i(buildMin.X - margin, buildMin.Y - margin, buildMin.Z - margin);
            _max = new Vector3i(buildMax.X + margin, buildMax.Y + margin, buildMax.Z + margin);
        }

        public AirCell Classify(int x, int y, int z)
        {
            if (x < _min.X || x > _max.X || y < _min.Y || y > _max.Y || z < _min.Z || z > _max.Z)
            {
                return AirCell.Sky; // the void beyond the hull box: this pocket has a hole in its hull
            }

            int cx = WorldConstants.WrapX(x, _circ), cz = WorldConstants.WrapZ(z, _circ);
            var chunk = ChunkAt(cx, y, cz);
            if (chunk is null)
            {
                bool inBuild = x >= _buildMin.X && x <= _buildMax.X && y >= _buildMin.Y && y <= _buildMax.Y
                    && z >= _buildMin.Z && z <= _buildMax.Z;
                return inBuild ? AirCell.Unknown : AirCell.Sky;
            }

            int lx = cx & 15, ly = y & 15, lz = cz & 15;
            ushort id = chunk.Get(lx, ly, lz).Value;
            if (id == 0)
            {
                // A door built into an air cell seals (#1481) — an airlock is an airlock whichever way it was placed.
                return _doorCells.Contains(new Vector3i(cx, y, cz)) ? AirCell.Door : AirCell.Open;
            }

            if (_doorBlocks.Contains(id))
            {
                return AirCell.Door;
            }

            if (id == _core || (_airtight[id] && ShapeCode.IsCube(chunk.HasShapes ? chunk.GetShape(lx, ly, lz) : 0)))
            {
                return AirCell.Wall;
            }

            return AirCell.Open;
        }

        public long ColumnKey(int x, int z) => ((long)x << 32) | (uint)z;

        public Vector3i Canonical(int x, int y, int z) => new(x, y, z);

        public bool UnknownAboveIsSky(int x, int y, int z) => false;

        public bool HoldsBlock(int x, int y, int z)
        {
            int cx = WorldConstants.WrapX(x, _circ), cz = WorldConstants.WrapZ(z, _circ);
            var chunk = ChunkAt(cx, y, cz);
            return chunk != null && !chunk.Get(cx & 15, y & 15, cz & 15).IsAir;
        }

        public int DoorAt(int x, int y, int z) => -1;

        public void AddDoorSeeds(int door, List<Vector3i> into)
        {
        }

        public int WrapDx(int dx) => dx;

        public int WrapDz(int dz) => dz;

        private ChunkData? ChunkAt(int cx, int y, int cz)
        {
            var coord = new ChunkCoord(cx >> 4, y >> 4, cz >> 4);
            if (!_chunkValid || !coord.Equals(_chunkCoord))
            {
                _chunkCoord = coord;
                _chunk = _world.LoadedChunkOrNull(coord);
                _chunkValid = true;
            }

            return _chunk;
        }
    }
}
