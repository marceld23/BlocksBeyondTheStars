// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "My festivals" (#3021): every festival of the calendar by its next start — on now first — each with its explanation and
    /// a "Celebrate" switch, and the four presets on top (big festivals + my language, all, only my language, none). Ticking
    /// a festival turns the selection into the player's own list. Opened from the menu card's "More festivals" and from
    /// Settings → Comfort → "Choose festivals…"; a modal overlay on the caller's canvas. Every change is saved at once and
    /// sent to the server of a joined game (<see cref="FestivalMood.SendPrefs"/>), so chip, mood, greeting and challenges
    /// follow without a restart.
    /// </summary>
    public static class UiFestivalPicker
    {
        private const float DlgX = 290f, DlgY = 60f, DlgW = 1340f, DlgH = 960f;
        private const float RowH = 100f;
        private static readonly Color Amber = new Color(1f, 0.82f, 0.45f);

        /// <summary>Opens the picker on <paramref name="root"/>; <paramref name="onClosed"/> runs when it closes after a change.</summary>
        public static GameObject Open(AppShell shell, Transform root, Action onClosed = null, float scroll = 1f, bool changed = false)
        {
            var (overlay, dlg) = UiKit.AddModalOverlay(root, DlgX, DlgY, DlgW, DlgH);
            overlay.transform.SetAsLastSibling();
            UiKit.AddText(dlg, 30f, 20f, DlgW - 60f, 34f, shell.L("ui.festival.picker_title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hint = UiKit.AddText(dlg, 40f, 58f, DlgW - 80f, 26f, shell.L("ui.festival.picker_hint"), 16, UiKit.TextCol, TextAnchor.MiddleCenter);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            ScrollRect rect = null;
            void Reopen()
            {
                float at = rect != null ? rect.verticalNormalizedPosition : 1f;
                UnityEngine.Object.Destroy(overlay);
                Open(shell, root, onClosed, at, changed: true);
            }

            AddPresets(shell, dlg, Reopen);
            rect = AddList(shell, dlg, Reopen);
            rect.verticalNormalizedPosition = Mathf.Clamp01(scroll);

            UiKit.AddButton(dlg, DlgW / 2f - 160f, DlgH - 74f, 320f, 54f, shell.L("ui.festival.close"), () =>
            {
                UnityEngine.Object.Destroy(overlay);
                if (changed)
                {
                    onClosed?.Invoke();
                }
            }, "btn_exit");
            return overlay;
        }

        /// <summary>The preset buttons: the active one lit; "My own choice" shows as a label when the list is the player's own.</summary>
        private static void AddPresets(AppShell shell, Transform dlg, Action reopen)
        {
            var current = FestivalMood.Preset(shell.Settings);
            var presets = new[] { FestivalPreset.BigAndLanguage, FestivalPreset.All, FestivalPreset.Language, FestivalPreset.None };
            float w = (DlgW - 80f - 3 * 12f) / 4f;
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i];
                var b = UiKit.AddButton(dlg, 40f + i * (w + 12f), 96f, w, 46f, shell.L("ui.festival.preset." + (int)preset), () =>
                {
                    shell.Settings.FestivalPreset = (int)preset;
                    Commit(shell);
                    reopen();
                });
                if (preset == current)
                {
                    b.GetComponent<Image>().color = UiKit.Cyan;
                }
            }

            if (current == FestivalPreset.Custom)
            {
                UiKit.AddText(dlg, 40f, 146f, DlgW - 80f, 22f, shell.L("ui.festival.preset.4"), 15, UiKit.CyanDim, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>The scrolling list: one row per festival, its switch, name, when and explanation.</summary>
        private static ScrollRect AddList(AppShell shell, Transform dlg, Action reopen)
        {
            var content = UiKit.AddScrollViewport(dlg, "FestivalList", 30f, 176f, DlgW - 76f, DlgH - 266f, out var scroll);
            UiKit.AddVerticalScrollbar(dlg, scroll, DlgW - 42f, 176f, 12f, DlgH - 266f);
            var celebrated = FestivalMood.Celebrated(shell.Content, shell.Settings);
            var rows = FestivalCard.Calendar(shell.Content, DateTime.UtcNow);
            float y = 0f;
            foreach (var line in rows)
            {
                AddRow(shell, content, line, celebrated, y, reopen);
                y += RowH;
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(y + 12f, 1f));
            return scroll;
        }

        private static void AddRow(AppShell shell, Transform content, FestivalCard.Line line, HashSet<string> celebrated, float y, Action reopen)
        {
            string key = line.Festival.Key;
            bool on = celebrated.Contains(key);
            var b = UiKit.AddButton(content, 10f, y + 6f, 230f, 44f, shell.L(on ? "ui.festival.celebrate_on" : "ui.festival.celebrate_off"), () =>
            {
                var next = FestivalMood.Celebrated(shell.Content, shell.Settings);
                if (!next.Remove(key))
                {
                    next.Add(key);
                }

                shell.Settings.FestivalPreset = (int)FestivalPreset.Custom;
                shell.Settings.FestivalCustom = string.Join(",", next.OrderBy(k => k, StringComparer.Ordinal));
                Commit(shell);
                reopen();
            });
            if (on)
            {
                b.GetComponent<Image>().color = UiKit.Cyan;
            }

            string name = (line.Festival.Glyph + " " + shell.L(line.Festival.NameKey)).Trim();
            UiKit.AddText(content, 260f, y + 6f, 760f, 30f, name, 20, on ? Amber : UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(content, 1020f, y + 6f, 220f, 30f, FestivalCard.When(line, shell.L), 16, UiKit.CyanDim, TextAnchor.MiddleRight);
            var body = UiKit.AddText(content, 260f, y + 38f, 980f, 58f, shell.L(line.Festival.ExplainKey), 14, UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>Saves the settings and tells a joined game's server.</summary>
        private static void Commit(AppShell shell)
        {
            shell.Settings.Save();
            FestivalMood.SendPrefs(shell.Settings);
        }
    }
}
