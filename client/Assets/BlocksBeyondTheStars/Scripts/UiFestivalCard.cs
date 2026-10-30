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
    /// calendar and its UTC clock — the same file and code the server runs, so the card and the world agree. "More
    /// festivals" opens a small overlay with the next sixty days. Nothing is built when the client setting is off, when
    /// nothing is on or near, or when no content is loaded (the Editor's hierarchy test), so the menu stays as it was.
    /// </summary>
    public static class UiFestivalCard
    {
        private const float X = 1290f, Y = 200f, W = 590f;
        private const float LineH = 100f;
        private const int ListMax = 7;
        private static readonly Color Amber = new Color(1f, 0.82f, 0.45f);

        public static void Build(AppShell shell, Transform root)
        {
            var now = DateTime.UtcNow;
            if (shell == null || shell.Content == null || shell.Settings == null
                || !FestivalCard.ShouldShow(shell.Content, now, shell.Settings.Festivals))
            {
                return;
            }

            var active = FestivalCard.Active(shell.Content, now);
            var lines = active.Count > 0 ? active : (IReadOnlyList<FestivalCard.Line>)new[] { FestivalCard.Next(shell.Content, now)!.Value };
            float h = 50f + lines.Count * LineH + 60f;
            UiHolo.AddPanel(root, X, Y, W, h, UiKit.Panel, 18f, 1.5f, 1f);
            string title = shell.L(lines.Count > 1 ? "ui.festival.card_title_plural" : "ui.festival.card_title");
            UiKit.AddText(root, X + 24f, Y + 14f, W - 48f, 24f, title, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            float y = Y + 48f;
            foreach (var line in lines)
            {
                AddLine(root, shell, line, X + 24f, ref y, W - 48f);
            }

            GameObject overlay = BuildList(shell, root);
            UiKit.AddButton(root, X + 24f, y + 4f, 280f, 46f, shell.L("ui.festival.more"), () => overlay.SetActive(true));
        }

        private static void AddLine(Transform root, AppShell shell, FestivalCard.Line line, float x, ref float y, float w)
        {
            UiKit.AddText(root, x, y, w, 28f, FestivalCard.Headline(line, shell.L), 20, Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            var body = UiKit.AddText(root, x, y + 30f, w, 64f, shell.L(line.Festival.ExplainKey), 14, UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            y += LineH;
        }

        /// <summary>The "More festivals" overlay: today's festivals and the next sixty days, at most <see cref="ListMax"/> rows,
        /// hidden until the card's button opens it.</summary>
        private static GameObject BuildList(AppShell shell, Transform root)
        {
            var (overlay, dlg) = UiKit.AddModalOverlay(root, 310f, 90f, 1300f, 900f);
            string days = FestivalCard.UpcomingHorizonDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UiKit.AddText(dlg, 30f, 24f, 1240f, 32f, shell.L("ui.festival.upcoming_title").Replace("{days}", days), 24,
                UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var now = DateTime.UtcNow;
            var rows = new List<FestivalCard.Line>(FestivalCard.Active(shell.Content, now));
            rows.AddRange(FestivalCard.Upcoming(shell.Content, now, FestivalCard.UpcomingHorizonDays));
            float y = 76f;
            if (rows.Count == 0)
            {
                UiKit.AddText(dlg, 40f, y, 1220f, 28f, shell.L("ui.festival.upcoming_none").Replace("{days}", days), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            }

            for (int i = 0; i < rows.Count && i < ListMax; i++)
            {
                AddLine(dlg, shell, rows[i], 40f, ref y, 1220f);
            }

            UiKit.AddButton(dlg, 490f, 830f, 320f, 54f, shell.L("ui.festival.close"), () => overlay.SetActive(false), "btn_exit");
            overlay.SetActive(false);
            return overlay;
        }
    }
}
