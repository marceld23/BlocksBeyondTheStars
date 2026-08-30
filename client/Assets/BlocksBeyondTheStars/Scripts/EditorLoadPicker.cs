// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The build editors' LOAD dialog (#1394, #1395): a modal with a scrollable, sectioned list of
    /// designs — built-in ships / templates from the loaded content, the user's own templates and the
    /// user's exports — each row naming the design and its size. Picking a row loads it; when the
    /// current build is not empty the dialog first asks, because loading replaces everything placed.
    /// Shared by <see cref="ShipEditor"/> and <see cref="StructureEditor"/> so both pickers behave alike.
    /// </summary>
    internal sealed class EditorLoadPicker
    {
        internal sealed class Item
        {
            public string Label;   // the design's display name (localized for built-ins)
            public string Detail;  // right-aligned facts: tier · W×L×H · cells
            public Action Load;    // performs the load; the picker closes afterwards
        }

        internal sealed class Section
        {
            public string Title;
            public readonly List<Item> Items = new();
        }

        private const float W = 640f, H = 620f;

        private readonly AppShell _shell;
        private readonly GameObject _overlay;
        private readonly Transform _panel;
        private readonly Action _onClosed;

        public GameObject Overlay => _overlay;

        private EditorLoadPicker(AppShell shell, Transform canvas, Action onClosed)
        {
            _shell = shell;
            _onClosed = onClosed;
            // Shared menu-modal chrome (#588): a scrim so the editor behind stays dim and unclickable.
            var (overlay, panel) = UiKit.AddModalOverlay(canvas, 640f, 220f, W, H);
            _overlay = overlay;
            _panel = panel;
        }

        private string L(string key) => _shell != null ? _shell.L(key) : key;

        /// <summary>Opens the picker. <paramref name="currentCells"/> &gt; 0 makes every load ask first.
        /// Returns the picker so the caller can destroy it (e.g. when the whole UI is rebuilt).</summary>
        public static EditorLoadPicker Show(AppShell shell, Transform canvas, IReadOnlyList<Section> sections, int currentCells, Action onClosed = null)
        {
            var picker = new EditorLoadPicker(shell, canvas, onClosed);
            picker.BuildList(sections, currentCells);
            return picker;
        }

        public void Close()
        {
            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(_overlay);
            }

            _onClosed?.Invoke();
        }

        private void ClearPanel()
        {
            for (int i = _panel.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_panel.GetChild(i).gameObject);
            }
        }

        private void BuildList(IReadOnlyList<Section> sections, int currentCells)
        {
            ClearPanel();
            UiKit.AddText(_panel, 20f, 14f, W - 40f, 28f, L("ui.struct.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            var list = UiKit.ScrollList(_panel, 16f, 52f, W - 32f, H - 52f - 64f);
            int rows = 0;
            foreach (var section in sections)
            {
                if (section.Items.Count == 0)
                {
                    continue;
                }

                AddHeader(list, section.Title);
                foreach (var item in section.Items)
                {
                    AddRow(list, item, currentCells);
                    rows++;
                }
            }

            if (rows == 0)
            {
                var row = Row(list, 32f);
                UiKit.AddText(row, 8f, 4f, W - 80f, 24f, L("ui.ed.none"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            }

            UiKit.AddButton(_panel, 20f, H - 54f, W - 40f, 40f, L("ui.menu.back"), Close);
        }

        /// <summary>The "replace your current build?" step: shown instead of loading straight away when
        /// something is already placed, so a mis-click never wipes an hour of work.</summary>
        private void BuildConfirm(Item item, int currentCells)
        {
            ClearPanel();
            UiKit.AddText(_panel, 20f, 14f, W - 40f, 28f, L("ui.struct.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var msg = UiKit.AddText(_panel, 20f, 70f, W - 40f, 120f,
                string.Format(L("ui.ed.confirm_replace"), currentCells, item.Label), 16, UiKit.Warn, TextAnchor.UpperLeft);
            msg.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(_panel, 20f, 210f, (W - 52f) / 2f, 42f, L("ui.ed.confirm_load"), () => { item.Load?.Invoke(); Close(); });
            UiKit.AddButton(_panel, 32f + (W - 52f) / 2f, 210f, (W - 52f) / 2f, 42f, L("ui.menu.back"), Close);
        }

        /// <summary>Row height under <see cref="UiKit.ScrollList"/> must be set on the rect itself — the
        /// layout group has <c>childControlHeight = false</c> and ignores a LayoutElement (#1386).</summary>
        private static RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return rt;
        }

        private static void AddHeader(Transform list, string title)
        {
            var row = Row(list, 28f);
            UiKit.AddText(row, 6f, 4f, W - 80f, 24f, title, 13, UiKit.CyanDim, TextAnchor.LowerLeft, FontStyle.Bold);
        }

        private void AddRow(Transform list, Item item, int currentCells)
        {
            var row = Row(list, 38f);
            float rowW = W - 32f - 16f; // list width minus the layout padding (4 + 12 for the scrollbar)
            var btn = UiKit.AddButton(row, 0f, 2f, rowW, 34f, string.Empty, () =>
            {
                if (currentCells > 0)
                {
                    BuildConfirm(item, currentCells);
                }
                else
                {
                    item.Load?.Invoke();
                    Close();
                }
            });

            // Two texts over the button: the name left, the facts right (a single centred label could
            // not tell "Corvette · 6×7×4 · 169 cells" apart from the next row at a glance).
            var name = UiKit.AddText(btn.transform, 14f, 0f, rowW * 0.55f, 34f, "▸  " + item.Label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            var detail = UiKit.AddText(btn.transform, rowW * 0.55f, 0f, rowW * 0.45f - 14f, 34f, item.Detail ?? string.Empty, 13, UiKit.CyanDim, TextAnchor.MiddleRight);
            detail.raycastTarget = false;
        }
    }
}
