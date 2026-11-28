// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    public static partial class UiSaveSelect
    {
        /// <summary>Rows built per frame (#3259): 25 rows are 25 buttons with fitted labels — a few milliseconds, so even
        /// hundreds of worlds never cost a frame of their own.</summary>
        private const int RowsPerFrame = 25;

        /// <summary>How long the list may take before the spinner shows: a list that arrives almost at once must not
        /// flash one.</summary>
        private const float SpinnerDelaySeconds = 0.15f;

        /// <summary>
        /// The left card of "My worlds" (#3259, #3664). Clicking "Singleplayer" used to read every save and build two buttons
        /// per world inside ONE frame — 264 worlds froze the menu for 0.3 s with a warm file cache and 5.8 s on the first
        /// click after a game start. The screen now opens at once: the list comes from <see cref="LocalServerLauncher.WorldList"/>
        /// (read in the background since the main menu showed), its rows arrive <see cref="RowsPerFrame"/> per frame (the
        /// visible ones first), and while the list is still being read the card shows a spinner and "Loading worlds…".
        /// <para>
        /// A row SELECTS its world (#3664) — the right pane shows it with Play and Delete — instead of launching it on the
        /// first click; each row says the world's mode, when it was last played and how long. The last-played world is
        /// selected when the list arrives, so Play is one click away as before.
        /// </para>
        /// </summary>
        private sealed class WorldListView : MonoBehaviour
        {
            private readonly List<Button> _gated = new List<Button>();
            private readonly List<GameObject> _loading = new List<GameObject>(2);
            private readonly Dictionary<string, Button> _rows = new Dictionary<string, Button>(System.StringComparer.OrdinalIgnoreCase);
            private AppShell _shell;
            private Transform _panel;
            private float _restoreScrollY;
            private System.Action<SavedWorld> _select, _arrived;
            private IReadOnlyList<SavedWorld> _worlds;
            private RectTransform _content;
            private float _rowW;
            private float _waited;
            private int _first; // the first row built: the top visible one, so a restored scroll position fills at once
            private int _built;
            private string _marked;

            /// <summary>Hangs the list on the picker's canvas; a list already in memory shows (and starts filling) now.
            /// <paramref name="select"/> runs when a row is clicked, <paramref name="arrived"/> once when the list is there,
            /// with the world to preselect (null when there is none).</summary>
            public static WorldListView Attach(GameObject host, AppShell shell, Transform panel, float restoreScrollY,
                System.Action<SavedWorld> select, System.Action<SavedWorld> arrived)
            {
                var view = host.AddComponent<WorldListView>();
                view._shell = shell;
                view._panel = panel;
                view._restoreScrollY = restoreScrollY;
                view._select = select;
                view._arrived = arrived;
                view.enabled = true;
                return view;
            }

            /// <summary>True once the world list is there (the rows may still be filling in).</summary>
            public bool Loaded => _worlds != null;

            /// <summary>The list's scroll offset, carried across the rebuild a delete triggers.</summary>
            public float ScrollY => _content != null ? _content.anchoredPosition.y : 0f;

            /// <summary>True when a save with this name exists (case-insensitive, like the file system).</summary>
            public bool Has(string name) => Find(name) != null;

            /// <summary>The save with this name, or null (also while the list is loading).</summary>
            public SavedWorld Find(string name)
            {
                if (_worlds == null || string.IsNullOrEmpty(name))
                {
                    return null;
                }

                foreach (var world in _worlds)
                {
                    if (string.Equals(world.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return world;
                    }
                }

                return null;
            }

            /// <summary>Keeps these buttons disabled until the list is there (no-op when it already is).</summary>
            public void GateUntilLoaded(params Button[] buttons)
            {
                foreach (var button in buttons)
                {
                    if (button != null && !Loaded)
                    {
                        button.interactable = false;
                        _gated.Add(button);
                    }
                }
            }

            /// <summary>Draws the row of <paramref name="name"/> as the selected one (null: none).</summary>
            public void MarkSelected(string name)
            {
                _marked = name;
                foreach (var pair in _rows)
                {
                    PaintRow(pair.Key, pair.Value);
                }
            }

            private void PaintRow(string name, Button row)
            {
                bool selected = _marked != null && string.Equals(name, _marked, System.StringComparison.OrdinalIgnoreCase);
                ((Image)row.targetGraphic).color = selected ? UiKit.Cyan : Color.white;
            }

            private void Start() => TryShow();

            private void Update()
            {
                if (_worlds == null)
                {
                    _waited += Time.unscaledDeltaTime;
                    if (!TryShow() && _loading.Count == 0 && _waited >= SpinnerDelaySeconds)
                    {
                        ShowLoading();
                    }

                    return;
                }

                BuildSlice();
            }

            private bool TryShow()
            {
                if (_worlds != null)
                {
                    return true;
                }

                var worlds = LocalServerLauncher.WorldList.TryGet(); // starts the background read when none runs
                if (worlds == null)
                {
                    return false;
                }

                _worlds = worlds;
                foreach (var go in _loading)
                {
                    Destroy(go);
                }

                _loading.Clear();
                foreach (var button in _gated)
                {
                    if (button != null)
                    {
                        button.interactable = true;
                    }
                }

                _gated.Clear();
                BuildFrame();

                // Play stays one click away: the world the player was in last (else the newest) is selected on arrival.
                var start = Find(_shell.Settings?.LastWorld) ?? (_worlds.Count > 0 ? _worlds[0] : null);
                _arrived(start);
                return true;
            }

            /// <summary>The spinner + "Loading worlds…" where the first row will be. The spinner turns because the main
            /// thread is free — the list is read on a background task.</summary>
            private void ShowLoading()
            {
                const float y = ListY + 14f, h = 28f;
                _loading.Add(UiKit.AddSpinner(_panel, 24f, y, h));
                var label = UiKit.AddText(_panel, 62f, y, 642f, h, _shell.L("ui.save.loading_worlds"), 17, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(label, 642f, 13, 17);
                _loading.Add(label.gameObject);
            }

            /// <summary>The empty-list line, or the scroll viewport sized for every row (so the scrollbar is right from the
            /// first frame) with the restored scroll offset; the rows follow in slices.</summary>
            private void BuildFrame()
            {
                if (_worlds.Count == 0)
                {
                    var none = UiKit.AddText(_panel, 24f, ListY + 14f, ListW - 48f, 60f, _shell.L("ui.save.none"), 18, UiKit.TextCol, TextAnchor.UpperLeft);
                    none.horizontalOverflow = HorizontalWrapMode.Wrap;
                    return;
                }

                // Every save gets a row; a long list scrolls (#2010: a tenth world used to vanish silently).
                float contentH = _worlds.Count * RowPitch - (RowPitch - RowH);
                bool scrolls = contentH > ListH;
                var scroll = BuildWorldListViewport(_panel, out _content);
                _content.sizeDelta = new Vector2(0f, Mathf.Max(ListH, contentH));

                // A list that fits uses the full width; a scrolling one leaves a lane for the full-size, draggable bar —
                // it is what says "there is more below", and a child must be able to grab it.
                _rowW = scrolls ? ListW - 40f - 26f : ListW - 40f;
                if (scrolls)
                {
                    UiKit.AddVerticalScrollbar(_panel, scroll, ListW - 34f, ListY, 14f, ListH);
                    float y = Mathf.Clamp(_restoreScrollY, 0f, contentH - ListH);
                    _content.anchoredPosition = new Vector2(0f, y);
                    _first = Mathf.Clamp(Mathf.FloorToInt(y / RowPitch), 0, _worlds.Count - 1);
                }

                BuildSlice(); // the first slice in this frame: the visible rows come with the list
            }

            /// <summary>The next <see cref="RowsPerFrame"/> rows: from the first visible one down, then the ones above it.</summary>
            private void BuildSlice()
            {
                if (_content == null || _built >= _worlds.Count)
                {
                    return;
                }

                int count = _worlds.Count;
                int end = Mathf.Min(count, _built + RowsPerFrame);
                for (; _built < end; _built++)
                {
                    AddRow((_first + _built) % count);
                }
            }

            private void AddRow(int i)
            {
                var world = _worlds[i];
                var row = UiKit.AddTierButton(_content, 0f, i * RowPitch, _rowW, RowH, world.Name, () => _select(world),
                    ButtonTier.Secondary, "btn_singleplayer");
                var label = row.GetComponentInChildren<Text>();
                UiKit.Place(label.gameObject, 56f, 8f, _rowW - 70f, 34f);
                label.fontSize = 22;
                label.resizeTextForBestFit = false;
                UiKit.FitLabelSingleLine(label, _rowW - 70f, 14, 22);
                var meta = UiKit.AddText(row.transform, 56f, 42f, _rowW - 70f, 26f, MetaLine(world), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(meta, _rowW - 70f, 11, 15);
                _rows[world.Name] = row;
                PaintRow(world.Name, row);
                if (i < _first)
                {
                    // A row above the restored position comes after the ones below it — move it back into list order, so the
                    // hierarchy (what the pad's focus walks) reads top to bottom.
                    row.transform.SetSiblingIndex(i);
                }
            }

            /// <summary>"Explorer · 5 min ago · 2 h 10 min" — mode and playtime only when the save's sidecar has them.</summary>
            private string MetaLine(SavedWorld world)
            {
                var parts = new List<string>(3);
                if (world.ModeKey.Length > 0)
                {
                    parts.Add(_shell.L(world.ModeKey));
                }

                parts.Add(UiKit.TimeAgo(_shell.L, world.LastWriteUtc));
                if (world.PlaytimeSeconds > 0)
                {
                    parts.Add(FormatPlaytime(world.PlaytimeSeconds));
                }

                return string.Join("  ·  ", parts);
            }
        }
    }
}
