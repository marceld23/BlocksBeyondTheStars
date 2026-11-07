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
/// <see cref="RessawCrackRules.LeakSeconds"/> more it breaks (<c>ressaw_leak</c>): the cell becomes the still liquid
/// <c>ressaw</c> and a puddle of at most <see cref="RessawCrackRules.MaxPuddleCells"/> cells spreads from it, face by face
/// in a fixed order, into cells on the SAME level that hold air and stand on a solid block — never into a solid block,
/// never above the source, never into a cell a body stands in (nobody is soaked by surprise). A cracked block somebody
/// stands on does not break under their feet: it waits until they step off (and stay near).</para>
/// <para>Every change is an ordinary block edit (<see cref="ServerWorld.SetBlock"/>: saved as a delta row) broadcast as a
/// <see cref="BlockChanged"/> flagged <see cref="BlockChanged.Moved"/> (no mining crumble: the sound comes as its own
/// <see cref="SoundFx"/>). The clocks are not saved: a restart only restarts them.</para>
/// <para>Protection: a cell is cracked only for a player who could build there — the bases and factories of others, a
/// parked ship and a station stay whole (the lingering player is the actor, as for a mined block). A settlement's own
/// protection is left out on purpose: the buried G.D.S. ruins may lie under the city, inside its protected depth, and
/// their Ressaw blocks must still crack.</para>
/// <para>Cost: per occupied world one look every 0.5 s — 100 cached-chunk reads per player on foot (no chunk is loaded by
/// the look), a dictionary entry per Ressaw block near a player (dropped once nobody is near). A world makes at most
/// <see cref="RessawCrackRules.MaxChangesPerBeat"/> cracks / leaks per beat, a leak at most 1 + 5 block edits.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The per-world dwell clocks (kept with the world, dropped with it).</summary>
    private sealed class RessawCrackClocks
    {
        public double SinceBeat;
        public Dictionary<Vector3i, double> Dwell { get; } = new();
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
            LeakRessaw(cell, actor);
            return true;
        }

        return false;
    }

    /// <summary>The cracked block breaks: its cell becomes Ressaw and the puddle spreads (see the class summary).</summary>
    private void LeakRessaw(Vector3i source, string actor)
    {
        WriteRessawCell(source, _ressawCrackLiquidId);
        OnSupportRemoved(source); // loose sand above sinks into it, a plant that stood on the block pops
        int circ = _world.Circumference;
        int placed = 0;
        var visited = new HashSet<Vector3i> { source };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(source);
        while (queue.Count > 0 && placed < RessawCrackRules.MaxPuddleCells)
        {
            var at = queue.Dequeue();
            foreach (var face in RessawCrackRules.SpillFaces)
            {
                var n = WorldConstants.CanonicalBlock(at + face, circ);
                if (placed >= RessawCrackRules.MaxPuddleCells || !visited.Add(n) || !RessawMaySpillInto(n, actor))
                {
                    continue;
                }

                WriteRessawCell(n, _ressawCrackLiquidId);
                placed++;
                queue.Enqueue(n);
            }
        }

        PlayRessawSound(RessawCrackRules.LeakSound, source);
    }

    /// <summary>A puddle cell: air, on a solid floor, nobody in it, and a cell the lingering player may build in.</summary>
    private bool RessawMaySpillInto(Vector3i cell, string actor)
    {
        if (!_world.GetBlock(cell).IsAir)
        {
            return false; // never replaces a block — a solid one, a plant or another liquid
        }

        var floor = _world.Definition(_world.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)));
        return floor is { Solid: true, Liquid: false } && !CellOccupiedByBody(cell) && !RessawCrackProtected(cell, actor);
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
}
