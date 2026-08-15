// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Suit teleporter — a craftable device with two destinations: it recalls the player to their ship (the
/// heal-tank / landing-zone respawn point), or beams them to an <b>allied</b> player standing on the same
/// body (#1056). Server-authoritative: both need the device, enough suit energy, and share one cooldown;
/// neither works while flying in space.
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
        if (session is null || !TeleporterReady(session))
        {
            return;
        }

        var p = session.State;
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

    /// <summary>
    /// Beams the player to an allied player on the same body (#1056). Beyond the device gates this needs the
    /// target online, allied (enforced HERE, whatever the client's picker showed), on the same body and not in
    /// space, and not aboard their own ship — ships stay private, exactly as the alliance text promises. The
    /// arrival lands <i>beside</i> the target (<see cref="LandingSpotNear"/>, #1055), never inside them.
    /// </summary>
    public void TeleportToPlayer(string playerId, string targetId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(targetId) || targetId == playerId)
        {
            Reject(session, "teleport", "@srv.tp.bad_target");
            return;
        }

        var target = FindSessionByPlayerId(targetId);
        if (target is null || !target.Joined)
        {
            Reject(session, "teleport", "@srv.tp.target_offline");
            return;
        }

        if (!AreAllied(playerId, targetId))
        {
            Reject(session, "teleport", "@srv.tp.not_allied:" + target.State.Name);
            return;
        }

        if (!TeleporterReady(session))
        {
            return;
        }

        // A position is only meaningful inside its own scene (#1030): a target flying in space or standing on
        // another body has coordinates that mean nothing here.
        if (InSpace(targetId)
            || !string.Equals(target.CurrentLocationId, session.CurrentLocationId, System.StringComparison.Ordinal))
        {
            Reject(session, "teleport", "@srv.tpp.not_here:" + target.State.Name);
            return;
        }

        if (target.State.AboardShip)
        {
            Reject(session, "teleport", "@srv.tp.target_aboard:" + target.State.Name);
            return;
        }

        var p = session.State;
        p.SuitEnergy -= TeleportEnergyCost;
        _teleportCooldown[playerId] = TeleportCooldownSeconds;
        p.Position = LandingSpotNear(target.State.Position, target.State.Yaw);

        // Same snap-channel rule as the recall (#414 M7).
        Send(session, new RespawnNotice { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Reason = "@srv.tp.to:" + target.State.Name });
        SendPlayerState(session);
        UpdateAboard(session); // arriving on/off a parked ship must flip the aboard state now, not on the next move
    }

    /// <summary>The device gates every teleporter use shares: carrying the item, not in space flight, off
    /// cooldown, enough suit energy. Sends the matching reject toast and returns false when one fails.</summary>
    private bool TeleporterReady(PlayerSession session)
    {
        var p = session.State;
        if (!p.Inventory.Has(TeleporterItem, 1))
        {
            Reject(session, "teleport", "@srv.tp.no_teleporter");
            return false;
        }

        if (InSpace(p.PlayerId))
        {
            Reject(session, "teleport", "@srv.tp.not_in_space");
            return false;
        }

        if (_teleportCooldown.GetValueOrDefault(p.PlayerId) > 0)
        {
            Reject(session, "teleport", "@srv.tp.recharging");
            return false;
        }

        if (p.SuitEnergy < TeleportEnergyCost)
        {
            Reject(session, "teleport", "@srv.tp.no_energy");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The <c>StarterTeleporter</c> world rule (#1056): a multiplayer host can hand every player a suit
    /// teleporter without each of them grinding the blueprint first. Idempotent — a player who already carries
    /// one gets nothing; called on every join and again for everyone online when the admin flips the rule on.
    /// The device stays an ordinary, discardable item (deliberately NOT part of <c>StarterKit.Items</c>).
    /// Returns true when a device was actually added — the join path sends the inventory afterwards anyway,
    /// so only the live-toggle caller has to push an update.
    /// </summary>
    private bool GrantStarterTeleporter(PlayerSession session)
    {
        if (!Rules.StarterTeleporter)
        {
            return false;
        }

        var p = session.State;
        if (p.Inventory.Has(TeleporterItem, 1) || _content.GetItem(TeleporterItem) is null)
        {
            return false;
        }

        return p.Inventory.Add(TeleporterItem, 1, _content.MaxStackOf(TeleporterItem)) == 0;
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

    private void HandleTeleportToPlayer(PlayerSession session, TeleportToPlayerIntent intent)
        => TeleportToPlayer(session.State.PlayerId, intent.TargetPlayerId);

    /// <summary>Test seam: runs the StarterTeleporter grant for a session as a join would.</summary>
    public bool GrantStarterTeleporterForTest(PlayerSession session) => GrantStarterTeleporter(session);
}
