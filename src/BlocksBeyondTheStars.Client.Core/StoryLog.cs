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

        /// <summary>The beats this player has heard as text keys, in arc order (#2462, B08). <paramref name="heard"/> holds
        /// beat indexes (<c>StoryStateMessage.HeardBeats</c> plus any heard live this session); indexes the pack does not
        /// have are dropped, like every other unknown key here.</summary>
        public static List<string> Beats(StoryDefinition? pack, IEnumerable<int>? heard)
        {
            var rows = new List<string>();
            if (pack is null || heard is null)
            {
                return rows;
            }

            var mine = new HashSet<int>(heard);
            if (mine.Count == 0)
            {
                return rows;
            }

            var beats = new List<StoryBeat>(pack.Beats);
            beats.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var b in beats)
            {
                if (mine.Contains(b.Index) && !string.IsNullOrEmpty(b.TextKey))
                {
                    rows.Add(b.TextKey);
                }
            }

            return rows;
        }

        /// <summary>The index of the pack beat whose narrator line is <paramref name="lineKey"/>, or -1 when the line is
        /// not a beat — an insight, the Guardian reveal, a relay line, one of VEGA's old memories (#2462, B08).</summary>
        public static int BeatIndexOf(StoryDefinition? pack, string? lineKey)
        {
            if (pack is null || string.IsNullOrEmpty(lineKey))
            {
                return -1;
            }

            foreach (var b in pack.Beats)
            {
                if (string.Equals(b.TextKey, lineKey, StringComparison.Ordinal))
                {
                    return b.Index;
                }
            }

            return -1;
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

    /// <summary>
    /// The Story tab's beat list (#2462, B08). It used to collect every narrator line (ShipAiLine Kind 2) of the session:
    /// empty after a rejoin, and mixed with insights, the Guardian reveal, relay lines and VEGA's old memories. Now it is
    /// rebuilt from the server's <c>StoryStateMessage.HeardBeats</c> and holds only the pack's beats, in arc order. Beats
    /// heard live are kept on top of the snapshot — the join sends the snapshot BEFORE its catch-up lines — and an older
    /// server (no <c>HeardBeats</c>) therefore still gets this session's beats, as before. Without the pack (a story this
    /// client does not have) it falls back to collecting the lines. Pure — unit-tested headless in Client.Tests.
    /// </summary>
    public sealed class StoryBeatLog
    {
        private readonly HashSet<int> _snapshot = new HashSet<int>();
        private readonly HashSet<int> _live = new HashSet<int>();
        private string _storyId = string.Empty;

        /// <summary>The beats as text keys, in arc order — what the Story tab lists.</summary>
        public List<string> Keys { get; } = new List<string>();

        /// <summary>A story snapshot arrived: its heard beats replace the previous snapshot. Another story id than before
        /// (an admin switched the story) starts the log afresh.</summary>
        public void OnStoryState(StoryDefinition? pack, string? storyId, IEnumerable<int>? heardBeats)
        {
            string id = storyId ?? string.Empty;
            if (!string.Equals(id, _storyId, StringComparison.Ordinal))
            {
                if (_storyId.Length > 0)
                {
                    _live.Clear();
                    Keys.Clear();
                }
                else if (pack is not null)
                {
                    // The first snapshot of the session: lines collected before it (no pack known yet) become indexes.
                    foreach (var key in Keys)
                    {
                        int index = StoryLog.BeatIndexOf(pack, key);
                        if (index >= 0)
                        {
                            _live.Add(index);
                        }
                    }
                }

                _storyId = id;
            }

            _snapshot.Clear();
            if (heardBeats is not null)
            {
                _snapshot.UnionWith(heardBeats);
            }

            Rebuild(pack);
        }

        /// <summary>A narrator line (ShipAiLine Kind 2) arrived. True when it is one of the pack's beats — it is in the
        /// list now; false for any other story line (an insight, the Guardian reveal, a relay line, an old memory), which
        /// does not belong in the beat list. Without the pack every line counts, as before #2462.</summary>
        public bool OnStoryLine(StoryDefinition? pack, string? lineKey)
        {
            if (string.IsNullOrEmpty(lineKey))
            {
                return false;
            }

            if (pack is null)
            {
                if (!Keys.Contains(lineKey!))
                {
                    Keys.Add(lineKey!);
                }

                return true;
            }

            int index = StoryLog.BeatIndexOf(pack, lineKey);
            if (index < 0)
            {
                return false;
            }

            _live.Add(index);
            Rebuild(pack);
            return true;
        }

        private void Rebuild(StoryDefinition? pack)
        {
            if (pack is null)
            {
                return; // no pack to resolve indexes — keep the lines collected so far
            }

            var heard = new HashSet<int>(_snapshot);
            heard.UnionWith(_live);
            Keys.Clear();
            Keys.AddRange(StoryLog.Beats(pack, heard));
        }
    }
}
