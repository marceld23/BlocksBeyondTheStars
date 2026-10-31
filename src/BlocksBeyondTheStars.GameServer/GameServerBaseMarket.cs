// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The market day at a Market base (#3039, Justus: "traders show up and trade with each other"). The travelling merchant
/// comes only to a Market base (<c>PickGuestKind</c>), twice as often there, and with <see cref="BaseFocus.MarketPairChance"/>
/// a second merchant comes along who sells one trade's goods (a themed stock beside the first one's bit of everything). Once
/// both stand waiting at the base they trade with each other: they turn to face each other, their nameplates say "trading",
/// the players near them read "Market day! {a} and {b} are trading with each other.", and both stay
/// <see cref="MarketTradeExtraWait"/> seconds longer — a market is worth a longer look. RAM only, like every visit.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How much longer two merchants stay after they traded (on top of the merchant's wait).</summary>
    private const double MarketTradeExtraWait = 120.0;

    /// <summary>Players this close to the two merchants hear the market day.</summary>
    private const float MarketTradeHearRange = 24f;

    /// <summary>Merchant guests (NPC ids) that already traded on this visit.</summary>
    private readonly HashSet<int> _marketTraded = new();

    /// <summary>After a merchant was rolled for a Market base: sometimes a second one with a trade of its own comes along.</summary>
    private void MaybeBringMarketPartner(ServerBase b, PlayerSession owner)
    {
        if (!IsMarketBase(b) || _guestRng.NextDouble() >= BaseFocus.MarketPairChance)
        {
            return;
        }

        if (SpawnBaseGuest(b, owner, BaseGuestKind.Merchant) is not { } second
            || _npcs.FirstOrDefault(n => n.Id == second.NpcId) is not { } npc)
        {
            return;
        }

        var themes = MarketStock.Themes(_content.Recipes.Values);
        if (themes.Count > 0)
        {
            npc.Theme = themes[_guestRng.Next(themes.Count)]; // one trade's goods, beside the first one's bit of everything
        }
    }

    /// <summary>Two merchants waiting at the same base trade with each other, once per visit (see the class summary).</summary>
    private void StepMarketDays()
    {
        _marketTraded.RemoveWhere(id => !_baseGuests.Any(g => g.NpcId == id));
        foreach (var group in _baseGuests.Where(g => g.Kind == BaseGuestKind.Merchant && g.Phase == BaseGuestPhase.Waiting)
                     .GroupBy(g => g.BaseId))
        {
            var pair = group.Where(g => !_marketTraded.Contains(g.NpcId)).Take(2).ToList();
            if (pair.Count < 2
                || _npcs.FirstOrDefault(n => n.Id == pair[0].NpcId) is not { } a
                || _npcs.FirstOrDefault(n => n.Id == pair[1].NpcId) is not { } c)
            {
                continue;
            }

            _marketTraded.Add(a.Id);
            _marketTraded.Add(c.Id);
            FaceTowards(a, c.Pos);
            FaceTowards(c, a.Pos);
            a.ActivityKey = "npc.activity.trading";
            c.ActivityKey = "npc.activity.trading";
            foreach (var g in pair)
            {
                g.WaitUntil += MarketTradeExtraWait;
                g.WaitHardEnd += MarketTradeExtraWait;
            }

            float r2 = MarketTradeHearRange * MarketTradeHearRange;
            foreach (var s in _sessions.Values.Where(s => s.Joined && s.CurrentLocationId == _world.LocationId
                                                         && WrapDistSq(s.State.Position, a.Pos) <= r2))
            {
                Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.market.trade").Replace("{a}", a.Name).Replace("{b}", c.Name) });
            }

            BroadcastNpcs();
        }
    }

    /// <summary>Test seam (#3039): runs the market-day step now.</summary>
    public void StepMarketDaysForTest() => StepMarketDays();

    /// <summary>Test seam (#3036): one guest roll for a base ("merchant", "neighbour", "traveler"; "" = none).</summary>
    public string PickGuestKindForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b && PickGuestKind(b) is { } kind ? GuestKindKey(kind) : string.Empty;

    /// <summary>Test seam (#3039): the trade themes of the merchant guests on the active world.</summary>
    public IReadOnlyList<string> MerchantGuestThemesForTest
        => _baseGuests.Where(g => g.Kind == BaseGuestKind.Merchant).Select(g => _npcs.FirstOrDefault(n => n.Id == g.NpcId)?.Theme ?? string.Empty).ToList();
}
