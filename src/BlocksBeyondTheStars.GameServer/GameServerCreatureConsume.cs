// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Eating and consuming (food heals, poison harms).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Player eats/uses a consumable item. Server applies its effect and consumes one.</summary>
    public void ConsumeItem(string playerId, string itemKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        // #2202: a preparation from the bio lab starts its effect instead of being eaten like food.
        if (TryTakePreparation(session, itemKey))
        {
            return;
        }

        var p = session.State;
        var item = _content.GetItem(itemKey);
        if (item is null || item.Category != ItemCategory.Consumable)
        {
            Reject(session, "consume", "@srv.misc.not_consumable");
            return;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            Reject(session, "consume", "@srv.misc.no_item");
            return;
        }

        p.Inventory.Remove(itemKey, 1);
        if (item.ConsumeHealth > 0f)
        {
            p.Health = System.Math.Min(100f, p.Health + item.ConsumeHealth);
        }
        else if (item.ConsumeHealth < 0f)
        {
            DamagePlayer(p, -item.ConsumeHealth); // bad food — harmless in Sandbox (#2712)
        }

        if (item.ConsumeHunger != 0f)
        {
            p.Hunger = System.Math.Min(100f, System.Math.Max(0f, p.Hunger + item.ConsumeHunger));
        }

        if (item.ConsumeHunger > 0f)
        {
            RaiseGameEvent(session, GameEventKeys.EatAny, itemKey); // #2465
        }

        SendInventory(session);
        SendPlayerState(session);
        if (p.Health <= 0f)
        {
            RespawnPlayer(session, "@srv.death.poison");
        }
    }

    private void HandleConsume(PlayerSession session, ConsumeItemIntent intent)
        => ConsumeItem(session.State.PlayerId, intent.ItemKey);
}
