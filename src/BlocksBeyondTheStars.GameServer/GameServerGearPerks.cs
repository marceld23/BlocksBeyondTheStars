// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What two of the worn sets DO besides looking the part (Justus' ideas, v2026.11.7):
/// <list type="bullet">
/// <item><b>The robber disguise</b> (#3291): a lone robber who walks up to a player wearing the mask AND the jacket takes
/// them for one of his own — he says so (<c>@srv.bandit.disguised</c>), demands nothing and walks off, and the player
/// gets the quiet spell a payment would buy. The gate sits where the demand would open (<c>BeginBanditDemand</c>), so the
/// robber's walk up and his leaving are the ordinary ones. Camp guards stay hostile (they guard, they do not mug), bandit
/// ships in space are not fooled, and attacking a robber still starts a fight.</item>
/// <item><b>The flower set's trader discount</b> (#3292): every market trade costs less — the inputs shrink by the
/// discount (<see cref="MarketPricing"/>, one rule for server and client). A player trades with the BEST of their own worn
/// discount and that of an ally (<see cref="AreAllied"/>: alliance or crew) wearing flowers within
/// <see cref="TraderDiscountAllyRange"/> on the same world — never several added up; companions do not count. The client
/// learns the value from <see cref="TraderDiscountInfo"/>, pushed when it changes (looked at once a second), so the
/// vendor screen shows the price the server takes.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3292: how near (same world) an ally wearing flowers must stand for their discount to count.</summary>
    internal const float TraderDiscountAllyRange = 32f;

    /// <summary>#3292: how often each player's discount is looked at for the push.</summary>
    private const double TraderDiscountCheckSeconds = 1.0;

    /// <summary>#3291: the quiet spell after a robber let a disguised player pass — the same as after paying him.</summary>
    private const double DisguisedPassQuietSeconds = 1200.0;

    // ---------------- The robber disguise (#3291) ----------------

    /// <summary>Whether the player wears the full robber disguise (mask and jacket).</summary>
    private bool BanditDisguised(PlayerState p) => SuitEquipment.BanditDisguised(_content.Items.Values, key => Wears(p, key));

    /// <summary>A robber reached a player he meant to hold up. In the full disguise he takes them for one of his own: he
    /// says so, walks off and leaves the player in peace for as long as a payment would. False (nothing done) without
    /// the disguise — the hold-up then goes on as ever.</summary>
    private bool LetDisguisedMarkPass(PlayerSession mark, CombatEntity bandit)
    {
        if (!BanditDisguised(mark.State))
        {
            return false;
        }

        Send(mark, new ServerMessage { Text = "@srv.bandit.disguised:" + bandit.Name });
        BeginBanditLeave(bandit);
        mark.Bandit.NextBanditAmbushAt = _uptime + DisguisedPassQuietSeconds;
        BroadcastPlanetEnemies();
        return true;
    }

    // ---------------- The trader discount (#3292) ----------------

    /// <summary>The discount of the player's own worn gear, in whole percent (0..25).</summary>
    private int OwnTraderDiscountPercent(PlayerState p)
        => MarketPricing.ToPercent(SuitEquipment.TraderDiscount(_content.Items.Values, key => Wears(p, key)));

    /// <summary>The discount <paramref name="session"/> trades with right now, in whole percent, and the ally it comes
    /// from (empty = the player's own gear or none): the best of their own and every ally's within
    /// <see cref="TraderDiscountAllyRange"/> on the same world. A tie keeps the player's own.</summary>
    private (int Percent, string FromName) TraderDiscountFor(PlayerSession session)
    {
        var me = session.State;
        int best = OwnTraderDiscountPercent(me);
        string from = string.Empty;
        double range2 = (double)TraderDiscountAllyRange * TraderDiscountAllyRange;
        foreach (var other in _sessions.Values)
        {
            if (ReferenceEquals(other, session) || !other.Joined || other.Spectating
                || other.CurrentLocationId != session.CurrentLocationId
                || !AreAllied(me.PlayerId, other.State.PlayerId))
            {
                continue;
            }

            int theirs = OwnTraderDiscountPercent(other.State);
            if (theirs > best && WrapDistSq(me.Position, other.State.Position) <= range2)
            {
                best = theirs;
                from = other.State.Name;
            }
        }

        return (best, from);
    }

    /// <summary>The inputs of ONE <paramref name="recipe"/> as <paramref name="session"/> pays them: discounted for a
    /// market trade (<see cref="MarketPricing.Inputs"/>), the recipe's own for everything else. Also brings the client's
    /// shown discount up to date when a trade finds it stale.</summary>
    private IReadOnlyList<ItemAmount> PricedInputs(PlayerSession session, RecipeDefinition recipe)
    {
        if (recipe.Station != CraftingStation.Market)
        {
            return recipe.Inputs;
        }

        var (percent, from) = TraderDiscountFor(session);
        PushTraderDiscount(session, percent, from);
        return MarketPricing.Inputs(recipe, MarketPricing.FromPercent(percent));
    }

    /// <summary>Per world, once a second per player: push the discount when it changed (gear put on or taken off, an ally
    /// walking up or away, an alliance formed or ended).</summary>
    private void TickTraderDiscounts(double dt)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            session.Sync.TraderDiscountCheckIn -= dt;
            if (session.Sync.TraderDiscountCheckIn > 0)
            {
                continue;
            }

            session.Sync.TraderDiscountCheckIn = TraderDiscountCheckSeconds;
            var (percent, from) = TraderDiscountFor(session);
            PushTraderDiscount(session, percent, from);
        }
    }

    /// <summary>Sends <see cref="TraderDiscountInfo"/> when it differs from what the client was last told (nothing is
    /// sent while there never was a discount — the client starts at none).</summary>
    private void PushTraderDiscount(PlayerSession session, int percent, string from)
    {
        if (percent == session.Sync.SentTraderDiscount && from == session.Sync.SentTraderDiscountFrom)
        {
            return;
        }

        session.Sync.SentTraderDiscount = percent;
        session.Sync.SentTraderDiscountFrom = from;
        Send(session, new TraderDiscountInfo { Percent = percent, FromName = from });
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3292): the discount (whole percent) and its source a player trades with right now.</summary>
    public (int Percent, string FromName) TraderDiscountForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return (0, string.Empty);
        }

        Serve(session);
        return TraderDiscountFor(session);
    }

    /// <summary>Test seam (#3292): one pass of the per-world discount push over the first joined player's world, with the
    /// check due for everybody.</summary>
    public void TickTraderDiscountsForTest()
    {
        PlayerSession? first = null;
        foreach (var session in _sessions.Values)
        {
            session.Sync.TraderDiscountCheckIn = 0;
            first ??= session.Joined ? session : null;
        }

        if (first != null)
        {
            Serve(first);
            TickTraderDiscounts(TraderDiscountCheckSeconds);
        }
    }
}
