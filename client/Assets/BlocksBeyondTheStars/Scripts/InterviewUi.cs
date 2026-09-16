// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The reporter's interview (2026-09 NPC professions): after "interview me" the player writes what they have been up
    /// to — at most 300 characters, screened by the server like a chat line and kept as the place's local news. In Safe chat
    /// mode there is no free text: the player picks one of four ready answers instead. Esc or "Not now" walks away.
    /// </summary>
    public sealed class InterviewUi : MonoBehaviour
    {
        public GameBootstrap Game;

        public static InterviewUi Instance { get; private set; }

        /// <summary>The server-side cap (<c>NewsArticle.MaxTextLength</c>).</summary>
        private const int MaxLength = 300;

        private static readonly string[] Presets = { "news.preset.explored", "news.preset.built", "news.preset.guardians", "news.preset.tamed" };

        private Canvas _canvas;
        private GameObject _overlay;
        private InputField _input;
        private GameObject _freeText;
        private GameObject _presetRow;
        private int _npcId;
        private bool _open;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Opens the interview for the reporter NPC that just asked.</summary>
        public void Open(int npcId)
        {
            if (Game == null)
            {
                return;
            }

            EnsureUi();
            _npcId = npcId;
            bool safe = string.Equals(Game.Rules?.ChatMode, "Safe", System.StringComparison.OrdinalIgnoreCase);
            _freeText.SetActive(!safe);
            _presetRow.SetActive(safe);
            _input.text = string.Empty;
            _overlay.SetActive(true);
            _open = true;
            Game.SetMenuOwner(this, true);
            if (!safe)
            {
                _input.ActivateInputField();
            }
        }

        private void Update()
        {
            if (_open && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        private void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Game?.Network?.SendInterviewAnswer(_npcId, text.Trim());
            Close();
        }

        private void Close()
        {
            UiKit.ReleaseTextFieldFocus(_overlay != null ? _overlay.transform : null);
            _overlay?.SetActive(false);
            _open = false;
            Game?.SetMenuOwner(this, false);
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("InterviewUi");
            _canvas.sortingOrder = 62; // above the HUD and the dialogue panel
            UiNav.Enable(_canvas.gameObject);

            const float w = 760f, h = 470f, m = 36f, innerW = w - 2f * m;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - w) / 2f, (1080f - h) / 2f, w, h);
            _overlay = overlay;

            UiKit.AddText(panel, m, 22, innerW, 34, L("ui.interview.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var prompt = UiKit.AddText(panel, m, 66, innerW, 56, L("ui.interview.prompt"), 16, UiKit.TextCol, TextAnchor.UpperLeft);
            prompt.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Free text (Open / Filtered chat mode).
            _freeText = new GameObject("FreeText", typeof(RectTransform));
            _freeText.transform.SetParent(panel, false);
            UiKit.Place(_freeText, 0, 0, w, h);
            _input = UiKit.AddInput(_freeText.transform, m, 130, innerW, 190, string.Empty, null, L("ui.interview.placeholder"), MaxLength, 18, multiline: true);
            UiKit.AddButton(_freeText.transform, m, 350, 330, 56, L("ui.interview.send"), () => Send(_input.text), "btn_feedback");
            UiKit.AddButton(_freeText.transform, m + 358, 350, 330, 56, L("ui.interview.cancel"), Close, "btn_exit");

            // Ready answers (Safe chat mode): the server stores the key and every reader sees it in their own language.
            _presetRow = new GameObject("Presets", typeof(RectTransform));
            _presetRow.transform.SetParent(panel, false);
            UiKit.Place(_presetRow, 0, 0, w, h);
            UiKit.AddText(_presetRow.transform, m, 124, innerW, 24, L("ui.interview.presets"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            for (int i = 0; i < Presets.Length; i++)
            {
                string key = Presets[i];
                UiKit.AddButton(_presetRow.transform, m, 154 + i * 52, innerW, 44, L(key), () => Send("@" + key));
            }

            UiKit.AddButton(_presetRow.transform, m, 380, 330, 56, L("ui.interview.cancel"), Close, "btn_exit");
            _overlay.SetActive(false);
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;
    }
}
