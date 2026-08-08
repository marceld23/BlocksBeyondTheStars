// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Suit teleporter — a craftable device that recalls the player to their ship (the heal-tank /
/// landing-zone respawn point). Server-authoritative: requires the device, enough suit energy, and
/// a cooldown between uses; it can't be used while flying in space.
/// </summary>
public sealed partial class GameServer
{
    private const string TeleporterItem = "suit_teleporter";
    private const double TeleportCooldownSeconds = 30.0;
    private const float TeleportEnergyCost = 10f;

    private readonly Dictionary<string, double> _teleportCooldown = new();

    /// <summary>Recalls the player to their ship if they carry a teleporter, are charged and off cooldown.</summary>
    public void TeleportToShip(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var p = session.State;
        if (!p.Inventory.Has(TeleporterItem, 1))
        {
            Reject(session, "teleport", "@srv.tp.no_teleporter");
            return;
        }

        if (InSpace(playerId))
        {
            Reject(session, "teleport", "@srv.tp.not_in_space");
            return;
        }

        if (_teleportCooldown.GetValueOrDefault(playerId) > 0)
        {
            Reject(session, "teleport", "@srv.tp.recharging");
            return;
        }

        if (p.SuitEnergy < TeleportEnergyCost)
        {
            Reject(session, "teleport", "@srv.tp.no_energy");
            return;
        }

        p.SuitEnergy -= TeleportEnergyCost;
        p.Position = p.RespawnPoint; // the heal-tank / landing zone in the ship
        p.AboardShip = true;
        _teleportCooldown[playerId] = TeleportCooldownSeconds;

        // The snap must ride the RespawnNotice channel (Died=false → no death feedback): a plain
        // PlayerStateUpdate position is discarded by the client, whose next MoveIntent would then
        // revert this teleport server-side — "aboard" per server, still standing outside per client
        // (#414 N17). Same pattern as the void-fall rescue in GameServerSpawnSafety.
        Send(session, new RespawnNotice
        {
            X = p.RespawnPoint.X,
            Y = p.RespawnPoint.Y,
            Z = p.RespawnPoint.Z,
            Reason = "@srv.tp.to_ship",
        });
        SendPlayerState(session);
    }

    /// <summary>Counts down a player's teleporter cooldown (called from the environment tick).</summary>
    private void DecayTeleportCooldown(string playerId, double dt)
    {
        if (_teleportCooldown.TryGetValue(playerId, out var cd) && cd > 0)
        {
            _teleportCooldown[playerId] = System.Math.Max(0, cd - dt);
        }
    }

    private void HandleTeleportToShip(PlayerSession session) => TeleportToShip(session.State.PlayerId);
}
