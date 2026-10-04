// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which kinds of sample the sample case lists (#2300).</summary>
    public enum SampleKindFilter
    {
        All = 0,
        Plants = 1,
        Animals = 2,
        Deposits = 3,
    }

    /// <summary>
    /// Which effects the sample case lists (#2300): every sample, one <see cref="BioEffectFamily"/> (the same numbers),
    /// or the samples nobody has analysed yet — deposits included.
    /// </summary>
    public enum SampleEffectFilter
    {
        All = 0,
        Movement = 1,
        Protection = 2,
        Work = 3,
        Survival = 4,
        Senses = 5,
        NotAnalysed = 6,
    }

    /// <summary>One kind of sample in the case, with what the player may know of it.</summary>
    public sealed class SampleCaseEntry
    {
        /// <summary>The full sample key as it lies in the case.</summary>
        public string Item { get; set; } = string.Empty;
        public uint Seed { get; set; }
        public BioKind Kind { get; set; }
        public int Count { get; set; }
        public bool Analysed { get; set; }

        /// <summary>The effect of an analysed plant or animal; <see cref="BioEffect.None"/> for anything else — an
        /// unanalysed sample keeps its secret, as the lab card does.</summary>
        public BioEffect Effect { get; set; }
        public int Level { get; set; }
        public BioEffectFamily Family { get; set; }

        /// <summary>The research book's entry for the species; null when the book does not hold it.</summary>
        public NetBioSpecies? Species { get; set; }
    }

    /// <summary>
    /// The sample case as the lists show it (#2299/#2300): one entry per kind, filtered and in a fixed order — plants,
    /// animals, deposits; inside a kind the unanalysed samples first (they still need a look), then by effect family,
    /// effect, level (highest first) and name. The bio lab's case, its slot pickers and the inventory's Samples page
    /// all read it, so they list alike. Unity-free, so the headless tests pin it.
    /// The filter is one for the whole case and lives for the session only (static, never saved), so the lab and the
    /// inventory show the same choice.
    /// </summary>
    public static class SampleCaseView
    {
        /// <summary>The kind filter of this session.</summary>
        public static SampleKindFilter Kind { get; set; }

        /// <summary>The effect filter of this session.</summary>
        public static SampleEffectFilter Effect { get; set; }

        /// <summary>Whether a filter narrows the list right now.</summary>
        public static bool Filtered => Kind != SampleKindFilter.All || Effect != SampleEffectFilter.All;

        /// <summary>The effect filter after <paramref name="current"/>: All, the five families, Not analysed, All again.</summary>
        public static SampleEffectFilter NextEffect(SampleEffectFilter current)
            => current >= SampleEffectFilter.NotAnalysed ? SampleEffectFilter.All : current + 1;

        /// <summary>The kind a sample key and its species stand for: a mineral sample is a deposit, anything else the
        /// species' kind (a plant when the book does not know it).</summary>
        public static BioKind KindOf(string item, NetBioSpecies? species)
            => ItemKey.Base(item) == BioItems.MineralSample ? BioKind.Mineral
                : species != null && species.Kind == (int)BioKind.Animal ? BioKind.Animal
                : BioKind.Plant;

        /// <summary>Every kind of sample in the case, in case order. Two stacks of one key count as one entry.</summary>
        public static List<SampleCaseEntry> Entries(BioClientState bio)
        {
            var list = new List<SampleCaseEntry>();
            var byItem = new Dictionary<string, SampleCaseEntry>(StringComparer.Ordinal);
            foreach (var s in bio.Samples)
            {
                if (s == null || s.Count <= 0 || string.IsNullOrEmpty(s.Item))
                {
                    continue;
                }

                if (byItem.TryGetValue(s.Item, out var known))
                {
                    known.Count += s.Count;
                    continue;
                }

                uint seed = ItemKey.Seed(s.Item);
                bio.Species.TryGetValue(seed, out var species);
                var entry = new SampleCaseEntry
                {
                    Item = s.Item,
                    Seed = seed,
                    Kind = KindOf(s.Item, species),
                    Count = s.Count,
                    Analysed = bio.Analysed(seed),
                    Species = species,
                };

                if (entry.Analysed && entry.Kind != BioKind.Mineral && bio.ProfileOf(seed) is { } profile)
                {
                    entry.Effect = profile.Effect;
                    entry.Level = profile.Level;
                    entry.Family = BioRules.Family(profile.Effect);
                }

                byItem[s.Item] = entry;
                list.Add(entry);
            }

            return list;
        }

        /// <summary>Whether an entry passes a kind filter.</summary>
        public static bool MatchesKind(SampleCaseEntry entry, SampleKindFilter kind) => kind switch
        {
            SampleKindFilter.Plants => entry.Kind == BioKind.Plant,
            SampleKindFilter.Animals => entry.Kind == BioKind.Animal,
            SampleKindFilter.Deposits => entry.Kind == BioKind.Mineral,
            _ => true,
        };

        /// <summary>Whether an entry passes an effect filter. A family takes only analysed plants and animals of it;
        /// "Not analysed" takes every unanalysed sample, deposits too.</summary>
        public static bool MatchesEffect(SampleCaseEntry entry, SampleEffectFilter effect) => effect switch
        {
            SampleEffectFilter.All => true,
            SampleEffectFilter.NotAnalysed => !entry.Analysed,
            _ => entry.Analysed && entry.Family == (BioEffectFamily)(int)effect,
        };

        /// <summary>How many entries a kind chip stands for under the current effect filter.</summary>
        public static int Count(IReadOnlyList<SampleCaseEntry> entries, SampleKindFilter kind, SampleEffectFilter effect)
        {
            int n = 0;
            foreach (var e in entries)
            {
                if (MatchesKind(e, kind) && MatchesEffect(e, effect))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The entries that pass both filters, in the lists' order. <paramref name="nameOf"/> names an entry
        /// the way the list shows it (the order inside equal effects).</summary>
        public static List<SampleCaseEntry> Ordered(IEnumerable<SampleCaseEntry> entries, SampleKindFilter kind,
            SampleEffectFilter effect, Func<SampleCaseEntry, string> nameOf)
        {
            var list = new List<SampleCaseEntry>();
            foreach (var e in entries)
            {
                if (MatchesKind(e, kind) && MatchesEffect(e, effect))
                {
                    list.Add(e);
                }
            }

            var names = new Dictionary<SampleCaseEntry, string>();
            foreach (var e in list)
            {
                names[e] = nameOf(e) ?? string.Empty;
            }

            list.Sort((a, b) =>
            {
                int c = ((int)a.Kind).CompareTo((int)b.Kind);
                if (c == 0) c = a.Analysed.CompareTo(b.Analysed); // unanalysed (false) first
                if (c == 0) c = ((int)a.Family).CompareTo((int)b.Family);
                if (c == 0) c = ((int)a.Effect).CompareTo((int)b.Effect);
                if (c == 0) c = b.Level.CompareTo(a.Level); // the strongest first
                if (c == 0) c = string.Compare(names[a], names[b], StringComparison.CurrentCultureIgnoreCase);
                if (c == 0) c = a.Seed.CompareTo(b.Seed);
                return c;
            });
            return list;
        }

        /// <summary>The case under this session's filter, in the lists' order.</summary>
        public static List<SampleCaseEntry> Current(BioClientState bio, Func<SampleCaseEntry, string> nameOf)
            => Ordered(Entries(bio), Kind, Effect, nameOf);
    }
}
