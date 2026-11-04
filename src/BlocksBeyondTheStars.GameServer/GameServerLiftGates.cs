// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lift gate (#3174, Lyxette 2026-11-04: "a barrier so nobody falls into the shaft when the platform isn't there"):
/// a knee-high energy grid placed on a floor's landing right beside the shaft — the ring a lift stop stands in, two cells
/// from the motor's column, at the height a player stands on that floor. The gate is a plain block pair, no Crystal Net
/// device: <c>lift_gate</c> (solid, closed) and <c>lift_gate_open</c> (walk-through). Every gate in the ring belongs to the
/// lift and to the floor it stands on — a stack of gates to the floor of its lowest one — and the lift switches them:
/// <b>open while the platform rests at that floor, closed otherwise</b> (while it moves, every gate is closed). A gate at a
/// height with no floor stays closed; a gate anywhere else is never touched.
/// <list type="bullet">
/// <item><b>Kid rule.</b> A gate never closes onto a player, an NPC or an animal standing in it — it waits and tries again
/// on every tick until the cell is free.</item>
/// <item><b>Always right.</b> The gates are re-read on arrival and departure, and every <see cref="LiftGateSyncSeconds"/>
/// anyway — so a gate placed at the waiting floor opens, and a save whose gates disagree with the platform (the motor's row
/// and the chunk deltas are written at different times) is put right once its chunks are loaded. A gate in a chunk that is
/// not loaded is left alone until it is.</item>
/// <item><b>A mined motor</b> closes its gates: the shaft has no platform any more.</item>
/// </list>
/// Cost: one pass over the shaft's ring (16 columns × the lift's height, chunk-cache reads only) per lift every half second
/// and on arrival / departure; a BlockChanged only for a gate that really switches.
/// </summary>
public sealed partial class GameServer
{
    private const string LiftGateKey = "lift_gate";
    private const string LiftGateOpenKey = "lift_gate_open";

    /// <summary>How often a lift reads its gates again when nothing happened (placement, a stale save).</summary>
    private const double LiftGateSyncSeconds = 0.5;

    /// <summary>A lift's tick for its gates: the periodic re-read, and at once when a gate that waited for a body is free.</summary>
    private void TickLiftGates(ServerLift lift)
    {
        if (_uptime < lift.NextGateSync && !AWaitingGateIsFree(lift))
        {
            return;
        }

        SyncLiftGates(lift);
    }

    private bool AWaitingGateIsFree(ServerLift lift)
    {
        foreach (var cell in lift.GatesWaiting)
        {
            if (!CellOccupiedByBody(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Brings every gate of a lift in line with its platform: open on the floor where it rests, closed on every
    /// other floor — and everywhere while it moves or once the lift is <paramref name="gone"/>.</summary>
    private void SyncLiftGates(ServerLift lift, bool gone = false)
    {
        lift.NextGateSync = _uptime + LiftGateSyncSeconds;
        lift.GatesWaiting.Clear();
        if (_crystalFrame is not null || _content.GetBlock(LiftGateKey) is not { } closed || _content.GetBlock(LiftGateOpenKey) is not { } open)
        {
            return; // lifts never run aboard a ship; a pack without the gate has nothing to switch
        }

        int? openFloor = gone || lift.Moving ? null : RestingLevel(lift);
        int ring = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;
        var gates = (Closed: closed.NumericId, Open: open.NumericId);
        bool sounded = false;
        for (int dx = -ring; dx <= ring; dx++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == ring)
                {
                    SyncLiftGateColumn(lift, lift.Motor.X + dx, lift.Motor.Z + dz, openFloor, gates, ref sounded);
                }
            }
        }
    }

    /// <summary>The floor a resting platform waits at, or null while it is between floors.</summary>
    private static int? RestingLevel(ServerLift lift)
    {
        int level = (int)Math.Round(lift.PlatformY);
        return Math.Abs(lift.PlatformY - level) < 0.01f ? level : null;
    }

    /// <summary>One column of the ring, bottom up: each run of stacked gates belongs to the floor of its lowest gate (the
    /// floor is one below the cell a player stands in — the platform's height there).</summary>
    private void SyncLiftGateColumn(ServerLift lift, int x, int z, int? openFloor, (BlockId Closed, BlockId Open) gates, ref bool sounded)
    {
        int runFloor = int.MinValue;
        for (int y = lift.Motor.Y + 2; y - lift.Motor.Y <= CrystalNetRules.LiftMaxHeight; y++)
        {
            var cell = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
            var id = _world.GetBlockIfLoaded(cell);
            bool isOpen = id.Value == gates.Open.Value;
            if (!isOpen && id.Value != gates.Closed.Value)
            {
                runFloor = int.MinValue; // no gate here: the next one starts a stack of its own
                continue;
            }

            runFloor = runFloor == int.MinValue ? y - 1 : runFloor;
            bool wantOpen = openFloor == runFloor;
            if (wantOpen != isOpen && SwitchLiftGate(lift, cell, wantOpen ? gates.Open : gates.Closed, closing: !wantOpen) && !sounded)
            {
                sounded = true; // one sound per floor that switches, not one per gate
                BroadcastToWorld(new SoundFx { SoundId = wantOpen ? "force_field_off" : "force_field_on", X = cell.X + 0.5f, Y = cell.Y + 0.5f, Z = cell.Z + 0.5f, SourceId = 200000 + lift.Id });
            }
        }
    }

    /// <summary>Swaps one gate, keeping its dye and form. A gate that would close onto a body waits (false).</summary>
    private bool SwitchLiftGate(ServerLift lift, Vector3i cell, BlockId to, bool closing)
    {
        if (closing && CellOccupiedByBody(cell))
        {
            lift.GatesWaiting.Add(cell); // kid rule: never close onto someone — try again when the cell is free
            return false;
        }

        var (tint, glow) = _world.GetModifier(cell);
        CrystalWriteCell(cell, to, tint, glow, _world.GetShape(cell));
        return true;
    }
}
