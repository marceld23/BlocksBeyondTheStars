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
    /// <para>
    /// On the <see cref="EditorShell"/> (#3690) the picker asks through the shell instead
    /// (<see cref="Show(AppShell, EditorShell, IReadOnlyList{Section}, Action, string)"/>): only over UNSAVED work, with the
    /// shell's one "Replace your work?" question, while the list stays open behind it — Cancel goes back to the list.
    /// </para>
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
        private readonly string _title;
        private EditorShell _editor;   // set: the shell asks before replacing unsaved work (#3690)
        private bool _closed;

        public GameObject Overlay => _overlay;

        /// <summary>True until <see cref="Close"/> ran.</summary>
        public bool IsOpen => !_closed && _overlay != null;

        private EditorLoadPicker(AppShell shell, Transform canvas, Action onClosed, string title)
        {
            _shell = shell;
            _onClosed = onClosed;
            _title = title;
            // Shared menu-modal chrome (#588): a scrim so the editor behind stays dim and unclickable.
            var (overlay, panel) = UiKit.AddModalOverlay(canvas, 640f, 220f, W, H);
            _overlay = overlay;
            _panel = panel;
        }

        private string L(string key) => _shell != null ? _shell.L(key) : key;

        /// <summary>Opens the picker. <paramref name="currentCells"/> &gt; 0 makes every load ask first.
        /// Returns the picker so the caller can destroy it (e.g. when the whole UI is rebuilt). <paramref name="title"/>
        /// replaces "Load" for a list that picks something else (the structure editor's generators, #3523).</summary>
        public static EditorLoadPicker Show(AppShell shell, Transform canvas, IReadOnlyList<Section> sections, int currentCells, Action onClosed = null,
            string title = null)
        {
            var picker = new EditorLoadPicker(shell, canvas, onClosed, title);
            picker.BuildList(sections, currentCells);
            return picker;
        }

        /// <summary>
        /// Opens the picker over an editor on the <see cref="EditorShell"/> (#3690). Picking a row closes the picker and
        /// loads — over unsaved work only after the shell's question (<see cref="EditorShell.ConfirmDiscard"/>), which opens
        /// above the still-open list (Cancel keeps the list). The picker counts no cells and asks nothing itself.
        /// </summary>
        public static EditorLoadPicker Show(AppShell shell, EditorShell editor, IReadOnlyList<Section> sections, Action onClosed = null,
            string title = null)
        {
            var picker = new EditorLoadPicker(shell, editor.Canvas.transform, onClosed, title) { _editor = editor };
            picker.BuildList(sections, 0);
            return picker;
        }

        /// <summary>Closes the picker (once; later calls do nothing).</summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            if (_overlay != null)
            {
                _overlay.SetActive(false); // gone this frame for the pad's modal scope, destroyed at its end
                EditorSceneKit.Discard(_overlay);
            }

            _onClosed?.Invoke();
        }

        private void ClearPanel()
        {
            for (int i = _panel.childCount - 1; i >= 0; i--)
            {
                EditorSceneKit.Discard(_panel.GetChild(i).gameObject);
            }
        }

        private void BuildList(IReadOnlyList<Section> sections, int currentCells)
        {
            ClearPanel();
            UiKit.AddText(_panel, 20f, 14f, W - 40f, 28f, _title ?? L("ui.editor.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

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

            UiKit.AddFitButton(_panel, 20f, H - 58f, W - 40f, 44f, L("ui.menu.back"), Close); // a tap target (#3695)
        }

        /// <summary>The "replace your work?" step of a picker without an editor shell: shown instead of loading straight away
        /// when something is already placed, so a mis-click never wipes an hour of work. Same words as the shell's question
        /// (<see cref="EditorShell.ConfirmDiscard"/>).</summary>
        private void BuildConfirm(Item item, int currentCells)
        {
            ClearPanel();
            UiKit.AddText(_panel, 20f, 14f, W - 40f, 28f, _title ?? L("ui.editor.load"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var msg = UiKit.AddText(_panel, 20f, 70f, W - 40f, 120f, L("ui.editor.confirm_replace"), 16, UiKit.Warn, TextAnchor.UpperLeft);
            msg.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddFitButton(_panel, 20f, 210f, (W - 52f) / 2f, 44f, L("ui.editor.confirm_replace_yes"), () => { item.Load?.Invoke(); Close(); });
            UiKit.AddFitButton(_panel, 32f + (W - 52f) / 2f, 210f, (W - 52f) / 2f, 44f, L("ui.menu.back"), Close);
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
            var row = Row(list, 48f); // a 44 px tap target per design (#3695)
            float rowW = W - 32f - 16f; // list width minus the layout padding (4 + 12 for the scrollbar)
            var btn = UiKit.AddFitButton(row, 0f, 2f, rowW, 44f, string.Empty, () => Pick(item, currentCells));

            // Two texts over the button: the name left, the facts right (a single centred label could
            // not tell "Corvette · 6×7×4 · 169 cells" apart from the next row at a glance).
            var name = UiKit.AddText(btn.transform, 14f, 0f, rowW * 0.55f, 44f, "▸  " + item.Label, 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            var detail = UiKit.AddText(btn.transform, rowW * 0.55f, 0f, rowW * 0.45f - 14f, 44f, item.Detail ?? string.Empty, 13, UiKit.CyanDim, TextAnchor.MiddleRight);
            detail.raycastTarget = false;
        }

        /// <summary>A row was picked: on the editor shell through its question over unsaved work (#3690), else the picker's
        /// own "replace your current build?" step when something is placed, else load at once.</summary>
        private void Pick(Item item, int currentCells)
        {
            if (_editor != null)
            {
                _editor.ConfirmDiscard(() =>
                {
                    Close();
                    item.Load?.Invoke();
                });
            }
            else if (currentCells > 0)
            {
                BuildConfirm(item, currentCells);
            }
            else
            {
                item.Load?.Invoke();
                Close();
            }
        }
    }
}
