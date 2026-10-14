// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Water classes and finite still water (#2515, ADR 0017).
/// <para>
/// A source cell (untracked water) is <b>endless</b> only where something can feed it for ever: at or below the planet's
/// sea level, in a routed river, in a generated waterfall column, or because a player placed it (the placed-source
/// mark). Every other source — a pond, a lake, water a settle moved — is <b>finite still water</b>: it never spreads as a
/// flow. When such a body is disturbed and has somewhere to go, it <b>settles</b> as a whole: layer by layer from the
/// top, its water moves into the lowest air it can reach through cells no higher than itself (communicating vessels, a
/// sill is respected), and a layer whose reach finds lower endless water, more than <see cref="SettleRunoff"/> blocks of
/// runoff or more room than <see cref="SettleExploreCap"/> seeps away. The result is applied over the next steps at a
/// rate that follows the size of the opening, a draining outlet keeps a short stream running, and the plan is renewed
/// every second — so plugging the gap stops the drain.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>W5: a still body bigger than this counts as a reservoir — endless, but the fall rule still bounds its spills.</summary>
    private const int SettleBodyCap = 2048;

    /// <summary>W5: how many cells one layer's reach may explore before the water counts as lost into the room below.</summary>
    private const int SettleExploreCap = 4096;

    /// <summary>W3: how far past the body drained water may run before it has seeped away.</summary>
    private const int SettleRunoff = 8;

    /// <summary>The fastest a settle moves — cells per fluid step, for a wide opening.</summary>
    private const int SettleMaxRate = 24;

    /// <summary>A running settle re-plans this often (fluid steps — about once a second), so a plugged gap stops it. The
    /// rest memo clears on the same cadence.</summary>
    private const int SettleRecheckSteps = 8;

    /// <summary>At most this many settle plans (new or renewed) per fluid step; the rest waits a step. A plan of a body at
    /// the cap costs about 8 ms warm, so a step's planning stays well under a server tick.</summary>
    private const int SettlePlansPerStep = 2;

    // Per fluid step: plans made so far, and every cell a plan or an at-rest verdict covered this step (the other woken
    // cells of the same pond need no plan of their own).
    private int _settlePlansThisStep;
    private readonly HashSet<Vector3i> _settleCovered = new();

    /// <summary>The water-class memo is dropped beyond this many columns (it only saves generator queries).</summary>
    private const int WaterKindCacheCap = 32768;

    // The settle reads thousands of neighbours that mostly share a chunk: the last chunk's "may be read" answer is kept for
    // the length of one search (a chunk does not load or unload in between).
    private ChunkCoord _settleChunk;
    private bool _settleChunkReadable, _settleChunkKnown;

    private bool SettleCanRead(Vector3i cell)
    {
        var chunk = WorldConstants.WorldToChunk(cell);
        if (!_settleChunkKnown || !chunk.Equals(_settleChunk))
        {
            _settleChunk = chunk;
            _settleChunkReadable = _world.IsNeighbourhoodLoaded(cell, 0, _fluidMayLoad);
            _settleChunkKnown = true;
        }

        return _settleChunkReadable;
    }

    private const string PlacedWaterMigrationKey = "migration.fluid_source.v1";

    private static readonly Vector3i[] SettleDirs =
    {
        new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1), new Vector3i(0, 1, 0), new Vector3i(0, -1, 0),
    };

    /// <summary>The placed-source marks of the active world, read from the save on first use — so every world that can hold
    /// placed water has them, a station (void) world included, which never runs the planet fluid boot.</summary>
    private HashSet<Vector3i> _placedSources
    {
        get
        {
            var world = _worlds.Active;
            if (!world.PlacedFluidSourcesLoaded && _waterId != 0)
            {
                world.PlacedFluidSourcesLoaded = true;
                LoadPlacedSources(world.PlacedFluidSources);
            }

            return world.PlacedFluidSources;
        }
    }
    private List<FluidSettle> _settles => _worlds.Active.FluidSettles;
    private HashSet<Vector3i> _settling => _worlds.Active.SettlingFluid;
    private HashSet<Vector3i> _drained => _worlds.Active.DrainedFluid;

    /// <summary>One running settle: the cells it owns, what it still fills and empties, and the drain outlets that feed
    /// the visible stream.</summary>
    internal sealed class FluidSettle
    {
        public readonly HashSet<Vector3i> Cells = new();
        public readonly List<Vector3i> Drained = new();
        public readonly Dictionary<Vector3i, Vector3i> Outlets = new(); // outlet water cell → the opening it pours into
        public List<Vector3i> Add = new();    // lowest first
        public List<Vector3i> Remove = new(); // highest layer first, each layer's outlets last
        public int Ai, Ri;
        public int Rate = 1;
        public bool Drains;
        public long NextCheck;
    }

    private enum WaterClass : byte
    {
        Finite,
        Endless,
        ConfinedFall,
    }

    private long _settleTicksMax, _settleTicksTotal, _replanTicksMax, _replanTicksTotal;
    private int _settlesPlanned, _replans;

    /// <summary>Test seam: how many settles have been planned, and the slowest and the total planning time in milliseconds.</summary>
    public (int Count, double MaxMs, double TotalMs) SettleStatsForTest
        => (_settlesPlanned, _settleTicksMax * 1000.0 / Stopwatch.Frequency, _settleTicksTotal * 1000.0 / Stopwatch.Frequency);

    /// <summary>Test seam: the once-a-second re-plans of running settles — the warm cost of one plan.</summary>
    public (int Count, double MaxMs, double TotalMs) SettleReplanStatsForTest
        => (_replans, _replanTicksMax * 1000.0 / Stopwatch.Frequency, _replanTicksTotal * 1000.0 / Stopwatch.Frequency);

    /// <summary>Test seam: running settles on the active world.</summary>
    public int SettleJobCountForTest => _settles.Count;

    /// <summary>Test seam: the cells waiting for a busy settle to end.</summary>
    internal int SettleWaitingCountForTest => _worlds.Active.SettleWaiting.Count;

    /// <summary>Test seam: the settle entry for one cell, exactly as the step runs it for a woken finite cell (with a fresh
    /// per-step budget).</summary>
    internal bool TrySettleForTest(int x, int y, int z)
    {
        _settlePlansThisStep = 0;
        _settleCovered.Clear();
        return TrySettle(FluidCell(new Vector3i(x, y, z)));
    }

    /// <summary>Test seam: whether a cell carries the placed-source mark.</summary>
    public bool IsPlacedFluidSourceForTest(int x, int y, int z) => _placedSources.Contains(FluidCell(new Vector3i(x, y, z)));

    /// <summary>Test seam: whether a cell is a tracked flowing cell (has a level row).</summary>
    public bool IsFlowingFluidForTest(int x, int y, int z) => IsFlowingCell(new Vector3i(x, y, z));

    /// <summary>The class of an untracked (source) water cell.</summary>
    private WaterClass ClassifyWater(Vector3i cell)
    {
        if (_placedSources.Contains(cell) || _worlds.Active.ReservoirFluid.Contains(cell))
        {
            return WaterClass.Endless;
        }

        var columns = _worlds.Active.WaterColumns;
        if (!columns.TryGetValue((cell.X, cell.Z), out var column))
        {
            column = _world.Generator.WaterColumnAt(_world.Planet, cell.X, cell.Z);
            if (columns.Count >= WaterKindCacheCap)
            {
                columns.Clear();
            }

            columns[(cell.X, cell.Z)] = column;
        }

        var kind = column.KindAt(cell.Y);
        return WorldGenerator.IsConfinedFall(kind) ? WaterClass.ConfinedFall
            : WorldGenerator.IsEndless(kind) ? WaterClass.Endless
            : WaterClass.Finite;
    }

    /// <summary>Whether an untracked fluid cell feeds the flowing cells beside or below it: lava always (its sources stay
    /// endless, W4); water when it is endless, or while it is the outlet of a draining settle (its stream).</summary>
    private bool FeedsAsSource(Vector3i cell, ushort id)
    {
        if (id != _waterId)
        {
            return true;
        }

        if (_settling.Contains(cell))
        {
            return IsDrainOutlet(cell);
        }

        return ClassifyWater(cell) != WaterClass.Finite;
    }

    private bool IsDrainOutlet(Vector3i cell)
    {
        foreach (var settle in _settles)
        {
            if (settle.Drains && settle.Outlets.ContainsKey(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A draining settle's outlet pours into its opening while the drain runs — the short visible stream (W3).
    /// The stream is ordinary flowing water, so the fall rule bounds it and it dries up once the outlet is gone.</summary>
    private void FeedDrainStream(Vector3i cell)
    {
        foreach (var settle in _settles)
        {
            if (!settle.Drains || !settle.Outlets.TryGetValue(cell, out var opening))
            {
                continue;
            }

            if (FluidCanEnter(opening))
            {
                FillFluid(opening, new BlockId(_waterId), FluidFull - 1, falling: opening.Y < cell.Y);
            }

            WakeFluidCell(cell); // keep pouring until the settle removes the outlet
            return;
        }
    }

    // --- Placed sources (W1) -----------------------------------------------------------------------------------------

    private void MarkPlacedSource(Vector3i pos)
    {
        var cell = FluidCell(pos);
        if (_placedSources.Add(cell))
        {
            _repo.SaveFluidSource(_world.LocationId, cell);
        }
    }

    private void ForgetPlacedSource(Vector3i pos)
    {
        var cell = FluidCell(pos);
        if (_placedSources.Remove(cell))
        {
            _repo.DeleteFluidSource(_world.LocationId, cell);
        }
    }

    /// <summary>Restores this world's placed-source marks into <paramref name="into"/> — after the one-time migration that
    /// marks water players placed before the marks existed (the migration covers every world of the save at once).</summary>
    private void LoadPlacedSources(HashSet<Vector3i> into)
    {
        if (_repo.LoadNamedBlob(PlacedWaterMigrationKey) is null)
        {
            int marked = _repo.MarkPlacedWaterSources(_waterId);
            _repo.SaveNamedBlob(PlacedWaterMigrationKey, "{\"marked\":" + marked + "}");
            if (marked > 0)
            {
                _log.Info($"Water model (#2515): {marked} player-placed water block(s) marked as sources.");
            }
        }

        foreach (var p in _repo.ListFluidSources(_world.LocationId))
        {
            into.Add(FluidCell(p));
        }
    }

    // --- Settling ----------------------------------------------------------------------------------------------------

    /// <summary>Takes over a woken finite cell that has somewhere to go. True when it is handled — a settle now runs, the
    /// body is at rest, or it waits at the loaded edge; false when the body is too big to settle (a reservoir), and the
    /// caller lets the cell act as an endless source.</summary>
    private bool TrySettle(Vector3i start)
    {
        // Cost control: a cell another plan of this step already covered needs none of its own; a body found at rest within
        // the last second is held back until the memo clears (then woken again — nothing is lost, only delayed); and no
        // more than SettlePlansPerStep plans run per step (the rest is re-queued for the next one).
        if (_settleCovered.Contains(start))
        {
            return true;
        }

        if (_worlds.Active.SettleRestMemo.Contains(start))
        {
            _worlds.Active.SettleDeferred.Add(start);
            return true;
        }

        if (_settlePlansThisStep >= SettlePlansPerStep)
        {
            WakeFluidCell(start);
            return true;
        }

        _settlePlansThisStep++;
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            var body = CollectStillBody(start, out var outcome, out int top, out int bottom, out int radius);
            if (outcome == BodyOutcome.Reservoir)
            {
                foreach (var c in body)
                {
                    _worlds.Active.ReservoirFluid.Add(c);
                }

                return false;
            }

            if (outcome == BodyOutcome.Busy)
            {
                _worlds.Active.SettleWaiting.Add(start); // retried when a settle ends
                return true;
            }

            if (outcome != BodyOutcome.Collected)
            {
                return true;
            }

            _settleCovered.UnionWith(body);
            var settle = PlanSettle(start, body, top, bottom, radius);
            if (settle is null)
            {
                _worlds.Active.SettleRestMemo.UnionWith(body); // at rest
                return true;
            }

            settle.NextCheck = _worlds.Active.FluidStep + SettleRecheckSteps;
            foreach (var c in settle.Cells)
            {
                _settling.Add(c);
            }

            _settles.Add(settle);
            foreach (var outlet in settle.Outlets.Keys)
            {
                WakeFluidCell(outlet); // the stream starts on the next step
            }

            return true;
        }
        finally
        {
            long dt = Stopwatch.GetTimestamp() - t0;
            _settlesPlanned++;
            _settleTicksTotal += dt;
            _settleTicksMax = Math.Max(_settleTicksMax, dt);
        }
    }

    private enum BodyOutcome : byte
    {
        Collected,
        Reservoir,
        Busy,
        Parked,
    }

    /// <summary>The connected finite still water around <paramref name="start"/> (6-connected). Endless water and flowing
    /// tongues bound it; a cell another settle owns, or a neighbour in an unloaded chunk, defers it. Water standing in a
    /// doorway (#2561) belongs to no body: it bounds it, and is woken so the step clears it.</summary>
    private HashSet<Vector3i> CollectStillBody(Vector3i start, out BodyOutcome outcome, out int top, out int bottom, out int radius)
    {
        _settleChunkKnown = false;
        var doorCells = FluidDoorCells;
        var body = new HashSet<Vector3i> { start };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(start);
        top = bottom = start.Y;
        radius = 0;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var d in SettleDirs)
            {
                var n = FluidCell(new Vector3i(c.X + d.X, c.Y + d.Y, c.Z + d.Z));
                if (body.Contains(n))
                {
                    continue;
                }

                if (!SettleCanRead(n))
                {
                    _worlds.Active.ParkedFluid.Add(start); // never generate terrain for a settle (#1824)
                    outcome = BodyOutcome.Parked;
                    return body;
                }

                if (_world.GetBlock(n).Value != _waterId || _fluidLevel.ContainsKey(n))
                {
                    continue;
                }

                if (doorCells.Count > 0 && doorCells.Contains(n))
                {
                    WakeFluidCell(n); // #2561: an older save's water in a doorway — the step clears it
                    continue;
                }

                if (_settling.Contains(n))
                {
                    outcome = BodyOutcome.Busy; // the settle that owns it re-plans within a second
                    return body;
                }

                if (_worlds.Active.ReservoirFluid.Contains(n))
                {
                    outcome = BodyOutcome.Reservoir; // part of a body already found too big: the whole body is the reservoir
                    return body;
                }

                if (ClassifyWater(n) != WaterClass.Finite)
                {
                    continue; // the sea, a river or a placed source beside the pond: a boundary, not part of it
                }

                body.Add(n);
                queue.Enqueue(n);
                top = Math.Max(top, n.Y);
                bottom = Math.Min(bottom, n.Y);
                radius = Math.Max(radius, HorizontalDistance(n, start));
                if (body.Count > SettleBodyCap)
                {
                    outcome = BodyOutcome.Reservoir;
                    return body;
                }
            }
        }

        outcome = BodyOutcome.Collected;
        return body;
    }

    private int HorizontalDistance(Vector3i a, Vector3i b)
        => Math.Max(Math.Abs(WorldConstants.WrapDeltaX(a.X - b.X, _world.Circumference)), Math.Abs(a.Z - b.Z));

    /// <summary>The layer drain. Null when nothing would move.</summary>
    private FluidSettle? PlanSettle(Vector3i start, HashSet<Vector3i> body, int top, int bottom, int radius)
    {
        var water = new HashSet<Vector3i>(body);
        var outlets = new Dictionary<Vector3i, Vector3i>();
        int runoff = radius + SettleRunoff;
        int rate = 1;
        bool drains = false;
        for (int h = top; h >= bottom; h--)
        {
            var layer = water.Where(c => c.Y == h).OrderBy(c => c.X).ThenBy(c => c.Z).ToList();
            if (layer.Count == 0)
            {
                continue;
            }

            var reach = ReachFromLayer(layer, water, h, start, runoff);
            rate = Math.Max(rate, reach.Openings);
            if (reach.Sink)
            {
                drains = true;
                foreach (var c in layer)
                {
                    water.Remove(c);
                }

                if (reach.Outlet is { } outlet && body.Contains(outlet.Water))
                {
                    outlets[outlet.Water] = outlet.Opening;
                }

                continue;
            }

            if (reach.Targets.Count == 0)
            {
                continue;
            }

            int k = Math.Min(reach.Targets.Count, layer.Count);
            for (int i = 0; i < k; i++)
            {
                water.Remove(layer[i]);
                water.Add(reach.Targets[i]);
            }
        }

        var add = water.Where(c => !body.Contains(c)).OrderBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z).ToList();
        var remove = body.Where(c => !water.Contains(c))
            .OrderByDescending(c => c.Y).ThenBy(c => outlets.ContainsKey(c) ? 1 : 0).ThenBy(c => c.X).ThenBy(c => c.Z).ToList();
        if (add.Count == 0 && remove.Count == 0)
        {
            return null;
        }

        var settle = new FluidSettle { Add = add, Remove = remove, Drains = drains, Rate = Math.Clamp(rate, 1, SettleMaxRate) };
        settle.Cells.UnionWith(body);
        settle.Cells.UnionWith(add);
        foreach (var (w, o) in outlets)
        {
            settle.Outlets[w] = o;
        }

        return settle;
    }

    private readonly struct LayerReach
    {
        public LayerReach(bool sink, List<Vector3i> targets, int openings, (Vector3i Water, Vector3i Opening)? outlet)
        {
            Sink = sink;
            Targets = targets;
            Openings = openings;
            Outlet = outlet;
        }

        public bool Sink { get; }
        public List<Vector3i> Targets { get; }
        public int Openings { get; }
        public (Vector3i Water, Vector3i Opening)? Outlet { get; }
    }

    /// <summary>Where one layer's water can go: the air below it reachable through cells no higher than the layer (the
    /// body's own water, air, and flowing tongues — passable, never filled), sorted lowest first; or a sink — lower endless
    /// water reached through the air, the runoff limit, or more room than the exploration cap. Openings counts the distinct
    /// gaps through which the water leaves the body toward somewhere lower — the drain rate follows it. A doorway is a wall
    /// (#2561), whatever it holds: still water never settles through a door, open or shut.</summary>
    private LayerReach ReachFromLayer(List<Vector3i> layer, HashSet<Vector3i> water, int h, Vector3i start, int runoff)
    {
        _settleChunkKnown = false;
        var doorCells = FluidDoorCells;
        var seen = new HashSet<Vector3i>(layer);
        var queue = new Queue<Vector3i>(layer);
        var origin = new Dictionary<Vector3i, Vector3i>();      // a cell outside the body → the gap it was reached through
        var enteredFrom = new Dictionary<Vector3i, Vector3i>(); // a gap → the body cell beside it
        var useful = new HashSet<Vector3i>();                   // gaps that lead somewhere lower
        var targets = new List<Vector3i>();
        int explored = 0;
        Vector3i? sinkGap = null;
        while (queue.Count > 0 && sinkGap is null)
        {
            var c = queue.Dequeue();
            bool fromWater = water.Contains(c);
            foreach (var d in SettleDirs)
            {
                var n = FluidCell(new Vector3i(c.X + d.X, c.Y + d.Y, c.Z + d.Z));
                if (n.Y > h || !seen.Add(n))
                {
                    continue;
                }

                if (water.Contains(n))
                {
                    queue.Enqueue(n);
                    continue;
                }

                var gap = fromWater ? n : origin[c];
                if (!SettleCanRead(n))
                {
                    continue; // never generate terrain for a settle: an unloaded cell is a wall
                }

                if (doorCells.Count > 0 && doorCells.Contains(n))
                {
                    continue; // #2561: a doorway is a wall — and like every wall it never counts toward the cap
                }

                var b = _world.GetBlock(n);
                bool flowing = b.Value == _waterId && _fluidLevel.ContainsKey(n);
                if (b.Value == _waterId && !flowing)
                {
                    if (!fromWater && n.Y < h && IsEndlessSurface(n))
                    {
                        sinkGap = gap; // the surface of lower endless water, reached through the air: the layer drains into it
                        break;
                    }

                    continue; // other still water: a wall
                }

                if (!flowing && (!b.IsAir || InShipInterior(n)))
                {
                    continue; // solid — walls never count toward the cap, so a sealed winding body is not judged open
                }

                if (++explored > SettleExploreCap)
                {
                    sinkGap = gap; // more open room than we look at: the water is lost into it
                    if (fromWater)
                    {
                        enteredFrom[n] = c;
                    }

                    break;
                }

                if (fromWater)
                {
                    enteredFrom[n] = c;
                }

                if (HorizontalDistance(n, start) > runoff)
                {
                    sinkGap = gap; // past the runoff: the water seeps away
                    break;
                }

                origin[n] = gap; // air, or a flowing tongue (this pond's own stream): passable — only air is ever filled
                if (n.Y < h && !flowing)
                {
                    targets.Add(n);
                    useful.Add(gap);
                }

                queue.Enqueue(n);
            }
        }

        if (sinkGap is { } s)
        {
            useful.Add(s);
            (Vector3i, Vector3i)? outlet = enteredFrom.TryGetValue(s, out var w) ? (w, s) : null;
            return new LayerReach(true, targets, useful.Count, outlet);
        }

        targets.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        return new LayerReach(false, targets, useful.Count, null);
    }

    /// <summary>Lower water a draining layer may run into: the open surface of endless water — the sea, a river, a placed
    /// source — with air or a flowing tongue above it. Endless water buried under the pond itself (the part of a coastal
    /// pond below the sea line) is no sink: a hole dug into such a pond is filled, not drained through.</summary>
    private bool IsEndlessSurface(Vector3i cell)
    {
        if (_settling.Contains(cell) || ClassifyWater(cell) == WaterClass.Finite)
        {
            return false;
        }

        var above = FluidCell(new Vector3i(cell.X, cell.Y + 1, cell.Z));
        if (!SettleCanRead(above))
        {
            return false;
        }

        var block = _world.GetBlock(above);
        return block.IsAir || (block.Value == _waterId && _fluidLevel.ContainsKey(above));
    }

    /// <summary>Advances every running settle by its rate; re-plans each about once a second. Starts the fluid step's
    /// settle bookkeeping: the per-step plan budget and coverage, and the rest memo's once-a-second clear.</summary>
    private void StepSettles()
    {
        _settlePlansThisStep = 0;
        _settleCovered.Clear();
        var world = _worlds.Active;
        if (world.FluidStep >= world.SettleRestMemoStep + SettleRecheckSteps)
        {
            world.SettleRestMemoStep = world.FluidStep;
            world.SettleRestMemo.Clear();
            foreach (var deferred in world.SettleDeferred)
            {
                WakeFluidCell(deferred); // the held-back wakes get their turn now
            }

            world.SettleDeferred.Clear();
        }

        for (int j = _settles.Count - 1; j >= 0; j--)
        {
            var settle = _settles[j];
            if (world.FluidStep >= settle.NextCheck)
            {
                if (_settlePlansThisStep >= SettlePlansPerStep)
                {
                    settle.NextCheck = world.FluidStep + 1; // the step's planning budget is spent: renew it next step
                }
                else if (!Replan(settle))
                {
                    continue;
                }
            }

            _settleChunkKnown = false;
            bool ended = false;
            for (int n = 0; n < settle.Rate && (settle.Ai < settle.Add.Count || settle.Ri < settle.Remove.Count); n++)
            {
                if (settle.Ai < settle.Add.Count)
                {
                    var a = settle.Add[settle.Ai];
                    if (!SettleCanRead(a))
                    {
                        AbortSettle(settle); // its chunk is gone (a player moved away): never regenerate terrain for it (#1824)
                        ended = true;
                        break;
                    }

                    if (!_world.GetBlock(a).IsAir || InShipInterior(a) || FluidDoorCells.Contains(a))
                    {
                        // The cell filled up (or a door was hung in it, #2561) meanwhile: re-plan rather than lose the
                        // matching water.
                        settle.NextCheck = 0;
                        break;
                    }

                    settle.Ai++;
                    _world.SetBlock(a, new BlockId(_waterId));
                    BroadcastToWorld(new BlockChanged { X = a.X, Y = a.Y, Z = a.Z, Block = _waterId });
                    WakeAround(a); // what lies beside the filled cell — another pool, a source — sees the water arrive
                }

                if (settle.Ri < settle.Remove.Count)
                {
                    var r = settle.Remove[settle.Ri];
                    if (!SettleCanRead(r))
                    {
                        AbortSettle(settle);
                        ended = true;
                        break;
                    }

                    settle.Ri++;
                    if (_world.GetBlock(r).Value == _waterId && !_fluidLevel.ContainsKey(r))
                    {
                        _world.SetBlock(r, BlockId.Air);
                        BroadcastToWorld(new BlockChanged { X = r.X, Y = r.Y, Z = r.Z, Block = BlockId.AirValue });
                        if (settle.Drains && _drained.Add(r))
                        {
                            settle.Drained.Add(r);
                        }

                        WakeNeighbors(r);
                        OnSupportRemoved(r); // sand resting on the water drops with it (#1319)
                    }
                }
            }

            if (!ended && settle.Ai >= settle.Add.Count && settle.Ri >= settle.Remove.Count)
            {
                FinishSettle(settle);
            }
        }
    }

    /// <summary>Queues the six neighbours of a cell without reading them — the step parks what it cannot read and drops
    /// what is no fluid (the #2448 pattern).</summary>
    private void WakeAround(Vector3i p)
    {
        foreach (var d in SettleDirs)
        {
            WakeFluidCell(new Vector3i(p.X + d.X, p.Y + d.Y, p.Z + d.Z));
        }
    }

    /// <summary>Renews a running settle's plan from what is left of its body — the gap may have been plugged or widened.
    /// False when the settle ended (nothing left to move, or a cell of it can no longer be read).</summary>
    private bool Replan(FluidSettle settle)
    {
        _settlePlansThisStep++;
        foreach (var c in settle.Cells)
        {
            _settling.Remove(c);
        }

        _settleChunkKnown = false;
        var remaining = new List<Vector3i>();
        foreach (var c in settle.Cells)
        {
            if (!SettleCanRead(c))
            {
                EndSettle(settle); // a chunk of it is gone: end cleanly; the next wake settles what is left
                return false;
            }

            if (_world.GetBlock(c).Value == _waterId && !_fluidLevel.ContainsKey(c))
            {
                remaining.Add(c);
            }
        }

        remaining.Sort((a, b) => a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        FluidSettle? next = null;
        if (remaining.Count > 0)
        {
            long t0 = Stopwatch.GetTimestamp();
            var body = CollectStillBody(remaining[0], out var outcome, out int top, out int bottom, out int radius);
            if (outcome == BodyOutcome.Collected)
            {
                _settleCovered.UnionWith(body);
                next = PlanSettle(remaining[0], body, top, bottom, radius);
            }

            long dt = Stopwatch.GetTimestamp() - t0;
            _replans++;
            _replanTicksTotal += dt;
            _replanTicksMax = Math.Max(_replanTicksMax, dt);
        }

        if (next is null)
        {
            EndSettle(settle);
            return false;
        }

        settle.Cells.Clear();
        settle.Cells.UnionWith(next.Cells);
        settle.Outlets.Clear();
        foreach (var (w, o) in next.Outlets)
        {
            settle.Outlets[w] = o;
        }

        settle.Add = next.Add;
        settle.Remove = next.Remove;
        settle.Ai = settle.Ri = 0;
        settle.Rate = next.Rate;
        settle.Drains |= next.Drains;
        settle.NextCheck = _worlds.Active.FluidStep + SettleRecheckSteps;
        foreach (var c in settle.Cells)
        {
            _settling.Add(c);
        }

        foreach (var outlet in settle.Outlets.Keys)
        {
            WakeFluidCell(outlet);
        }

        return true;
    }

    private void FinishSettle(FluidSettle settle)
    {
        foreach (var c in settle.Cells)
        {
            _settling.Remove(c);
        }

        EndSettle(settle);
    }

    /// <summary>Ends a settle whose cells can no longer be read: its marks go, nothing unreadable is touched.</summary>
    private void AbortSettle(FluidSettle settle) => FinishSettle(settle);

    /// <summary>Drops a settle: its drained marks go, and everything it may have held back is woken once — what is left of
    /// its water (at rest unless something changed meanwhile), the fluid beside the cells it drained (an endless source or
    /// a stream that saw no air while the drain closed them), its stream's opening, and the cells that met it busy. Only
    /// readable cells are read; the rest are queued without reading.</summary>
    private void EndSettle(FluidSettle settle)
    {
        _settles.Remove(settle);
        _settleChunkKnown = false;
        foreach (var d in settle.Drained)
        {
            _drained.Remove(d);
        }

        foreach (var d in settle.Drained)
        {
            foreach (var dir in SettleDirs)
            {
                var n = FluidCell(new Vector3i(d.X + dir.X, d.Y + dir.Y, d.Z + dir.Z));
                if (!settle.Cells.Contains(n) && !_drained.Contains(n) && SettleCanRead(n) && IsFluid(_world.GetBlock(n).Value))
                {
                    WakeFluidCell(n);
                }
            }
        }

        foreach (var c in settle.Cells)
        {
            if (SettleCanRead(c) && _world.GetBlock(c).Value == _waterId && HasAirNeighbor(c))
            {
                WakeFluidCell(c);
            }
        }

        foreach (var (_, opening) in settle.Outlets)
        {
            WakeFluidCell(opening); // the stream loses its feed and dries up
        }

        var waiting = _worlds.Active.SettleWaiting;
        foreach (var w in waiting)
        {
            WakeFluidCell(w);
        }

        waiting.Clear();
    }
}
