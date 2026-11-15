// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The envelope of the paint and shape editors' drafts (#3689): the texture editor, the appearance / avatar designer,
    /// the block paint tool, the form and tool-look editors and the in-game shape designer each write their open work
    /// through <c>IEditorDocument.SaveDraft</c> as one header line plus their own payload:
    /// <code>bbts-draft &lt;kind&gt; v&lt;version&gt;
    /// &lt;payload&gt;</code>
    /// The header names which editor wrote it and in which layout, so a draft written by an older (or newer) game version
    /// is recognised and refused instead of being misread — the shell then sets it aside and the editor opens fresh.
    /// Unity-free so the rule is tested headless.
    /// </summary>
    public static class CanvasDraftFormat
    {
        /// <summary>The first word of every draft header.</summary>
        public const string Magic = "bbts-draft";

        /// <summary>The draft of <paramref name="kind"/> (<c>texture</c>, <c>form</c>, …) in layout
        /// <paramref name="version"/>; null when there is no payload (nothing worth keeping — the shell removes the draft).</summary>
        public static string? Wrap(string kind, int version, string? payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            return Magic + " " + Clean(kind) + " v" + Math.Max(1, version) + "\n" + payload;
        }

        /// <summary>
        /// Reads a draft written by <see cref="Wrap"/>: true when it is a <paramref name="kind"/> draft in a layout this
        /// editor reads (1 … <paramref name="maxVersion"/>), with its <paramref name="version"/> and <paramref name="payload"/>.
        /// False for anything else — another editor's draft, a newer layout, a missing header, an empty payload.
        /// Never throws.
        /// </summary>
        public static bool TryUnwrap(string? draft, string kind, int maxVersion, out int version, out string payload)
        {
            version = 0;
            payload = string.Empty;
            if (string.IsNullOrEmpty(draft))
            {
                return false;
            }

            int newline = draft!.IndexOf('\n');
            if (newline <= 0 || newline == draft.Length - 1)
            {
                return false;
            }

            string[] header = draft.Substring(0, newline).TrimEnd('\r').Split(' ');
            if (header.Length != 3 || header[0] != Magic || header[1] != Clean(kind) || header[2].Length < 2 || header[2][0] != 'v'
                || !int.TryParse(header[2].Substring(1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int v)
                || v < 1 || v > maxVersion)
            {
                return false;
            }

            version = v;
            payload = draft.Substring(newline + 1);
            return true;
        }

        /// <summary>A kind is one word: spaces and line breaks would break the header.</summary>
        private static string Clean(string? kind)
        {
            string k = string.IsNullOrWhiteSpace(kind) ? "editor" : kind!.Trim();
            return k.Replace(' ', '_').Replace('\n', '_').Replace('\r', '_');
        }
    }
}
