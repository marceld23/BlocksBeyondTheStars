// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3520: what the ship editor writes into <c>usercontent/ship_types/&lt;key&gt;.json</c> beside its export — the rules the
    /// Unity-side writer (<c>ShipEditor.UserContent.cs</c>) follows, kept here so they are tested headless.
    /// <list type="bullet">
    /// <item><b>The key.</b> A key the game reserves (<c>custom</c>, <c>starter</c>) or a shipped ship or layout has would be
    /// dropped by the server's loader, so the copy goes under the first free <c>&lt;key&gt;_2</c>, <c>_3</c>, … instead
    /// (<see cref="FreeKey"/>) and the editor says so.</item>
    /// <item><b>The uses.</b> A file already there keeps its <c>uses</c> list — also an empty one (the ship only lives in the
    /// editor). <c>JsonUtility</c> reads a missing list and <c>[]</c> alike, so the raw text decides
    /// (<see cref="NamesUses"/>); a file without the field gets <see cref="UserShipUses.Default"/>.</item>
    /// </list>
    /// </summary>
    public static class UserShipFileRules
    {
        /// <summary>The longest key the server's loader accepts.</summary>
        public const int MaxKeyLength = 64;

        private static readonly Regex UsesField = new("(?<!\\\\)\"uses\"\\s*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary><paramref name="key"/> itself when <paramref name="isTaken"/> says it is free, else the first
        /// <c>&lt;key&gt;_n</c> (n = 2, 3, …) that is — cut so it stays within <see cref="MaxKeyLength"/>.</summary>
        public static string FreeKey(string key, Func<string, bool> isTaken)
        {
            if (isTaken is null)
            {
                throw new ArgumentNullException(nameof(isTaken));
            }

            string k = (key ?? string.Empty).Trim();
            if (!isTaken(k))
            {
                return k;
            }

            for (int n = 2; ; n++)
            {
                string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
                string head = k.Length + suffix.Length > MaxKeyLength ? k.Substring(0, MaxKeyLength - suffix.Length) : k;
                if (!isTaken(head + suffix))
                {
                    return head + suffix;
                }
            }
        }

        /// <summary>True when the raw JSON of a user ship file has a <c>uses</c> field (an empty list counts).</summary>
        public static bool NamesUses(string? json) => !string.IsNullOrEmpty(json) && UsesField.IsMatch(json);

        /// <summary>The uses a re-save writes: the old file's list when it named one (<paramref name="oldJson"/>, read as
        /// <paramref name="oldUses"/>; an empty list stays empty), else <see cref="UserShipUses.Default"/>.</summary>
        public static List<string> UsesToWrite(string? oldJson, IReadOnlyList<string>? oldUses)
            => NamesUses(oldJson) ? new List<string>(oldUses ?? Array.Empty<string>()) : new List<string>(UserShipUses.Default);
    }
}
