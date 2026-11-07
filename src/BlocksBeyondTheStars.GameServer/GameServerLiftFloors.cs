// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lift's floor picker (#3171) and the device names (#3172) — the device actions behind the client's panels:
/// <list type="bullet">
/// <item><b>5 = go to a floor.</b> Sent at the lift's motor with the floor's platform height as the mode. A rider (on the
/// platform) or a player within reach of one of the lift's stops or its motor may send it — the motor of a tall lift lies
/// far below its upper floors, so the device's own reach would refuse everyone up there. The floor must be one of the lift's
/// (<c>LiftLevels</c>); operating needs the motor's operate rights.</item>
/// <item><b>6 = name a floor.</b> The same reach; the name goes to every lift stop on that floor the player may configure.
/// An empty name clears it.</item>
/// <item><b>7 = name the device.</b> The device menu's name field sends the name alone — on Enter, on leaving the field and
/// when the menu closes, not only on "Save" — so an empty field clears the label (the configure action keeps a label it
/// gets no new one for, as an older client sends the field's empty text with every click).</item>
/// </list>
/// The lift's floors and their names ride on the lift list (<see cref="NetLift.Levels"/>), re-read every
/// <see cref="LiftFloorCheckSeconds"/> while the lift exists: one pass over the net's cells per lift.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3171: the device action that sends a lift to a floor (the mode is the floor's platform height).</summary>
    internal const int LiftGoToAction = 5;

    /// <summary>#3171: the device action that names a floor (the mode is the floor's platform height, the label the name).</summary>
    internal const int LiftNameFloorAction = 6;

    /// <summary>#3172: the device action that names the device itself (an empty label clears the name).</summary>
    internal const int CrystalRenameAction = 7;

    /// <summary>#3171: how often a lift's floors and names are read again for the wire.</summary>
    private const double LiftFloorCheckSeconds = 0.5;

    private static bool IsLiftFloorAction(int action) => action is LiftGoToAction or LiftNameFloorAction;

    /// <summary>The motor a lift action names: the motor itself, or the lift a stop belongs to. Null when the cell belongs to
    /// no working lift.</summary>
    private ServerCrystalCell? LiftMotorOf(ServerCrystalCell cell)
        => cell.Kind == CrystalDeviceKind.LiftMotor ? (cell.Inert ? null : cell)
            : cell.Kind == CrystalDeviceKind.LiftStop ? MotorOfStop(cell.Cell) : null;

    /// <summary>#3171: whether a player may choose a lift's floor from where they are — standing on its platform, or within
    /// reach of its motor or of one of its stops. A cell that belongs to no lift falls back to the device's own reach (the
    /// handler then says what is wrong).</summary>
    private bool CanReachLift(PlayerSession session, ServerCrystalCell cell)
    {
        if (_crystalFrame is not null || LiftMotorOf(cell) is not { } motor || !CrystalNet.Lifts.TryGetValue(motor.Cell, out var lift))
        {
            return WithinReach(session.State, CrystalToWorld(cell.Cell));
        }

        if (OnLiftPlatform(session.State, lift) || WithinReach(session.State, motor.Cell))
        {
            return true;
        }

        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.LiftStop && StopBelongsTo(c.Cell, motor.Cell) && WithinReach(session.State, c.Cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Actions 5..7 (reach and rights are checked by <c>HandleSetCrystalDevice</c>).</summary>
    private void HandleCrystalFloorOrName(PlayerSession session, ServerCrystalCell cell, SetCrystalDeviceIntent intent)
    {
        if (intent.Action == CrystalRenameAction)
        {
            if (ApplyCrystalLabel(session, cell, intent.Label, clear: true))
            {
                SaveCrystalCell(cell);
                CrystalNet.DeviceListDirty = true;
                ForgetLiftFloors(); // a stop's name is its floor's name
            }

            return;
        }

        if (cell.Kind is not (CrystalDeviceKind.LiftMotor or CrystalDeviceKind.LiftStop))
        {
            return; // a floor action on another device: nothing to do
        }

        if (LiftMotorOf(cell) is not { } motor)
        {
            Send(session, new ServerMessage { Text = cell.Kind == CrystalDeviceKind.LiftStop ? LiftStopWithoutLiftText(cell.Cell) : LiftStopNoShaftText });
            return;
        }

        if (!LiftLevels(motor.Cell).Contains(intent.Mode))
        {
            Send(session, new ServerMessage { Text = "@srv.lift.no_floor" }); // the stop went while the picker was open
            return;
        }

        if (intent.Action == LiftGoToAction)
        {
            LiftGoTo(motor, intent.Mode, session);
        }
        else
        {
            NameLiftFloor(session, motor, intent.Mode, intent.Label);
        }
    }

    /// <summary>#3171: names a floor — every lift stop on it that the player may configure carries the name (an empty one
    /// clears it). A floor without a stop (the bottom one may have none) has nothing to carry a name.</summary>
    private void NameLiftFloor(PlayerSession session, ServerCrystalCell motor, int level, string? label)
    {
        var stops = CrystalNet.Cells.Values
            .Where(c => c.Kind == CrystalDeviceKind.LiftStop && !c.Inert && c.Cell.Y - 1 == level && StopBelongsTo(c.Cell, motor.Cell))
            .ToList();
        if (stops.Count == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.lift.floor_no_stop" });
            return;
        }

        var mine = stops.Where(c => CanConfigureCrystal(c, session.State.PlayerId, session.State.IsAdmin)).ToList();
        if (mine.Count == 0)
        {
            Reject(session, "crystal", "@srv.crystal.owner_only");
            return;
        }

        if (!ApplyCrystalLabel(session, mine[0], label, clear: true))
        {
            return; // refused by the content screen — the player has been told
        }

        foreach (var stop in mine)
        {
            stop.Label = mine[0].Label;
            SaveCrystalCell(stop);
        }

        CrystalNet.DeviceListDirty = true;
        ForgetLiftFloors();
    }

    /// <summary>#3172: sets a device's name from what the player typed — sanitised and content-screened like a beacon's. An
    /// empty name keeps the label, unless <paramref name="clear"/> (the name actions) clears it. False when the screen
    /// refused the name (the player has been told).</summary>
    private bool ApplyCrystalLabel(PlayerSession session, ServerCrystalCell cell, string? label, bool clear)
    {
        if (string.IsNullOrEmpty(label))
        {
            if (clear)
            {
                cell.Label = string.Empty;
            }

            return true;
        }

        if (ScreenPlayerName(session, SanitizeBeamName(label), "crystal") is not { } screened)
        {
            return false;
        }

        cell.Label = screened;
        return true;
    }

    /// <summary>The next tick reads every lift's floors again (a stop was named).</summary>
    private void ForgetLiftFloors()
    {
        foreach (var lift in CrystalNet.Lifts.Values)
        {
            lift.NextFloorCheck = double.NegativeInfinity;
        }
    }

    /// <summary>#3171: re-reads a lift's floors and their names every <see cref="LiftFloorCheckSeconds"/>; a change goes out
    /// with the next lift list.</summary>
    private void RefreshLiftFloors(ServerLift lift)
    {
        if (_uptime < lift.NextFloorCheck)
        {
            return;
        }

        lift.NextFloorCheck = _uptime + LiftFloorCheckSeconds;
        var levels = LiftLevels(lift.Motor);
        var names = new string[levels.Count];
        var hasStop = new bool[levels.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = string.Empty;
        }

        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind != CrystalDeviceKind.LiftStop || c.Inert || !StopBelongsTo(c.Cell, lift.Motor))
            {
                continue;
            }

            int i = levels.IndexOf(c.Cell.Y - 1);
            if (i >= 0)
            {
                hasStop[i] = true;
                names[i] = names[i].Length > 0 ? names[i] : c.Label;
            }
        }

        if (!levels.SequenceEqual(lift.Levels) || !names.SequenceEqual(lift.LevelNames) || !hasStop.SequenceEqual(lift.LevelHasStop))
        {
            lift.Levels = levels.ToArray();
            lift.LevelNames = names;
            lift.LevelHasStop = hasStop;
            CrystalNet.LiftListDirty = true;
        }
    }

    /// <summary>Test seam: a device's name (its label), or null when no device sits there.</summary>
    public string? CrystalDeviceLabelForTest(Vector3i cell)
        => CrystalNet.Cells.TryGetValue(cell, out var c) && !c.IsConduit ? c.Label : null;

    /// <summary>Test seam (#3171): a lift's floors as the players were last told them, or null when no lift stands there.</summary>
    public IReadOnlyList<int>? LiftLevelsForTest(Vector3i motor) => CrystalNet.Lifts.TryGetValue(motor, out var l) ? l.Levels : null;
}
