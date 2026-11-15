// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared frame of every editor (#3688, epic #3696): the menu template for the creative tools. Before it, no
    /// editor used the template — every button the same tier, Back in eight places, five title styles, ten words for
    /// Save, errors in green, file paths in the status line.
    /// <para>
    /// Built on the editor's own canvas (<see cref="UiKit.CreateCanvas"/>, 1920 × 1080 reference, not UI-scalable — see
    /// <see cref="EditorShellLayout"/>): a header bar — Back top left, the title (the start card's name) with the work's
    /// name and "Not saved yet" under it, then on the right Undo / Redo, Load / New, Submit and the ONE primary Save with a
    /// subline saying what saving does — the work area for the editor's own content (<see cref="WorkArea"/>, opaque cards
    /// via <see cref="AddSideCard"/>), and a status bar that colours messages by severity (<see cref="SetStatus"/>) and
    /// never shows a file path. Optional buttons exist only when the editor provides their action.
    /// </para>
    /// <para>
    /// Drafts (#3689): the open work is written as a draft while it is unsaved (debounced, <see cref="EditorDraftStore"/>),
    /// once more when the editor is left, and offered again on the next opening (<see cref="OfferDraft"/>); Save removes
    /// it. Esc / pad B (#3690): <see cref="HandleCancel"/> closes the topmost layer and never the editor. Confirmations:
    /// <see cref="Confirm"/>, <see cref="ConfirmDelete"/>, <see cref="ConfirmDiscard"/>.
    /// </para>
    /// <para>
    /// Pad: the header is built after the work area, and Back is the header's last control, so the first focus is the
    /// editor's first control (or <see cref="PreferFocus"/>), never Back. The shell reads no shoulder buttons.
    /// </para>
    /// </summary>
    public sealed partial class EditorShell : MonoBehaviour
    {
        public const string WorkAreaName = "EditorWorkArea", StatusBarName = "EditorStatusBar", HeaderName = "EditorHeader";

        public const string BackName = "EditorBack", UndoName = "EditorUndo", RedoName = "EditorRedo", LoadName = "EditorLoad",
            NewName = "EditorNew", SubmitName = "EditorSubmit", SaveName = "EditorSave";

        public const string TitleName = "EditorTitle", DocumentLineName = "EditorDocument", UnsavedName = "EditorUnsaved",
            SublineName = "EditorSaveSubline", StatusTextName = "EditorStatusText", StatusMarkName = "EditorStatusMark",
            HintName = "EditorHint";

        /// <summary>The header and status bar's opaque fill (the menu card's colour).</summary>
        private static readonly Color BarFill = new Color(0.02f, 0.06f, 0.13f, 0.98f);

        private EditorShellLayout _layout;
        private Text _title, _documentLine, _unsaved, _subline, _statusText, _hint;
        private Image _unsavedDot, _statusMark, _statusError;
        private string _shownName;
        private bool _shownDirty, _built;
        private int _statusVersion;

        // Every live shell, newest last: an editor can open over another (the appearance editor over the avatar designer),
        // and closing the top one must hand Esc back to the one below instead of to nobody.
        private static readonly System.Collections.Generic.List<EditorShell> Live = new System.Collections.Generic.List<EditorShell>(2);

        /// <summary>The most recently built editor shell that is still alive — the one Esc / pad B goes to
        /// (<c>AppShell</c> / <c>GameMenu</c> call <see cref="HandleCancel"/> on it). When it goes, the shell below it (if
        /// any) is current again.</summary>
        public static EditorShell Current
        {
            get
            {
                for (int i = Live.Count - 1; i >= 0; i--)
                {
                    if (Live[i] != null && Live[i].isActiveAndEnabled)
                    {
                        return Live[i];
                    }

                    if (Live[i] == null)
                    {
                        Live.RemoveAt(i); // destroyed without OnDestroy (domain reload, edit-mode tests)
                    }
                }

                return null;
            }
        }

        /// <summary>False in the browser: the editors are desktop-only (maintainer, 2026-11-15), and the ones reachable in
        /// a world hide desktop-only functions there — "open folder", file exports, pasting from the clipboard.</summary>
        public static bool DesktopFeatures
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }

        public IEditorDocument Document { get; private set; }

        public EditorShellOptions Options { get; private set; }

        public Canvas Canvas { get; private set; }

        /// <summary>Where the editor's own content goes: a full-canvas container that sits BEFORE the header in the
        /// hierarchy (pad first focus) — place content inside <see cref="WorkRect"/> with <see cref="UiKit.Place"/>
        /// coordinates as before.</summary>
        public Transform WorkArea { get; private set; }

        /// <summary>The free area between header and status bar, in canvas reference coordinates.</summary>
        public static HudRect WorkRect => EditorShellLayout.WorkRect;

        public Button BackButton { get; private set; }

        public Button SaveButton { get; private set; }

        /// <summary>Null unless <see cref="EditorShellOptions.HasUndo"/>.</summary>
        public Button UndoButton { get; private set; }

        public Button RedoButton { get; private set; }

        /// <summary>Null unless <see cref="EditorShellOptions.OnLoad"/> is set.</summary>
        public Button LoadButton { get; private set; }

        /// <summary>Null unless <see cref="EditorShellOptions.OnNew"/> is set.</summary>
        public Button NewButton { get; private set; }

        /// <summary>Null unless <see cref="EditorShellOptions.OnSubmit"/> is set.</summary>
        public Button SubmitButton { get; private set; }

        /// <summary>The status bar's message.</summary>
        public Text StatusText => _statusText;

        /// <summary>The severity of the message on show.</summary>
        public EditorStatusSeverity StatusSeverity { get; private set; }

        /// <summary>
        /// Builds the shell on <paramref name="canvas"/> for <paramref name="document"/> and returns it (a component on the
        /// canvas object). Call it right after creating the canvas, before the editor's content — the content goes under
        /// <see cref="WorkArea"/>. Then <see cref="OfferDraft"/> once the editor can take a draft.
        /// </summary>
        public static EditorShell Build(Canvas canvas, IEditorDocument document, EditorShellOptions options)
        {
            if (canvas == null || document == null || options == null)
            {
                throw new System.ArgumentNullException(canvas == null ? nameof(canvas) : document == null ? nameof(document) : nameof(options));
            }

            var shell = canvas.gameObject.AddComponent<EditorShell>();
            shell.Init(canvas, document, options);
            return shell;
        }

        private void Init(Canvas canvas, IEditorDocument document, EditorShellOptions options)
        {
            Canvas = canvas;
            Document = document;
            Options = options;
            _layout = EditorShellLayout.Compute(ActionsOf(options));
            var root = canvas.transform;
            WorkArea = UiKit.AddStretchChild(root, WorkAreaName).transform;
            BuildStatusBar(root);
            BuildHeader(root);
            UiNav.Enable(canvas.gameObject, options.PadHints);
            InitDrafts();
            _built = true;
            Live.Remove(this);
            Live.Add(this);
            Refresh();
        }

        /// <summary>The header actions <paramref name="options"/> provide.</summary>
        public static EditorShellActions ActionsOf(EditorShellOptions options)
        {
            var actions = EditorShellActions.None;
            actions |= options.HasUndo ? EditorShellActions.Undo : 0;
            actions |= options.OnLoad != null ? EditorShellActions.Load : 0;
            actions |= options.OnNew != null ? EditorShellActions.New : 0;
            actions |= options.OnSubmit != null ? EditorShellActions.Submit : 0;
            return actions;
        }

        private string L(string key) => string.IsNullOrEmpty(key) ? string.Empty : (Options.Localize ?? UiKit.L)(key);

        // ---- header ------------------------------------------------------------------------------------------------

        private void BuildHeader(Transform root)
        {
            var bar = Bar(root, HeaderName, EditorShellLayout.HeaderRect);
            UiKit.AddImage(bar, 0f, EditorShellLayout.HeaderH - 2f, EditorShellLayout.CanvasW, 2f, UiKit.SolidSprite,
                new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.6f));
            BuildTitle(bar);

            if (_layout.Has(EditorHeaderSlot.Undo))
            {
                UndoButton = HeaderButton(bar, EditorHeaderSlot.Undo, UndoName, "ui.editor.undo", Undo, ButtonTier.Quiet, "btn_undo");
                RedoButton = HeaderButton(bar, EditorHeaderSlot.Redo, RedoName, "ui.editor.redo", Redo, ButtonTier.Quiet, "btn_redo");
            }

            if (_layout.Has(EditorHeaderSlot.Load))
            {
                LoadButton = HeaderButton(bar, EditorHeaderSlot.Load, LoadName, "ui.editor.load", () => Options.OnLoad?.Invoke(),
                    ButtonTier.Secondary, "btn_load");
            }

            if (_layout.Has(EditorHeaderSlot.New))
            {
                NewButton = HeaderButton(bar, EditorHeaderSlot.New, NewName, "ui.editor.new", StartNew, ButtonTier.Secondary, "btn_new");
            }

            if (_layout.Has(EditorHeaderSlot.Submit))
            {
                SubmitButton = HeaderButton(bar, EditorHeaderSlot.Submit, SubmitName, "ui.submit.button",
                    () => Options.OnSubmit?.Invoke(), ButtonTier.Secondary, "btn_submit");
            }

            SaveButton = HeaderButton(bar, EditorHeaderSlot.Save, SaveName, "ui.editor.save", Save, ButtonTier.Primary, "btn_save");
            var sub = _layout.Rect(EditorHeaderSlot.SaveSubline);
            _subline = UiKit.AddText(bar, sub.X, sub.Y, sub.W, sub.H, L(Options.SaveSublineKey), EditorShellLayout.MinTextSize,
                UiKit.CyanDim, TextAnchor.MiddleRight);
            _subline.name = SublineName;
            UiKit.FitLabelSingleLine(_subline, sub.W, EditorShellLayout.MinTextSize, EditorShellLayout.MinTextSize);

            // Last in the hierarchy: the pad's first focus never lands on Back (UiNav focuses in hierarchy order).
            BackButton = HeaderButton(bar, EditorHeaderSlot.Back, BackName, "ui.menu.back", Leave, ButtonTier.Quiet, "btn_back");
        }

        private void BuildTitle(Transform bar)
        {
            var r = _layout.Rect(EditorHeaderSlot.Title);
            _title = UiKit.AddText(bar, r.X, 6f, r.W, 44f, L(Options.TitleKey), 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _title.name = TitleName;
            UiKit.FitLabelSingleLine(_title, r.W, 20, 30);
            _documentLine = UiKit.AddText(bar, r.X, 52f, r.W, 28f, string.Empty, 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _documentLine.name = DocumentLineName;
            _unsavedDot = UiKit.AddImage(bar, r.X, 60f, 12f, 12f, UiKit.DiscSprite, UiKit.Warn);
            _unsaved = UiKit.AddText(bar, r.X + 18f, 52f, r.W, 28f, L("ui.editor.unsaved"), 18, UiKit.Warn, TextAnchor.MiddleLeft);
            _unsaved.name = UnsavedName;
        }

        private Button HeaderButton(Transform bar, EditorHeaderSlot slot, string name, string labelKey, System.Action onClick,
            ButtonTier tier, string icon)
        {
            var r = _layout.Rect(slot);
            var button = UiKit.AddTierButton(bar, r.X, r.Y, r.W, r.H, L(labelKey), onClick, tier, icon);
            button.name = name;
            FitButtonLabel(button, r.W);
            return button;
        }

        /// <summary>Keeps a header label on one line and never under the shell's minimum text size (AddButton's own fit
        /// may go down to 11).</summary>
        private static void FitButtonLabel(Button button, float width)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                return;
            }

            float labelW = Mathf.Max(EditorShellLayout.MinControl, label.rectTransform.sizeDelta.x);
            UiKit.FitLabelSingleLine(label, Mathf.Min(labelW, width), EditorShellLayout.MinTextSize, 22);
        }

        /// <summary>An opaque bar that catches clicks (they must not reach a 3D view behind it).</summary>
        private static Transform Bar(Transform root, string name, HudRect rect)
        {
            var image = UiKit.AddImage(root, rect.X, rect.Y, rect.W, rect.H, UiKit.SolidSprite, BarFill);
            image.gameObject.name = name;
            image.raycastTarget = true;
            return image.transform;
        }

        // ---- status bar ----------------------------------------------------------------------------------------------

        private void BuildStatusBar(Transform root)
        {
            var bar = Bar(root, StatusBarName, EditorShellLayout.StatusRect);
            UiKit.AddImage(bar, 0f, 0f, EditorShellLayout.CanvasW, 2f, UiKit.SolidSprite,
                new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.6f));
            const float m = EditorShellLayout.Margin, h = EditorShellLayout.StatusH;
            _statusMark = UiKit.AddImage(bar, m, (h - 14f) / 2f, 14f, 14f, UiKit.DiscSprite, Color.white);
            _statusMark.name = StatusMarkName;
            _statusError = UiKit.AddIcon(bar, m - 5f, (h - 24f) / 2f, 24f, "btn_close");
            _statusText = UiKit.AddText(bar, m + 26f, 2f, 1140f, h - 4f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            _statusText.name = StatusTextName;
            UiKit.FitLabel(_statusText, EditorShellLayout.MinTextSize, 18);
            const float hintW = 680f;
            _hint = UiKit.AddText(bar, EditorShellLayout.CanvasW - m - hintW, 2f, hintW, h - 4f, string.Empty,
                EditorShellLayout.MinTextSize, UiKit.CyanDim, TextAnchor.MiddleRight);
            _hint.name = HintName;
            UiKit.FitLabel(_hint, EditorShellLayout.MinTextSize, EditorShellLayout.MinTextSize);
            ShowMark(null);
        }

        /// <summary>
        /// Shows a message in the status bar, coloured by <paramref name="severity"/> — Info light, Success green (the only
        /// green), Warning amber, Error red with a cross. File paths are cut to their last part
        /// (<see cref="EditorStatusText.WithoutPaths"/>), line breaks become spaces. It stays until the next message.
        /// </summary>
        public void SetStatus(string text, EditorStatusSeverity severity)
        {
            _statusVersion++;
            StatusSeverity = severity;
            _statusText.text = EditorStatusText.WithoutPaths(text).Replace("\r", string.Empty).Replace('\n', ' ');
            _statusText.color = SeverityColor(severity);
            ShowMark(string.IsNullOrEmpty(_statusText.text) ? (EditorStatusSeverity?)null : severity);
        }

        /// <summary>Empties the status bar.</summary>
        public void ClearStatus() => SetStatus(string.Empty, EditorStatusSeverity.Info);

        /// <summary>The short controls line on the right of the status bar (at most four keys, as the HUD's zone G).</summary>
        public void SetHint(string text) => _hint.text = EditorStatusText.WithoutPaths(text);

        /// <summary>The colour of a status message.</summary>
        public static Color SeverityColor(EditorStatusSeverity severity) => severity switch
        {
            EditorStatusSeverity.Success => UiKit.Ok,
            EditorStatusSeverity.Warning => UiKit.Warn,
            EditorStatusSeverity.Error => UiKit.DangerCol,
            _ => UiKit.TextCol,
        };

        private void ShowMark(EditorStatusSeverity? severity)
        {
            bool error = severity == EditorStatusSeverity.Error && _statusError != null;
            _statusMark.enabled = severity.HasValue && severity != EditorStatusSeverity.Info && !error;
            _statusMark.color = severity.HasValue ? SeverityColor(severity.Value) : Color.white;
            if (_statusError != null)
            {
                _statusError.enabled = error;
                _statusError.color = UiKit.DangerCol; // the cross, not a green dot: an error reads as one without colour too
            }
        }

        // ---- editor-facing helpers -----------------------------------------------------------------------------------

        /// <summary>An opaque card of <paramref name="width"/> on the left or right of the work area (palette, inspector).
        /// It catches clicks, so a click on its border never places a block in the 3D view behind it.</summary>
        public Transform AddSideCard(bool right, float width)
        {
            var r = EditorShellLayout.SideCard(right, width);
            return AddCard(r.X, r.Y, r.W, r.H);
        }

        /// <summary>An opaque card at a rect of the canvas (inside <see cref="WorkRect"/>), catching clicks.</summary>
        public Transform AddCard(float x, float y, float w, float h)
        {
            var card = UiKit.AddCard(WorkArea, x, y, w, h);
            var image = card.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = true;
            }

            return card;
        }

        /// <summary>The control a pad lands on first (the editor's main tool) — see <see cref="UiNav.Prefer"/>.</summary>
        public void PreferFocus(Selectable control, bool takeOver = false) => UiNav.Prefer(Canvas.gameObject, control, takeOver);

        /// <summary>Replaces the title (an editor with modes: station / village).</summary>
        public void SetTitle(string text)
        {
            _title.text = text ?? string.Empty;
            UiKit.FitLabelSingleLine(_title, _layout.Rect(EditorHeaderSlot.Title).W, 20, 30);
        }

        /// <summary>Replaces the line under Save (what saving does changes with a mode).</summary>
        public void SetSaveSubline(string text)
        {
            _subline.text = text ?? string.Empty;
            UiKit.FitLabelSingleLine(_subline, _layout.Rect(EditorHeaderSlot.SaveSubline).W, EditorShellLayout.MinTextSize,
                EditorShellLayout.MinTextSize);
        }

        /// <summary>Shows Submit only where this work can be submitted, instead of a button that answers "no".</summary>
        public void SetSubmitAvailable(bool available)
        {
            if (SubmitButton != null)
            {
                SubmitButton.gameObject.SetActive(available);
            }
        }

        // ---- per frame -----------------------------------------------------------------------------------------------

        private void Update()
        {
            if (!_built)
            {
                return;
            }

            HandleShortcuts();
            Tick(Time.unscaledTimeAsDouble);
        }

        /// <summary>One frame of the shell without input: the header follows the document (undo / redo state, name,
        /// "Not saved yet") and a due draft is written. <c>Update</c> calls it; tests call it with their own clock.</summary>
        public void Tick(double now)
        {
            Refresh();
            TickDrafts(now);
        }

        /// <summary>Brings the header in line with the document right away.</summary>
        public void Refresh()
        {
            if (UndoButton != null)
            {
                UndoButton.interactable = Document.CanUndo;
                RedoButton.interactable = Document.CanRedo;
            }

            bool dirty = Document.IsDirty;
            string name = Document.DocumentName ?? string.Empty;
            if (dirty == _shownDirty && name == _shownName)
            {
                return;
            }

            _shownDirty = dirty;
            _shownName = name;
            var r = _layout.Rect(EditorHeaderSlot.Title);
            float maxW = r.W * 0.6f; // the rest of the line is for "Not saved yet"
            string shown = name.Length > 0 && UiKit.MeasureWidth(name, 18, FontStyle.Normal) > maxW ? UiKit.Ellipsize(name, maxW, 26f, 18) : name;
            _documentLine.text = shown;
            float nameW = shown.Length == 0 ? 0f : Mathf.Min(UiKit.MeasureWidth(shown, 18, FontStyle.Normal), maxW);
            float x = r.X + (nameW > 0f ? nameW + 20f : 0f);
            Place(_unsavedDot.rectTransform, x, 60f);
            Place(_unsaved.rectTransform, x + 18f, 52f);
            float unsavedW = Mathf.Max(EditorShellLayout.MinControl, r.Right - x - 18f);
            _unsaved.rectTransform.sizeDelta = new Vector2(unsavedW, 28f);
            UiKit.FitLabelSingleLine(_unsaved, unsavedW, EditorShellLayout.MinTextSize, 18);
            _unsavedDot.enabled = dirty;
            _unsaved.enabled = dirty;
        }

        private static void Place(RectTransform rt, float x, float y) => rt.anchoredPosition = new Vector2(x, -y);

        private void HandleShortcuts()
        {
            if (!Options.Shortcuts || IsDialogOpen || UiKit.TextFieldFocused())
            {
                return;
            }

            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                return;
            }

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                Save();
            }
        }

        /// <summary>Takes the newest step back (the Undo button, Ctrl+Z).</summary>
        public void Undo()
        {
            if (!Document.CanUndo)
            {
                return;
            }

            Document.Undo();
            MarkChanged();
            Refresh();
        }

        /// <summary>Puts the newest undone step back (the Redo button, Ctrl+Y, Ctrl+Shift+Z).</summary>
        public void Redo()
        {
            if (!Document.CanRedo)
            {
                return;
            }

            Document.Redo();
            MarkChanged();
            Refresh();
        }

        private void OnDestroy()
        {
            FlushDraftOnDestroy();
            Live.Remove(this);
        }
    }
}
