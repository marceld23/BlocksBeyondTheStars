// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// What a report says about the textures the player was looking at (#1964). Since players can repaint the
    /// game — for themselves, and admins for a whole world — "the grass looks wrong" is no longer a statement
    /// about the game's own grass. The report therefore names the keys that were replaced, never the pixels.
    ///
    /// An IMMUTABLE snapshot: the crash reporter builds its body on the log callback's thread, where the live
    /// texture layers (main thread only) must not be touched — it reads the last snapshot instead.
    /// </summary>
    public sealed class TexturePackReportInfo
    {
        /// <summary>Keys listed per layer. A pack can hold hundreds; the count tells the rest.</summary>
        public const int MaxListedKeys = 24;

        /// <summary>The snapshot of an untouched game.</summary>
        public static readonly TexturePackReportInfo None = new TexturePackReportInfo(true, Array.Empty<string>(), 0, true, Array.Empty<string>(), 0);

        private readonly string[] _localKeys;
        private readonly string[] _worldKeys;

        private TexturePackReportInfo(bool localEnabled, string[] localKeys, int localCount, bool worldShown, string[] worldKeys, int worldCount)
        {
            LocalEnabled = localEnabled;
            _localKeys = localKeys;
            LocalCount = localCount;
            WorldShown = worldShown;
            _worldKeys = worldKeys;
            WorldCount = worldCount;
        }

        /// <summary>The player's "use my texture pack" setting.</summary>
        public bool LocalEnabled { get; }

        public int LocalCount { get; }

        /// <summary>The player's "show this world's textures" setting.</summary>
        public bool WorldShown { get; }

        public int WorldCount { get; }

        public IReadOnlyList<string> LocalKeys => _localKeys;

        public IReadOnlyList<string> WorldKeys => _worldKeys;

        /// <summary>True when the player saw at least one texture that is not the game's own.</summary>
        public bool AnythingReplaced => (LocalEnabled && LocalCount > 0) || (WorldShown && WorldCount > 0);

        public static TexturePackReportInfo Create(bool localEnabled, IEnumerable<string>? localKeys, bool worldShown, IEnumerable<string>? worldKeys)
        {
            string[] local = Sorted(localKeys);
            string[] world = Sorted(worldKeys);
            return new TexturePackReportInfo(
                localEnabled, local.Take(MaxListedKeys).ToArray(), local.Length,
                worldShown, world.Take(MaxListedKeys).ToArray(), world.Length);
        }

        private static string[] Sorted(IEnumerable<string>? keys)
            => keys == null ? Array.Empty<string>() : keys.Where(k => !string.IsNullOrEmpty(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        /// <summary>Adds the <c>texturePack</c> node to a report's diagnostic snapshot.</summary>
        public void WriteTo(IDictionary<string, object> reportJson)
        {
            if (reportJson == null)
            {
                return;
            }

            reportJson["texturePack"] = new Dictionary<string, object>
            {
                ["replaced"] = AnythingReplaced,
                ["localEnabled"] = LocalEnabled,
                ["localCount"] = LocalCount,
                ["localKeys"] = _localKeys,
                ["worldShown"] = WorldShown,
                ["worldCount"] = WorldCount,
                ["worldKeys"] = _worldKeys,
            };
        }
    }
}
