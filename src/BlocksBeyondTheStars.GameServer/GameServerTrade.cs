// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// An open player-to-player trade: each side stages an offer and a "ready" flag; the swap only
/// happens when <b>both</b> have confirmed. Items are not removed until that commit, and any change
/// to either offer clears both ready flags.
/// </summary>
public sealed class TradeSession
{
    public string A { get; set; } = string.Empty;
    public string B { get; set; } = string.Empty;
    public List<ItemAmount> OfferA { get; } = new();
    public List<ItemAmount> OfferB { get; } = new();
    public bool ConfirmA { get; set; }
    public bool ConfirmB { get; set; }

    /// <summary>Knowledge points each side offers to teach (item 11; the giver keeps theirs).</summary>
    public int KnowledgeA { get; set; }
    public int KnowledgeB { get; set; }

    public bool Involves(string p) => p == A || p == B;
    public string Other(string p) => p == A ? B : A;
    public List<ItemAmount> OfferOf(string p) => p == A ? OfferA : OfferB;
    public bool ConfirmOf(string p) => p == A ? ConfirmA : ConfirmB;
    public void SetConfirm(string p, bool v) { if (p == A) ConfirmA = v; else ConfirmB = v; }
    public int KnowledgeOf(string p) => p == A ? KnowledgeA : KnowledgeB;
    public void SetKnowledge(string p, int v) { if (p == A) KnowledgeA = v; else KnowledgeB = v; }
}

public sealed partial class GameServer
{
    private const float TradeRange = 8f;

    private readonly List<TradeSession> _trades = new();
    private readonly Dictionary<string, string> _tradeRequests = new(); // target -> requester

    /// <summary>The open trade a player is in, if any (test/inspection).</summary>
    public TradeSession? ActiveTrade(string playerId) => _trades.FirstOrDefault(t => t.Involves(playerId));

    /// <summary>Both players are joined and close enough to see each other: the same world (or flight space) and within
    /// <see cref="TradeRange"/> — the check docking uses too (<see cref="PlayersTogether"/>, #2720). It used to compare
    /// positions alone, so players on two planets with overlapping coordinates, or in flight by their stale surface
    /// spots, could trade.</summary>
    private bool CanTradeTogether(string a, string b) => PlayersTogether(a, b, TradeRange);

    public void RequestTrade(string fromPlayer, string toPlayer)
    {
        var from = FindSessionByPlayerId(fromPlayer);
        if (from is null)
        {
            return;
        }

        // #3200: one open invitation per target. A second one used to replace the first without a word — the first asker
        // waited for an answer that could no longer come, and the target's "accept" went to whoever had asked last.
        bool targetAsked = _tradeRequests.TryGetValue(toPlayer, out var pending) && pending != fromPlayer;
        if (fromPlayer == toPlayer || ActiveTrade(fromPlayer) is not null || ActiveTrade(toPlayer) is not null || targetAsked)
        {
            Reject(from, "trade", "@srv.trade.busy");
            return;
        }

        if (!CanTradeTogether(fromPlayer, toPlayer))
        {
            Reject(from, "trade", "@srv.trade.too_far");
            return;
        }

        _tradeRequests[toPlayer] = fromPlayer;
        if (FindSessionByPlayerId(toPlayer) is { } to)
        {
            Send(to, new ServerMessage { Text = "@srv.trade.request:" + fromPlayer });
            // …and as an answerable invitation (#981). The chat line alone could not be acted on: nothing in
            // the client sent TradeRespondIntent, so every trade request died where it landed.
            Send(to, new TradeRequestNotice { Requester = fromPlayer });
        }

        // The asker gets a confirmation too: pressing T used to produce nothing whatsoever on the
        // requesting side, which is indistinguishable from a key that does not work (#981).
        Send(from, new ServerMessage { Text = "@srv.trade.request_sent:" + toPlayer });
    }

    public void RespondTrade(string player, bool accept)
    {
        if (!_tradeRequests.TryGetValue(player, out var requester))
        {
            return;
        }

        _tradeRequests.Remove(player);
        if (!accept || !CanTradeTogether(requester, player))
        {
            TellAsker(requester, "@srv.trade.declined");
            return;
        }

        // #3200: the invitation was checked when it was SENT; either side may have started another trade since (one player
        // inviting two, both accepting) — a second session made the first partner's window jump and left the second stuck.
        if (ActiveTrade(requester) is not null || ActiveTrade(player) is not null)
        {
            if (FindSessionByPlayerId(player) is { } answering)
            {
                Reject(answering, "trade", "@srv.trade.busy");
            }

            TellAsker(requester, "@srv.trade.busy");
            return;
        }

        var session = new TradeSession { A = requester, B = player };
        _trades.Add(session);
        WithdrawInvitationsOf(requester, player);
        SendTradeUpdate(session);
    }

    /// <summary>A line to a player whose invitation is off. #3200: a plain message, no longer a <see cref="TradeClosed"/> —
    /// the asker has no trade window that could close, but a <see cref="TradeClosed"/> also cleared the invitation THEY were
    /// looking at (or shut the window of a trade they had meanwhile started), which then hung unanswerable on the
    /// server.</summary>
    private void TellAsker(string requester, string reason)
    {
        if (FindSessionByPlayerId(requester) is { } r)
        {
            Send(r, new ServerMessage { Text = reason });
        }
    }

    /// <summary>#3200: once two players trade, every other invitation to or from either of them is withdrawn — an
    /// invitation on the target's screen closes (it is a modal window), an asker who waits is told.</summary>
    private void WithdrawInvitationsOf(string a, string b)
    {
        var stale = _tradeRequests.Where(kv => kv.Key == a || kv.Key == b || kv.Value == a || kv.Value == b).ToList();
        foreach (var (target, asker) in stale)
        {
            _tradeRequests.Remove(target);
            if (target == a || target == b)
            {
                if (asker != a && asker != b) // the partners' invitation to each other needs no word
                {
                    TellAsker(asker, "@srv.trade.busy");
                }
            }
            else if (ActiveTrade(target) is null && FindSessionByPlayerId(target) is { } shown)
            {
                Send(shown, new TradeClosed { Completed = false, Reason = "@srv.trade.busy" });
            }
        }
    }

    public void SetTradeOffer(string player, IEnumerable<ItemAmount> items)
    {
        var session = ActiveTrade(player);
        var s = FindSessionByPlayerId(player);
        if (session is null || s is null)
        {
            return;
        }

        // Aggregate per item id: duplicate entries (e.g. [{iron,50},{iron,50}]) would each pass the
        // Has() check against the full owned count and then be Add()ed once per entry on commit —
        // minting items from nothing. Summing through long clamps a wrap-around to an impossible
        // amount, so an overflowing offer simply fails validation below.
        var offer = items.Where(i => i.Count > 0)
            .GroupBy(i => i.Item)
            .Select(g => new ItemAmount(g.Key, (int)System.Math.Min(g.Sum(i => (long)i.Count), int.MaxValue)))
            .ToList();
        // Validate against the offering player's OWN ship cargo, not the ship cursor (`_ship`) — see TradePool. #3201: and
        // never against the worn gear, which the trade window does not list either.
        var pool = TradePool(s);
        if (!pool.Has(offer))
        {
            Reject(s, "trade", "@srv.trade.missing_items");
            return;
        }

        var target = session.OfferOf(player);
        target.Clear();
        target.AddRange(offer);

        // Any change to an offer voids both ready states.
        session.ConfirmA = false;
        session.ConfirmB = false;
        SendTradeUpdate(session);
    }

    /// <summary>The most knowledge <paramref name="giver"/> may still teach <paramref name="receiver"/> right
    /// now: never more than they know in total minus what they've already given (give-once), and never enough to
    /// raise the receiver above the giver's own level — so the same knowledge can't be cycled to inflate totals.</summary>
    private static int KnowledgeTeachable(Shared.State.PlayerState giver, Shared.State.PlayerState receiver)
    {
        int alreadyGiven = giver.KnowledgeGivenTo.TryGetValue(receiver.PlayerId, out var g) ? g : 0;
        int cumulativeBudget = giver.KnowledgePoints - alreadyGiven;        // can't out-give what you know
        int levelBudget = giver.KnowledgePoints - receiver.KnowledgePoints; // can't lift them past your level
        return System.Math.Max(0, System.Math.Min(cumulativeBudget, levelBudget));
    }

    public void SetTradeKnowledge(string player, int amount)
    {
        var session = ActiveTrade(player);
        var s = FindSessionByPlayerId(player);
        if (session is null || s is null)
        {
            return;
        }

        var partner = FindSessionByPlayerId(session.Other(player));
        int cap = partner is null ? 0 : KnowledgeTeachable(s.State, partner.State);
        session.SetKnowledge(player, System.Math.Clamp(amount, 0, cap));

        // Any change to an offer voids both ready states.
        session.ConfirmA = false;
        session.ConfirmB = false;
        SendTradeUpdate(session);
    }

    public void ConfirmTrade(string player)
    {
        var session = ActiveTrade(player);
        if (session is null)
        {
            return;
        }

        session.SetConfirm(player, true);
        if (session.ConfirmA && session.ConfirmB)
        {
            CommitTrade(session);
        }
        else
        {
            SendTradeUpdate(session);
        }
    }

    public void CancelTrade(string player)
    {
        _tradeRequests.Remove(player);
        if (ActiveTrade(player) is { } session)
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "@srv.trade.cancelled");
        }
    }

    private void CommitTrade(TradeSession session)
    {
        var sa = FindSessionByPlayerId(session.A);
        var sb = FindSessionByPlayerId(session.B);

        if (sa is null || sb is null || !CanTradeTogether(session.A, session.B))
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "@srv.trade.out_of_range");
            return;
        }

        var poolA = TradePool(sa);
        var poolB = TradePool(sb);

        // Re-validate both sides still hold their offers (they could have used items meanwhile).
        if (!poolA.Has(session.OfferA) || !poolB.Has(session.OfferB))
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "@srv.trade.items_gone");
            return;
        }

        // #3191: both sides are dry-run BEFORE anything moves — give your own offer, take the partner's. The swap used to
        // take both offers and hand back whatever did not fit, ignoring what the hand-back itself could not store: with
        // two full backpacks the partner's goods filled the very slot the giver had just freed, and the rest vanished
        // while both players read "Trade complete". A side without room now refuses; the trade stays open to adjust.
        bool roomA = poolA.CanExchange(session.OfferA, session.OfferB);
        bool roomB = poolB.CanExchange(session.OfferB, session.OfferA);
        if (!roomA || !roomB)
        {
            RefuseTradeForRoom(session, sa, sb, roomA, roomB);
            return;
        }

        poolA.Remove(session.OfferA);
        poolB.Remove(session.OfferB);
        HandOverTradeGoods(sb, poolB, session.OfferA);
        HandOverTradeGoods(sa, poolA, session.OfferB);
        TransferTradeKnowledge(session, sa, sb);

        _trades.Remove(session);
        SendInventory(sa); // carries the (possibly updated) knowledge total to each client
        SendInventory(sb);
        CloseTrade(session, completed: true, "@srv.trade.complete");
    }

    /// <summary>The pool one side of a trade draws from and pays into. It wraps that side's OWN ship cargo — not the ship
    /// cursor (`_ship`), which points at whoever's message triggered the commit (the confirmer); using `_ship` for both
    /// would make a partner-aboard trade read/remove items from the confirmer's hold: item dupe/loss (the pattern the
    /// mission payout uses, GameServerMissions.RewardMissionPoster). #3201: and it leaves the worn gear out.</summary>
    private MaterialPool TradePool(PlayerSession side)
    {
        var ship = side.Ships.TryGetValue(side.ActiveShipId, out var own) ? own : _noShip;
        return MaterialPool.WithoutEquipment(_content, side.State, ship);
    }

    /// <summary>#3191: a trade a side has no room for is refused before anything moves. Both ready flags reset, so the two
    /// can change their offers (or make room) and confirm again; the side without room is told so, the partner why.</summary>
    private void RefuseTradeForRoom(TradeSession session, PlayerSession sa, PlayerSession sb, bool roomA, bool roomB)
    {
        session.ConfirmA = false;
        session.ConfirmB = false;
        TellTradeRoom(sa, sb, roomA, roomB);
        TellTradeRoom(sb, sa, roomB, roomA);
        SendTradeUpdate(session);
    }

    private void TellTradeRoom(PlayerSession who, PlayerSession partner, bool room, bool partnerRoom)
    {
        if (!room)
        {
            Reject(who, "trade", "@srv.trade.no_room");
        }
        else if (!partnerRoom)
        {
            Reject(who, "trade", "@srv.trade.partner_no_room:" + partner.State.PlayerId);
        }
    }

    /// <summary>Pays one side's offer into the receiving pool. #3191: the dry run said everything fits, so nothing should
    /// be left over — should anything still be, it lies at the receiver's feet instead of vanishing.</summary>
    private void HandOverTradeGoods(PlayerSession receiver, MaterialPool pool, List<ItemAmount> goods)
    {
        foreach (var item in goods)
        {
            pool.Add(item.Item, item.Count);
        }

        SpillPoolOverflow(receiver, pool, receiver.State.Position.ToBlock());
    }

    /// <summary>Knowledge transfer (item 11): the giver KEEPS their points; the receiver gains, capped by the
    /// teach-up-to-your-level + give-once rule. Both credits are computed from the pre-transfer state so a mutual teach in
    /// one trade is symmetric, then applied and recorded in each giver's give-once ledger.</summary>
    private static void TransferTradeKnowledge(TradeSession session, PlayerSession sa, PlayerSession sb)
    {
        int creditAtoB = System.Math.Min(session.KnowledgeA, KnowledgeTeachable(sa.State, sb.State));
        int creditBtoA = System.Math.Min(session.KnowledgeB, KnowledgeTeachable(sb.State, sa.State));
        if (creditAtoB > 0)
        {
            sb.State.KnowledgePoints += creditAtoB;
            sa.State.KnowledgeGivenTo[sb.State.PlayerId] = (sa.State.KnowledgeGivenTo.TryGetValue(sb.State.PlayerId, out var ga) ? ga : 0) + creditAtoB;
        }

        if (creditBtoA > 0)
        {
            sa.State.KnowledgePoints += creditBtoA;
            sb.State.KnowledgeGivenTo[sa.State.PlayerId] = (sb.State.KnowledgeGivenTo.TryGetValue(sa.State.PlayerId, out var gb) ? gb : 0) + creditBtoA;
        }
    }

    /// <summary>Cancels any pending request or open trade involving a player (e.g. on disconnect).</summary>
    private void CancelTradesFor(string playerId)
    {
        _tradeRequests.Remove(playerId);
        foreach (var key in _tradeRequests.Where(kv => kv.Value == playerId).Select(kv => kv.Key).ToList())
        {
            _tradeRequests.Remove(key);

            // Withdraw the invitation on the target's screen too (#981): it is a MODAL window now — it frees
            // the cursor and pauses on-foot control — so a requester who walks off or crashes must not leave
            // an unanswerable dialog sitting in someone else's face.
            if (FindSessionByPlayerId(key) is { } target)
            {
                Send(target, new TradeClosed { Completed = false, Reason = "@srv.trade.partner_left" });
            }
        }

        if (_trades.FirstOrDefault(t => t.Involves(playerId)) is { } session)
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "@srv.trade.partner_left");
        }
    }

    private void SendTradeUpdate(TradeSession session)
    {
        SendTradeUpdateTo(session, session.A);
        SendTradeUpdateTo(session, session.B);
    }

    private void SendTradeUpdateTo(TradeSession session, string player)
    {
        if (FindSessionByPlayerId(player) is not { } s)
        {
            return;
        }

        var partner = FindSessionByPlayerId(session.Other(player));
        Send(s, new TradeUpdate
        {
            Partner = session.Other(player),
            MyOffer = session.OfferOf(player).Select(ToNetTradeItem).ToArray(),
            TheirOffer = session.OfferOf(session.Other(player)).Select(ToNetTradeItem).ToArray(),
            MyConfirmed = session.ConfirmOf(player),
            TheirConfirmed = session.ConfirmOf(session.Other(player)),
            MyKnowledgeOffered = session.KnowledgeOf(player),
            TheirKnowledgeOffered = session.KnowledgeOf(session.Other(player)),
            MyKnowledge = s.State.KnowledgePoints,
            MyKnowledgeMax = partner is null ? 0 : KnowledgeTeachable(s.State, partner.State),
        });
    }

    private void CloseTrade(TradeSession session, bool completed, string reason)
    {
        foreach (var p in new[] { session.A, session.B })
        {
            if (FindSessionByPlayerId(p) is { } s)
            {
                Send(s, new TradeClosed { Completed = completed, Reason = reason });
                if (completed)
                {
                    RaiseGameEvent(s, GameEventKeys.TradePlayer, session.Other(p)); // #2465: arg = the trade partner
                }
            }
        }
    }

    private static NetTradeItem ToNetTradeItem(ItemAmount a) => new() { Item = a.Item, Count = a.Count };

    private void HandleTradeRequest(PlayerSession session, TradeRequestIntent intent)
        => RequestTrade(session.State.PlayerId, intent.TargetPlayer);

    private void HandleTradeRespond(PlayerSession session, TradeRespondIntent intent)
        => RespondTrade(session.State.PlayerId, intent.Accept);

    private void HandleTradeOffer(PlayerSession session, TradeOfferIntent intent)
        => SetTradeOffer(session.State.PlayerId, intent.Items.Select(i => new ItemAmount(i.Item, i.Count)));

    private void HandleTradeKnowledge(PlayerSession session, TradeKnowledgeIntent intent)
        => SetTradeKnowledge(session.State.PlayerId, intent.Amount);

    private void HandleTradeConfirm(PlayerSession session) => ConfirmTrade(session.State.PlayerId);

    private void HandleTradeCancel(PlayerSession session) => CancelTrade(session.State.PlayerId);
}
