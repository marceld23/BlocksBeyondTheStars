// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Inventory tab's frame in <see cref="CraftingTechShipUI"/> (#3671): its pages (Backpack, Suit, Cargo hold, Samples
    /// and the Sandbox catalogue) as a segmented bar in the sub-tab row instead of a tall, mostly empty sidebar column — the
    /// list takes that width — and the detail column's overview while no item is picked: how full the backpack and the hold
    /// are, what the suit gives and what the sample case holds.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        /// <summary>The Inventory's pages as the sub-tab bar.</summary>
        private void AddInventoryPages(Transform p)
        {
            var pages = Categories(); // personal, suit, cargo, samples (+ catalog in Sandbox), with their labels and icons
            var labels = new string[pages.Count];
            var icons = new string[pages.Count];
            int selected = -1;
            for (int i = 0; i < pages.Count; i++)
            {
                labels[i] = pages[i].label;
                icons[i] = pages[i].icon;
                if (pages[i].key == _category)
                {
                    selected = i;
                }
            }

            const float segW = 240f;
            float w = Mathf.Min(1160f, pages.Count * segW + (pages.Count - 1) * TabGap);
            var bar = UiKit.AddSegmented(p, 40f, SubTabY, w, SubTabH, labels, selected, i => SelectInventoryPage(pages[i].key), icons, TabGap);
            if (_selectSubTabOnBuild && selected >= 0)
            {
                _selectSubTabOnBuild = false;
                SelectOnBuild(bar.Buttons[selected]);
            }
        }

        /// <summary>Opens an Inventory page (from its sub-tab or an overview row). Each page brings its own strip: the sample
        /// filter (#2300), search + category filter (#3110).</summary>
        private void SelectInventoryPage(string key)
        {
            bool strip = key != _category;
            _category = key;
            _selected = string.Empty;
            if (strip)
            {
                _selectSubTabOnBuild = true; // the pad stays on the page bar
                BuildHeader();
            }

            RebuildList();
            RebuildDetail();
        }

        /// <summary>The detail column while nothing is picked: one row per page with what it holds — each a button onto that
        /// page — and how to see an item's details. Returns the content height.</summary>
        private float BuildInventoryOverview()
        {
            float y = 4f;
            UiKit.AddKicker(_detail, 8f, y, 612f, L("ui.inventory.overview"));
            y += 34f;
            y = AddOverviewRow(y, "personal", "cat_inventory", L("ui.inventory.backpack"),
                SlotsLine(StacksIn(Game.Personal), PersonalSlotTotal));
            y = AddOverviewRow(y, "cargo", "cat_cargo", L("ui.cargo.title"), AboardShipNow() && Game.CargoSlots > 0
                ? SlotsLine(StacksIn(Game.Cargo), Game.CargoSlots)
                : L("ui.cargo.not_aboard"));
            y = AddOverviewRow(y, "suit", "cat_suit", L("ui.inventory.suit"),
                L("ui.inventory.overview_worn").Replace("{n}", Game.WornKeys().Count().ToString()) + "\n" + SuitStatusLine());
            int samples = SampleCaseView.Entries(Game.Bio).Sum(e => e.Count);
            y = AddOverviewRow(y, "samples", "cat_medicine", L("ui.inventory.samples"),
                L("ui.inventory.overview_samples").Replace("{n}", samples.ToString()));

            var hint = UiKit.AddText(_detail, 8, y + 8f, 612, 60, L("ui.inventory.overview_pick"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + 8f + Mathf.Max(60f, hint.preferredHeight);
        }

        /// <summary>One overview row: the page's icon, its name and what it holds (up to two lines); a click opens the page.
        /// Returns the y below it.</summary>
        private float AddOverviewRow(float y, string page, string icon, string title, string status)
        {
            const float rowW = 620f, rowH = 96f;
            var row = UiKit.AddButton(_detail, 0f, y, rowW, rowH, string.Empty, () => SelectInventoryPage(page));
            UiKit.AddIcon(row.transform, 16f, 22f, 52f, icon);
            var name = UiKit.AddText(row.transform, 84f, 8f, rowW - 100f, 34f, title, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabel(name, 12, 22);
            var line = UiKit.AddText(row.transform, 84f, 42f, rowW - 100f, 48f, status, 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiKit.FitLabel(line, 11, 16);
            return y + rowH + 8f;
        }

        private string SlotsLine(int used, int total)
            => L("ui.inventory.overview_slots").Replace("{used}", used.ToString()).Replace("{total}", total.ToString());

        /// <summary>How many slots hold something (the snapshots carry only occupied slots).</summary>
        private static int StacksIn(NetItemStack[] stacks)
            => stacks == null ? 0 : stacks.Count(s => s != null && s.Count > 0 && !string.IsNullOrEmpty(s.Item));

        /// <summary>Moves a page laid out for the list's standard width (<paramref name="columnW"/>) to the middle of the
        /// wide inventory list — the suit figure would otherwise hug the left edge of a pane half again as wide.</summary>
        private void CenterListColumn(float columnW)
        {
            float dx = Mathf.Floor((ListViewW - columnW) / 2f);
            if (dx < 1f)
            {
                return;
            }

            foreach (Transform child in _listContent)
            {
                if (child is RectTransform rt)
                {
                    rt.anchoredPosition += new Vector2(dx, 0f);
                }
            }
        }
    }
}
