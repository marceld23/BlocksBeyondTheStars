// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3336: Ressaw blocks crack and leak when a player lingers next to them (the numbers: <see cref="RessawCrackRules"/>).
/// <para>Every <see cref="RessawCrackRules.BeatSeconds"/> the cells around each player on foot are looked at — a 5 × 5
/// square around the feet column, from the floor under the feet to the cell above the head. A <c>ressaw_block</c> or a
/// <c>ressaw_block_cracked</c> found there keeps a dwell clock in memory; a cell nobody stands near on a beat loses its
/// clock (walking past starts it over every time, so it never cracks anything). After
/// <see cref="RessawCrackRules.CrackSeconds"/> the block cracks (<c>ressaw_crack</c> sound); after
/// <see cref="RessawCrackRules.LeakSeconds"/> more it breaks (<c>ressaw_leak</c>): the cell becomes the liquid <c>ressaw</c>
/// and the Ressaw <b>flows out</b> (#3433, a running <see cref="RessawLeak"/>): every
/// <see cref="RessawCrackRules.FlowSeconds"/> it spreads one cell further across the floor, face by face in a fixed order,
/// and a cell with air below it falls one cell per step until it lands — over a step, down a hole, into the room below —
/// without leaving a hanging column behind. At most <see cref="RessawCrackRules.MaxPuddleCells"/> cells pour out besides the
/// broken block's own; the leak ends when they are spent and nothing falls any more, or after
/// <see cref="RessawCrackRules.MaxLeakSteps"/> steps (a drop still in the air then — a bottomless shaft — runs dry). It flows
/// only into air — never into a solid block, a plant or another liquid, never up, never into a cell a body stands in (a drop
/// above someone waits: nobody is soaked by surprise), a ship, a doorway or an unloaded chunk. A cracked block somebody
/// stands on does not break under their feet: it waits until they step off (and stay near). The generated Ressaw pools of
/// the ruins are not leaks: they stay still.</para>
/// <para>Every change is an ordinary block edit (<see cref="ServerWorld.SetBlock"/>: saved as a delta row) broadcast as a
/// <see cref="BlockChanged"/> flagged <see cref="BlockChanged.Moved"/> (no mining crumble: the sound comes as its own
/// <see cref="SoundFx"/>). The clocks and the running leaks are not saved: a restart restarts the clocks and freezes a leak
/// where it is.</para>
/// <para>Protection: a cell is cracked only for a player who could build there — the bases and factories of others, a
/// parked ship and a station stay whole (the lingering player is the actor, as for a mined block). A settlement's own
/// protection is left out on purpose: the buried G.D.S. ruins may lie under the city, inside its protected depth, and
/// their Ressaw blocks must still crack.</para>
/// <para>Cost: per occupied world one look every 0.5 s — 100 cached-chunk reads per player on foot (no chunk is loaded by
/// the look), a dictionary entry per Ressaw block near a player (dropped once nobody is near). A world makes at most
/// <see cref="RessawCrackRules.MaxChangesPerBeat"/> cracks / leaks per beat; a running leak writes at most its
/// <see cref="RessawCrackRules.MaxPuddleCells"/> new cells plus two edits per falling cell a step, for at most
/// <see cref="RessawCrackRules.MaxLeakSteps"/> steps.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The per-world dwell clocks and running leaks (kept with the world, dropped with it).</summary>
    private sealed class RessawCrackClocks
    {
        public double SinceBeat;
        public double SinceFlow;
        public Dictionary<Vector3i, double> Dwell { get; } = new();
        public List<RessawLeak> Leaks { get; } = new();
    }

    /// <summary>#3433: one broken block's Ressaw on its way out (see the class summary).</summary>
    private sealed class RessawLeak
    {
        public RessawLeak(string actor, int volume)
        {
            Actor = actor;
            Volume = volume;
        }

        /// <summary>The lingering player who broke it — the flow goes only where they could build.</summary>
        public string Actor { get; }

        /// <summary>The cells still to pour out.</summary>
        public int Volume { get; set; }

        /// <summary>The flow steps taken.</summary>
        public int Steps { get; set; }

        /// <summary>Cells with air below: each drops one cell a step until it lands.</summary>
        public List<Vector3i> Falling { get; set; } = new();

        /// <summary>Cells on a floor that spread sideways on the next step.</summary>
        public List<Vector3i> Front { get; set; } = new();
    }

    private readonly ConditionalWeakTable<LoadedWorld, RessawCrackClocks> _ressawCrackClocks = new();

    /// <summary>Reused every beat: the Ressaw cells near a player, with the player who stands there (the actor).</summary>
    private readonly Dictionary<Vector3i, string> _ressawCrackNear = new();
    private readonly List<Vector3i> _ressawCrackGone = new();

    private bool _ressawCrackIdsResolved;
    private ushort _ressawCrackBlockId;
    private ushort _ressawCrackedId;
    private ushort _ressawCrackLiquidId;

    private void TickRessawBlocks(double dt)
    {
        var world = _worlds.Active;
        if (world is null)
        {
            return;
        }

        var clocks = _ressawCrackClocks.GetValue(world, _ => new RessawCrackClocks());
        StepRessawLeaks(clocks, dt); // #3433: the flow keeps its own, quicker pace
        clocks.SinceBeat += dt;
        if (clocks.SinceBeat < RessawCrackRules.BeatSeconds - 1e-9) // five 0.1 s ticks are one beat, rounding or not
        {
            return;
        }

        double step = clocks.SinceBeat;
        clocks.SinceBeat = 0;
        if (!ResolveRessawCrackIds())
        {
            return;
        }

        var near = _ressawCrackNear;
        near.Clear();
        foreach (var session in JoinedInActiveWorld())
        {
            if (!session.Spectating && !session.State.InEva && session.State.Health > 0f)
            {
                CollectRessawBlocksNear(session, near);
            }
        }

        ForgetRessawClocksNobodyIsNear(clocks, near);
        int budget = RessawCrackRules.MaxChangesPerBeat;
        foreach (var pair in near)
        {
            double t = clocks.Dwell.TryGetValue(pair.Key, out double was) ? was + step : 0; // the first look starts the clock
            clocks.Dwell[pair.Key] = t;
            if (budget > 0 && AdvanceRessawBlock(clocks, pair.Key, pair.Value, t))
            {
                budget--;
            }
        }
    }

    private bool ResolveRessawCrackIds()
    {
        if (!_ressawCrackIdsResolved)
        {
            _ressawCrackIdsResolved = true;
            _ressawCrackBlockId = _content.GetBlock(RessawCrackRules.BlockKey)?.NumericId.Value ?? 0;
            _ressawCrackedId = _content.GetBlock(RessawCrackRules.CrackedKey)?.NumericId.Value ?? 0;
            _ressawCrackLiquidId = _content.GetBlock(RessawCrackRules.LiquidKey)?.NumericId.Value ?? 0;
        }

        return _ressawCrackBlockId != 0 && _ressawCrackedId != 0 && _ressawCrackLiquidId != 0;
    }

    /// <summary>The intact and cracked Ressaw blocks within reach of one player (loaded chunks only), each with the first
    /// player found near it who may build there.</summary>
    private void CollectRessawBlocksNear(PlayerSession session, Dictionary<Vector3i, string> near)
    {
        var feet = session.State.Position.ToBlock();
        string actor = session.State.PlayerId;
        int circ = _world.Circumference;
        for (int dy = -RessawCrackRules.ReachBelow; dy <= RessawCrackRules.ReachAbove; dy++)
        {
            for (int dx = -RessawCrackRules.Reach; dx <= RessawCrackRules.Reach; dx++)
            {
                for (int dz = -RessawCrackRules.Reach; dz <= RessawCrackRules.Reach; dz++)
                {
                    var cell = new Vector3i(feet.X + dx, feet.Y + dy, feet.Z + dz);
                    ushort id = _world.GetBlockIfLoaded(cell).Value;
                    if (id != _ressawCrackBlockId && id != _ressawCrackedId)
                    {
                        continue;
                    }

                    var key = WorldConstants.CanonicalBlock(cell, circ);
                    if (!near.ContainsKey(key) && !RessawCrackProtected(key, actor))
                    {
                        near[key] = actor;
                    }
                }
            }
        }
    }

    /// <summary>A cell nobody stands near on this beat starts over: the clocks hold only the cells seen right now.</summary>
    private void ForgetRessawClocksNobodyIsNear(RessawCrackClocks clocks, Dictionary<Vector3i, string> near)
    {
        if (clocks.Dwell.Count == 0)
        {
            return;
        }

        _ressawCrackGone.Clear();
        foreach (var cell in clocks.Dwell.Keys)
        {
            if (!near.ContainsKey(cell))
            {
                _ressawCrackGone.Add(cell);
            }
        }

        foreach (var cell in _ressawCrackGone)
        {
            clocks.Dwell.Remove(cell);
        }
    }

    /// <summary>The bases and factories of others, a parked ship and a station — see the class summary for why a settlement's
    /// protection is not on the list.</summary>
    private bool RessawCrackProtected(Vector3i cell, string actor)
        => IsShipBlock(cell) || IsStationBlock(cell) || IsFactoryProtected(cell, actor, false) || IsBaseProtected(cell, actor, false);

    /// <summary>Cracks an intact block whose clock ran out, or breaks a cracked one. True when the cell changed.</summary>
    private bool AdvanceRessawBlock(RessawCrackClocks clocks, Vector3i cell, string actor, double seconds)
    {
        ushort id = _world.GetBlockIfLoaded(cell).Value;
        if (id == _ressawCrackBlockId && seconds >= RessawCrackRules.CrackSeconds)
        {
            var (tint, glow) = _world.GetModifier(cell);
            WriteRessawCell(cell, _ressawCrackedId, tint, glow, _world.GetShape(cell)); // a dyed or formed block cracks as it is
            clocks.Dwell[cell] = 0; // the leak takes its own few seconds
            PlayRessawSound(RessawCrackRules.CrackSound, cell);
            return true;
        }

        if (id == _ressawCrackedId && seconds >= RessawCrackRules.LeakSeconds)
        {
            if (CellOccupiedByBody(new Vector3i(cell.X, cell.Y + 1, cell.Z)))
            {
                return false; // a block never breaks under someone's feet: it leaks once they step off (and stay near)
            }

            clocks.Dwell.Remove(cell);
            LeakRessaw(clocks, cell, actor);
            return true;
        }

        return false;
    }

    /// <summary>The cracked block breaks: its cell becomes Ressaw and a leak starts that lets it flow out over the next
    /// steps (#3433, see the class summary).</summary>
    private void LeakRessaw(RessawCrackClocks clocks, Vector3i source, string actor)
    {
        WriteRessawCell(source, _ressawCrackLiquidId);
        OnSupportRemoved(source); // loose sand above sinks into it, a plant that stood on the block pops
        var leak = new RessawLeak(actor, RessawCrackRules.MaxPuddleCells);
        (RessawHasFloor(source) ? leak.Front : leak.Falling).Add(source);
        clocks.Leaks.Add(leak);
        PlayRessawSound(RessawCrackRules.LeakSound, source);
    }

    /// <summary>#3433: one flow step of every running leak, every <see cref="RessawCrackRules.FlowSeconds"/>; a finished
    /// leak is dropped.</summary>
    private void StepRessawLeaks(RessawCrackClocks clocks, double dt)
    {
        if (clocks.Leaks.Count == 0)
        {
            clocks.SinceFlow = 0;
            return;
        }

        clocks.SinceFlow += dt;
        if (clocks.SinceFlow < RessawCrackRules.FlowSeconds - 1e-9)
        {
            return;
        }

        clocks.SinceFlow = 0;
        int kept = 0;
        for (int i = 0; i < clocks.Leaks.Count; i++)
        {
            var leak = clocks.Leaks[i];
            if (StepRessawLeak(leak))
            {
                clocks.Leaks[kept++] = leak;
            }
        }

        clocks.Leaks.RemoveRange(kept, clocks.Leaks.Count - kept);
    }

    /// <summary>One step of a leak: the falling cells drop (or land, or wait above a body), then the landed cells spread one
    /// cell sideways while the leak has Ressaw left. True while it still runs.</summary>
    private bool StepRessawLeak(RessawLeak leak)
    {
        leak.Steps++;
        var falling = new List<Vector3i>();
        var front = new List<Vector3i>();
        foreach (var cell in leak.Falling)
        {
            DropRessaw(leak, cell, falling, front);
        }

        int circ = _world.Circumference;
        foreach (var cell in leak.Front)
        {
            if (_world.GetBlockIfLoaded(cell).Value != _ressawCrackLiquidId)
            {
                continue; // pumped up or built over meanwhile
            }

            foreach (var face in RessawCrackRules.SpillFaces)
            {
                var n = WorldConstants.CanonicalBlock(cell + face, circ);
                if (leak.Volume <= 0 || !RessawMayFlowInto(n, leak.Actor))
                {
                    continue;
                }

                WriteRessawCell(n, _ressawCrackLiquidId);
                leak.Volume--;
                (RessawHasFloor(n) ? front : falling).Add(n);
            }
        }

        leak.Falling = falling;
        leak.Front = front;
        bool running = falling.Count > 0 || (front.Count > 0 && leak.Volume > 0);
        if (running && leak.Steps >= RessawCrackRules.MaxLeakSteps)
        {
            foreach (var cell in falling)
            {
                DryRessawDrop(cell); // still in the air at the end — a bottomless shaft, or someone who never moved
            }

            return false;
        }

        return running;
    }

    /// <summary>A falling cell of a leak: moves one cell down into open air, waits above a body standing there, runs dry
    /// above any other cell it may not enter (a ship, a doorway, someone else's base, an unloaded chunk), or lands on what
    /// is below it and joins the front.</summary>
    private void DropRessaw(RessawLeak leak, Vector3i cell, List<Vector3i> falling, List<Vector3i> front)
    {
        if (_world.GetBlockIfLoaded(cell).Value != _ressawCrackLiquidId)
        {
            return; // pumped up or built over meanwhile
        }

        if (RessawHasFloor(cell))
        {
            front.Add(cell); // landed
            return;
        }

        var below = WorldConstants.CanonicalBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z), _world.Circumference);
        if (RessawMayFlowInto(below, leak.Actor))
        {
            WriteRessawCell(cell, BlockId.AirValue);
            WriteRessawCell(below, _ressawCrackLiquidId);
            falling.Add(below);
        }
        else if (CellOccupiedByBody(below))
        {
            falling.Add(cell); // somebody stands below: it waits until they step away
        }
        else
        {
            DryRessawDrop(cell);
        }
    }

    /// <summary>A drop that cannot go on runs dry: its cell becomes air again.</summary>
    private void DryRessawDrop(Vector3i cell)
    {
        if (_world.GetBlockIfLoaded(cell).Value == _ressawCrackLiquidId)
        {
            WriteRessawCell(cell, BlockId.AirValue);
        }
    }

    /// <summary>Whether a Ressaw cell rests on something — a block, a plant or a liquid in a loaded chunk. Over air it falls;
    /// over an unloaded chunk it counts as falling too, so <see cref="DropRessaw"/> lets it run dry instead of leaving it
    /// hanging above a chunk nobody can see.</summary>
    private bool RessawHasFloor(Vector3i cell)
    {
        var below = new Vector3i(cell.X, cell.Y - 1, cell.Z);
        return _world.IsNeighbourhoodLoaded(below, 0) && !_world.GetBlockIfLoaded(below).IsAir;
    }

    /// <summary>A cell the flow may enter: open air in a loaded chunk (never a block — a solid one, a plant or another
    /// liquid), nobody in it, outside a parked ship and a doorway, and a cell the lingering player may build in.</summary>
    private bool RessawMayFlowInto(Vector3i cell, string actor)
    {
        if (!_world.IsNeighbourhoodLoaded(cell, 0) || !_world.GetBlockIfLoaded(cell).IsAir)
        {
            return false;
        }

        return !InShipInterior(cell) && !FluidDoorCells.Contains(WorldConstants.CanonicalBlock(cell, _world.Circumference))
            && !CellOccupiedByBody(cell) && !RessawCrackProtected(cell, actor);
    }

    /// <summary>An ordinary block edit (saved as a delta row, no owner: nobody built it) broadcast to the world.</summary>
    private void WriteRessawCell(Vector3i cell, ushort block, int tint = 0, int glow = 0, int shape = 0)
    {
        _world.SetBlock(cell, new BlockId(block), tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block, Tint = tint, Glow = glow, Shape = shape, Moved = true });
    }

    private void PlayRessawSound(string soundId, Vector3i cell)
        => BroadcastToWorld(new SoundFx { SoundId = soundId, X = cell.X + 0.5f, Y = cell.Y + 0.5f, Z = cell.Z + 0.5f });

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3336): runs only the crack beat — no creature can wander into a puddle cell.</summary>
    public void TickRessawBlocksForTest(double dt) => TickRessawBlocks(dt);

    /// <summary>Test seam (#3336): the Ressaw cells of the active world whose dwell clock runs.</summary>
    public int RessawCrackClockCountForTest
        => _worlds.Active is { } w && _ressawCrackClocks.TryGetValue(w, out var clocks) ? clocks.Dwell.Count : 0;

    /// <summary>Test seam (#3433): the leaks of the active world still flowing.</summary>
    public int RessawLeakCountForTest
        => _worlds.Active is { } w && _ressawCrackClocks.TryGetValue(w, out var clocks) ? clocks.Leaks.Count : 0;
}
