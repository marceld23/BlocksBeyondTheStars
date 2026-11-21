// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A ship lost in a flight that other pilots share (#3903). <see cref="DisableShip"/> used to send EVERY pilot of the
/// flight instance home and unload the instance when one ship's hull was gone — in a fight flown together (the escort
/// cruiser) one unlucky pilot ended it for all. Now, while at least one other pilot flies in the instance, only the
/// downed ship's crew goes home: its pilot, and a friend docked aboard it. The instance, its hostiles and the other
/// pilots stay. A pilot alone in the instance takes the old path unchanged.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The ship cursor's ship was defeated. When other pilots fly in the instance, sends only that ship's crew
    /// home and returns true (the flight goes on); returns false — nothing done — when the pilot is alone, so the caller
    /// runs the classic path. The outcome for the ship itself follows <see cref="Shared.Configuration.GameRules.KeepShipOnDeath"/>
    /// exactly as there: recovered whole, or left a wreck on its owner's pad.</summary>
    private bool TryDisableOneShipOfSeveral(SpaceInstance instance)
    {
        if (_current is not { } pilot || instance.Players.Count < 2 || !instance.Players.Contains(pilot.State.PlayerId))
        {
            return false;
        }

        string playerId = pilot.State.PlayerId;
        bool keepShip = Rules.KeepShipOnDeath;
        if (keepShip)
        {
            _ship.Hull = _shipHullMax;
            _ship.Shield = _shipShieldMax;
            _ship.Downed = false;
        }
        else
        {
            _ship.Hull = 0f;
            _ship.Shield = 0f;
            _ship.Downed = true;
            WreckShipHull(playerId);
        }

        SendShipGuestHome(pilot, "@srv.visit.host_left"); // a friend docked aboard this ship goes home with it

        // Out of the flight, by hand: LeaveSpace would close the view with "returned" instead of the ship's loss.
        _playerInstance.Remove(playerId);
        instance.Players.Remove(playerId);
        instance.PilotSims.Remove(playerId);
        instance.ShipPoses.Remove(playerId);
        instance.PlayerPoses.Remove(playerId);
        instance.Structures.Remove("ship:" + playerId);

        var p = pilot.State;
        p.InEva = false;
        p.Health = 100f;
        p.Oxygen = 100f;
        string reason = keepShip ? "@srv.space.ship_disabled" : "@srv.space.ship_destroyed";
        Send(pilot, new SpaceClosed { Reason = reason, ShipDisabled = true });
        RecoverToShip(pilot, reason, salvaged: false, died: false, parkShip: !keepShip);
        SendShipCombatStatus(pilot);
        if (!keepShip)
        {
            SendShipRepairStatus(pilot);
        }

        foreach (var other in PilotsIn(instance))
        {
            Send(other, new ServerMessage { Text = "@srv.space.ally_ship_down:" + p.Name });
        }

        BroadcastSpaceState(instance); // the others stop drawing that ship
        _log.Info($"Ship of '{playerId}' was lost in '{instance.Id}'; {instance.Players.Count} pilot(s) fly on.");
        return true;
    }
}
