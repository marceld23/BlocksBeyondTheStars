using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.State;

namespace Spacecraft.GameServer;

/// <summary>
/// Suit equipment effects derived from the gear a player <b>carries</b> (no separate equip slots
/// yet): armor damage resistance, extra oxygen capacity, scanner knowledge bonus, and the stealth
/// field. Server-authoritative — these feed the vitals/combat/scan systems. Data-driven via the
/// item definitions (`ArmorResistance`, `OxygenBonus`, `ScanKnowledgeMultiplier`).
/// </summary>
public sealed partial class GameServer
{
    private const string StealthItem = "stealth_suit";
    private const float StealthDrainPerSecond = 3f; // suit energy spent while cloaked
    private const float MaxArmorResistance = 0.75f;

    /// <summary>Total physical-damage resistance (0..0.75) from carried armor pieces.</summary>
    private float ArmorResistance(PlayerState p)
    {
        float sum = 0f;
        foreach (var item in _content.Items.Values)
        {
            if (item.ArmorResistance > 0f && p.Inventory.Has(item.Key, 1))
            {
                sum += item.ArmorResistance;
            }
        }

        return System.Math.Min(MaxArmorResistance, sum);
    }

    /// <summary>Maximum suit oxygen — base 100 plus any carried tank bonuses.</summary>
    private float MaxOxygen(PlayerState p)
    {
        float bonus = 0f;
        foreach (var item in _content.Items.Values)
        {
            if (item.OxygenBonus > 0f && p.Inventory.Has(item.Key, 1))
            {
                bonus += item.OxygenBonus;
            }
        }

        return 100f + bonus;
    }

    /// <summary>Best scanner knowledge multiplier from carried scanners (1 = no bonus).</summary>
    private float ScanMultiplier(PlayerState p)
    {
        float best = 1f;
        foreach (var item in _content.Items.Values)
        {
            if (item.ScanKnowledgeMultiplier > best && p.Inventory.Has(item.Key, 1))
            {
                best = item.ScanKnowledgeMultiplier;
            }
        }

        return best;
    }

    /// <summary>Applies armor resistance to an incoming physical-damage amount.</summary>
    private float Mitigate(PlayerState p, float damage) => damage * (1f - ArmorResistance(p));

    /// <summary>Toggles the stealth field on/off if the player carries a stealth suit and has energy.</summary>
    public void ToggleStealth(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var p = session.State;
        if (!p.Inventory.Has(StealthItem, 1))
        {
            Reject(session, "stealth", "You have no stealth suit.");
            return;
        }

        if (!p.Stealthed && p.SuitEnergy <= 0f)
        {
            Reject(session, "stealth", "Not enough suit energy to cloak.");
            return;
        }

        p.Stealthed = !p.Stealthed;
        SendPlayerState(session);
    }

    /// <summary>Drains suit energy while cloaked; drops stealth when the energy runs out.</summary>
    private void TickStealth(PlayerSession session, double dt)
    {
        var p = session.State;
        if (!p.Stealthed)
        {
            return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - (float)(dt * StealthDrainPerSecond));
        if (p.SuitEnergy <= 0f)
        {
            p.Stealthed = false;
        }
    }

    private void HandleToggleStealth(PlayerSession session) => ToggleStealth(session.State.PlayerId);
}
