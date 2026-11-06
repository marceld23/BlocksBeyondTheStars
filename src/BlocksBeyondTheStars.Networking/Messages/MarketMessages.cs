// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3292, the flower set): the trader discount this player trades with right now — the best of their
/// own worn discount and that of an ally wearing flowers near them on the same world. Pushed when it changes (looked at
/// once a second), so the vendor screen and the crafting menu price a market trade exactly as the server will
/// (<c>MarketPricing.Discounted</c>). A new tag with no protocol bump: an older client drops it and shows the full price
/// (the server still charges the discounted one — never more than shown).
/// </summary>
public sealed class TraderDiscountInfo
{
    /// <summary>The discount in whole percent, 0..25 (0 = none).</summary>
    public int Percent { get; set; }

    /// <summary>The ally whose flowers give it, or empty when the player's own gear does (or there is none).</summary>
    public string FromName { get; set; } = string.Empty;
}
