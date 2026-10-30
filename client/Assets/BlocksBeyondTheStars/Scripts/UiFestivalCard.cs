// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Festivals;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The main menu's festival card (#3003): "Today: ✦ Halloween" with the festival's two-sentence explanation, or the next
    /// festival within two weeks, in the right column between the title and the world-info panel. Built onto the menu's own
    /// canvas by <see cref="UiMainMenu.Build"/> (before the overlays, so they draw over it) from the client's bundled
    /// calendar and its UTC clock — the same file and code the server runs, so the card and the world agree. Since #3021 it
    /// names only the festivals the player celebrates ("My festivals"), and "More festivals" opens <see cref="UiFestivalPicker"/>
    /// with every festival and its "Celebrate" switch. Nothing is built when no celebrated festival is on or near, or when no
    /// content is loaded (the Editor's hierarchy test), so the menu stays as it was.
    /// </summary>
    /// <remarks>The picker is built when the button is pressed, as the canvas' last child — so neither the card's button nor
    /// the menu's bottom bar draws over the open list or stays clickable through its scrim (#3017).</remarks>
    public static class UiFestivalCard
    {
        private const float X = 1290f, Y = 200f, W = 590f;
        private const float LineH = 100f;
        private static readonly Color Amber = new Color(1f, 0.82f, 0.45f);

        /// <summary>Builds the card — nothing when no festival the player celebrates is on or near (#3021). "More festivals" opens
        /// <see cref="UiFestivalPicker"/> on top of the canvas when pressed.</summary>
        public static void Build(AppShell shell, Transform root)
        {
            var now = DateTime.UtcNow;
            var celebrated = shell?.Content == null || shell.Settings == null ? null : FestivalMood.Celebrated(shell.Content, shell.Settings);
            if (celebrated == null || !FestivalCard.ShouldShow(shell.Content, now, celebrated))
            {
                return;
            }

            var active = FestivalCard.Active(shell.Content, now, celebrated);
            var lines = active.Count > 0 ? active : (IReadOnlyList<FestivalCard.Line>)new[] { FestivalCard.Next(shell.Content, now, celebrated)!.Value };
            float h = 50f + lines.Count * LineH + 60f;
            UiHolo.AddPanel(root, X, Y, W, h, UiKit.Panel, 18f, 1.5f, 1f);
            string title = shell.L(lines.Count > 1 ? "ui.festival.card_title_plural" : "ui.festival.card_title");
            UiKit.AddText(root, X + 24f, Y + 14f, W - 48f, 24f, title, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            float y = Y + 48f;
            foreach (var line in lines)
            {
                AddLine(root, shell, line, X + 24f, ref y, W - 48f);
            }

            UiKit.AddButton(root, X + 24f, y + 4f, 280f, 46f, shell.L("ui.festival.more"),
                () => UiFestivalPicker.Open(shell, root, shell.RefreshMainMenu)); // built last on the canvas: over the bottom bar (#3017)
        }

        private static void AddLine(Transform root, AppShell shell, FestivalCard.Line line, float x, ref float y, float w)
        {
            UiKit.AddText(root, x, y, w, 28f, FestivalCard.Headline(line, shell.L), 20, Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            var body = UiKit.AddText(root, x, y + 30f, w, 64f, shell.L(line.Festival.ExplainKey), 14, UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            y += LineH;
        }

    }
}
