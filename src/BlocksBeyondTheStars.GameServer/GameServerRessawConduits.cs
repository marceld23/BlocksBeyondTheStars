// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3340: the Ressaw conduit. It is a crystal conduit in every respect — a <see cref="CrystalDeviceKind.Conduit"/> cell whose
/// block key is <see cref="CrystalNetRules.RessawConduitBlockKey"/>: it joins the net, carries its ON/OFF and glows while ON
/// — and a network that holds at least one may grow to <see cref="CrystalNetRules.MaxCellsPerRessawNet"/> (512) cells
/// instead of <see cref="CrystalNetRules.MaxCellsPerNet"/> (256). The net stays one bit (ADR 0013); only the size cap grows.
/// A ship's own net keeps <see cref="CrystalNetRules.MaxShipCellsPerNet"/>, Ressaw or not.
/// <para>The cap is checked where a cell joins (<c>JoinCrystalNet</c>), against the network the join would make
/// (<see cref="CrystalCellCapFor"/>). A join is incremental — the cap sees the cells that are already in, never the ones still
/// to come — so three paths keep a network's cap a function of its whole shape instead of the order its cells arrived in:</para>
/// <list type="bullet">
/// <item><b>Load.</b> The rows come back in the store's order (by coordinate): a 400-cell net whose Ressaw conduit sits at its
/// far end would be cut at 256 before the conduit is read. <see cref="RessawReachOfRows"/> floods the stored rows once (the
/// same faces a join uses: gates are no members, a watcher's eye is no wire), and every cell of a stored component that
/// holds a Ressaw conduit joins under the larger cap.</item>
/// <item><b>Placing a Ressaw conduit</b> into a network that is already full re-tries the inert cells beside it, wave by
/// wave (<see cref="ReviveBesideRessawConduit"/>): the conduits placed over the cap join now, not after the next reload.</item>
/// <item><b>Mining the last Ressaw conduit</b> out of a network over 256 cells trims it back
/// (<see cref="TrimAfterRessawConduitLeft"/>): the 256 cells nearest its oldest cell stay, the rest go inert as if they had
/// been placed over the cap — and come back when a Ressaw conduit is laid into the network again.</item>
/// </list>
/// <para>Cost: nothing on the beats. The check walks the merged network (at most 512 lookups) only on a join that would pass
/// 256 cells; the load flood is one pass over the rows and runs only when a Ressaw conduit row exists; revive and trim run
/// only when a Ressaw conduit is placed or mined, bounded by the 512 cap. A larger network costs the 100 ms logic beat what
/// its cells cost anyway — the beat walks the world's cell table, not the networks — so the world's worst case grows from
/// 64 × 256 to 64 × 512 registered cells (each a few field reads per beat), and the <c>CrystalNetList</c> a level change
/// broadcasts carries three ints per net cell (a full Ressaw net about 1.5 k ints).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3340: while <c>LoadCrystalNet</c> registers the rows, the stored cells that share a component with a Ressaw
    /// conduit row; null outside a load (and during a load of a world without one).</summary>
    private HashSet<Vector3i>? _crystalRessawLoadReach;

    /// <summary>The cell cap a join into <paramref name="neighbours"/> (making a network of <paramref name="total"/> cells) is
    /// checked against: a ship's own cap aboard; 256 while the merged network stays within it (no walk); otherwise 512 when the
    /// joining cell is a Ressaw conduit or one of the merged networks holds one — during a load, when the cell's stored
    /// component holds one.</summary>
    private int CrystalCellCapFor(ServerCrystalCell cell, List<CrystalNetwork> neighbours, int total)
    {
        if (_crystalFrame is not null)
        {
            return CrystalNetRules.MaxShipCellsPerNet; // a ship is small: its own cap, Ressaw or not
        }

        if (total <= CrystalNetRules.MaxCellsPerNet)
        {
            return CrystalNetRules.MaxCellsPerNet;
        }

        if (_crystalRessawLoadReach is { } reach)
        {
            return CrystalNetRules.MaxCellsFor(reach.Contains(cell.Cell));
        }

        return CrystalNetRules.MaxCellsFor(IsRessawConduitCell(cell) || neighbours.Any(NetHoldsRessawConduit));
    }

    private static bool IsRessawConduitCell(ServerCrystalCell c) => c.IsConduit && CrystalNetRules.IsRessawConduitKey(c.BlockKey);

    /// <summary>Whether a network holds a Ressaw conduit (an inert one is no member of any network).</summary>
    private bool NetHoldsRessawConduit(CrystalNetwork net)
    {
        foreach (var pos in net.Cells)
        {
            if (CrystalNet.Cells.TryGetValue(pos, out var c) && IsRessawConduitCell(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The stored cells that share a component with a Ressaw conduit row, flooded over the rows alone (no chunk is
    /// touched); null when no row is a Ressaw conduit — the load then runs exactly as before.</summary>
    private static HashSet<Vector3i>? RessawReachOfRows(IReadOnlyList<StoredCrystalCell> rows)
    {
        var members = new Dictionary<Vector3i, StoredCrystalCell>();
        var queue = new Queue<Vector3i>();
        foreach (var row in rows)
        {
            if (!Enum.TryParse<CrystalDeviceKind>(row.Kind, out var kind) || kind == CrystalDeviceKind.None || CrystalNetRules.IsGate(kind))
            {
                continue; // a gate is no member of any network
            }

            var pos = new Vector3i(row.X, row.Y, row.Z);
            members[pos] = row;
            if (kind == CrystalDeviceKind.Conduit && CrystalNetRules.IsRessawConduitKey(CrystalConfigValue(row.Config, "key")))
            {
                queue.Enqueue(pos);
            }
        }

        if (queue.Count == 0)
        {
            return null;
        }

        var reach = new HashSet<Vector3i>(queue);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = at + face;
                if (members.ContainsKey(n) && !RowEyeBetween(members, at, n) && reach.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return reach;
    }

    /// <summary>The rows' twin of <c>CrystalEyeBetween</c> (#3092): a stored watcher looking at the other cell.</summary>
    private static bool RowEyeBetween(Dictionary<Vector3i, StoredCrystalCell> members, Vector3i a, Vector3i b)
        => RowWatcherLooksAt(members, a, b) || RowWatcherLooksAt(members, b, a);

    private static bool RowWatcherLooksAt(Dictionary<Vector3i, StoredCrystalCell> members, Vector3i watcher, Vector3i cell)
        => members.TryGetValue(watcher, out var row) && row.Kind == nameof(CrystalDeviceKind.Watcher)
           && watcher + CrystalNetRules.OutputFace(CrystalNetRules.ClampYaw(CrystalConfigInt(row.Config, "yaw", 0))) == cell;

    /// <summary>A Ressaw conduit at <paramref name="pos"/> just joined a network: the inert cells touching that network (placed
    /// over the 256 cap before) re-try their join, wave by wave so a chain of them rejoins in order. Each re-runs every cap
    /// (<c>OverCrystalCap</c>, then the join's own): a cell still over one stays inert. The rows are unchanged — they never
    /// stored the inert flag.</summary>
    private void ReviveBesideRessawConduit(Vector3i pos)
    {
        var state = CrystalNet;
        if (_crystalFrame is not null || !state.Cells.TryGetValue(pos, out var placed) || placed.Inert || !IsRessawConduitCell(placed)
            || !state.Nets.TryGetValue(placed.NetId, out var net))
        {
            return;
        }

        var queue = new Queue<ServerCrystalCell>();
        var seen = new HashSet<Vector3i>();
        foreach (var member in net.Cells)
        {
            EnqueueInertBeside(member, queue, seen);
        }

        int tries = 0;
        while (queue.Count > 0 && tries++ < CrystalNetRules.MaxCellsPerRessawNet)
        {
            var c = queue.Dequeue();
            if (!c.Inert || !state.Cells.TryGetValue(c.Cell, out var current) || current != c)
            {
                continue;
            }

            c.Inert = OverCrystalCap(c);
            if (!c.Inert)
            {
                JoinCrystalNet(c); // may leave it inert again: the network is full at 512
            }

            if (c.Inert)
            {
                continue;
            }

            if (c.Kind == CrystalDeviceKind.LiftMotor)
            {
                LiftOf(c); // as a registration does for a live motor
            }

            state.DeviceListDirty = true;
            state.NetListDirty = true;
            EnqueueInertBeside(c.Cell, queue, seen);
        }
    }

    private void EnqueueInertBeside(Vector3i pos, Queue<ServerCrystalCell> queue, HashSet<Vector3i> seen)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            var n = pos + face;
            if (CrystalNet.Cells.TryGetValue(n, out var c) && c.Inert && !c.IsGate && !CrystalEyeBetween(pos, n) && seen.Add(n))
            {
                queue.Enqueue(c);
            }
        }
    }

    /// <summary>A Ressaw conduit at <paramref name="pos"/> was mined: every network beside it that is now over 256 cells and
    /// holds no other Ressaw conduit is trimmed back (<see cref="TrimCrystalNet"/>).</summary>
    private void TrimAfterRessawConduitLeft(Vector3i pos, ServerCrystalCell removed)
    {
        if (_crystalFrame is not null || !IsRessawConduitCell(removed))
        {
            return;
        }

        var state = CrystalNet;
        foreach (var face in CrystalNetRules.Faces)
        {
            if (state.Cells.TryGetValue(pos + face, out var c) && c.NetId != 0 && state.Nets.TryGetValue(c.NetId, out var net)
                && net.Cells.Count > CrystalNetRules.MaxCellsPerNet && !NetHoldsRessawConduit(net))
            {
                TrimCrystalNet(net);
            }
        }
    }

    /// <summary>Cuts a network back to <see cref="CrystalNetRules.MaxCellsPerNet"/> cells: the first 256 a flood from its oldest
    /// cell reaches stay (they stay connected), every other cell goes inert like a cell placed over the cap — its actuator
    /// lets go (a dark lamp lights up, a held sentry fires again, a lift motor drops its platform), and it no longer holds a
    /// spout closed, a gate open or a sentry or heal tank off.</summary>
    private void TrimCrystalNet(CrystalNetwork net)
    {
        var state = CrystalNet;
        ServerCrystalCell? oldest = null;
        foreach (var p in net.Cells)
        {
            if (state.Cells.TryGetValue(p, out var c) && (oldest is null || c.Id < oldest.Id))
            {
                oldest = c;
            }
        }

        if (oldest is null)
        {
            return;
        }

        var keep = new HashSet<Vector3i> { oldest.Cell };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(oldest.Cell);
        while (queue.Count > 0 && keep.Count < CrystalNetRules.MaxCellsPerNet)
        {
            var at = queue.Dequeue();
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = at + face;
                if (keep.Count < CrystalNetRules.MaxCellsPerNet && net.Cells.Contains(n) && !CrystalEyeBetween(at, n) && keep.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        foreach (var p in net.Cells.Where(p => !keep.Contains(p)).ToList())
        {
            net.Cells.Remove(p);
            if (!state.Cells.TryGetValue(p, out var c))
            {
                continue;
            }

            c.NetId = 0;
            c.Inert = true;
            StopCrystalActuator(c, relight: true);
            state.ClosedSpouts.Remove(p);
            state.OpenGates.Remove(p);
            state.DisabledSentries.Remove(p);
            state.DisabledHealTanks.Remove(p);
            state.OpenFences.Remove(p);
        }

        state.DeviceListDirty = true;
        state.NetListDirty = true;
    }
}
