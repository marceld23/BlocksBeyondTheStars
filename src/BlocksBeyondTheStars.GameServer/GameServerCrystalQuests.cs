// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Crystal workshop (#2258): a VEGA mission chain whose steps finish only when a circuit WORKS — a switch lights a
/// lamp, a step plate rings a chime, a daylight sensor switches a lamp, a logic block holds a door, a signal starts a
/// machine that finishes its job, a phase block opens — plus the Crystal Net achievements. The net reports these
/// events itself (<see cref="OnCrystalCircuitEvent"/>), for the owner of the device that made the circuit work, when that
/// player is on the server: a <see cref="MissionObjectiveType.Circuit"/> objective advances and the
/// <c>crystal:&lt;event&gt;</c> achievement counter counts.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A circuit of <paramref name="ownerId"/>'s worked. World circuits and ownerless devices count for nobody.</summary>
    private void OnCrystalCircuitEvent(string ownerId, string circuitEvent)
    {
        if (ownerId.Length == 0 || CrystalNetRules.IsWorldOwner(ownerId) || FindSessionByPlayerId(ownerId) is not { } session)
        {
            return;
        }

        Advance(session, AchievementCounters.Crystal(circuitEvent));
        foreach (var pr in session.State.Missions)
        {
            if (pr.Status != MissionStatus.Active || GetMissionDef(pr.MissionId) is not { } def)
            {
                continue;
            }

            for (int i = 0; i < def.Objectives.Count && i < pr.ObjectiveProgress.Count; i++)
            {
                var obj = def.Objectives[i];
                if (obj.Type == MissionObjectiveType.Circuit && obj.Target == circuitEvent && pr.ObjectiveProgress[i] < obj.Required)
                {
                    pr.ObjectiveProgress[i]++;
                }
            }
        }
    }

    /// <summary>The first source of this kind that is ON in a network, or null.</summary>
    private ServerCrystalCell? CrystalSourceOnIn(int netId, CrystalDeviceKind kind)
    {
        if (netId == 0 || !CrystalNet.Nets.TryGetValue(netId, out var net))
        {
            return null;
        }

        foreach (var pos in net.Cells)
        {
            if (CrystalNet.Cells.TryGetValue(pos, out var c) && c.Kind == kind && c.Output && !c.Inert)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>A lamp went on: a switch or a daylight sensor on its network made a circuit work.</summary>
    private void OnCrystalLampLit(ServerCrystalCell lamp)
    {
        if (CrystalSourceOnIn(lamp.NetId, CrystalDeviceKind.Switch) is { } sw)
        {
            OnCrystalCircuitEvent(sw.OwnerId, CircuitEvents.LampBySwitch);
        }

        if (CrystalSourceOnIn(lamp.NetId, CrystalDeviceKind.DaylightSensor) is { } sun)
        {
            OnCrystalCircuitEvent(sun.OwnerId, CircuitEvents.LampByDaylight);
        }
    }

    /// <summary>A chime rang: it counts, and with a step plate on its network it is a doorbell.</summary>
    private void OnCrystalChimeRang(ServerCrystalCell chime)
    {
        OnCrystalCircuitEvent(chime.OwnerId, CircuitEvents.Chime);
        if (CrystalSourceOnIn(chime.NetId, CrystalDeviceKind.StepPlate) is { } plate)
        {
            OnCrystalCircuitEvent(plate.OwnerId, CircuitEvents.ChimeByPlate);
        }
    }

    /// <summary>A door changed its mode: when a logic block drives the network beside it, an airlock works.</summary>
    private void OnCrystalDoorDriven(ServerDoor door, int netId)
    {
        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.LogicBlock && !c.Inert && CrystalGateOutputNet(c) == netId)
            {
                OnCrystalCircuitEvent(door.Owner.Length > 0 ? door.Owner : c.OwnerId, CircuitEvents.DoorByGate);
                return;
            }
        }
    }

    /// <summary>Test seam: a player's progress on an active mission's objectives, or null.</summary>
    public int[]? MissionProgressForTest(string playerId, string missionId)
        => FindSessionByPlayerId(playerId)?.State.Missions.FirstOrDefault(m => m.MissionId == missionId)?.ObjectiveProgress.ToArray();
}
