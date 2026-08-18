// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The story reader panel (#1110): net fragments, personal memories and environmental lore (#1111) open
    /// in a modal reader instead of flashing by as a toast — title, category label, the archive text in a
    /// scrollable body, one Close button. Re-readable entries route back in from the Story tab. Modal like
    /// <see cref="TeleporterUi"/> (cursor freed, on-foot control paused, Esc / pad B closes); texts longer
    /// than one uGUI mesh are chunked (<see cref="UiTextChunks"/>, #1097). When another menu is open the
    /// reader queues and opens once the screen is free — a pickup during trading never eats the text.
    /// </summary>
    public sealed class StoryReaderUi : MonoBehaviour
    {
        public static StoryReaderUi Instance { get; private set; }
        public GameBootstrap Game;

        private Canvas _canvas;
        private RectTransform _list;
        private Text _title;
        private Text _label;
        private bool _open, _built;
        private int _openFrame = -1;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly Queue<(string Title, string Label, string Body)> _pending = new();

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        public bool IsOpen => _open;

        /// <summary>Shows a text in the reader. <paramref name="body"/> is the LOCALIZED text (the caller
        /// resolves keys); <paramref name="label"/> is an optional category line under the title. Queues when
        /// another menu owns the screen.</summary>
        public void Open(string title, string label, string body)
        {
            if (_open || (Game != null && Game.MenuOpen))
            {
                _pending.Enqueue((title, label, body)); // never overwrite an unread text / fight another menu
                return;
            }

            EnsureBuilt();
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            Fill(title, label, body);
            Game?.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        /// <summary>Opens straight from an already-open menu (the Story tab's Read buttons) — no queueing.</summary>
        public void OpenOverMenu(string title, string label, string body)
        {
            EnsureBuilt();
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            Fill(title, label, body);
            Game?.SetMenuOwner(this, true);
        }

        private void Update()
        {
            if (!_open)
            {
                // A queued text opens once the screen is free again (e.g. the trade window just closed).
                if (_pending.Count > 0 && Game != null && !Game.MenuOpen)
                {
                    var (t, l, b) = _pending.Dequeue();
                    Open(t, l, b);
                }

                return;
            }

            if (Time.frameCount != _openFrame && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)))
            {
                Game?.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Close()
        {
            _open = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Game?.SetMenuOwner(this, false);

            if (_pending.Count > 0)
            {
                var (t, l, b) = _pending.Dequeue();
                Open(t, l, b); // the next queued find follows immediately (rare: a double pickup)
            }
        }

        private void Fill(string title, string label, string body)
        {
            _title.text = title;
            _label.text = label ?? string.Empty;
            _label.gameObject.SetActive(!string.IsNullOrEmpty(label));

            foreach (var go in _rows) Destroy(go);
            _rows.Clear();

            foreach (string chunk in UiTextChunks.Split(body))
            {
                var go = new GameObject("Chunk", typeof(RectTransform));
                go.transform.SetParent(_list, false);
                var t = UiKit.AddText(go.transform, 0f, 0f, 620f, 100f, chunk, 19, UiKit.TextCol, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                float h = Mathf.Max(28f, t.preferredHeight + 8f);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, h);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = h;
                _rows.Add(go);
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;

            _canvas = UiKit.CreateCanvas("StoryReaderUI");
            _canvas.sortingOrder = 60; // just above the teleporter/beam layer — a find outranks pickers
            var root = _canvas.transform;

            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            const float w = 700f, h = 560f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            _title = UiKit.AddText(root, x + 24, y + 20, w - 48, 32, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;
            _label = UiKit.AddText(root, x + 24, y + 56, w - 48, 24, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            _list = UiKit.ScrollList(root, x + 16, y + 88, w - 32, h - 166, 6f);

            UiKit.AddButton(root, x + w - 24 - 220, y + h - 60, 220, 44, L("ui.reader.close"), Close);

            UiNav.Enable(_canvas.gameObject); // pad: A/B close, stick scrolls
            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
