// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What one submit dialog sends: a texture's frames, or a creation's export files + preview (#3262).</summary>
    public sealed class CreationSubmitRequest
    {
        /// <summary><see cref="CreationSubmission.KindTexture"/>, <c>KindShip</c>, <c>KindStructure</c> or <c>KindAvatar</c>.</summary>
        public string Kind = CreationSubmission.KindTexture;
        public string Key = string.Empty;
        public string DisplayName = string.Empty;

        // A texture (#1965): the raw frames, the speed, tile or icon.
        public byte[][] Frames;
        public int Fps;
        public bool Icon;

        // A ship, a station/town module or an avatar skin (#3262): the export files and the editor's picture of it.
        public CreationPayload Payload;
        public byte[] PreviewPng;

        public bool IsTexture => Kind == CreationSubmission.KindTexture;
    }

    /// <summary>
    /// "Submit to the developers": the editors' way to offer something the player made for the game itself — a
    /// texture (#1965), a ship, a station or town module, an avatar skin (#3262). It rides the feedback channel — same
    /// inbox, same spam key, same offline spool, same reply thread, so the answer ("it ships in version …") reaches the
    /// player inside the game — but it is its own dialog, because what it asks for is different: not "what went wrong"
    /// but three explicit consents.
    ///
    /// The players are often children. The dialog therefore asks for a NICKNAME and says not to use the real name,
    /// offers "no name", sends neither e-mail nor location nor machine facts (see <see cref="TextureSubmission"/> and
    /// <see cref="CreationSubmission"/>), and sends nothing until all three boxes are ticked. Submit stays clickable
    /// all the same: a click with a box still open names what is missing and rings the open boxes (#2576) — a button
    /// that is merely off looked clickable and did nothing, which a child reads as "broken". Each kind has its own
    /// title, intro, consent sentences and "what is sent" (<see cref="TextKey"/>); the texture's approved wording
    /// (<c>ui.tex.submit.*</c>) stays as it was. A change of meaning needs a new consent text version.
    ///
    /// Lives on the editor's object and works without a world: the menu's own <see cref="FeedbackUi"/> (#2523) sleeps
    /// inside the editors. A report it could not deliver goes to the shared spool, which the next world session flushes.
    /// The sending half lives in <c>CreationSubmitDialog.Send.cs</c>.
    /// </summary>
    public sealed partial class CreationSubmitDialog : MonoBehaviour
    {
        private const float W = 1920f, H = 1080f;

        /// <summary>Inset of a consent sentence inside its button (left, right).</summary>
        private const float ConsentInsetLeft = 14f, ConsentInsetRight = 10f;

        /// <summary>Object names the EditMode tests find the dialog's parts by.</summary>
        public const string CanvasName = "CreationSubmitDialog", SubmitName = "Submit", CancelName = "Cancel",
            StatusName = "Status", ConsentName = "Consent", ConsentRingName = "ConsentRing";

        /// <summary>The nickname typed last, offered again for the next submission of the same sitting.</summary>
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
        private readonly Image[] _checkRings = new Image[3];
        private readonly bool[] _checked = new bool[3];
        private Text _status;
        private bool _noName;
        private bool _consentMissingShown; // Submit was pressed with a box open: the open boxes wear the ring
        private bool _done;                // sent or queued — the dialog is closing, nothing goes out twice

        private CreationSubmitRequest _request;

        private bool _sending;
        private string _pendingJson, _pendingTitle;
        private Task<FeedbackUploadResult> _uploadTask;

        private static int _closedFrame = -1;
        private static int _openDialogs;

        /// <summary>True while the dialog is on screen — the editor then leaves its own input alone.</summary>
        public bool IsOpen => _canvas != null;

        /// <summary>True while any submit dialog is on screen — an editor's camera and placement keep still meanwhile.</summary>
        public static bool IsAnyOpen => _openDialogs > 0;

        /// <summary>True while any submit dialog owns the cancel key — including the frame it closed in, so the
        /// SAME press does not also close the editor underneath, whichever Update runs first.</summary>
        public static bool OwnsCancel => _openDialogs > 0 || _closedFrame == Time.frameCount;

        /// <summary>The locale key of a text of the dialog for <paramref name="kind"/>. The texture keeps its approved
        /// keys (<c>ui.tex.submit.*</c>); a creation has its own title, intro, name label, the first two consent
        /// sentences, "what is sent" and the thank-you (<c>ui.submit.&lt;kind&gt;.*</c>) and shares the rest
        /// (nickname, note, the age consent, the buttons).</summary>
        public static string TextKey(string kind, string suffix)
        {
            if (kind == CreationSubmission.KindTexture || string.IsNullOrEmpty(kind))
            {
                return "ui.tex.submit." + suffix;
            }

            switch (suffix)
            {
                case "title":
                case "intro":
                case "name_label":
                case "check_1":
                case "check_2":
                case "what_is_sent":
                case "sent":
                    return "ui.submit." + kind + "." + suffix;
                case "not_configured":
                    return "ui.submit.not_configured";
                default:
                    return "ui.tex.submit." + suffix;
            }
        }

        private string L(string key) => Localize != null ? Localize(key) : key;

        private string T(string suffix) => L(TextKey(_request?.Kind ?? CreationSubmission.KindTexture, suffix));

        /// <summary>The texture editor's entry (#1965).</summary>
        public void Open(string key, string displayName, byte[][] frames, int fps, bool icon = false)
            => Open(new CreationSubmitRequest { Key = key, DisplayName = displayName, Frames = frames, Fps = fps, Icon = icon });

        /// <summary>Opens the dialog for a texture or a creation. Consent is given per submission, never remembered.</summary>
        public void Open(CreationSubmitRequest request)
        {
            if (IsOpen || request == null
                || (request.IsTexture ? request.Frames == null || request.Frames.Length == 0 : request.Payload == null))
            {
                return;
            }

            _request = request;
            _noName = false;
            _consentMissingShown = false;
            _done = false;
            Array.Clear(_checked, 0, _checked.Length);
            Build(string.IsNullOrEmpty(request.DisplayName) ? request.Key : request.DisplayName);
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
            _canvas = UiKit.CreateCanvas(CanvasName);
            _openDialogs++;
            _canvas.sortingOrder = 70; // above the editor
            UiNav.Enable(_canvas.gameObject);
            var dim = UiKit.AddModalDim(_canvas.transform);

            const float pw = 900f, ph = 900f, m = 36f, innerW = pw - (2f * m);
            var panel = UiKit.AddDialogPanel(dim.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph);

            float y = BuildForm(panel, m, innerW, displayName);
            y = BuildConsents(panel, m, innerW, y);
            Wrapped(panel, m, y, innerW, 84f, T("what_is_sent"), 14, UiKit.TextCol); // what is sent is no decoration (#3263)
            y += 90f;

            _status = UiKit.AddText(panel, m, y, innerW, 40f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.gameObject.name = StatusName;

            float half = (innerW - 28f) / 2f;
            _submitBtn = UiKit.AddFitButton(panel, m, ph - 76f, half, 52f, T("send"), OnSubmitClicked, "btn_feedback");
            _submitBtn.gameObject.name = SubmitName;
            _cancelBtn = UiKit.AddFitButton(panel, m + half + 28f, ph - 76f, half, 52f, T("cancel"), Close, "btn_exit");
            _cancelBtn.gameObject.name = CancelName;

            RefreshLabels();
        }

        /// <summary>Title, intro, name, nickname (+ "no name") and the note. Returns the next free y.</summary>
        private float BuildForm(Transform panel, float m, float innerW, string displayName)
        {
            float y = 20f;
            UiKit.AddText(panel, m, y, innerW, 34f, T("title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            y += 42f;
            Wrapped(panel, m, y, innerW, 44f, T("intro"), 15, UiKit.TextCol);
            y += 52f;

            UiKit.AddText(panel, m, y, innerW, 20f, T("name_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 22f;
            _nameInput = UiKit.AddInput(panel, m, y, innerW, 38f, displayName, null, string.Empty, CreationSubmission.MaxNameLength);
            y += 48f;

            UiKit.AddText(panel, m, y, innerW, 20f, T("nick_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 22f;
            _nickInput = UiKit.AddInput(panel, m, y, innerW - 230f, 38f, _lastNickname, null, T("nick_placeholder"), CreationSubmission.MaxNicknameLength);
            _noNameBtn = UiKit.AddFitButton(panel, m + innerW - 220f, y, 220f, 38f, string.Empty, ToggleNoName);
            y += 48f;

            Wrapped(panel, m, y, innerW, 40f, T("note_label"), 15, UiKit.TextCol);
            y += 42f;
            _noteInput = UiKit.AddInput(panel, m, y, innerW, 84f, string.Empty, null, string.Empty, CreationSubmission.MaxNoteLength);
            _noteInput.lineType = InputField.LineType.MultiLineNewline;
            if (_noteInput.textComponent != null)
            {
                _noteInput.textComponent.alignment = TextAnchor.UpperLeft;
            }

            return y + 96f;
        }

        /// <summary>The three consents. Each is a button over the whole sentence, so a child hits it easily and a gamepad
        /// can walk them. The amber ring behind each one lights up when Submit was pressed while that box was still open.
        /// Returns the next free y.</summary>
        private float BuildConsents(Transform panel, float m, float innerW, float y)
        {
            float[] heights = { 40f, 62f, 40f };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _checkRings[i] = UiKit.AddImage(panel, m - 3f, y - 3f, innerW + 6f, heights[i] + 6f, UiKit.PanelSprite, UiKit.Warn, Image.Type.Sliced);
                _checkRings[i].gameObject.name = ConsentRingName + (i + 1);
                _checkRings[i].enabled = false;
                _checks[i] = UiKit.AddButton(panel, m, y, innerW, heights[i], string.Empty, () => ToggleCheck(index));
                _checks[i].gameObject.name = ConsentName + (i + 1);
                LayoutConsentLabel(_checks[i].GetComponentInChildren<Text>(), innerW, heights[i]);
                y += heights[i] + 8f;
            }

            return y + 4f;
        }

        /// <summary>
        /// Lays a consent sentence over its button. <see cref="UiKit.AddButton"/> pins the label to a top-left POINT
        /// anchor (<see cref="UiKit.Place"/>), so the inset is a position plus a size. <c>offsetMin</c>/<c>offsetMax</c>
        /// are an inset only on a rect STRETCHED over its parent; here they turned the label into a rect from x = 14 to
        /// x = −10, a width of −24, and all three sentences vanished (#2575). Best fit (15 down to 13, the editors' floor —
        /// #3263; <c>EditorTextFitTests</c> checks every consent at 13 px) keeps the WHOLE sentence inside the box in every
        /// language: a consent the child cannot read in full is no consent.
        /// </summary>
        private static void LayoutConsentLabel(Text label, float width, float height)
        {
            if (label == null)
            {
                return;
            }

            label.alignment = TextAnchor.MiddleLeft;
            var rt = label.rectTransform;
            rt.anchoredPosition = new Vector2(ConsentInsetLeft, 0f);
            rt.sizeDelta = new Vector2(width - ConsentInsetLeft - ConsentInsetRight, height);
            UiKit.FitLabel(label, UiKit.EditorMinFontSize, 15);
        }

        private bool AllConsentsGiven => _checked[0] && _checked[1] && _checked[2];

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
            if (_sending || _done)
            {
                return;
            }

            _checked[index] = !_checked[index];
            if (_consentMissingShown && AllConsentsGiven)
            {
                _consentMissingShown = false;
                SetStatus(string.Empty, UiKit.CyanDim); // the hint has done its job
            }

            RefreshLabels();
        }

        private void RefreshLabels()
        {
            SetLabel(_noNameBtn, (_noName ? "☑  " : "☐  ") + T("no_name"));
            if (_nickInput != null)
            {
                _nickInput.interactable = !_noName && !_sending;
            }

            for (int i = 0; i < 3; i++)
            {
                SetLabel(_checks[i], (_checked[i] ? "☑  " : "☐  ") + T("check_" + (i + 1)));
                if (_checkRings[i] != null)
                {
                    _checkRings[i].enabled = _consentMissingShown && !_checked[i];
                }
            }

            if (_submitBtn != null)
            {
                // Not gated on the boxes: a click with one open says what is missing (OnSubmitClicked).
                _submitBtn.interactable = !_sending && !_done;
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
                if (Application.isPlaying)
                {
                    Destroy(_canvas.gameObject);
                }
                else
                {
                    DestroyImmediate(_canvas.gameObject); // EditMode tests close the dialog through its Cancel button
                }

                _canvas = null;
                _openDialogs = Mathf.Max(0, _openDialogs - 1);
                _closedFrame = Time.frameCount;
            }
        }
    }
}
