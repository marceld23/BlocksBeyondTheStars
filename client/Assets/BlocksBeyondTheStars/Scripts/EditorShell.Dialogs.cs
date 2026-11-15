// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine.EventSystems;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What <see cref="EditorShell.HandleCancel"/> did with an Esc / pad B press.</summary>
    public enum EditorCancelResult
    {
        /// <summary>A text field just took the key (it only left the field) — nothing closed.</summary>
        TextField,

        /// <summary>A layer that reads its own cancel had it (the submit dialog, the pad's canvas mode).</summary>
        OwnedElsewhere,

        /// <summary>One of the shell's dialogs closed (a confirmation, the draft question).</summary>
        ClosedDialog,

        /// <summary>The editor closed its topmost layer (<see cref="IEditorDocument.CloseTopLayer"/>).</summary>
        ClosedLayer,

        /// <summary>Nothing was open: the editor stays, the status bar says how to leave and a pad's focus moves to Back.</summary>
        PointedAtBack,
    }

    /// <summary>
    /// The shell's dialogs and the Esc / pad B rule (#3690). Esc and B used to close any menu editor at once — while typing
    /// in a text field, with a picker open, and in the ship editor's build view where B only meant "leave the view". Now a
    /// press closes the topmost layer: the shell's own dialog, then the editor's picker / dialog / panel; with nothing
    /// open it closes nothing. An editor is left only through Back, and its draft keeps the work.
    /// </summary>
    public sealed partial class EditorShell
    {
        private UiKit.ConfirmDialog _dialog;
        private bool _draftPromptOpen;

        /// <summary>The layers that read their own cancel key and must keep the press (the submit dialog, the pad's canvas
        /// mode — each covers the frame it closed in, whichever Update runs first). A host may add its own; tests swap it,
        /// since those flags are static.</summary>
        public static System.Func<bool> CancelOwnedElsewhere = DefaultCancelOwner;

        /// <summary>The stock <see cref="CancelOwnedElsewhere"/>.</summary>
        public static bool DefaultCancelOwner() => CreationSubmitDialog.OwnsCancel || PadCanvasFocus.OwnsCancel;

        /// <summary>True while one of the shell's dialogs is open (a confirmation, the draft question).</summary>
        public bool IsDialogOpen => _dialog != null && _dialog.IsOpen;

        /// <summary>The open dialog of the shell, or null — for tests.</summary>
        public UiKit.ConfirmDialog OpenDialogNow => IsDialogOpen ? _dialog : null;

        /// <summary>
        /// Esc / pad B for this editor. The ONE reader of the key (<c>AppShell</c> for the menu editors, <c>GameMenu</c> in a
        /// world) calls it instead of closing the editor — the shell reads no cancel key itself, so a press is never handled
        /// twice. <paramref name="textFieldRecent"/> is the caller's "a text field had the keyboard this or last frame"
        /// (the field may have dropped its focus on the same Esc before this runs).
        /// </summary>
        public EditorCancelResult HandleCancel(bool textFieldRecent)
        {
            if (textFieldRecent || UiKit.TextFieldFocused())
            {
                return EditorCancelResult.TextField;
            }

            if (CancelOwnedElsewhere != null && CancelOwnedElsewhere())
            {
                return EditorCancelResult.OwnedElsewhere;
            }

            if (IsDialogOpen)
            {
                _dialog.Escape();
                return EditorCancelResult.ClosedDialog;
            }

            if (Document.CloseTopLayer())
            {
                return EditorCancelResult.ClosedLayer;
            }

            SetStatus(L("ui.editor.leave_hint"), EditorStatusSeverity.Info);
            var es = EventSystem.current;
            if (es != null && InputMap.ActiveDevice == InputDeviceKind.Gamepad && BackButton != null)
            {
                es.SetSelectedGameObject(BackButton.gameObject); // A now leaves — a deliberate second step
            }

            return EditorCancelResult.PointedAtBack;
        }

        /// <summary>
        /// Asks before a permanent action (#3690) — delete, clear without undo, back to the default look, generate over a
        /// build: <paramref name="message"/> with a red <paramref name="dangerLabel"/> and Cancel; the pad lands on Cancel,
        /// Esc / B cancel. <paramref name="icon"/> is the danger button's icon (<c>btn_delete</c> by default).
        /// </summary>
        public UiKit.ConfirmDialog Confirm(string message, string dangerLabel, System.Action onConfirm, string icon = "btn_delete")
            => OpenDialog(new UiKit.ConfirmSpec
            {
                Message = message,
                YesLabel = dangerLabel,
                YesIcon = icon,
                OnYes = onConfirm,
                NoLabel = L("ui.action.cancel"),
            });

        /// <summary>"Delete “<paramref name="name"/>” for good?" — the standard question for deleting saved work.</summary>
        public UiKit.ConfirmDialog ConfirmDelete(string name, System.Action onDelete)
            => Confirm(L("ui.editor.confirm_delete").Replace("{name}", name ?? string.Empty), L("ui.editor.delete"), onDelete);

        /// <summary>
        /// Runs <paramref name="proceed"/> — after asking when it would throw away unsaved work (load another piece of work,
        /// start new, generate or assemble over a build). Without unsaved work it runs at once. The message / button keys
        /// default to "Replace your work?" / "Replace".
        /// </summary>
        public void ConfirmDiscard(System.Action proceed, string messageKey = "ui.editor.confirm_replace",
            string yesKey = "ui.editor.confirm_replace_yes", string icon = "btn_load")
        {
            if (!Document.IsDirty)
            {
                proceed?.Invoke();
                return;
            }

            Confirm(L(messageKey), L(yesKey), () =>
            {
                _store.CancelPending();
                proceed?.Invoke();
                Refresh();
            }, icon);
        }

        private UiKit.ConfirmDialog OpenDialog(UiKit.ConfirmSpec spec)
        {
            CloseDialog();
            _draftPromptOpen = false;
            _dialog = UiKit.Confirm(Canvas.transform, spec);
            return _dialog;
        }

        private void CloseDialog()
        {
            if (_dialog != null)
            {
                _dialog.Close();
                _dialog = null;
            }

            _draftPromptOpen = false;
        }
    }
}
