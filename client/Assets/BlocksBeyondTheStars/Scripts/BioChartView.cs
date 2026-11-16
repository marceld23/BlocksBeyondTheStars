// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The reaction chart (#3721): the eight substance groups against each other, a cell showing what the two do to
    /// each other once the player has mixed two samples of those groups — a question mark before. What is known comes
    /// from <see cref="ReactionChart"/> (the research book's tried mixes); the reactions themselves from
    /// <see cref="BioRules.Reaction"/>. Drawn by the bio lab's Reactions page and by the Codex, so both show one chart.
    /// </summary>
    public static class BioChartView
    {
        private const float Cell = 46f, Mark = 26f;
        private static readonly Color CellFill = new Color(0.10f, 0.17f, 0.26f, 0.85f);
        private static readonly Color CellKnown = new Color(0.13f, 0.24f, 0.36f, 0.95f);

        /// <summary>The colour a reaction's mark is drawn in: good green, bad amber, the two changes in their own hues.</summary>
        public static Color ReactionColor(BioReaction reaction) => reaction switch
        {
            BioReaction.Amplify => UiKit.Ok,
            BioReaction.Inhibit => UiKit.Warn,
            BioReaction.Couple => UiKit.Cyan,
            BioReaction.Transmute => new Color(0.71f, 0.55f, 1.00f),
            _ => UiKit.CyanDim,
        };

        /// <summary>The chart of this player's research book.</summary>
        public static ReactionChart Of(GameBootstrap game)
            => ReactionChart.From(game.Bio.Reactions, seed => game.Bio.Analysed(seed) ? game.Bio.ProfileOf(seed) : null);

        /// <summary>Draws the chart with its hint, its count and its legend at (<paramref name="x"/>,
        /// <paramref name="y"/>); the legend stands beside the grid where <paramref name="w"/> has room, under it
        /// otherwise. Returns the y under it.</summary>
        public static float Build(GameBootstrap game, Transform parent, float x, float y, float w)
        {
            string L(string key) => game?.Localizer?.Get(key) ?? key;
            var chart = Of(game);

            var hint = UiKit.AddText(parent, x, y, w, 40f, L("ui.bio.chart.hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            float hintH = hint.preferredHeight;
            hint.rectTransform.sizeDelta = new Vector2(w, hintH + 6f);
            y += hintH + 10f;

            string progress = L("ui.bio.chart.progress").Replace("{0}", chart.Known.ToString()).Replace("{1}", ReactionChart.Pairs.ToString());
            UiKit.AddText(parent, x, y, w, 26f, progress, 17, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 34f;

            float gridW = (BioRules.Groups + 1) * Cell;
            Grid(parent, x, y, chart);
            bool beside = w >= gridW + 36f + 300f;
            float legendY = Legend(game, parent, beside ? x + gridW + 36f : x, beside ? y : y + gridW + 14f,
                beside ? w - gridW - 36f : w);
            return Mathf.Max(y + gridW, legendY) + 8f;
        }

        private static void Grid(Transform parent, float x, float y, ReactionChart chart)
        {
            for (int g = 0; g < BioRules.Groups; g++)
            {
                float at = (g + 1) * Cell + (Cell - Mark) * 0.5f - 2f;
                BioIcons.Add(parent, x + at, y + (Cell - Mark) * 0.5f - 2f, Mark, BioSymbols.GroupIcon(g), UiKit.Cyan); // the top row
                BioIcons.Add(parent, x + (Cell - Mark) * 0.5f - 2f, y + at, Mark, BioSymbols.GroupIcon(g), UiKit.Cyan); // the left column
            }

            for (int row = 0; row < BioRules.Groups; row++)
            {
                for (int col = 0; col < BioRules.Groups; col++)
                {
                    float cx = x + (col + 1) * Cell, cy = y + (row + 1) * Cell;
                    bool known = chart.Knows(row, col);
                    UiKit.AddImage(parent, cx, cy, Cell - 4f, Cell - 4f, UiKit.SolidSprite, known ? CellKnown : CellFill);
                    if (known)
                    {
                        var reaction = BioRules.Reaction(row, col);
                        BioIcons.Add(parent, cx + (Cell - 4f - Mark) * 0.5f, cy + (Cell - 4f - Mark) * 0.5f, Mark,
                            BioSymbols.ReactionIcon(reaction), ReactionColor(reaction));
                    }
                    else
                    {
                        UiKit.AddText(parent, cx, cy, Cell - 4f, Cell - 4f, "?", 20, UiKit.CyanDim, TextAnchor.MiddleCenter);
                    }
                }
            }
        }

        /// <summary>The legend: every group's mark with its name, every reaction's mark with what it does, and the
        /// question mark. Two columns where the width allows. Returns the y under it.</summary>
        private static float Legend(GameBootstrap game, Transform parent, float x, float y, float w)
        {
            string L(string key) => game?.Localizer?.Get(key) ?? key;
            const float rowH = 30f;
            bool twoColumns = w >= 520f;
            float colW = twoColumns ? w * 0.5f : w;

            UiKit.AddText(parent, x, y, colW, 24f, L("ui.bio.group"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            float gy = y + 28f;
            for (int g = 0; g < BioRules.Groups; g++)
            {
                BioIcons.Add(parent, x, gy + 3f, 22f, BioSymbols.GroupIcon(g), UiKit.Cyan);
                Label(parent, x + 32f, gy, colW - 36f, L("bio.group." + g));
                gy += rowH;
            }

            float rx = twoColumns ? x + colW : x;
            float ry = twoColumns ? y : gy + 10f;
            UiKit.AddText(parent, rx, ry, colW, 24f, L("ui.bio.reaction"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            ry += 28f;
            var reactions = new[] { BioReaction.Amplify, BioReaction.Inhibit, BioReaction.Couple, BioReaction.Transmute, BioReaction.Neutral };
            foreach (var reaction in reactions)
            {
                BioIcons.Add(parent, rx, ry + 3f, 22f, BioSymbols.ReactionIcon(reaction), ReactionColor(reaction));
                Label(parent, rx + 32f, ry, colW - 36f, L("bio.reaction." + reaction.ToString().ToLowerInvariant()));
                ry += rowH;
            }

            UiKit.AddText(parent, rx, ry, 22f, 28f, "?", 20, UiKit.CyanDim, TextAnchor.MiddleCenter);
            Label(parent, rx + 32f, ry, colW - 36f, L("ui.bio.chart.untried"));
            ry += rowH;
            return Mathf.Max(gy, ry);
        }

        private static void Label(Transform parent, float x, float y, float w, string text)
        {
            var label = UiKit.AddText(parent, x, y, w, 28f, text, 17, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabel(label, 11, 17);
        }
    }
}
