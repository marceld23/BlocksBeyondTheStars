// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet-type page of world creation (#2970, Justus: "if I put one at 100 %, all the others are at 0 %"). Unity-free,
    /// so the shortcuts and the share column are unit-tested; the Unity page (<c>UiWorldOptions.BuildAdvanced</c>) only draws
    /// them. A row's value is a <see cref="Frequency"/> index (Off · Very rare · Rare · Normal · Frequent, the page's slider
    /// steps); a row the player never touched follows the type's data weight like the universe generator does — an exotic
    /// type scaled by the main page's "Exotic worlds" slider.
    /// </summary>
    public static class PlanetTypeMix
    {
        /// <summary>One selectable planet type as the page lists it.</summary>
        public readonly struct Row
        {
            public Row(string key, int spawnWeight, bool exotic)
            {
                Key = key;
                SpawnWeight = spawnWeight;
                Exotic = exotic;
            }

            public string Key { get; }

            public int SpawnWeight { get; }

            public bool Exotic { get; }
        }

        /// <summary>"Only this": the chosen type Frequent, every other listed type Off — the whole map is written, so the
        /// server's roll sees nothing else.</summary>
        public static void OnlyThis(IDictionary<string, int> overrides, IEnumerable<Row> rows, string key)
        {
            foreach (var row in rows)
            {
                overrides[row.Key] = row.Key == key ? (int)Frequency.Frequent : (int)Frequency.Off;
            }
        }

        /// <summary>"All normal": every listed type at Normal — one even mix, unlike "Reset", which goes back to the data
        /// weights.</summary>
        public static void AllNormal(IDictionary<string, int> overrides, IEnumerable<Row> rows)
        {
            foreach (var row in rows)
            {
                overrides[row.Key] = (int)Frequency.Normal;
            }
        }

        /// <summary>The weight one row brings into the planet-type roll — the override's frequency weight, else the data
        /// weight (an exotic type scaled like <c>UniverseGenerator</c> does with the exotic slider).</summary>
        public static int Weight(IReadOnlyDictionary<string, int> overrides, Row row, int exoticIndex)
        {
            if (overrides.TryGetValue(row.Key, out var index))
            {
                return ((Frequency)Math.Max(0, Math.Min(index, (int)Frequency.Frequent))).Weight();
            }

            int weight = Math.Max(0, row.SpawnWeight);
            return row.Exotic ? (int)Math.Round(weight * ExoticFactor(exoticIndex)) : weight;
        }

        /// <summary>Each row's share of the mix in whole percent (0 for a type that never rolls). The shares come from the
        /// same weights the generator rolls with, so they are what a big galaxy averages out to; rounding can leave the
        /// column a point off 100.</summary>
        public static Dictionary<string, int> SharesPercent(IReadOnlyDictionary<string, int> overrides, IReadOnlyList<Row> rows, int exoticIndex)
        {
            var shares = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            foreach (var row in rows)
            {
                total += Weight(overrides, row, exoticIndex);
            }

            foreach (var row in rows)
            {
                int w = Weight(overrides, row, exoticIndex);
                shares[row.Key] = total <= 0 || w <= 0 ? 0 : Math.Max(1, (int)Math.Round(100.0 * w / total));
            }

            return shares;
        }

        /// <summary>The exotic slider's factor (mirrors <c>UniverseGenerator.BuildGenerationWeights</c>).</summary>
        private static double ExoticFactor(int exoticIndex) => (Frequency)exoticIndex switch
        {
            Frequency.Off => 0.0,
            Frequency.VeryRare => 0.34,
            Frequency.Rare => 0.6,
            Frequency.Frequent => 2.5,
            _ => 1.0,
        };
    }
}
