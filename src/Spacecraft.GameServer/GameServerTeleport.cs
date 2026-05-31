using System.Collections.Generic;
using Spacecraft.Networking.Messages;

namespace Spacecraft.GameServer;

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
            Reject(session, "teleport", "You have no suit teleporter.");
            return;
        }

        if (InSpace(playerId))
        {
            Reject(session, "teleport", "The teleporter can't be used while flying in space.");
            return;
        }

        if (_teleportCooldown.GetValueOrDefault(playerId) > 0)
        {
            Reject(session, "teleport", "The teleporter is still recharging.");
            return;
        }

        if (p.SuitEnergy < TeleportEnergyCost)
        {
            Reject(session, "teleport", "Not enough suit energy to teleport.");
            return;
        }

        p.SuitEnergy -= TeleportEnergyCost;
        p.Position = p.RespawnPoint; // the heal-tank / landing zone in the ship
        p.AboardShip = true;
        _teleportCooldown[playerId] = TeleportCooldownSeconds;

        SendPlayerState(session);
        Send(session, new ServerMessage { Text = "Teleported back to your ship." });
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
