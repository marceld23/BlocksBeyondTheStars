// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The text-fit rules the screen tests share (the editors, #3263; the Test Pilot screens): a <see cref="Text"/> is
    /// measured with Unity's own <see cref="TextGenerator"/> in reference units — words are at least a given size (a
    /// glyph or a number may be smaller), a wrapping text breaks only between words (or between CJK characters) and is
    /// not cut, and a one-line text stays inside its box.
    /// </summary>
    internal static class UiTextFit
    {
        /// <summary>A one-line text may overhang its box by this much (the glyphs' side bearings).</summary>
        private const float OverhangTolerance = 3f;

        /// <summary>One line per text that breaks a rule: where it is, what it says, what is wrong.</summary>
        public static List<string> Problems(IEnumerable<Text> texts, int minWordSize)
        {
            var problems = new List<string>();
            foreach (var text in texts)
            {
                string problem = Check(text, minWordSize);
                if (problem != null)
                {
                    problems.Add($"{HierarchyPath(text.transform)} \"{Shorten(text.text)}\": {problem}");
                }
            }

            return problems;
        }

        /// <summary>The problem with one text, or null.</summary>
        public static string Check(Text text, int minWordSize)
        {
            string s = text.text ?? string.Empty;
            if (s.Trim().Length == 0 || (text.supportRichText && s.IndexOf('<') >= 0) || text.GetComponentInParent<InputField>() != null)
            {
                return null; // empty, rich text (tags skew the indices) or a field the player types into
            }

            var rt = text.rectTransform;
            var settings = text.GetGenerationSettings(rt.rect.size);
            settings.scaleFactor = 1f; // reference units, as UiKit measures — independent of the test window
            var generator = new TextGenerator();
            generator.Populate(s, settings);
            int size = text.resizeTextForBestFit ? generator.fontSizeUsedForBestFit : text.fontSize;
            if (IsWords(s) && size < minWordSize)
            {
                return $"{size} px — words are at least {minWordSize} px here";
            }

            if (text.horizontalOverflow == HorizontalWrapMode.Wrap)
            {
                if (text.verticalOverflow == VerticalWrapMode.Truncate && generator.characterCountVisible < s.Replace("\n", string.Empty).Length)
                {
                    return $"cut — only {generator.characterCountVisible} of {s.Length} characters fit {rt.rect.width:0}×{rt.rect.height:0}";
                }

                return BrokenWord(s, generator);
            }

            float width = generator.GetPreferredWidth(s, settings);
            return width > rt.rect.width + OverhangTolerance ? $"{width:0} px wide in a {rt.rect.width:0} px box at {size} px" : null;
        }

        /// <summary>A line that starts inside a word (not after a space or a line break, not at a CJK character).</summary>
        private static string BrokenWord(string s, TextGenerator generator)
        {
            var lines = new List<UILineInfo>();
            generator.GetLines(lines);
            for (int i = 1; i < lines.Count; i++)
            {
                int start = lines[i].startCharIdx;
                if (start <= 0 || start >= s.Length)
                {
                    continue;
                }

                char prev = s[start - 1], next = s[start];
                if (char.IsWhiteSpace(prev) || char.IsWhiteSpace(next) || ButtonLabelFit.HasCjk(prev.ToString()) || ButtonLabelFit.HasCjk(next.ToString())
                    || prev == '-' || prev == '/')
                {
                    continue;
                }

                return $"broken inside a word at \"{s.Substring(Math.Max(0, start - 8), Math.Min(16, s.Length - Math.Max(0, start - 8)))}\"";
            }

            return null;
        }

        /// <summary>True for a text with words: letters, more than three characters (a glyph, an arrow or "88°" is decoration).</summary>
        private static bool IsWords(string s) => s.Trim().Length > 3 && s.Count(char.IsLetter) >= 3;

        /// <summary>The locale table straight from the repository's <c>data/locales</c> — the source of truth, also in a
        /// checkout whose StreamingAssets copy was never synced.</summary>
        public static Dictionary<string, string> LocaleTable(GameLocale locale)
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "locales", locale.Code() + ".json"));
            if (!File.Exists(file))
            {
                Assert.Ignore($"locale file not found: {file}");
            }

            return ContentLoader.ParseLocaleTable(File.ReadAllText(file));
        }

        public static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && p.parent != null; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }

            return sb.ToString();
        }

        public static string Shorten(string s) => s.Length <= 60 ? s.Replace("\n", "⏎") : s.Substring(0, 57).Replace("\n", "⏎") + "…";

        /// <summary>The fit check measures glyphs; a run that cannot rasterize the font cannot judge it.</summary>
        public static void RequireFontRasterization()
        {
            var probe = new TextGenerator();
            probe.Populate("Ab", new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 15,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(200f, 50f),
                textAnchor = TextAnchor.MiddleLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            });
            if (probe.characterCount == 0 || probe.lineCount == 0)
            {
                Assert.Inconclusive("this run cannot rasterize the UI font — the fit check needs glyph metrics");
            }
        }
    }
}
