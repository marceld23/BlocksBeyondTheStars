// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>
    /// Picks the next background track for a context: a <b>shuffle bag</b> per pool (every track of the pool
    /// plays once, in random order, before anything repeats), a <b>shared filler bag</b> for the neutral
    /// tracks that may be blended into biome pools, and a short <b>cross-context history</b> so a neutral
    /// that just played on the surface does not pop up again right after entering a cave (#1172).
    ///
    /// Replaces the old <c>PickFrom</c> in <c>ClientMusic</c>, which only avoided the track that had just
    /// ended — a two-track pool was a strict A-B-A-B alternation. Pure (no UnityEngine), so it is unit-tested
    /// headless; the Unity director owns the RNG and the pool contents and only asks for the next name.
    ///
    /// Bag reconciliation: the caller passes the <em>current</em> pool on every call (pools change with the
    /// time of day and when a track file turns out to be missing) — entries that left the pool are dropped
    /// from the bag, entries that joined are shuffled in.
    /// </summary>
    public sealed class MusicPicker
    {
        /// <summary>Bag key of the shared neutral-filler rotation.</summary>
        public const string FillerBag = "*fillers";

        private readonly Random _rng;
        private readonly int _historySize;
        private readonly Dictionary<string, List<string>> _bags = new(StringComparer.Ordinal);
        private readonly LinkedList<string> _history = new();

        public MusicPicker(Random rng, int historySize = 4)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _historySize = Math.Max(0, historySize);
        }

        /// <summary>The most recent picks, newest last (exposed for tests/diagnostics).</summary>
        public IEnumerable<string> History => _history;

        /// <summary>
        /// The next track for <paramref name="bagKey"/>: with probability <paramref name="fillerShare"/> (and
        /// only when both sets are non-empty) a track from the shared filler rotation, otherwise the next one
        /// from the pool's own bag. <paramref name="avoid"/> (the track that is playing / just ended) is never
        /// returned while any alternative exists. Returns <c>null</c> when both sets are empty.
        /// </summary>
        public string? Next(string bagKey, IReadOnlyList<string> primary, IReadOnlyList<string> fillers, double fillerShare, string? avoid)
        {
            bool havePrimary = primary != null && primary.Count > 0;
            bool haveFillers = fillers != null && fillers.Count > 0;
            if (!havePrimary && !haveFillers)
            {
                return null;
            }

            bool useFiller = haveFillers && (!havePrimary || (fillerShare > 0.0 && _rng.NextDouble() < fillerShare));
            return useFiller
                ? Draw(FillerBag, fillers!, avoid)
                : Draw(bagKey, primary!, avoid);
        }

        /// <summary>Forget every bag and the history (e.g. when a new world is joined).</summary>
        public void Reset()
        {
            _bags.Clear();
            _history.Clear();
        }

        private string Draw(string key, IReadOnlyList<string> candidates, string? avoid)
        {
            if (!_bags.TryGetValue(key, out var bag))
            {
                bag = new List<string>();
                _bags[key] = bag;
            }

            // Reconcile with the current pool: drop what left, shuffle in what joined.
            bag.RemoveAll(n => !Contains(candidates, n));
            var joined = new List<string>();
            foreach (var c in candidates)
            {
                if (!bag.Contains(c) && !joined.Contains(c))
                {
                    joined.Add(c);
                }
            }

            if (bag.Count == 0)
            {
                // A fresh bag holds every candidate once; `joined` already is that set (minus duplicates).
                Shuffle(joined);
                bag.AddRange(joined);
            }
            else if (joined.Count > 0)
            {
                Shuffle(joined);
                bag.AddRange(joined);
                Shuffle(bag);
            }

            // Prefer an entry that is neither the current track nor in the recent history; then one that is
            // at least not the current track; else whatever is left (single-track pools loop in place).
            int index = bag.FindIndex(n => n != avoid && !_history.Contains(n));
            if (index < 0)
            {
                index = bag.FindIndex(n => n != avoid);
            }

            if (index < 0)
            {
                index = 0;
            }

            string pick = bag[index];
            bag.RemoveAt(index);
            Remember(pick);
            return pick;
        }

        private void Remember(string name)
        {
            if (_historySize == 0)
            {
                return;
            }

            _history.AddLast(name);
            while (_history.Count > _historySize)
            {
                _history.RemoveFirst();
            }
        }

        private void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
