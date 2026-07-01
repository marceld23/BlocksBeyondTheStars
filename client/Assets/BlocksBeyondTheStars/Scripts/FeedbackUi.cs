// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Player feedback ("Spieler Feedback"): the F1 hotkey opens a modal dialog where any player can send a bug
    /// report OR a feature wish — one form, no type distinction: a title, a description, an optional e-mail and a
    /// short note that game data + a screenshot are attached. (F1 is advertised in the on-foot HUD controls hint.)
    ///
    /// On send we grab a full-frame screenshot WITH the HUD but WITHOUT this dialog (captured at the moment the
    /// dialog opens, while the live HUD is still on screen), gather a small client-side diagnostic snapshot,
    /// and fire BOTH paths:
    ///   • a client-direct HTTPS POST to the website API (<see cref="FeedbackUploader"/>) — reaches the devs on
    ///     any server, even someone else's dedicated server;
    ///   • the existing <c>/bump</c> message (<see cref="NetworkClient.SendBumpReport"/>) so the server also
    ///     writes its rich local snapshot (inventory/position/surroundings) when on an own/singleplayer server.
    ///
    /// Wired by <see cref="WorldRig"/> next to <see cref="HudUi"/> / <see cref="ChatUi"/>.
    /// </summary>
    public sealed class FeedbackUi : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        private const float W = 1920f, H = 1080f;

        private FeedbackUploader _uploader;
        private string _sessionId = string.Empty;

        // Dialog (built lazily on first open).
        private Canvas _dialogCanvas;
        private GameObject _dialog;
        private InputField _titleInput, _descInput, _emailInput;
        private Text _status;
        private Button _sendBtn, _cancelBtn;

        private bool _open;
        private bool _sending;
        private byte[] _shotJpg;                 // screenshot captured when the dialog opened
        private Task<FeedbackUploadResult> _uploadTask;

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void Start()
        {
            // A random id groups several reports from one sitting (varies per session; no Unity-restricted Date use).
            _sessionId = Guid.NewGuid().ToString("N");
            _uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // F1 opens feedback only during normal on-foot play (not in menus, flight, chat or the death prompt)
            // — matching when the HUD (and its controls hint) is up.
            bool canLaunch = !Game.MenuOpen && !Game.ChatTyping && !Game.AwaitingRespawnConfirm && !Game.SpaceViewActive;

            if (!_open && canLaunch && Input.GetKeyDown(KeyCode.F1))
            {
                Open();
            }

            if (_open && !_sending && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }

            // Marshal the background upload result back onto the main thread.
            if (_uploadTask != null && _uploadTask.IsCompleted)
            {
                var task = _uploadTask;
                _uploadTask = null;
                FeedbackUploadResult result;
                try { result = task.Result; }
                catch (Exception e) { result = new FeedbackUploadResult { Error = e.GetType().Name }; }
                OnUploadFinished(result);
            }
        }

        // --- Open / close ----------------------------------------------------------------------------------

        /// <summary>Opens the feedback dialog (the F1 hotkey's target). Captures the gameplay frame first — HUD
        /// visible, dialog not yet shown — then dims the screen and shows the form.</summary>
        public void Open()
        {
            if (_open || Game == null)
            {
                return;
            }

            _open = true;
            StartCoroutine(OpenRoutine());
        }

        private IEnumerator OpenRoutine()
        {
            // Capture at end of frame, before the dialog is shown and before MenuOpen hides the HUD: the shot
            // is the full frame WITH the HUD but WITHOUT this dialog (the requested look).
            yield return new WaitForEndOfFrame();
            _shotJpg = TryCaptureJpg();

            EnsureDialog();
            ResetFields();
            _dialog.SetActive(true);

            // Modal: free the cursor + pause on-foot control (mirrors GameMenu / BeamPadUi).
            Game.MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            CancelInvoke();
            _open = false;
            _sending = false;
            _shotJpg = null;
            if (_dialog != null) _dialog.SetActive(false);

            if (Game != null)
            {
                Game.MenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void ResetFields()
        {
            if (_titleInput != null) _titleInput.text = string.Empty;
            if (_descInput != null) _descInput.text = string.Empty;
            if (_emailInput != null) _emailInput.text = string.Empty;
            if (_status != null) { _status.text = string.Empty; _status.color = UiKit.CyanDim; }
            SetSendInteractable(true);
        }

        private void SetSendInteractable(bool on)
        {
            if (_sendBtn != null) _sendBtn.interactable = on;
        }

        // --- Dialog construction ---------------------------------------------------------------------------

        private void EnsureDialog()
        {
            if (_dialog != null)
            {
                return;
            }

            _dialogCanvas = UiKit.CreateCanvas("FeedbackDialog");
            _dialogCanvas.sortingOrder = 60; // above the HUD and the in-game menu
            UiNav.Enable(_dialogCanvas.gameObject); // gamepad can drive the dialog (inert on KB/mouse)
            var dim = UiKit.AddModalDim(_dialogCanvas.transform, 0.7f);
            _dialog = dim.gameObject;

            const float pw = 760f, ph = 648f;
            var panel = UiKit.AddPanel(_dialog.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph, UiKit.Panel).transform;
            const float m = 36f, innerW = pw - 2f * m;

            UiKit.AddText(panel, m, 22, innerW, 34, L("ui.feedback.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            UiKit.AddText(panel, m, 74, innerW, 20, L("ui.feedback.title_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _titleInput = UiKit.AddInput(panel, m, 96, innerW, 40, string.Empty, null, L("ui.feedback.title_placeholder"), 80);

            UiKit.AddText(panel, m, 146, innerW, 20, L("ui.feedback.desc_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _descInput = UiKit.AddInput(panel, m, 168, innerW, 150, string.Empty, null, L("ui.feedback.desc_placeholder"), 1500);
            _descInput.lineType = InputField.LineType.MultiLineNewline;
            if (_descInput.textComponent != null) _descInput.textComponent.alignment = TextAnchor.UpperLeft;

            UiKit.AddText(panel, m, 330, innerW, 20, L("ui.feedback.email_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _emailInput = UiKit.AddInput(panel, m, 352, innerW, 40, string.Empty, null, L("ui.feedback.email_placeholder"), 120);

            var hint = UiKit.AddText(panel, m, 404, innerW, 116, L("ui.feedback.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Truncate;

            _status = UiKit.AddText(panel, m, 524, innerW, 24, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);

            _sendBtn = UiKit.AddButton(panel, m, 560, 330, 56, L("ui.feedback.send"), OnSendClicked, "btn_feedback");
            _cancelBtn = UiKit.AddButton(panel, m + 358, 560, 330, 56, L("ui.menu.back"), Close, "btn_exit");

            _dialog.SetActive(false);
        }

        // --- Send ------------------------------------------------------------------------------------------

        private void OnSendClicked()
        {
            if (_sending)
            {
                return;
            }

            string desc = _descInput != null ? (_descInput.text ?? string.Empty).Trim() : string.Empty;
            if (desc.Length < 3)
            {
                if (_status != null) { _status.text = L("ui.feedback.need_text"); _status.color = UiKit.Warn; }
                return;
            }

            _sending = true;
            SetSendInteractable(false);
            if (_status != null) { _status.text = L("ui.feedback.sending"); _status.color = UiKit.CyanDim; }

            // Build the report on the MAIN thread (Unity APIs must not run off-thread); only the HTTP POST is
            // backgrounded.
            string title = _titleInput != null ? (_titleInput.text ?? string.Empty).Trim() : string.Empty;
            string email = _emailInput != null ? (_emailInput.text ?? string.Empty).Trim() : string.Empty;
            var report = BuildReport(title, desc, email);
            byte[] jpg = _shotJpg;

            // Path A — rich server snapshot via the existing /bump pipeline (meaningful on own/SP servers).
            string serverNote = string.IsNullOrEmpty(title) ? desc : title + " — " + desc;
            Game?.Network?.SendBumpReport("[feedback] " + serverNote, jpg ?? Array.Empty<byte>());

            // Path B — client-direct upload to the website API, off the game thread.
            if (_uploader != null && _uploader.IsConfigured)
            {
                _uploadTask = Task.Run(() => _uploader.Upload(report, jpg));
            }
            else
            {
                // Dev build without an API key: the local /bump snapshot was still written.
                if (_status != null) { _status.text = L("ui.feedback.sent_local"); _status.color = UiKit.Ok; }
                Game?.ShowMessage(L("ui.feedback.sent_local"));
                _sending = false;
                Invoke(nameof(Close), 1.4f);
            }
        }

        private void OnUploadFinished(FeedbackUploadResult result)
        {
            _sending = false;
            if (result != null && result.Ok)
            {
                if (_status != null) { _status.text = L("ui.feedback.sent"); _status.color = UiKit.Ok; }
                Game?.ShowMessage(L("ui.feedback.sent"));
                Invoke(nameof(Close), 1.2f);
            }
            else
            {
                if (_status != null) { _status.text = L("ui.feedback.failed"); _status.color = UiKit.Warn; }
                SetSendInteractable(true); // allow a retry
            }
        }

        private FeedbackReport BuildReport(string title, string desc, string email)
        {
            var report = new FeedbackReport
            {
                Title = title,
                Description = desc,
                Email = email,
                GameVersion = AppShell.Version,
                BuildNumber = Application.buildGUID ?? string.Empty,
                PlayerId = Settings != null ? Settings.PlayerToken : string.Empty,
                PlayerName = Settings != null && !string.IsNullOrEmpty(Settings.PlayerName) ? Settings.PlayerName : (Game != null ? Game.PlayerName : string.Empty),
                SessionId = _sessionId,
                Platform = Application.platform.ToString(),
                ClientTimestamp = DateTime.UtcNow.ToString("o"),
                ScreenshotFileName = "feedback_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".jpg",
                ReportJson = new Dictionary<string, object>
                {
                    ["location"] = Game != null ? Game.LocationName : string.Empty,
                    ["station"] = Game != null ? Game.StationName : string.Empty,
                    ["worldSeed"] = Game != null ? Game.WorldSeed : 0L,
                    ["health"] = Game != null ? Mathf.RoundToInt(Game.Health) : 0,
                    ["oxygen"] = Game != null ? Mathf.RoundToInt(Game.Oxygen) : 0,
                    ["energy"] = Game != null ? Mathf.RoundToInt(Game.SuitEnergy) : 0,
                    ["hunger"] = Game != null ? Mathf.RoundToInt(Game.Hunger) : 0,
                    ["sessionSeconds"] = Game != null ? Mathf.RoundToInt(Game.SessionSeconds) : 0,
                    ["language"] = Settings != null ? Settings.Language : string.Empty,
                },
            };
            return report;
        }

        // --- Screenshot ------------------------------------------------------------------------------------

        private byte[] TryCaptureJpg()
        {
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                try { return EncodeDownscaledJpg(shot, 1600, 70); }
                finally { Destroy(shot); }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Feedback] screenshot failed: {e.Message}");
                return null;
            }
        }

        /// <summary>JPG-encodes a screenshot, downscaled so its longest side is at most <paramref name="maxDim"/>
        /// (keeps the upload small). Mirrors <see cref="ChatUi"/>'s /bump encoder.</summary>
        private static byte[] EncodeDownscaledJpg(Texture2D src, int maxDim, int quality)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)maxDim / Mathf.Max(w, h));
            int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int th = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            if (tw == w && th == h)
            {
                return ImageConversion.EncodeToJPG(src, quality);
            }

            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(tw, th, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = ImageConversion.EncodeToJPG(small, quality);
            UnityEngine.Object.Destroy(small);
            return jpg;
        }
    }
}
