// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The watcher (#2050, #3092): a source that gives a 0.5 s pulse whenever what stands in front of it changes.
/// <para><b>What it sees.</b> A block change in the cell in front of it (mined, placed, a crop that ripened, a lamp that
/// swapped) arrives as an event from the world (<see cref="OnCrystalWatchedCellChanged"/>). Since #3092 it also sees a
/// <b>device change its state</b> there — a switch flipped, a button pressed, a sensor or a gate switching, a machine
/// reporting "blocked", a wire changing its level, a sentry the net holds or lets go — and a <b>door</b> in that cell
/// opening or closing, and a <b>sentry post</b> starting or stopping to shoot (wired or not). None of these is a block
/// change, so before #3092 the watcher never noticed them. They happen in many places (sensors, gates, machines,
/// statuses), so instead of a hook at each of them every watcher compares what it sees once per logic beat
/// (<see cref="CrystalWatcherBeat"/>): a dictionary lookup per watcher, nothing per cell or per block.</para>
/// <para><b>Its eye is no wire.</b> The face a watcher looks through (its arrow) does not connect: what stands in front
/// of it stays on a network of its own. Otherwise every device a watcher looked at would be on the watcher's own
/// network — a sentry it watched would hold its fire while the watcher was quiet, a door would be locked, a lamp would
/// follow the watcher's own pulses and set it off again. A watcher never reacts to its own network either (a wire run
/// round to the cell it watches), so it cannot become a clock by accident.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A watcher that has not looked yet (fresh, reloaded or turned): its first beat only remembers.</summary>
    private const int CrystalEyeUnknown = int.MinValue;

    /// <summary>#3092: whether the face between two neighbouring cells is a watcher's eye — a watcher on either cell that
    /// looks at the other. That face is no wire: no network joins through it, no port registers through it, no door is
    /// locked through it.</summary>
    private bool CrystalEyeBetween(Vector3i a, Vector3i b) => WatcherLooksAt(a, b) || WatcherLooksAt(b, a);

    private bool WatcherLooksAt(Vector3i watcher, Vector3i cell)
        => CrystalNet.Cells.TryGetValue(watcher, out var w) && w.Kind == CrystalDeviceKind.Watcher
           && w.Cell + CrystalNetRules.OutputFace(w.Yaw) == cell;

    /// <summary>#2050: a watcher pulses when the cell in front of it changes — a mined block, a placed one, a crop that
    /// ripened, a lamp that swapped. Doors, devices and sentries are compared on the logic beat (#3092).</summary>
    private void OnCrystalWatchedCellChanged(Vector3i changed)
    {
        var state = _worlds.Active.CrystalNet;
        if (state.Cells.Count == 0)
        {
            return;
        }

        if (state.ShootingSentries.Count > 0)
        {
            state.ShootingSentries.Remove(changed); // a mined post is not shooting any more
        }

        foreach (var face in CrystalNetRules.Faces)
        {
            if (state.Cells.TryGetValue(changed + face, out var c) && c.Kind == CrystalDeviceKind.Watcher && !c.Inert
                && c.Cell + CrystalNetRules.OutputFace(c.Yaw) == changed)
            {
                PulseCrystalCell(c);
            }
        }
    }

    /// <summary>#3092: every watcher compares what it sees in front of it with the last beat and pulses on a change. Cost:
    /// one lookup per watcher (plus a pass over the doors for a watcher that looks at no net cell, as the Device Eye does).</summary>
    private void CrystalWatcherBeat()
    {
        var state = CrystalNet;
        if (state.Watchers.Count == 0)
        {
            return;
        }

        foreach (var at in state.Watchers)
        {
            if (!state.Cells.TryGetValue(at, out var w) || w.Inert)
            {
                continue;
            }

            int seen = CrystalWatchedState(w);
            if (w.EyeState != CrystalEyeUnknown && seen != w.EyeState)
            {
                PulseCrystalCell(w);
            }

            w.EyeState = seen;
        }
    }

    /// <summary>What a watcher sees in the cell in front of it, as a few bits: a device's own state (a switch's lever, a
    /// sensor, a gate, a status such as "has a target" or "blocked"), the level of the wire that cell is on, whether the
    /// net holds a sentry / spout / heal tank there, a sentry post shooting, a door standing open.</summary>
    private int CrystalWatchedState(ServerCrystalCell w)
    {
        var state = CrystalNet;
        var eye = w.Cell + CrystalNetRules.OutputFace(w.Yaw);
        int seen = state.ShootingSentries.Contains(eye) ? 1 : 0;
        if (state.Cells.TryGetValue(eye, out var d) && !d.Inert)
        {
            if (d.NetId != 0 && d.NetId == w.NetId)
            {
                return 0; // a wire run round to the watched cell: its own network is never its own trigger
            }

            if (!d.IsConduit && d.Output)
            {
                seen |= 2;
            }

            if (d.NetId != 0 && state.Nets.TryGetValue(d.NetId, out var net) && net.Level)
            {
                seen |= 4;
            }

            if (CrystalDeviceHeld(d))
            {
                seen |= 8;
            }
        }
        else if (_doors.Count > 0 && CrystalDoorAt(eye) is { } door && door.Open)
        {
            seen |= 16;
        }

        return seen;
    }

    /// <summary>#3092: a watcher turned with its menu's Turn button — its eye moved. It leaves its network and joins again
    /// through its new five wire faces; a port it now looks at, held only by the side that became its eye, leaves the net
    /// (a lamp lights up again), and a port on the side it looked through before joins.</summary>
    private void RewireTurnedWatcher(ServerCrystalCell w)
    {
        var state = CrystalNet;
        w.EyeState = CrystalEyeUnknown;
        if (w.Inert)
        {
            return;
        }

        if (w.NetId != 0 && state.Nets.TryGetValue(w.NetId, out var net))
        {
            net.Cells.Remove(w.Cell);
            if (net.Cells.Count == 0)
            {
                state.Nets.Remove(net.Id);
            }
            else
            {
                SplitCrystalNet(net);
            }
        }

        w.NetId = 0;
        JoinCrystalNet(w);
        var eye = w.Cell + CrystalNetRules.OutputFace(w.Yaw);
        if (state.Cells.TryGetValue(eye, out var port) && CrystalNetRules.IsPassivePort(port.Kind) && !HasCrystalNeighbour(eye, excludePassive: true))
        {
            DissolvePortCluster(port);
        }

        DiscoverCrystalNeighbours(w.Cell, w.OwnerId);
    }
}
