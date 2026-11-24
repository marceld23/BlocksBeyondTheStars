// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A line's voxel stamp as resumable work (#3985; the builder is GameServerIntercityRailBuilder.cs): the stations whose hall
/// span lies in the stamped stretch (each worked out into a <see cref="BufferedCellSink"/> first, then written), then its
/// pylon columns, then the corridor of its spans, cell by cell. Run in one go it is the stamp it always was
/// (<see cref="StampIntercityVoxels(IntercityPlan, string, int, int)"/>: the same writes in the same order); run under a
/// deadline it stops after a station worked out, a few of its cells written, a pylon or a few corridor cells and goes on where
/// it stopped (a listed line's section, a few milliseconds per tick); run with a list of missing chunks it never reads a chunk
/// that is not resident — it stops before the station, pylon or corridor cell that needs one and names the chunks it waits for.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The corridor cells cleared between two looks at the deadline.</summary>
    private const int IntercityStampCellsPerCheck = 16;

    /// <summary>Where a line's stamp stands.</summary>
    internal enum IntercityStampStep
    {
        Stations,
        Pylons,
        Corridor,
        Done,
    }

    /// <summary>A line's voxel stamp in progress, from pylon <see cref="First"/> to pylon <see cref="Last"/>.</summary>
    internal sealed class IntercityStampWork
    {
        public IntercityStampWork(IntercityPlan plan, string surface, int first, int last)
        {
            Plan = plan;
            Surface = surface;
            First = first;
            Last = last;
        }

        public IntercityPlan Plan { get; }
        public string Surface { get; }
        public int First { get; }
        public int Last { get; }

        public IntercityStampStep Step { get; set; } = IntercityStampStep.Stations;

        /// <summary>The next stop (stations) or pylon (pylons) to stamp.</summary>
        public int Next { get; set; }

        /// <summary>The corridor's cells still to come, and the one the stamp stopped at to wait for its chunk.</summary>
        public IEnumerator<Vector3i>? Corridor { get; set; }
        public Vector3i? Held { get; set; }

        /// <summary>The station being written: its stamp worked out, its cells not all written yet.</summary>
        public BufferedCellSink? StationCells { get; set; }

        public BlockId PylonId { get; set; }
        public int TreeCap { get; set; }
        public HashSet<ushort> Vegetation { get; set; } = new();
        public Func<Vector3i, bool> Town { get; set; } = _ => true;

        /// <summary>#4016: whether a cell holds a player's edit — a listed line's stamp keeps it (never for any other line).</summary>
        public Func<Vector3i, bool> PlayerCell { get; set; } = _ => false;

        /// <summary>The corridor cells already looked at (the spans' corridors overlap at their pylons).</summary>
        public HashSet<Vector3i> Seen { get; } = new();

        public int Carved { get; set; }
        public int Trees { get; set; }
        public bool Done => Step == IntercityStampStep.Done;
    }

    /// <summary>Stamps the part of a line from pylon <paramref name="first"/> to pylon <paramref name="last"/> in one go (#3985:
    /// one section): the stations whose hall span lies in it, its pylon columns and the corridor of its spans. Must run inside
    /// a repo transaction.</summary>
    private (int Carved, int Trees) StampIntercityVoxels(IntercityPlan plan, string surface, int first, int last)
    {
        var work = NewIntercityStampWork(plan, surface, first, last);
        AdvanceIntercityStamp(work, long.MaxValue, missing: null);
        return (work.Carved, work.Trees);
    }

    /// <summary>Runs a stamp on until it is done (true), the <paramref name="deadline"/> (a <see cref="Stopwatch"/> timestamp)
    /// passed, or — with <paramref name="missing"/> — its next step needs a chunk that is not resident: those chunks are added
    /// to the list and nothing of that step is written. Without the list every chunk it needs is loaded inline. Must run
    /// inside a repo transaction.</summary>
    private bool AdvanceIntercityStamp(IntercityStampWork work, long deadline, List<ChunkCoord>? missing)
    {
        while (!work.Done)
        {
            bool stepped = work.Step switch
            {
                IntercityStampStep.Stations => StampNextIntercityStation(work, deadline, missing),
                IntercityStampStep.Pylons => StampNextIntercityPylon(work, missing),
                _ => ClearIntercityCorridorCells(work, deadline, missing),
            };
            if (!stepped)
            {
                return false; // waiting for chunks
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                return work.Done;
            }
        }

        return true;
    }

    /// <summary>Whether a stop's hall is stamped with the stretch <paramref name="first"/>..<paramref name="last"/>: the
    /// stretch that holds the span inside its hall.</summary>
    private static bool StampsWithStretch(IntercityPlan plan, IntercityStation st, int first, int last)
    {
        int span = Math.Min(plan.Pylons.IndexOf(st.EndPylon), plan.Pylons.IndexOf(st.ExitPylon));
        return span >= first && span < Math.Max(first + 1, last);
    }

    /// <summary>The next station of the stretch: its stamp worked out into a buffer (reading the world as it goes), then its
    /// cells written from the buffer until the deadline, run after run; then the next station, or the pylons after the last.</summary>
    private bool StampNextIntercityStation(IntercityStampWork work, long deadline, List<ChunkCoord>? missing)
    {
        if (work.StationCells is { } pending)
        {
            if (pending.WriteUntil(deadline))
            {
                work.StationCells = null;
                work.Next++;
            }

            return true;
        }

        var plan = work.Plan;
        while (work.Next < plan.Stops.Count && !StampsWithStretch(plan, plan.Stops[work.Next], work.First, work.Last))
        {
            work.Next++;
        }

        if (work.Next >= plan.Stops.Count)
        {
            BeginIntercityPylons(work);
            return true;
        }

        var st = plan.Stops[work.Next];
        if (missing is not null && !AllResident(IntercityStationChunks(st), missing))
        {
            return false;
        }

        var cells = new BufferedCellSink(_world) { Keep = work.PlayerCell };
        StampSettlementBlocks(new PlacedSettlement
        {
            Structure = st.Structure,
            Origin = st.Origin,
            GroundY = st.Origin.Y,
            Tier = RailStationGenerator.Tier,
            Ruined = false,
            OnIsland = false,
            Name = st.SettlementName,
            Rng = new Random(1),
            Seat = st.Seat,
            Cells = cells,
        }, work.Surface, crownRing: false);
        work.StationCells = cells;
        return true;
    }

    /// <summary>The pylons come next — or nothing more when the content has no pylon block.</summary>
    private void BeginIntercityPylons(IntercityStampWork work)
    {
        work.PylonId = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId ?? BlockId.Air;
        work.Step = work.PylonId.IsAir ? IntercityStampStep.Done : IntercityStampStep.Pylons;
        work.Next = work.First;
    }

    /// <summary>Stamps the next free pylon column of the stretch, foot to top (or moves on to the corridor after the last).</summary>
    private bool StampNextIntercityPylon(IntercityStampWork work, List<ChunkCoord>? missing)
    {
        int j = work.Next;
        if (j > work.Last)
        {
            BeginIntercityCorridor(work);
            return true;
        }

        if (!work.Plan.IsStationPylon(j)) // a station's own pylons are part of its hall
        {
            var top = work.Plan.Pylons[j];
            int foot = Math.Min(top.Y, IntercityPlanFoot(work.Plan, top.X, top.Z));
            if (missing is not null && !AllResident(ColumnChunks(top.X, top.Z, foot, top.Y), missing))
            {
                return false;
            }

            for (int yy = foot; yy <= top.Y; yy++)
            {
                var cell = new Vector3i(top.X, yy, top.Z);
                if (!work.PlayerCell(cell))
                {
                    _world.SetBlock(cell, work.PylonId);
                }
            }
        }

        work.Next = j + 1;
        return true;
    }

    /// <summary>The corridor comes next: its cells span by span, the tree budget (a section's share of it for a listed line)
    /// and what counts as town ground (a listed line clears the land a metropolis left to nature inside its box, #3985).</summary>
    private void BeginIntercityCorridor(IntercityStampWork work)
    {
        var plan = work.Plan;
        float route = plan.Listed ? IntercitySectionRoute(plan, work.First, work.Last) : plan.RouteLength;
        work.TreeCap = IntercityVegetationCapFor(_generator.TerrainGeneration, route);
        work.Vegetation = SettlementVegetationIds;
        work.Town = plan.Listed ? c => InAnySettlementColumns(c) || work.PlayerCell(c) : InAnySettlementBox; // #4016: a player's cell stays
        work.Corridor = IntercityStretchCorridor(plan, work.First, work.Last).GetEnumerator();
        work.Step = IntercityStampStep.Corridor;
    }

    /// <summary>The corridor cells of every span from pylon <paramref name="first"/> to pylon <paramref name="last"/>, in order —
    /// for a listed line along the curve its trains ride (#4014, <see cref="ListedSplineCorridor"/>).</summary>
    private IEnumerable<Vector3i> IntercityStretchCorridor(IntercityPlan plan, int first, int last)
    {
        if (plan.Listed)
        {
            foreach (var cell in ListedSplineCorridor(plan, first, last))
            {
                yield return cell;
            }

            yield break;
        }

        for (int j = first; j < last; j++)
        {
            foreach (var cell in IntercityCorridor(plan.Pylons[j], plan.Pylons[j + 1]))
            {
                yield return cell;
            }
        }
    }

    /// <summary>Clears corridor cells until the corridor is done, the deadline passed or (with <paramref name="missing"/>) a
    /// cell's chunk is not resident — that cell is held for the next run.</summary>
    private bool ClearIntercityCorridorCells(IntercityStampWork work, long deadline, List<ChunkCoord>? missing)
    {
        for (int n = 1; ; n++)
        {
            Vector3i c;
            if (work.Held is { } held)
            {
                c = held;
                work.Held = null;
            }
            else if (work.Corridor!.MoveNext())
            {
                c = WorldConstants.CanonicalBlock(work.Corridor.Current, _world.Circumference);
            }
            else
            {
                work.Corridor.Dispose();
                work.Corridor = null;
                work.Step = IntercityStampStep.Done;
                return true;
            }

            if (missing is not null && WithinBuildHeight(c.Y) && !_world.IsChunkLoaded(WorldConstants.WorldToChunk(c)))
            {
                work.Held = c;
                missing.Add(WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(c), _world.Circumference));
                return false;
            }

            ClearIntercityCorridorCell(work, c);
            if (n % IntercityStampCellsPerCheck == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                return true;
            }
        }
    }

    /// <summary>One corridor cell (canonical): anything in it goes but fluids, the pylons, the stops, the stations' own layout
    /// and the towns' blocks; a tree that reaches into it goes as a whole, within the tree budget.</summary>
    private void ClearIntercityCorridorCell(IntercityStampWork work, Vector3i c)
    {
        if (!work.Seen.Add(c) || !WithinBuildHeight(c.Y) || IsStationLayoutCell(c) || Intercity.PylonTops.Contains(c))
        {
            return; // the station's own blocks stand clear of the corridor by design; anything else in it goes
        }

        var id = _world.GetBlock(c);
        if (id.IsAir)
        {
            return;
        }

        var def = _content.BlockById(id);
        if (def is null || def.Liquid || IsFluid(id.Value)
            || def.Key is RailRules.PylonBlockKey or RailRules.StopBlockKey || work.Town(c))
        {
            return; // fluids stay (the line runs over them); the towns' blocks are never touched
        }

        if (work.Vegetation.Contains(id.Value) && work.Trees < work.TreeCap)
        {
            work.Trees += ClearTreeAt(c, work.Vegetation, work.TreeCap - work.Trees, work.Town);
            return;
        }

        _world.SetBlock(c, BlockId.Air);
        work.Carved++;
    }

    /// <summary>Adds every chunk of <paramref name="chunks"/> that is not resident to <paramref name="missing"/>; true when
    /// all are.</summary>
    private bool AllResident(IEnumerable<ChunkCoord> chunks, List<ChunkCoord> missing)
    {
        int before = missing.Count;
        foreach (var chunk in chunks)
        {
            if (!_world.IsChunkLoaded(chunk))
            {
                missing.Add(chunk);
            }
        }

        return missing.Count == before;
    }

    /// <summary>The chunks (canonical) a station's stamp reads and writes: its footprint and a block round it, from
    /// <see cref="IntercityStationDepth"/> under its floor up to the trees over it.</summary>
    private IEnumerable<ChunkCoord> IntercityStationChunks(IntercityStation st)
        => BoxChunks(st.Origin.X - 1, st.Origin.X + st.W, st.Origin.Y - IntercityStationDepth,
            st.Origin.Y + Math.Max(st.Structure.Height, SettlementVegetationRise) + 1, st.Origin.Z - 1, st.Origin.Z + st.L);

    /// <summary>How far under a station's floor its stamp reaches (its foundation).</summary>
    private const int IntercityStationDepth = 16;

    /// <summary>The chunks (canonical) of one block column from <paramref name="y0"/> to <paramref name="y1"/>.</summary>
    private IEnumerable<ChunkCoord> ColumnChunks(int x, int z, int y0, int y1) => BoxChunks(x, x, y0, y1, z, z);

    /// <summary>The chunks (canonical) a block box touches, clamped to the build height (X may run over the seam).</summary>
    private IEnumerable<ChunkCoord> BoxChunks(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        int cy0 = WorldConstants.WorldToChunk(Math.Max(y0, MinBuildY)), cy1 = WorldConstants.WorldToChunk(Math.Min(y1, MaxBuildY));
        for (int cx = WorldConstants.WorldToChunk(x0); cx <= WorldConstants.WorldToChunk(x1); cx++)
        {
            for (int cz = WorldConstants.WorldToChunk(z0); cz <= WorldConstants.WorldToChunk(z1); cz++)
            {
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    yield return WorldConstants.CanonicalChunk(new ChunkCoord(cx, cy, cz), _world.Circumference);
                }
            }
        }
    }
}

/// <summary>
/// A structure stamp worked out before it is written (#3985: a rail section's station hall): the builder's writes are kept in
/// order and its reads see them first, then the world — the very cells <see cref="GameServer"/>'s world sink reads and writes
/// (X wrapped round the seam) — and <see cref="WriteUntil"/> writes them into the world in that order, as many as a deadline
/// holds, run after run. Written out it leaves the world as the builder writing straight into it would.
/// </summary>
internal sealed class BufferedCellSink : IStructureCellSink
{
    private readonly ServerWorld _world;
    private readonly List<(Vector3i Cell, BlockId Block, int Tint, int Glow, int Shape)> _writes = new();
    private readonly Dictionary<Vector3i, BlockId> _latest = new();

    public BufferedCellSink(ServerWorld world) => _world = world;

    /// <summary>#4016: the cells the sink never writes (a player's, for a listed line's hall); none by default.</summary>
    public Func<Vector3i, bool> Keep { get; init; } = _ => false;

    /// <summary>The writes kept, and how many of them are in the world.</summary>
    public int Count => _writes.Count;
    public int Written { get; private set; }

    public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
    {
        var cell = Cell(x, y, z);
        _writes.Add((cell, block, tint, glow, shape));
        _latest[cell] = block;
    }

    public BlockId Get(int x, int y, int z)
    {
        var cell = Cell(x, y, z);
        return _latest.TryGetValue(cell, out var block) ? block : _world.GetBlock(cell);
    }

    public void Mark(string type, int x, int y, int z, string data)
    {
        // A station hall has no markers (the world sink of a settlement stamp acts on none either).
    }

    /// <summary>Writes the kept cells in order until the <paramref name="deadline"/> (a <see cref="System.Diagnostics.Stopwatch"/>
    /// timestamp; a few at least); true once all are written.</summary>
    public bool WriteUntil(long deadline)
    {
        while (Written < _writes.Count)
        {
            var (cell, block, tint, glow, shape) = _writes[Written++];
            if (!Keep(cell))
            {
                _world.SetBlock(cell, block, tint, glow, shape);
            }
            if (Written % 32 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }
        }

        return Written == _writes.Count;
    }

    private Vector3i Cell(int x, int y, int z) => new(WorldConstants.WrapX(x, _world.Circumference), y, z);
}
