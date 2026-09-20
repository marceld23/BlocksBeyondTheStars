// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Submit to the developers" (#1965): the texture editor's way to offer a painted texture for the game
    /// itself. It rides the feedback channel — same inbox, same spam key, same offline spool, same reply thread,
    /// so the answer ("it ships in version …") reaches the player inside the game — but it is its own dialog,
    /// because what it asks for is different: not "what went wrong" but three explicit consents.
    ///
    /// The players are often children. The dialog therefore asks for a NICKNAME and says not to use the real
    /// name, offers "no name", sends neither e-mail nor location nor machine facts (see
    /// <see cref="TextureSubmission"/>), and keeps the submit button off until all three boxes are ticked. The
    /// wording of the boxes was approved as it stands (<c>ui.tex.submit.check_*</c>); a change of meaning needs
    /// a new <see cref="TextureSubmission.ConsentTextVersion"/>.
    ///
    /// Lives on the texture editor's object and works without a world: the main menu has no
    /// <see cref="FeedbackUi"/>. A report it could not deliver goes to the shared spool, which the next world
    /// session flushes.
    /// </summary>
    public sealed class TextureSubmitDialog : MonoBehaviour
    {
        private const float W = 1920f, H = 1080f;

        /// <summary>The nickname typed last, offered again for the next texture of the same sitting.</summary>
        private static string _lastNickname = string.Empty;
        private static readonly string SessionId = Guid.NewGuid().ToString("N");

        public Func<string, string> Localize;
        public ClientSettings Settings;

        /// <summary>Reports the outcome to the editor's status line once the dialog closed.</summary>
        public Action<string, Color> OnOutcome;

        private Canvas _canvas;
        private InputField _nameInput, _nickInput, _noteInput;
        private Button _noNameBtn, _submitBtn, _cancelBtn;
        private readonly Button[] _checks = new Button[3];
        private readonly bool[] _checked = new bool[3];
        private Text _status;
        private bool _noName;

        private string _key;
        private byte[][] _frames;
        private int _fps;

        private bool _sending;
        private string _pendingJson, _pendingTitle;
        private Task<FeedbackUploadResult> _uploadTask;

        private static int _closedFrame = -1;
        private static int _openDialogs;

        /// <summary>True while the dialog is on screen — the editor then leaves canvas input alone.</summary>
        public bool IsOpen => _canvas != null;

        /// <summary>True while any submit dialog owns the cancel key — including the frame it closed in, so the
        /// SAME press does not also close the editor underneath, whichever Update runs first.</summary>
        public static bool OwnsCancel => _openDialogs > 0 || _closedFrame == Time.frameCount;

        private string L(string key) => Localize != null ? Localize(key) : key;

        public void Open(string key, string displayName, byte[][] frames, int fps)
        {
            if (IsOpen || frames == null || frames.Length == 0)
            {
                return;
            }

            _key = key;
            _frames = frames;
            _fps = fps;
            _noName = false;
            Array.Clear(_checked, 0, _checked.Length); // consent is given per texture, never remembered
            Build(displayName ?? key);
        }

        private void OnDestroy() => DestroyDialog();

        private void Update()
        {
            if (_uploadTask != null && _uploadTask.IsCompleted)
            {
                var task = _uploadTask;
                _uploadTask = null;
                OnUploadFinished(task.Status == TaskStatus.RanToCompletion ? task.Result : null);
            }

            if (IsOpen && !_sending && InputMap.Down(InputAction.UiCancel) && !UiKit.TextFieldFocused())
            {
                Close();
            }
        }

        // ---------------------------------------------------------------- dialog

        private void Build(string displayName)
        {
            _canvas = UiKit.CreateCanvas("TextureSubmitDialog");
            _openDialogs++;
            _canvas.sortingOrder = 70; // above the editor
            UiNav.Enable(_canvas.gameObject);
            var dim = UiKit.AddModalDim(_canvas.transform);

            const float pw = 900f, ph = 900f, m = 36f, innerW = pw - (2f * m);
            var panel = UiKit.AddDialogPanel(dim.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph);

            float y = 20f;
            UiKit.AddText(panel, m, y, innerW, 34f, L("ui.tex.submit.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            y += 42f;
            Wrapped(panel, m, y, innerW, 44f, L("ui.tex.submit.intro"), 15, UiKit.TextCol);
            y += 52f;

            UiKit.AddText(panel, m, y, innerW, 20f, L("ui.tex.submit.name_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 22f;
            _nameInput = UiKit.AddInput(panel, m, y, innerW, 38f, displayName, null, string.Empty, TextureSubmission.MaxNameLength);
            y += 48f;

            UiKit.AddText(panel, m, y, innerW, 20f, L("ui.tex.submit.nick_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 22f;
            _nickInput = UiKit.AddInput(panel, m, y, innerW - 230f, 38f, _lastNickname, null, L("ui.tex.submit.nick_placeholder"), TextureSubmission.MaxNicknameLength);
            _noNameBtn = UiKit.AddButton(panel, m + innerW - 220f, y, 220f, 38f, string.Empty, ToggleNoName);
            y += 48f;

            Wrapped(panel, m, y, innerW, 40f, L("ui.tex.submit.note_label"), 15, UiKit.TextCol);
            y += 42f;
            _noteInput = UiKit.AddInput(panel, m, y, innerW, 84f, string.Empty, null, string.Empty, TextureSubmission.MaxNoteLength);
            _noteInput.lineType = InputField.LineType.MultiLineNewline;
            if (_noteInput.textComponent != null)
            {
                _noteInput.textComponent.alignment = TextAnchor.UpperLeft;
            }

            y += 96f;

            // The three consents. Each is a button over the whole sentence, so a child hits it easily and a
            // gamepad can walk them.
            float[] heights = { 40f, 62f, 40f };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _checks[i] = UiKit.AddButton(panel, m, y, innerW, heights[i], string.Empty, () => ToggleCheck(index));
                var label = _checks[i].GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    label.fontSize = 15;
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    label.resizeTextForBestFit = false;
                    var rt = label.rectTransform;
                    rt.offsetMin = new Vector2(14f, rt.offsetMin.y);
                    rt.offsetMax = new Vector2(-10f, rt.offsetMax.y);
                }

                y += heights[i] + 8f;
            }

            y += 4f;
            Wrapped(panel, m, y, innerW, 84f, L("ui.tex.submit.what_is_sent"), 14, UiKit.CyanDim);
            y += 90f;

            _status = UiKit.AddText(panel, m, y, innerW, 40f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            float half = (innerW - 28f) / 2f;
            _submitBtn = UiKit.AddButton(panel, m, ph - 76f, half, 52f, L("ui.tex.submit.send"), OnSubmitClicked, "btn_feedback");
            _cancelBtn = UiKit.AddButton(panel, m + half + 28f, ph - 76f, half, 52f, L("ui.tex.submit.cancel"), Close, "btn_exit");

            RefreshLabels();
        }

        private static Text Wrapped(Transform parent, float x, float y, float w, float h, string text, int size, Color color)
        {
            var t = UiKit.AddText(parent, x, y, w, h, text, size, color, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        private void ToggleNoName()
        {
            _noName = !_noName;
            RefreshLabels();
        }

        private void ToggleCheck(int index)
        {
            if (_sending)
            {
                return;
            }

            _checked[index] = !_checked[index];
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            SetLabel(_noNameBtn, (_noName ? "☑  " : "☐  ") + L("ui.tex.submit.no_name"));
            if (_nickInput != null)
            {
                _nickInput.interactable = !_noName && !_sending;
            }

            for (int i = 0; i < 3; i++)
            {
                SetLabel(_checks[i], (_checked[i] ? "☑  " : "☐  ") + L("ui.tex.submit.check_" + (i + 1)));
            }

            if (_submitBtn != null)
            {
                _submitBtn.interactable = !_sending && _checked[0] && _checked[1] && _checked[2];
            }

            if (_cancelBtn != null)
            {
                _cancelBtn.interactable = !_sending;
            }
        }

        private static void SetLabel(Button button, string text)
        {
            var label = button != null ? button.GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                label.text = text;
            }
        }

        private void SetStatus(string text, Color color)
        {
            if (_status != null)
            {
                _status.text = text;
                _status.color = color;
            }
        }

        private void Close()
        {
            if (_sending)
            {
                return; // the answer decides between "sent", "queued" and "try again"
            }

            DestroyDialog();
        }

        private void DestroyDialog()
        {
            CancelInvoke();
            if (_canvas != null)
            {
                UiKit.ReleaseTextFieldFocus(_canvas.transform);
                Destroy(_canvas.gameObject);
                _canvas = null;
                _openDialogs = Mathf.Max(0, _openDialogs - 1);
                _closedFrame = Time.frameCount;
            }
        }

        // ---------------------------------------------------------------- send

        private void OnSubmitClicked()
        {
            if (_sending)
            {
                return;
            }

            var form = new TextureSubmissionForm
            {
                Key = _key,
                Name = _nameInput != null ? _nameInput.text : string.Empty,
                Nickname = _noName || _nickInput == null ? string.Empty : _nickInput.text,
                Note = _noteInput != null ? _noteInput.text : string.Empty,
                PaintedMyself = _checked[0],
                GrantsUse = _checked[1],
                OldEnoughOrParentsAgreed = _checked[2],
            };

            // Built on the main thread (PNG encoding is a Unity call); only the POST leaves the game loop.
            byte[] png = null;
            try
            {
                png = TexturePackFolder.EncodeStrip(_frames);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TextureSubmit] no PNG preview: " + e.Message); // the raw tile is what counts
            }

            var report = TextureSubmission.Build(form, _frames, _fps, png, AppShell.Version,
                FeedbackUi.ReplyKeyFor(Settings), SessionId, Application.platform.ToString(), DateTime.UtcNow);
            if (report == null)
            {
                SetStatus(L("ui.tex.submit.failed"), UiKit.Warn);
                return;
            }

            var uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);
            if (!uploader.IsConfigured)
            {
                // A build without the inbox key (a local or fork build) cannot submit — exporting still works.
                SetStatus(L("ui.tex.submit.not_configured"), UiKit.Warn);
                return;
            }

            if (!_noName)
            {
                _lastNickname = (form.Nickname ?? string.Empty).Trim();
            }

            _sending = true;
            RefreshLabels();
            SetStatus(L("ui.feedback.sending"), UiKit.CyanDim);

            string json = FeedbackUploader.Serialize(report, null);
            _pendingJson = json;
            _pendingTitle = report.Title;
#if UNITY_WEBGL && !UNITY_EDITOR
            // No sockets and no threads in the browser — the same body goes out via UnityWebRequest.
            StartCoroutine(FeedbackWebGlTransport.PostJson(json, OnUploadFinished));
#else
            _uploadTask = Task.Run(() => uploader.UploadRawJson(json));
#endif
        }

        private void OnUploadFinished(FeedbackUploadResult result)
        {
            _sending = false;
            string body = _pendingJson;
            string title = _pendingTitle ?? string.Empty;
            _pendingJson = null;
            _pendingTitle = null;

            if (result != null && result.Ok)
            {
                RememberSent(result.ReportId, title);
                Finish(L("ui.tex.submit.sent"));
            }
            else if (result != null && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                // The inbox refused this body — sending the same again cannot work, so it is not queued.
                SetStatus(L("ui.tex.submit.failed"), UiKit.Warn);
                RefreshLabels();
            }
            else if (!string.IsNullOrEmpty(body) && new FeedbackSpool(Path.Combine(AppPaths.Root, "feedback")).Write(body) != null)
            {
                WebGlStorage.Sync();
                Finish(L("ui.tex.submit.queued"));
            }
            else
            {
                SetStatus(L("ui.tex.submit.failed"), UiKit.Warn);
                RefreshLabels();
            }
        }

        /// <summary>The reply poll only runs while the install remembers a sent report (#1328) — a submission
        /// counts, or the developers' answer would never be fetched.</summary>
        private static void RememberSent(string reportId, string title)
        {
            if (string.IsNullOrEmpty(reportId))
            {
                return;
            }

            var log = new SentReportsLog(Path.Combine(AppPaths.Root, "feedback", "sent.json"));
            if (log.Record(reportId, title, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            {
                WebGlStorage.Sync();
            }
        }

        private void Finish(string message)
        {
            SetStatus(message, UiKit.Ok);
            RefreshLabels();
            if (_submitBtn != null)
            {
                _submitBtn.interactable = false; // sent is sent — no second copy by a double click
            }

            OnOutcome?.Invoke(message, UiKit.Ok);
            Invoke(nameof(DestroyDialog), 1.8f);
        }
    }
}
