// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shell's drafts (#3689) and its three document actions — Save, Back, New. The maintainer's decision
    /// (2026-11-15): unsaved work is kept as an automatic draft instead of a "Save changes?" question.
    /// <list type="bullet">
    /// <item>While the work is unsaved, a draft is written after it rested <see cref="EditorDraftStore.QuietSeconds"/> (or at
    /// the latest <see cref="EditorDraftStore.MaxWaitSeconds"/> into a long session) — the editor calls
    /// <see cref="NotifyChanged"/> after each change. An editor that never does is re-checked every
    /// <see cref="EditorDraftStore.MaxWaitSeconds"/> while dirty (that serializes the work, so announcing is cheaper).</item>
    /// <item>Back writes the draft once more and leaves; only when the draft cannot be written does it ask.</item>
    /// <item>Save removes the draft; work that went back to its saved state (undone) removes it too.</item>
    /// <item><see cref="OfferDraft"/> asks "Continue your draft?" when the editor opens: Continue restores it (Esc / B
    /// choose Continue — nothing is lost by a stray press); Start fresh leaves the file until new work replaces it, so one
    /// mis-click does not destroy an afternoon of building either. A draft the editor cannot read is set aside.</item>
    /// </list>
    /// </summary>
    public sealed partial class EditorShell
    {
        private EditorDraftStore _store;
        private bool _wroteThisSession;   // a draft of THIS session's work is on disk (cleared again when the work is clean)
        private bool _leaving;
        private double _lastSafetyCheck = double.NegativeInfinity;
        private bool _announced;          // the editor calls NotifyChanged itself: no periodic re-check needed

        /// <summary>The draft slot of this editor — for tests and diagnostics.</summary>
        public EditorDraftStore DraftStore => _store;

        private void InitDrafts()
        {
            string root = !string.IsNullOrEmpty(Options.DraftRoot)
                ? Options.DraftRoot
                : Path.Combine(AppPaths.Root, "drafts"); // persistentDataPath, or the portable data folder (#1285)
            _store = new EditorDraftStore(root, string.IsNullOrEmpty(Options.EditorKey) ? "editor" : Options.EditorKey);
        }

        /// <summary>The editor changed its work: a draft becomes due after the next rest. Cheap — call it after every
        /// edit (a stroke, a placed block, a changed field).</summary>
        public void NotifyChanged() => NotifyChanged(Time.unscaledTimeAsDouble);

        /// <summary>The editor changed its work at <paramref name="now"/> (tests drive their own clock).</summary>
        public void NotifyChanged(double now)
        {
            _announced = true;
            _store.MarkDirty(now);
        }

        /// <summary>A change the shell made itself (its Undo / Redo buttons).</summary>
        private void MarkChanged() => _store.MarkDirty(Time.unscaledTimeAsDouble);

        private void TickDrafts(double now)
        {
            if (IsDialogOpen && _draftPromptOpen)
            {
                return; // the old draft is still on offer — writing now would replace it
            }

            if (!Document.IsDirty)
            {
                _store.CancelPending();
                if (_wroteThisSession)
                {
                    _store.Clear(); // undone back to the saved state: the draft would only bring stale changes back
                    _wroteThisSession = false;
                }

                return;
            }

            if (!_announced && !_store.Pending && now - _lastSafetyCheck >= _store.MaxWaitSeconds)
            {
                _lastSafetyCheck = now;
                _store.MarkDirty(now); // a change the editor did not announce still gets its draft
            }

            if (_store.Due(now))
            {
                WriteDraft();
            }
        }

        /// <summary>Writes the draft now (also used by Back). Empty work removes the draft instead.</summary>
        private DraftWriteResult WriteDraft()
        {
            string content;
            try
            {
                content = Document.SaveDraft();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EditorShell] '{_store.EditorKey}': the draft could not be made: {e.Message}");
                _store.CancelPending();
                return DraftWriteResult.Failed;
            }

            if (string.IsNullOrEmpty(content))
            {
                _store.Clear();
                _wroteThisSession = false;
                return DraftWriteResult.Unchanged;
            }

            var result = _store.Write(content, Document.DocumentName, DateTime.UtcNow);
            if (result == DraftWriteResult.Written || result == DraftWriteResult.Unchanged)
            {
                _wroteThisSession = true;
            }
            else
            {
                Debug.LogWarning($"[EditorShell] '{_store.EditorKey}': draft not written ({result}).");
            }

            WebGlStorage.Sync(); // IDBFS writes are in-memory until synced — a no-op off WebGL
            return result;
        }

        /// <summary>
        /// Offers the draft left from last time, if there is one: "Continue your draft?" with the work's name and when it
        /// was written. Call once, after the editor has built its content and can take <see cref="IEditorDocument.LoadDraft"/>.
        /// Returns true when the question is on screen. A draft that cannot be read is set aside silently (logged).
        /// </summary>
        public bool OfferDraft()
        {
            if (!_store.HasDraft)
            {
                return false;
            }

            if (!_store.TryLoad(out var draft) || draft == null)
            {
                Debug.LogWarning($"[EditorShell] '{_store.EditorKey}': an unreadable draft was set aside.");
                _store.SetAside();
                return false;
            }

            string ago = UiKit.TimeAgo(L, draft.SavedUtc);
            string message = string.IsNullOrEmpty(draft.Title)
                ? L("ui.editor.draft.text_unnamed").Replace("{ago}", ago)
                : L("ui.editor.draft.text").Replace("{name}", draft.Title).Replace("{ago}", ago);
            OpenDialog(new UiKit.ConfirmSpec
            {
                Title = L("ui.editor.draft.title"),
                Message = message,
                YesLabel = L("ui.editor.draft.continue"),
                YesTier = ButtonTier.Primary,
                YesIcon = "btn_continue",
                NoLabel = L("ui.editor.draft.fresh"),
                NoTier = ButtonTier.Secondary,
                NoIcon = "btn_new",
                EscapeMeansYes = true,
                OnYes = () => RestoreDraft(draft.Content),
            });
            _draftPromptOpen = true;
            return true;
        }

        private void RestoreDraft(string content)
        {
            bool restored;
            try
            {
                restored = Document.LoadDraft(content);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EditorShell] '{_store.EditorKey}': the draft could not be restored: {e.Message}");
                restored = false;
            }

            if (restored)
            {
                _wroteThisSession = true; // it is this session's work now
                SetStatus(L("ui.editor.draft.restored"), EditorStatusSeverity.Info);
            }
            else
            {
                _store.SetAside();
                SetStatus(L("ui.editor.draft.broken"), EditorStatusSeverity.Warning);
            }

            Refresh();
        }

        /// <summary>Save (the primary button, Ctrl+S): the editor's own save; on success the draft is removed. When the
        /// editor said nothing itself, the shell reports "Saved." or "Could not save.".</summary>
        public void Save()
        {
            if (Options.OnSave == null || IsDialogOpen)
            {
                return;
            }

            int before = _statusVersion;
            bool saved;
            try
            {
                saved = Options.OnSave();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                saved = false;
            }

            if (saved)
            {
                _store.Clear();
                _wroteThisSession = false;
            }

            if (_statusVersion == before)
            {
                SetStatus(L(saved ? "ui.editor.saved" : "ui.editor.save_failed"),
                    saved ? EditorStatusSeverity.Success : EditorStatusSeverity.Error);
            }

            Refresh();
        }

        /// <summary>Back (top left): keeps unsaved work as a draft and leaves. Asks only when the draft could not be
        /// written (disk full, work too big) — then leaving would lose the work.</summary>
        public void Leave()
        {
            if (_leaving)
            {
                return;
            }

            if (Document.IsDirty)
            {
                var result = WriteDraft();
                if (result == DraftWriteResult.Failed || result == DraftWriteResult.TooLarge)
                {
                    OpenDialog(new UiKit.ConfirmSpec
                    {
                        Message = L("ui.editor.draft.keep_failed"),
                        YesLabel = L("ui.editor.leave_anyway"),
                        YesIcon = "btn_back",
                        NoLabel = L("ui.editor.stay"),
                        OnYes = LeaveNow,
                    });
                    return;
                }
            }

            LeaveNow();
        }

        private void LeaveNow()
        {
            _leaving = true;
            CloseDialog();
            Options.OnBack?.Invoke();
        }

        /// <summary>New (header): over unsaved work it asks first (the work is not kept as a draft then).</summary>
        private void StartNew()
            => ConfirmDiscard(() => Options.OnNew?.Invoke(), "ui.editor.confirm_new", "ui.editor.confirm_new_yes", "btn_new");

        /// <summary>The editor goes away without Back (the host switched phase, the world closed): unsaved work still gets
        /// its draft.</summary>
        private void FlushDraftOnDestroy()
        {
            if (!_built || _leaving || _store == null)
            {
                return;
            }

            try
            {
                if (Document.IsDirty)
                {
                    WriteDraft();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EditorShell] '{_store.EditorKey}': no draft on close: {e.Message}");
            }
        }
    }
}
