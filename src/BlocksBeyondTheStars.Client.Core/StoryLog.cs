// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Core
{
    /// <summary>
    /// Rebuilds the Story tab's re-readable logs from a <c>StoryStateMessage</c> snapshot (#1110/#1111): the
    /// server sends only the found KEYS (per save / per player); category, site and text keys resolve from the
    /// active story pack, in the pack's order. Unknown keys (a pack changed between sessions) are dropped
    /// rather than rendered as raw keys. Pure — unit-tested headless in Client.Tests.
    /// </summary>
    public static class StoryLog
    {
        /// <summary>The found net fragments as (category, textKey) rows, in pack order.</summary>
        public static List<(string Category, string TextKey)> Fragments(StoryDefinition? pack, IReadOnlyCollection<string>? foundKeys)
        {
            var rows = new List<(string, string)>();
            if (pack is null || foundKeys is null || foundKeys.Count == 0)
            {
                return rows;
            }

            var found = new HashSet<string>(foundKeys, StringComparer.Ordinal);
            foreach (var f in pack.Fragments)
            {
                if (found.Contains(f.Key) && !string.IsNullOrEmpty(f.TextKey))
                {
                    rows.Add((f.Category, f.TextKey));
                }
            }

            return rows;
        }

        /// <summary>This player's unlocked personal memories as textKeys, in pack (= unlock) order.</summary>
        public static List<string> Memories(StoryDefinition? pack, IReadOnlyCollection<string>? memoryKeys)
        {
            var rows = new List<string>();
            if (pack is null || memoryKeys is null || memoryKeys.Count == 0)
            {
                return rows;
            }

            var mine = new HashSet<string>(memoryKeys, StringComparer.Ordinal);
            foreach (var m in pack.Memories)
            {
                if (mine.Contains(m.Key) && !string.IsNullOrEmpty(m.TextKey))
                {
                    rows.Add(m.TextKey);
                }
            }

            return rows;
        }

        /// <summary>This player's found environmental lore as (site, textKey) rows, in pack order.</summary>
        public static List<(string Site, string TextKey)> Lore(StoryDefinition? pack, IReadOnlyCollection<string>? loreKeys)
        {
            var rows = new List<(string, string)>();
            if (pack is null || loreKeys is null || loreKeys.Count == 0)
            {
                return rows;
            }

            var mine = new HashSet<string>(loreKeys, StringComparer.Ordinal);
            foreach (var l in pack.LoreSites)
            {
                if (mine.Contains(l.Key) && !string.IsNullOrEmpty(l.TextKey))
                {
                    rows.Add((l.Site, l.TextKey));
                }
            }

            return rows;
        }
    }
}
