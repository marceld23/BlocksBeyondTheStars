// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The sample case as the bio lab, its pickers and the inventory list it (#2299/#2300): one entry per kind, plants →
/// animals → deposits, the unanalysed first, then by effect family, effect and level; and the two filters, which
/// never show an effect the player has not analysed.
/// </summary>
public sealed class SampleCaseViewTests
{
    private static NetBioSpecies Species(uint seed, BioKind kind, bool analysed, ulong tags = 0)
        => new()
        {
            Seed = seed,
            Kind = (int)kind,
            Name = "Species " + seed,
            OriginBodyName = "Kepler",
            HasContext = kind != BioKind.Mineral,
            Tags = tags,
            Carrier = (int)BioCarrier.Fibre,
            MaterialItem = kind == BioKind.Mineral ? "iron_ore" : string.Empty,
            Analysed = analysed,
        };

    private static string Key(NetBioSpecies s)
        => ItemKey.WithSeed(s.Kind == (int)BioKind.Mineral ? BioItems.MineralSample : BioItems.Sample, s.Seed);

    /// <summary>A case with twelve analysed plants (their effects follow from the seed), two unanalysed plants, two
    /// animals (one analysed) and two deposits (one analysed).</summary>
    private static BioClientState Case(out List<NetBioSpecies> species)
    {
        species = new List<NetBioSpecies>();
        for (uint seed = 100; seed < 112; seed++)
        {
            species.Add(Species(seed, BioKind.Plant, analysed: true, tags: (ulong)(seed % 7 == 0 ? BioTag.Lava | BioTag.Hot : BioTag.Water)));
        }

        species.Add(Species(200, BioKind.Plant, analysed: false));
        species.Add(Species(201, BioKind.Plant, analysed: false));
        species.Add(Species(300, BioKind.Animal, analysed: true, tags: (ulong)BioTag.Fast));
        species.Add(Species(301, BioKind.Animal, analysed: false));
        species.Add(Species(400, BioKind.Mineral, analysed: true));
        species.Add(Species(401, BioKind.Mineral, analysed: false));

        var bio = new BioClientState();
        bio.OnBook(new BioBook { Full = true, Species = species.ToArray() });

        // Deposits first and in reverse: the case's slot order must not decide the list's.
        var slots = species.AsEnumerable().Reverse().Select((s, i) => new NetItemStack { Slot = i, Item = Key(s), Count = 2 }).ToList();
        slots.Add(new NetItemStack { Slot = slots.Count, Item = Key(species[0]), Count = 3 }); // a second stack of one kind
        bio.OnInventory(new InventoryUpdate { Samples = slots.ToArray() });
        return bio;
    }

    private static string Name(SampleCaseEntry e) => e.Species?.Name ?? e.Item;

    [Fact]
    public void Entries_AreOnePerKind_AndAnEffectShowsOnlyOnceAnalysed()
    {
        var bio = Case(out var species);
        var entries = SampleCaseView.Entries(bio);

        Assert.Equal(species.Count, entries.Count);
        Assert.Equal(5, entries.Single(e => e.Seed == 100).Count); // two stacks of one kind are one entry

        foreach (var e in entries)
        {
            var s = species.Single(x => x.Seed == e.Seed);
            Assert.Equal((BioKind)s.Kind, e.Kind);
            Assert.Equal(s.Analysed, e.Analysed);
            if (!e.Analysed || e.Kind == BioKind.Mineral)
            {
                // Unanalysed keeps its secret (the client could derive it — the list must not tell); a deposit has traits.
                Assert.Equal(BioEffect.None, e.Effect);
                Assert.Equal(BioEffectFamily.None, e.Family);
            }
            else
            {
                Assert.Equal(bio.ProfileOf(e.Seed)!.Effect, e.Effect);
                Assert.Equal(bio.ProfileOf(e.Seed)!.Level, e.Level);
                Assert.Equal(BioRules.Family(e.Effect), e.Family);
                Assert.NotEqual(BioEffectFamily.None, e.Family);
            }
        }
    }

    [Fact]
    public void TheOrder_IsPlantsAnimalsDeposits_UnanalysedFirst_ThenFamilyEffectAndStrongestLevel()
    {
        var bio = Case(out _);
        var list = SampleCaseView.Ordered(SampleCaseView.Entries(bio), SampleKindFilter.All, SampleEffectFilter.All, Name);

        Assert.Equal(new[] { 200u, 201u }, list.Take(2).Select(e => e.Seed));   // the unanalysed plants lead
        Assert.Equal(new[] { 301u, 300u }, list.Skip(14).Take(2).Select(e => e.Seed)); // then the animals
        Assert.Equal(new[] { 401u, 400u }, list.Skip(16).Select(e => e.Seed));  // the deposits last

        for (int i = 1; i < list.Count; i++)
        {
            var a = list[i - 1];
            var b = list[i];
            Assert.True(a.Kind <= b.Kind);
            if (a.Kind != b.Kind || a.Analysed != b.Analysed)
            {
                continue;
            }

            Assert.True(a.Family <= b.Family, $"{a.Family} before {b.Family}");
            if (a.Family == b.Family && a.Effect == b.Effect)
            {
                Assert.True(a.Level >= b.Level);
            }
            else if (a.Family == b.Family)
            {
                Assert.True(a.Effect < b.Effect);
            }
        }
    }

    [Fact]
    public void TheFilters_Combine_AndTheCountsMatchWhatTheListShows()
    {
        var entries = SampleCaseView.Entries(Case(out _));

        var plants = SampleCaseView.Ordered(entries, SampleKindFilter.Plants, SampleEffectFilter.All, Name);
        Assert.Equal(14, plants.Count);
        Assert.All(plants, e => Assert.Equal(BioKind.Plant, e.Kind));

        // "Not analysed" covers every kind, deposits too.
        var open = SampleCaseView.Ordered(entries, SampleKindFilter.All, SampleEffectFilter.NotAnalysed, Name);
        Assert.Equal(new[] { 200u, 201u, 301u, 401u }, open.Select(e => e.Seed));
        Assert.Single(SampleCaseView.Ordered(entries, SampleKindFilter.Deposits, SampleEffectFilter.NotAnalysed, Name));

        foreach (var family in BioRules.Families)
        {
            var filter = (SampleEffectFilter)(int)family;
            var shown = SampleCaseView.Ordered(entries, SampleKindFilter.All, filter, Name);
            Assert.All(shown, e => Assert.True(e.Analysed && e.Family == family && e.Kind != BioKind.Mineral));
            Assert.Equal(entries.Count(e => e.Analysed && e.Family == family), shown.Count);
            Assert.Equal(shown.Count, SampleCaseView.Count(entries, SampleKindFilter.All, filter));
            Assert.Equal(shown.Count(e => e.Kind == BioKind.Animal), SampleCaseView.Count(entries, SampleKindFilter.Animals, filter));
        }

        // Every analysed plant or animal sits in exactly one family; nothing is lost between the filters.
        int inFamilies = BioRules.Families.Sum(f => SampleCaseView.Count(entries, SampleKindFilter.All, (SampleEffectFilter)(int)f));
        Assert.Equal(13, inFamilies);
        Assert.Equal(entries.Count, SampleCaseView.Count(entries, SampleKindFilter.All, SampleEffectFilter.All));
    }

    [Fact]
    public void TheEffectButton_StepsThroughEveryFamily_ThenNotAnalysed_ThenBackToAll()
    {
        var seen = new List<SampleEffectFilter>();
        var at = SampleEffectFilter.All;
        do
        {
            seen.Add(at);
            at = SampleCaseView.NextEffect(at);
        }
        while (at != SampleEffectFilter.All && seen.Count < 20);

        Assert.Equal(7, seen.Count);
        Assert.Equal(SampleEffectFilter.NotAnalysed, seen[^1]);
        Assert.Equal(BioRules.Families.Select(f => (SampleEffectFilter)(int)f), seen.Skip(1).Take(5));
    }
}
