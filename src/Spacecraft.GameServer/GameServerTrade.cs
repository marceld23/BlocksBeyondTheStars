using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;

namespace Spacecraft.GameServer;

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

    public bool Involves(string p) => p == A || p == B;
    public string Other(string p) => p == A ? B : A;
    public List<ItemAmount> OfferOf(string p) => p == A ? OfferA : OfferB;
    public bool ConfirmOf(string p) => p == A ? ConfirmA : ConfirmB;
    public void SetConfirm(string p, bool v) { if (p == A) ConfirmA = v; else ConfirmB = v; }
}

public sealed partial class GameServer
{
    private const float TradeRange = 8f;

    private readonly List<TradeSession> _trades = new();
    private readonly Dictionary<string, string> _tradeRequests = new(); // target -> requester

    /// <summary>The open trade a player is in, if any (test/inspection).</summary>
    public TradeSession? ActiveTrade(string playerId) => _trades.FirstOrDefault(t => t.Involves(playerId));

    /// <summary>Both players are joined and close enough to see each other (same world, within range).</summary>
    private bool CanTradeTogether(string a, string b)
    {
        var sa = FindSessionByPlayerId(a);
        var sb = FindSessionByPlayerId(b);
        if (sa is null || sb is null || !sa.Joined || !sb.Joined)
        {
            return false;
        }

        return sa.State.Position.DistanceSquared(sb.State.Position) <= TradeRange * TradeRange;
    }

    public void RequestTrade(string fromPlayer, string toPlayer)
    {
        var from = FindSessionByPlayerId(fromPlayer);
        if (from is null)
        {
            return;
        }

        if (fromPlayer == toPlayer || ActiveTrade(fromPlayer) is not null || ActiveTrade(toPlayer) is not null)
        {
            Reject(from, "trade", "Can't start a trade right now.");
            return;
        }

        if (!CanTradeTogether(fromPlayer, toPlayer))
        {
            Reject(from, "trade", "The other player must be next to you.");
            return;
        }

        _tradeRequests[toPlayer] = fromPlayer;
        if (FindSessionByPlayerId(toPlayer) is { } to)
        {
            Send(to, new ServerMessage { Text = $"{fromPlayer} wants to trade." });
        }
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
            if (FindSessionByPlayerId(requester) is { } r)
            {
                Send(r, new TradeClosed { Completed = false, Reason = "Trade declined." });
            }

            return;
        }

        var session = new TradeSession { A = requester, B = player };
        _trades.Add(session);
        SendTradeUpdate(session);
    }

    public void SetTradeOffer(string player, IEnumerable<ItemAmount> items)
    {
        var session = ActiveTrade(player);
        var s = FindSessionByPlayerId(player);
        if (session is null || s is null)
        {
            return;
        }

        var offer = items.Where(i => i.Count > 0).Select(i => new ItemAmount(i.Item, i.Count)).ToList();
        var pool = new MaterialPool(_content, s.State, _ship);
        if (!pool.Has(offer))
        {
            Reject(s, "trade", "You don't have those items to offer.");
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
            CloseTrade(session, completed: false, "Trade cancelled.");
        }
    }

    private void CommitTrade(TradeSession session)
    {
        var sa = FindSessionByPlayerId(session.A);
        var sb = FindSessionByPlayerId(session.B);

        if (sa is null || sb is null || !CanTradeTogether(session.A, session.B))
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "Trade partner is no longer in range.");
            return;
        }

        var poolA = new MaterialPool(_content, sa.State, _ship);
        var poolB = new MaterialPool(_content, sb.State, _ship);

        // Re-validate both sides still hold their offers (they could have used items meanwhile).
        if (!poolA.Has(session.OfferA) || !poolB.Has(session.OfferB))
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "Offered items are no longer available.");
            return;
        }

        // Atomic swap: take from each side, give to the other (returning anything that doesn't fit).
        poolA.Remove(session.OfferA);
        poolB.Remove(session.OfferB);
        foreach (var item in session.OfferA)
        {
            int leftover = poolB.Add(item.Item, item.Count);
            if (leftover > 0) poolA.Add(item.Item, leftover); // recipient full → hand it back
        }

        foreach (var item in session.OfferB)
        {
            int leftover = poolA.Add(item.Item, item.Count);
            if (leftover > 0) poolB.Add(item.Item, leftover);
        }

        _trades.Remove(session);
        SendInventory(sa);
        SendInventory(sb);
        CloseTrade(session, completed: true, "Trade complete.");
    }

    /// <summary>Cancels any pending request or open trade involving a player (e.g. on disconnect).</summary>
    private void CancelTradesFor(string playerId)
    {
        _tradeRequests.Remove(playerId);
        foreach (var key in _tradeRequests.Where(kv => kv.Value == playerId).Select(kv => kv.Key).ToList())
        {
            _tradeRequests.Remove(key);
        }

        if (_trades.FirstOrDefault(t => t.Involves(playerId)) is { } session)
        {
            _trades.Remove(session);
            CloseTrade(session, completed: false, "Trade partner left.");
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

        Send(s, new TradeUpdate
        {
            Partner = session.Other(player),
            MyOffer = session.OfferOf(player).Select(ToNetTradeItem).ToArray(),
            TheirOffer = session.OfferOf(session.Other(player)).Select(ToNetTradeItem).ToArray(),
            MyConfirmed = session.ConfirmOf(player),
            TheirConfirmed = session.ConfirmOf(session.Other(player)),
        });
    }

    private void CloseTrade(TradeSession session, bool completed, string reason)
    {
        foreach (var p in new[] { session.A, session.B })
        {
            if (FindSessionByPlayerId(p) is { } s)
            {
                Send(s, new TradeClosed { Completed = completed, Reason = reason });
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

    private void HandleTradeConfirm(PlayerSession session) => ConfirmTrade(session.State.PlayerId);

    private void HandleTradeCancel(PlayerSession session) => CancelTrade(session.State.PlayerId);
}
