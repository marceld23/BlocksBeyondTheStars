// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>An item + quantity in a trade offer.</summary>
public sealed class NetTradeItem
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Client asks another nearby player to trade.</summary>
public sealed class TradeRequestIntent
{
    public string TargetPlayer { get; set; } = string.Empty;
}

/// <summary>Client accepts/declines a pending trade request.</summary>
public sealed class TradeRespondIntent
{
    public bool Accept { get; set; }
}

/// <summary>Server tells a player that someone nearby wants to trade with them (#981) — the counterpart of
/// <see cref="DockRequestNotice"/>. Before this, the invitation was only a chat/toast line, and the client
/// had no way at all to answer it: the trade could never be opened. A pre-#981 client drops the unknown tag
/// and still sees the accompanying <see cref="ServerMessage"/>, so no protocol bump is needed.</summary>
public sealed class TradeRequestNotice
{
    public string Requester { get; set; } = string.Empty;
}

/// <summary>Client sets the items on its side of the open trade (replaces the previous offer).</summary>
public sealed class TradeOfferIntent
{
    public NetTradeItem[] Items { get; set; } = System.Array.Empty<NetTradeItem>();
}

/// <summary>Client sets the knowledge points it offers to teach on its side of the open trade (item 11).
/// The giver never loses points; the server caps it to what may still be taught to this partner.</summary>
public sealed class TradeKnowledgeIntent
{
    public int Amount { get; set; }
}

/// <summary>Client confirms ("ready") the current trade; the swap happens when both sides confirm.</summary>
public sealed class TradeConfirmIntent { }

/// <summary>Client cancels the open trade (or a pending request).</summary>
public sealed class TradeCancelIntent { }

/// <summary>Server view of the open trade for one participant (both offers + both ready states).</summary>
public sealed class TradeUpdate
{
    public string Partner { get; set; } = string.Empty;
    public NetTradeItem[] MyOffer { get; set; } = System.Array.Empty<NetTradeItem>();
    public NetTradeItem[] TheirOffer { get; set; } = System.Array.Empty<NetTradeItem>();
    public bool MyConfirmed { get; set; }
    public bool TheirConfirmed { get; set; }

    /// <summary>Knowledge points each side is teaching in this trade (item 11; the giver keeps theirs).</summary>
    public int MyKnowledgeOffered { get; set; }
    public int TheirKnowledgeOffered { get; set; }

    /// <summary>My current knowledge total, and the most I can still teach this partner (give-once cap +
    /// can't raise them above my own level) — drives the trade panel's knowledge control.</summary>
    public int MyKnowledge { get; set; }
    public int MyKnowledgeMax { get; set; }
}

/// <summary>A trade ended — completed (items swapped) or cancelled.</summary>
public sealed class TradeClosed
{
    public bool Completed { get; set; }
    public string Reason { get; set; } = string.Empty;
}
