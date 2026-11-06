// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Text that fits its box and stays readable (#3263) — opt-in, the editors first. <see cref="AddButton"/> keeps its
    /// Best Fit for the rest of the game; the editors' buttons use <see cref="AddFitButton"/> /
    /// <see cref="FitButtonLabel"/> instead, which follow <see cref="ButtonLabelFit"/>: one line down to 14 px, else
    /// two lines broken only between words at 13 px or more, else a warning in the log — never the 11 px and the
    /// "Spiegel|n" of the old rule. The headless <c>EditorTextFitTests</c> check every editor label against the same
    /// rule in all 14 languages.
    /// </summary>
    public static partial class UiKit
    {
        /// <summary>The smallest font size of any editor text (#3263) — decorative glyphs and numbers aside.</summary>
        public const int EditorMinFontSize = ButtonLabelFit.TwoLineMin;

        private static readonly HashSet<string> UnfitLabelsWarned = new HashSet<string>();

        /// <summary>Below this width a button without an icon centres its label over the whole button (a glyph button).</summary>
        private const float NarrowButtonWidth = 60f;

        /// <summary><see cref="AddButton"/> with its label fitted by <see cref="FitButtonLabel"/> — the editors' button.</summary>
        public static Button AddFitButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick, string icon = null)
        {
            var button = AddButton(parent, x, y, w, h, label, onClick, icon);
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null && string.IsNullOrEmpty(icon) && w < NarrowButtonWidth)
            {
                // A small glyph button (−, +, →, ×): AddButton's 18 px left inset + 10 px right margin leave a 28 px button a
                // zero-wide label box, so the glyph had no room at all. Centre it over the whole button instead.
                Place(text.gameObject, 2f, 0f, w - 4f, h);
                text.alignment = TextAnchor.MiddleCenter;
            }

            FitButtonLabel(text);
            return button;
        }

        /// <summary>
        /// Fits a button label by <see cref="ButtonLabelFit"/> (one line ≥ 14 px, else two lines without a broken word
        /// at ≥ 13 px, else a logged warning) — and again whenever its text or size changes, so a label that toggles
        /// ("Play"/"Stop", a picked value) stays fitted.
        /// </summary>
        public static void FitButtonLabel(Text label, int maxSize = ButtonLabelFit.MaxSize)
            => FitLabelLines(label, maxSize, ButtonLabelFit.SingleLineMin, ButtonLabelFit.TwoLineMin);

        /// <summary>
        /// <see cref="FitButtonLabel"/> with its own sizes — e.g. a hint line: <paramref name="maxSize"/> down to
        /// <paramref name="singleLineMin"/> on one line, then up to <paramref name="maxLines"/> lines at
        /// <paramref name="twoLineMin"/> or more. The rect must be tall enough for them (<see cref="ButtonLabelFit.LinesHeight"/>).
        /// </summary>
        public static void FitLabelLines(Text label, int maxSize, int singleLineMin, int twoLineMin, int maxLines = 2)
        {
            if (label == null)
            {
                return;
            }

            string fittedText = null;
            Vector2 fittedBox = default;

            void Refit()
            {
                if (label == null)
                {
                    return;
                }

                var box = label.rectTransform.rect.size;
                if (box.x <= 0f || (label.text == fittedText && box == fittedBox))
                {
                    return;
                }

                fittedText = label.text; // first: the size changes below dirty the layout and call back in here
                fittedBox = box;
                ApplyLineFit(label, box, maxSize, singleLineMin, twoLineMin, maxLines);
            }

            Refit();
            label.RegisterDirtyLayoutCallback(Refit);
        }

        private static readonly Dictionary<(string, float, float, FontStyle, int, int, int, int), ButtonLabelFit.Result> FitCache =
            new Dictionary<(string, float, float, FontStyle, int, int, int, int), ButtonLabelFit.Result>();

        /// <summary>
        /// <see cref="ButtonLabelFit.Fit"/> measured with the UI font (<see cref="MeasureWidth"/>), remembered per text and
        /// box — a palette rebuilds its rows on every keystroke of its search field, and each row asks again.
        /// </summary>
        public static ButtonLabelFit.Result FitLines(string text, float width, float height, FontStyle style, int maxSize,
            int singleLineMin = ButtonLabelFit.SingleLineMin, int twoLineMin = ButtonLabelFit.TwoLineMin, int maxLines = 2)
        {
            var key = (text ?? string.Empty, width, height, style, maxSize, singleLineMin, twoLineMin, maxLines);
            if (FitCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (FitCache.Count > 4096)
            {
                FitCache.Clear();
            }

            var fit = ButtonLabelFit.Fit(key.Item1, width, height, (t, size) => MeasureWidth(t, size, style), maxSize,
                singleLineMin, twoLineMin, maxLines);
            FitCache[key] = fit;
            return fit;
        }

        private static void ApplyLineFit(Text label, Vector2 box, int maxSize, int singleLineMin, int twoLineMin, int maxLines)
        {
            var fit = FitLines(label.text, box.x, box.y, label.fontStyle, maxSize, singleLineMin, twoLineMin, maxLines);
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = fit.Lines > 1 || !fit.Fits ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.fontSize = fit.Size;
            if (!fit.Fits && UnfitLabelsWarned.Add(label.text ?? string.Empty))
            {
                Debug.LogWarning($"[UiKit] \"{label.text}\" does not fit its {box.x:0}×{box.y:0} box in {maxLines} lines at {fit.Size} px "
                                 + "without breaking a word — give it a wider box or a shorter text (#3263).");
            }
        }

        /// <summary>
        /// A wrapping text whose rect is as tall as its lines (<see cref="MeasureHeight"/>) — for an instruction under a
        /// control that must neither be cut nor run into the next row. <paramref name="height"/> returns the height.
        /// </summary>
        public static Text AddMeasuredText(Transform parent, float x, float y, float w, string text, int size, Color color, out float height)
        {
            height = Mathf.Ceil(MeasureHeight(text ?? string.Empty, w, size, FontStyle.Normal, false));
            var label = AddText(parent, x, y, w, height, text, size, color, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }
    }
}
