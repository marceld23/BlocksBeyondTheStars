// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sandbox item catalog (#1930, "please unlock everything in Sandbox — you shouldn't have to craft anything any more"):
/// in the Creative game mode the inventory menu lists every item, and taking one hands it out straight away — backpack
/// first, then the hold while aboard. Only in that mode (the world's, or the player's own override); the server never
/// trusts the client's page.
/// </summary>
public sealed partial class GameServer
{
    private void HandleCreativeTakeItem(PlayerSession session, CreativeTakeItemIntent intent)
    {
        var p = session.State;
        if (Rules.ModeFor(p.ModeOverride) != GameMode.Creative)
        {
            Reject(session, "catalog", "@srv.catalog.not_sandbox");
            return;
        }

        string item = (intent.ItemKey ?? string.Empty).Trim();
        if (item.Length == 0 || _content.GetItem(item) is null)
        {
            Reject(session, "catalog", "@srv.catalog.unknown_item");
            return;
        }

        int count = System.Math.Clamp(intent.Count, 1, System.Math.Max(1, _content.MaxStackOf(item)));
        Serve(session);
        var pool = new MaterialPool(_content, p, _ship);
        pool.Add(item, count);
        if (pool.Overflow >= count)
        {
            Reject(session, "catalog", "@srv.catalog.full");
            return;
        }

        SendInventory(session);
    }

    /// <summary>Test seam: a catalog take as the intent would request it.</summary>
    public void CreativeTakeItemForTest(string playerId, string item, int count)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleCreativeTakeItem(session, new CreativeTakeItemIntent { ItemKey = item, Count = count });
        }
    }
}
