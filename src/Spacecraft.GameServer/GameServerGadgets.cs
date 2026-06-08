using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

/// <summary>
/// Right-click gadgets (item 36): the <b>field medkit</b> (heal yourself + nearby allies), the <b>stasis
/// projector</b> (briefly freeze creatures so they can be scanned safely) and the <b>terrain blaster</b>
/// (clear a sphere of terrain — no loot). All are reusable tools gated behind a blueprint, costing suit
/// energy with a short cooldown. The effect is keyed by the item id so one intent drives all three.
/// </summary>
public sealed partial class GameServer
{
    // Uses the existing monotonic _uptime clock (GameServerBump.SampleHistories increments it once per tick).
    private readonly Dictionary<string, double> _gadgetReadyAt = new(); // "playerId|gadget" -> uptime usable again

    // --- balance: field medkit ---
    private const float MedkitHealAmount = 45f; // HP restored to the user + each nearby ally
    private const float MedkitRadius = 6f;       // ally heal radius (blocks)
    private const double MedkitCooldown = 4.0;   // seconds between uses

    private void HandleUseGadget(PlayerSession session, UseGadgetIntent intent)
    {
        var p = session.State;
        var item = _content.GetItem(intent.GadgetKey);
        if (item?.Tool is null || item.Tool.Kind != ToolKind.Gadget)
        {
            Reject(session, "gadget", "Not a usable gadget.");
            return;
        }

        if (!p.Inventory.Has(intent.GadgetKey, 1))
        {
            Reject(session, "gadget", "You don't have that gadget.");
            return;
        }

        string cdKey = p.PlayerId + "|" + intent.GadgetKey;
        if (_gadgetReadyAt.TryGetValue(cdKey, out var readyAt) && _uptime < readyAt)
        {
            return; // still cooling down (the client also rate-limits) — ignore quietly
        }

        if (p.SuitEnergy < item.Tool.EnergyPerUse)
        {
            Reject(session, "gadget", "Not enough suit energy.");
            return;
        }

        var target = new Vector3f(intent.X, intent.Y, intent.Z);
        double cooldown;
        switch (intent.GadgetKey)
        {
            case "field_medkit":
                UseFieldMedkit(session);
                cooldown = MedkitCooldown;
                break;
            default:
                Reject(session, "gadget", "Unknown gadget.");
                return;
        }

        p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - item.Tool.EnergyPerUse);
        _gadgetReadyAt[cdKey] = _uptime + cooldown;
        SendPlayerState(session);
    }

    /// <summary>Heals the user and every other on-foot player within <see cref="MedkitRadius"/> in the same
    /// world (a shared first-aid pulse) — item 36.</summary>
    private void UseFieldMedkit(PlayerSession user)
    {
        var origin = user.State.Position;
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue; // piloting in space, not on foot
            }

            if (WrapDistSq(origin, s.State.Position) > MedkitRadius * MedkitRadius)
            {
                continue;
            }

            var t = s.State;
            if (t.Health <= 0f)
            {
                continue; // already down — a medkit can't revive
            }

            float before = t.Health;
            t.Health = System.Math.Min(100f, t.Health + MedkitHealAmount);
            if (t.Health != before)
            {
                SendPlayerState(s);
            }
        }
    }

    /// <summary>Test hook: how many seconds until the gadget is usable again for this player (0 = ready).</summary>
    public double GadgetCooldownForTest(string playerId, string gadgetKey)
        => _gadgetReadyAt.TryGetValue(playerId + "|" + gadgetKey, out var at) ? System.Math.Max(0, at - _uptime) : 0;

    /// <summary>Test hook: run a gadget use as if the intent arrived.</summary>
    public void UseGadgetForTest(string playerId, string gadgetKey, Vector3f target)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleUseGadget(s, new UseGadgetIntent { GadgetKey = gadgetKey, X = target.X, Y = target.Y, Z = target.Z });
        }
    }
}
