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
        /// <summary>Rows built per frame (#3259): 25 rows are 50 buttons with fitted labels — a few milliseconds, so even
        /// hundreds of worlds never cost a frame of their own.</summary>
        private const int RowsPerFrame = 25;

        /// <summary>How long the list may take before the spinner shows: a list that arrives almost at once must not
        /// flash one.</summary>
        private const float SpinnerDelaySeconds = 0.15f;

        /// <summary>
        /// The picker's left panel (#3259). Clicking "Singleplayer" used to read every save and build two buttons per
        /// world inside ONE frame — 264 worlds froze the menu for 0.3 s with a warm file cache and 5.8 s on the first
        /// click after a game start. The screen now opens at once: the list comes from <see cref="LocalServerLauncher.WorldList"/>
        /// (read in the background since the main menu showed), its rows arrive <see cref="RowsPerFrame"/> per frame
        /// (the visible ones first), and while the list is still being read the panel shows a spinner and "Loading
        /// worlds…". "Create" and "Random world" wait for the list: both check new names against it.
        /// </summary>
        private sealed class WorldListView : MonoBehaviour
        {
            private readonly List<Button> _gated = new List<Button>();
            private readonly List<GameObject> _loading = new List<GameObject>(2);
            private AppShell _shell;
            private Transform _panel;
            private float _restoreScrollY;
            private System.Action<string> _launch;
            private System.Action<string> _askDelete;
            private IReadOnlyList<SavedWorld> _worlds;
            private RectTransform _content;
            private float _rowW;
            private float _waited;
            private int _first; // the first row built: the top visible one, so a restored scroll position fills at once
            private int _built;

            /// <summary>Hangs the list on the picker's canvas; a list already in memory shows (and starts filling) now.</summary>
            public static WorldListView Attach(GameObject host, AppShell shell, Transform panel, float restoreScrollY,
                System.Action<string> launch, System.Action<string> askDelete)
            {
                var view = host.AddComponent<WorldListView>();
                view._shell = shell;
                view._panel = panel;
                view._restoreScrollY = restoreScrollY;
                view._launch = launch;
                view._askDelete = askDelete;
                view.TryShow();
                return view;
            }

            /// <summary>True once the world list is there (the rows may still be filling in).</summary>
            public bool Loaded => _worlds != null;

            /// <summary>The list's scroll offset, carried across the rebuild a delete triggers.</summary>
            public float ScrollY => _content != null ? _content.anchoredPosition.y : 0f;

            /// <summary>True when a save with this name exists (case-insensitive, like the file system).</summary>
            public bool Has(string name)
            {
                if (_worlds == null)
                {
                    return false;
                }

                foreach (var world in _worlds)
                {
                    if (string.Equals(world.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>The existing world names (empty while the list is loading).</summary>
            public List<string> Names()
            {
                var names = new List<string>(_worlds?.Count ?? 0);
                if (_worlds != null)
                {
                    foreach (var world in _worlds)
                    {
                        names.Add(world.Name);
                    }
                }

                return names;
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
                return true;
            }

            /// <summary>The spinner + "Loading worlds…" where the first row will be. The spinner turns because the main
            /// thread is free — the list is read on a background task.</summary>
            private void ShowLoading()
            {
                const float y = 70f, h = 28f;
                _loading.Add(UiKit.AddSpinner(_panel, 20f, y, h));
                var label = UiKit.AddText(_panel, 58f, y, 642f, h, _shell.L("ui.save.loading_worlds"), 17, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(label, 642f, 12, 17);
                _loading.Add(label.gameObject);
            }

            /// <summary>The empty-list line, or the scroll viewport sized for every row (so the scrollbar is right from the
            /// first frame) with the restored scroll offset; the rows follow in slices.</summary>
            private void BuildFrame()
            {
                if (_worlds.Count == 0)
                {
                    UiKit.AddText(_panel, 20f, 70f, 680f, 28f, _shell.L("ui.save.none"), 17, UiKit.TextCol, TextAnchor.MiddleLeft);
                    return;
                }

                // Every save gets a row. The list used to stop at the nine rows the panel holds, and the list is sorted
                // newest first — so a tenth world silently vanished, the player's OLDEST ones (#2010). A long list scrolls.
                float contentH = _worlds.Count * RowPitch - (RowPitch - RowH);
                bool scrolls = contentH > ListH;
                var scroll = BuildWorldListViewport(_panel, out _content);
                _content.sizeDelta = new Vector2(0f, Mathf.Max(ListH, contentH));

                // A list that fits keeps the old row widths; a scrolling one narrows them for the scrollbar. The bar is the
                // full-size, draggable one: it is what says "there is more below", and a child must be able to grab it.
                _rowW = scrolls ? 590f : 612f;
                if (scrolls)
                {
                    UiKit.AddVerticalScrollbar(_panel, scroll, 686f, ListY, 14f, ListH);
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
                string w = _worlds[i].Name;
                long played = _worlds[i].PlaytimeSeconds;
                string label = played > 0 ? $"▸  {w}    ({FormatPlaytime(played)})" : $"▸  {w}";
                var row = UiKit.AddButton(_content, 0f, i * RowPitch, _rowW, RowH, label, () => _launch(w), "btn_singleplayer");
                var delete = UiKit.AddButton(_content, _rowW + 8f, i * RowPitch, 60f, RowH, "✕", () => _askDelete(w), "btn_exit");
                if (i < _first)
                {
                    // A row above the restored position comes after the ones below it — move it back into list order, so
                    // the hierarchy (what the pad's focus walks) reads top to bottom.
                    row.transform.SetSiblingIndex(2 * i);
                    delete.transform.SetSiblingIndex(2 * i + 1);
                }
            }
        }
    }
}
