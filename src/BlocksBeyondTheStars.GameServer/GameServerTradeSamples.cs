// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3731: samples change hands in the player trade. An offer line whose base key is a sample or a mineral sample is a
/// <b>sample line</b>: it is counted against, taken from and paid into the <b>sample case</b> — never the backpack or the
/// hold, which is where every other line lives (<see cref="TradePool"/>). The trade messages are unchanged; an offer line
/// already carries the item key.
/// <list type="bullet">
/// <item>A sample line is valid when the register knows the seed and the case holds that many of exactly that key. A
/// sample key lying in the backpack (an admin <c>/give</c>) is no trade goods and does not count.</item>
/// <item>Both cases are dry-run before anything moves, like the backpacks (#3191): at most
/// <see cref="BioRules.SampleStack"/> of a kind and <see cref="BioRules.SampleCaseSlots"/> kinds, counting what leaves the
/// case in the same trade.</item>
/// <item>Receiving is not taking a sample: no <c>sample:taken</c> event, no first-sample hint. Knowledge does not travel —
/// the analysed state and the research book stay with each player.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One side's offer, split into what the trade pool handles and what the sample case handles.</summary>
    private readonly struct TradeLot
    {
        public TradeLot(IEnumerable<ItemAmount> offer)
        {
            Goods = new List<ItemAmount>();
            Samples = new List<ItemAmount>();
            foreach (var line in offer)
            {
                (IsTradeSample(line.Item) ? Samples : Goods).Add(line);
            }
        }

        public List<ItemAmount> Goods { get; }

        public List<ItemAmount> Samples { get; }
    }

    private static bool IsTradeSample(string item) => ItemKey.Base(item) is BioItems.Sample or BioItems.MineralSample;

    /// <summary>Whether a player's sample case holds every offered sample line: the register knows the seed, the key is
    /// the one the case files that species under, and the case holds at least that many.</summary>
    private bool HoldsTradeSamples(PlayerState p, List<ItemAmount> samples)
    {
        foreach (var line in samples)
        {
            if (BioEntry(ItemKey.Seed(line.Item)) is not { } entry || SampleKey(entry) != line.Item
                || p.SampleCase.CountOf(line.Item) < line.Count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The partner of an offer has to be able to name what is offered: sends them the species entry of every
    /// sample line they do not have yet — the entry a holder of that sample gets, nothing more (the analysed state in it
    /// is the partner's own).</summary>
    private void SendOfferedSpecies(TradeSession session, string offering, List<ItemAmount> samples)
    {
        if (samples.Count == 0 || FindSessionByPlayerId(session.Other(offering)) is not { } partner)
        {
            return;
        }

        foreach (var line in samples)
        {
            SendBioSpecies(partner, ItemKey.Seed(line.Item));
        }
    }

    /// <summary>The dry run of one sample case: after giving <paramref name="give"/> away, does everything in
    /// <paramref name="take"/> fit — a kind never above its stack, a new kind only into a free slot. Runs on a copy.</summary>
    private static bool SampleCaseCanExchange(PlayerState p, List<ItemAmount> give, List<ItemAmount> take)
    {
        if (take.Count == 0)
        {
            return true;
        }

        var copy = p.SampleCase.Clone();
        foreach (var line in give)
        {
            copy.Remove(line.Item, line.Count);
        }

        foreach (var line in take)
        {
            if (copy.CountOf(line.Item) + line.Count > BioRules.SampleStack || copy.Add(line.Item, line.Count, BioRules.SampleStack) > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A side whose sample case cannot take the offered samples is told so, the partner whose case it is.</summary>
    private void TellTradeSampleCase(PlayerSession who, PlayerSession partner, bool fits, bool partnerFits)
    {
        if (!fits)
        {
            Reject(who, "trade", "@srv.trade.sample_case_full");
        }
        else if (!partnerFits)
        {
            Reject(who, "trade", "@srv.trade.partner_sample_case_full:" + partner.State.PlayerId);
        }
    }

    /// <summary>Moves both sides' samples case to case. Both give before either takes — the order the dry run assumed.</summary>
    private void SwapTradeSamples(PlayerSession sa, PlayerSession sb, List<ItemAmount> fromA, List<ItemAmount> fromB)
    {
        TakeTradeSamples(sa, fromA);
        TakeTradeSamples(sb, fromB);
        HandOverTradeSamples(sa, sb, fromA);
        HandOverTradeSamples(sb, sa, fromB);
    }

    /// <summary>Takes the offered samples out of the giver's case and remembers the deposits among them: a deposit whose
    /// sample was traded away stays known to the giver, so its next block is not a "first block" again
    /// (<see cref="DepositKnownTo"/>).</summary>
    private void TakeTradeSamples(PlayerSession giver, List<ItemAmount> samples)
    {
        bool remembered = false;
        foreach (var line in samples)
        {
            giver.State.SampleCase.Remove(line.Item, line.Count);
            if (ItemKey.Base(line.Item) == BioItems.MineralSample)
            {
                remembered |= ResearchOf(giver.State.PlayerId).SamplesTradedAway.Add(ItemKey.Seed(line.Item));
            }
        }

        if (remembered)
        {
            SaveResearch(giver.State.PlayerId);
        }
    }

    /// <summary>Puts the samples into the receiver's case and sends them the species. This is deliberately not
    /// <see cref="GiveSample"/>: a sample handed over was not taken, so no story event fires and VEGA says nothing. The
    /// dry run said everything fits; should a sample still be left over, it goes back into the giver's case.</summary>
    private void HandOverTradeSamples(PlayerSession giver, PlayerSession receiver, List<ItemAmount> samples)
    {
        foreach (var line in samples)
        {
            int left = receiver.State.SampleCase.Add(line.Item, line.Count, BioRules.SampleStack);
            if (left > 0)
            {
                giver.State.SampleCase.Add(line.Item, left, BioRules.SampleStack);
                _log.Warn($"Trade: {left}x {line.Item} did not fit the sample case of '{receiver.State.Name}' and went back to '{giver.State.Name}'.");
            }

            SendBioSpecies(receiver, ItemKey.Seed(line.Item));
        }
    }

    /// <summary>Whether a deposit is known to a player without a sample of it in the case: they analysed it, or (#3731)
    /// they traded a sample of it away. Throwing a sample away is not remembered — that keeps re-arming the first block.</summary>
    private bool DepositKnownTo(string playerId, uint seed)
    {
        var research = ResearchOf(playerId);
        return research.Analysed.Contains(seed) || research.SamplesTradedAway.Contains(seed);
    }
}
