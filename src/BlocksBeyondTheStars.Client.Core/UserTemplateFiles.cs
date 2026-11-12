// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3518/#3523: the structure editor's designs and its generator entries share the user-content folders —
    /// <c>usercontent/&lt;kind&gt;_templates/&lt;key&gt;.json</c> and <c>usercontent/landmark_templates/&lt;key&gt;.json</c> — so
    /// saving one under the key of the other would overwrite it without a word. A file is a generator entry when it names a
    /// <c>source</c>; the editor refuses a save whose key a file of the OTHER kind holds (<see cref="HoldsOtherKind"/>).
    /// </summary>
    public static class UserTemplateFiles
    {
        private static readonly JsonDocumentOptions Options = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Whether a template file's text is a generator entry (a non-empty <c>source</c>, any letter case); null when
        /// it is empty or does not read as a JSON object.</summary>
        public static bool? IsEntry(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json, Options);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(p.Name, "source", StringComparison.OrdinalIgnoreCase))
                    {
                        return p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString());
                    }
                }

                return false;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>True when the file already at the save's path (<paramref name="existingJson"/>) is of the other kind than the
        /// save — a design over an entry or an entry over a design. An absent or unreadable file never blocks (it is overwritten,
        /// as before).</summary>
        public static bool HoldsOtherKind(string? existingJson, bool savingEntry)
            => IsEntry(existingJson) is { } entry && entry != savingEntry;
    }
}
