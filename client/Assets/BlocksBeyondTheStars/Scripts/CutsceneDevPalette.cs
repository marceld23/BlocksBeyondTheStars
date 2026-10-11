// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cutscene author's preview (#2468, synthesis §6.8 "Vorschau"): a small developer palette that lists every
    /// cutscene of the loaded story packs (and each world story's recap card) and plays the clicked one on this client
    /// alone — no server round trip, no ledger, no confirmation (<see cref="CutsceneDirector.PlayLocal"/>). Available in the
    /// Editor and development builds, or in any build started with <c>-devSeq</c>; F9 opens and closes it. Drawn with
    /// absolute IMGUI calls through <see cref="ImguiOverlay"/> (disabled while closed, so it costs nothing). The server-side
    /// path is the admin command <c>/cutscene &lt;pack&gt; &lt;id&gt;</c>.
    /// </summary>
    public sealed class CutsceneDevPalette : MonoBehaviour
    {
        public GameBootstrap Game;
        public CutsceneDirector Director;

        /// <summary>The command-line switch that enables the palette in a release build.</summary>
        public const string CommandLineFlag = "-devSeq";

        private const KeyCode ToggleKey = KeyCode.F9;
        private const float RefH = 1080f;
        private const float PanelX = 24f, PanelY = 80f, PanelW = 560f, RowH = 30f;

        private ImguiOverlay _overlay;
        private bool _enabled;
        private bool _open;
        private Vector2 _scroll;

        /// <summary>Whether this build offers the palette: the Editor, a development build or <c>-devSeq</c>.</summary>
        public static bool Available()
        {
            if (Debug.isDebugBuild)
            {
                return true;
            }

            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            _enabled = Available();
            _overlay = ImguiOverlay.Attach(gameObject, Draw, repaintOnly: false);
        }

        private void Update()
        {
            if (!_enabled || Game == null || Director == null)
            {
                return;
            }

            if (Input.GetKeyDown(ToggleKey) && !Game.ChatTyping)
            {
                SetOpen(!_open);
            }

            if (_open && Input.GetKeyDown(KeyCode.Escape))
            {
                SetOpen(false);
                Game.MarkMenuInputHandled(); // this Esc closes the palette, not the game
            }
        }

        private void SetOpen(bool open)
        {
            _open = open;
            Game?.SetMenuOwner(this, open); // the cursor is free and the controls rest while the list is up
            _overlay.Sync(open);
        }

        private void Draw()
        {
            if (!_open || Director == null)
            {
                return;
            }

            float s = Mathf.Max(0.5f, Screen.height / RefH);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            var entries = Director.PaletteEntries();
            float height = Mathf.Min(RefH - PanelY - 40f, 76f + entries.Count * RowH + 10f);
            GUI.Box(new Rect(PanelX, PanelY, PanelW, height), GUIContent.none);
            GUI.Label(new Rect(PanelX + 12f, PanelY + 8f, PanelW - 24f, 24f), L("ui.devseq.title"));
            if (GUI.Button(new Rect(PanelX + PanelW - 132f, PanelY + 36f, 120f, 26f), L("ui.devseq.close")))
            {
                SetOpen(false);
            }

            var view = new Rect(PanelX + 8f, PanelY + 70f, PanelW - 16f, height - 78f);
            var content = new Rect(0f, 0f, view.width - 20f, entries.Count * RowH);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < entries.Count; i++)
            {
                var (pack, id) = entries[i];
                if (GUI.Button(new Rect(0f, i * RowH, content.width, RowH - 4f), pack + " / " + id))
                {
                    SetOpen(false); // close first: the director waits for a free screen
                    Director.PlayLocal(pack, id);
                }
            }

            GUI.EndScrollView();
            GUI.matrix = keep;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnDestroy()
        {
            if (_open)
            {
                Game?.SetMenuOwner(this, false);
            }
        }
    }
}
