// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Converts the Codex articles' small HTML subset (the bodies authored for the old browser wiki —
    /// <c>&lt;p&gt; &lt;ul&gt; &lt;li&gt; &lt;b&gt; &lt;i&gt; &lt;span class="link"&gt;</c>) into Unity uGUI
    /// rich text, so the native Wiki (Stream D) can render the same content without the embedded browser.
    /// Pure string processing — no UnityEngine — so it lives in Client.Core and is unit-tested headless.
    ///
    /// uGUI's <see cref="UnityEngine.UI.Text"/> rich text understands <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>/
    /// <c>&lt;color&gt;</c>/<c>&lt;size&gt;</c> but NOT block tags, so paragraphs become blank-line breaks and
    /// list items become bulleted lines; cross-link spans keep their text (tinted) but drop the navigation.
    /// </summary>
    public static class WikiMarkup
    {
        /// <summary>uGUI rich-text colour tag wrapped around a cross-link's visible text (a cyan hint; the
        /// click navigation itself is not wired in the native renderer yet).</summary>
        private const string LinkColor = "#6fd8ff";

        // A short match timeout guards the substitutions against pathological input (MA0009 / ReDoS).
        private static readonly TimeSpan RxTimeout = TimeSpan.FromSeconds(1);

        private static string Rx(string input, string pattern, string replacement)
            => Regex.Replace(input, pattern, replacement, RegexOptions.IgnoreCase, RxTimeout);

        /// <summary>#2259: one piece of an article body — text (still in the HTML subset) or a picture.</summary>
        public sealed class Segment
        {
            public Segment(string text, string image)
            {
                Text = text;
                Image = image;
            }

            /// <summary>The text of a text piece (empty for a picture).</summary>
            public string Text { get; }

            /// <summary>The picture's path under the data folder (<c>wiki/img/…png</c>), or empty for text.</summary>
            public string Image { get; }

            public bool IsImage => Image.Length > 0;
        }

        // <img src="wiki/img/name.png"> — only plain relative PNG paths under wiki/img/ (no "..", no scheme).
        private static readonly Regex ImgTag = new Regex(@"<\s*img\b[^>]*\bsrc\s*=\s*""(?<src>[^""]*)""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, RxTimeout);

        private static readonly Regex SafeImagePath = new Regex(@"^wiki/img/[a-z0-9_\-]+\.png$", RegexOptions.None, RxTimeout);

        /// <summary>#2259: cuts an article body at its pictures, in order. A picture whose path is not a plain
        /// <c>wiki/img/*.png</c> is dropped (the text around it stays). A body without pictures is one text piece.</summary>
        public static IReadOnlyList<Segment> Segments(string? html)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(html))
            {
                return result;
            }

            int at = 0;
            foreach (Match m in ImgTag.Matches(html))
            {
                if (m.Index > at)
                {
                    result.Add(new Segment(html!.Substring(at, m.Index - at), string.Empty));
                }

                string src = m.Groups["src"].Value.Trim();
                if (SafeImagePath.IsMatch(src))
                {
                    result.Add(new Segment(string.Empty, src));
                }

                at = m.Index + m.Length;
            }

            if (at < html!.Length)
            {
                result.Add(new Segment(html.Substring(at), string.Empty));
            }

            return result;
        }

        public static string ToUnityRichText(string? html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            string s = html;

            // Block structure → newlines / bullets.
            s = Rx(s, @"<\s*p\s*>", string.Empty);
            s = Rx(s, @"<\s*/\s*p\s*>", "\n\n");
            s = Rx(s, @"<\s*ul\s*>", string.Empty);
            s = Rx(s, @"<\s*/\s*ul\s*>", "\n");
            s = Rx(s, @"<\s*li\s*>", "• ");
            s = Rx(s, @"<\s*/\s*li\s*>", "\n");
            s = Rx(s, @"<\s*br\s*/?\s*>", "\n");

            // Cross-link spans → keep the inner text, tinted; drop the (un-clickable) wrapper.
            s = Rx(s, @"<\s*span\b[^>]*>", "<color=" + LinkColor + ">");
            s = Rx(s, @"<\s*/\s*span\s*>", "</color>");

            // Drop any remaining tags EXCEPT the rich-text ones uGUI supports (b/i/color/size).
            s = Rx(s, @"<\s*/?\s*(?!(?:b|i|color|size)\b)[a-zA-Z][^>]*>", string.Empty);

            s = DecodeEntities(s);
            return CollapseBlankLines(s).Trim();
        }

        private static string DecodeEntities(string s)
        {
            // The article bodies only use a handful of named/numeric entities.
            return s.Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&#39;", "'")
                    .Replace("&nbsp;", " ");
        }

        /// <summary>Collapses runs of 3+ newlines to a single blank line and trims trailing spaces per line, so
        /// the paragraph/list substitutions above don't pile up vertical gaps.</summary>
        private static string CollapseBlankLines(string s)
        {
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = Rx(s, @"[ \t]+\n", "\n");
            s = Rx(s, @"\n{3,}", "\n\n");
            var sb = new StringBuilder(s.Length);
            foreach (var line in s.Split('\n'))
            {
                sb.Append(line.TrimEnd()).Append('\n');
            }

            return sb.ToString();
        }
    }
}
