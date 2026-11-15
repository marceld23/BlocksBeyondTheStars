// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How serious an editor's status message is — it picks the colour and the mark of the status bar (#3688).
    /// Before, every editor wrote its errors in the green of "saved".</summary>
    public enum EditorStatusSeverity
    {
        /// <summary>Neutral information ("Loaded 'My cruiser'.").</summary>
        Info,

        /// <summary>Something worked ("Saved."). The only green one.</summary>
        Success,

        /// <summary>It worked, but look here ("Give it a name first.").</summary>
        Warning,

        /// <summary>It did not work. Red, never green.</summary>
        Error,
    }

    /// <summary>
    /// The text rules of the editors' status bar (#3688): no file paths for the player. A message that still carries one
    /// (an exception text, an old locale line with "{2}" = the export folder) shows only the last part of it — the file or
    /// folder name — so a child never reads <c>C:\Users\…\AppData\LocalLow\…</c> and the user name in it never ends up in a
    /// screenshot.
    /// </summary>
    public static class EditorStatusText
    {
        // An absolute path runs to the end of its line or to a quote / closing bracket: Windows paths may hold spaces (the
        // user's own name), and keeping the rest of the line as the "last part" degrades gracefully — "Exported to C:\x\pack.
        // Restart." becomes "Exported to pack. Restart.".
        private static readonly Regex PathPattern = new Regex(
            @"(?:file:/+|\\\\|\b[A-Za-z]:[\\/]|(?<=^|[\s""'(\[“«‚„])~?/[^\s/""']+/)[^\r\n""'’“”«»)\]]*",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(200));

        /// <summary><paramref name="text"/> with every absolute path cut to its last part (file or folder name).</summary>
        public static string WithoutPaths(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            try
            {
                return PathPattern.Replace(text, m => LastPart(m.Value));
            }
            catch (RegexMatchTimeoutException)
            {
                return text!;
            }
        }

        /// <summary>The part after the last separator, trailing blanks kept; "…" when nothing is left (a bare drive or
        /// root). A folder path that ended in a separator before the sentence went on ("…/textures/ in the browser") keeps
        /// its folder name and the rest of the sentence.</summary>
        private static string LastPart(string path)
        {
            string core = path.TrimEnd();
            string trailing = path.Substring(core.Length);
            string tail = string.Empty;
            int cut = LastSeparator(core);
            if (cut >= 0 && cut + 1 < core.Length && char.IsWhiteSpace(core[cut + 1]))
            {
                tail = core.Substring(cut + 1);
                core = core.Substring(0, cut);
            }

            core = core.TrimEnd('\\', '/');
            cut = LastSeparator(core);
            string last = cut >= 0 ? core.Substring(cut + 1) : core;
            bool bare = last.Length == 0 || (last.Length == 2 && last[1] == ':') || last.EndsWith("file:", StringComparison.Ordinal);
            return (bare ? "…" : last) + tail + trailing;
        }

        private static int LastSeparator(string text) => Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
    }
}
