// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// NPC facial hair (#2123, "some with no beard, some a beard or a moustache"): the pick is deterministic per face seed,
/// androids never get any (they wear a grille), about 40 % of civilians are clean-shaven and every style shows up — and
/// the roll does not follow the seed bits that decide baldness and the hair tone.
/// </summary>
public sealed class NpcLooksTests
{
    private const int Samples = 10_000;

    /// <summary>Face seeds the way NpcView derives them: id · 486187739 XOR a name hash.</summary>
    private static IEnumerable<int> Seeds()
    {
        for (int id = 1; id <= Samples; id++)
        {
            yield return unchecked((id * 486187739) ^ ("Npc" + id).Aggregate(17, (h, c) => h * 31 + c));
        }
    }

    [Fact]
    public void ThePick_IsDeterministic()
    {
        foreach (int seed in Seeds().Take(500))
        {
            Assert.Equal(NpcLooks.FacialHairFor(seed, robot: false), NpcLooks.FacialHairFor(seed, robot: false));
            Assert.Equal(NpcLooks.LowerFaceFor(seed, robot: false), NpcLooks.LowerFaceFor(seed, robot: false));
        }
    }

    [Fact]
    public void Androids_HaveNoFacialHair_ButAGrille()
    {
        foreach (int seed in Seeds().Take(2000))
        {
            Assert.Equal(FacialHair.None, NpcLooks.FacialHairFor(seed, robot: true));
            Assert.Equal(LowerFace.Grille, NpcLooks.LowerFaceFor(seed, robot: true));
        }
    }

    [Fact]
    public void Civilians_NeverWearThePlayersBreatherStrip_OrAGrille()
    {
        foreach (int seed in Seeds().Take(2000))
        {
            var face = NpcLooks.LowerFaceFor(seed, robot: false);
            Assert.NotEqual(LowerFace.Breather, face);
            Assert.NotEqual(LowerFace.Grille, face);
        }
    }

    /// <summary>#2433: about two thirds clean-shaven (was 40 % after #2123 — Justus asked for "only some" with a beard);
    /// every style still shows up with a fair share of the remaining third.</summary>
    [Fact]
    public void AboutTwoThirdsAreCleanShaven_AndEveryStyleHasAFairShare()
    {
        var counts = Seeds().GroupBy(s => NpcLooks.FacialHairFor(s, robot: false)).ToDictionary(g => g.Key, g => g.Count());
        foreach (FacialHair style in Enum.GetValues(typeof(FacialHair)))
        {
            double share = counts.TryGetValue(style, out int n) ? n / (double)Samples : 0.0;
            Assert.InRange(share, 0.06, 0.70);
        }

        Assert.InRange(counts[FacialHair.None] / (double)Samples, 0.61, 0.69);
        Assert.Equal(65, NpcLooks.NonePercent);
    }

    [Fact]
    public void TheRoll_IsIndependentOfBaldness()
    {
        // NpcView: (seed & 7) == 0 means bald. A bald head wears a beard as often as anyone.
        var bald = Seeds().Where(s => (s & 0x7) == 0).ToList();
        var haired = Seeds().Where(s => (s & 0x7) != 0).ToList();
        double baldBare = bald.Count(s => NpcLooks.FacialHairFor(s, robot: false) == FacialHair.None) / (double)bald.Count;
        double hairedBare = haired.Count(s => NpcLooks.FacialHairFor(s, robot: false) == FacialHair.None) / (double)haired.Count;
        Assert.InRange(baldBare, 0.55, 0.75);
        Assert.InRange(Math.Abs(baldBare - hairedBare), 0.0, 0.08);
    }
}
