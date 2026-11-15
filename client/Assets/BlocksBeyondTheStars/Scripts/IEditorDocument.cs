// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The contract every editor implements for the <see cref="EditorShell"/> (#3688, #3689, #3690): what the shell needs
    /// to know about the open work — unsaved or not, undo / redo, its draft — and the one rule for Esc / pad B. The editor
    /// keeps its own content, tools and save logic; the shell owns the header, the status bar, the drafts and the
    /// confirmations.
    /// </summary>
    public interface IEditorDocument
    {
        /// <summary>The work's own name ("My cruiser") — shown under the title and in the draft prompt. Empty or null when
        /// the work has no name yet.</summary>
        string DocumentName { get; }

        /// <summary>True when the open work has changes that were not saved — since it was opened, loaded, started new or
        /// last saved. A restored draft is unsaved work: <see cref="LoadDraft"/> leaves the document dirty.</summary>
        bool IsDirty { get; }

        /// <summary>True when there is a step to take back (the shell greys its Undo button otherwise). An editor without
        /// undo answers false and leaves <see cref="EditorShellOptions.HasUndo"/> off.</summary>
        bool CanUndo { get; }

        /// <summary>True when there is an undone step to put back.</summary>
        bool CanRedo { get; }

        /// <summary>Takes the newest step back (only called while <see cref="CanUndo"/>).</summary>
        void Undo();

        /// <summary>Puts the newest undone step back (only called while <see cref="CanRedo"/>).</summary>
        void Redo();

        /// <summary>
        /// The open work as text — everything needed to bring it back exactly, in the editor's own format (the editor may
        /// reuse its save / export JSON). Null or empty means there is nothing worth keeping (an empty canvas); the shell
        /// then removes the draft. Called on the main thread, debounced (see <see cref="EditorDraftStore"/>), and once
        /// more when the editor is left with unsaved work.
        /// </summary>
        string SaveDraft();

        /// <summary>
        /// Brings back work from <paramref name="draft"/> (what <see cref="SaveDraft"/> wrote, possibly by an older game
        /// version). Returns false when it cannot be read — the shell then sets the draft aside and the editor opens fresh,
        /// so a broken draft never blocks the editor. Must not throw for bad input. Leaves the document dirty.
        /// </summary>
        bool LoadDraft(string draft);

        /// <summary>
        /// Esc / pad B (#3690): closes the editor's topmost open layer — a picker, a dialog, a side panel, the pad's canvas
        /// mode — and returns true; returns false when nothing of the editor's own is open. It never closes the editor
        /// itself: an editor is left only through its Back button (its draft keeps the work). The shell asks only when no
        /// text field just took the key and none of the shell's own dialogs is open.
        /// </summary>
        bool CloseTopLayer();
    }

    /// <summary>How an editor configures its <see cref="EditorShell"/>.</summary>
    public sealed class EditorShellOptions
    {
        /// <summary>The draft slot: one draft per editor (<c>ship</c>, <c>station</c>, <c>settlement</c>, <c>avatar</c>,
        /// <c>texture</c>, <c>form</c>, <c>tool_looks</c>, <c>content</c>, <c>material</c>, …).</summary>
        public string EditorKey;

        /// <summary>The title: the start card's name key (<c>ui.menu.ship_editor</c>), so card and screen say the same.</summary>
        public string TitleKey;

        /// <summary>The line under Save that says what saving does here (<c>ui.editor.save_sub.ship</c>).</summary>
        public string SaveSublineKey;

        /// <summary>Leaves the editor (the host's close). The shell has already written the draft.</summary>
        public System.Action OnBack;

        /// <summary>Saves the work for real; true when it was saved (the shell then removes the draft). The editor may set
        /// its own status meanwhile; when it does not, the shell says "Saved." or "Could not save.".</summary>
        public System.Func<bool> OnSave;

        /// <summary>Optional: opens the editor's load picker. Picking an entry over unsaved work should go through
        /// <see cref="EditorShell.ConfirmDiscard"/>.</summary>
        public System.Action OnLoad;

        /// <summary>Optional: starts new work. The shell asks first when there is unsaved work.</summary>
        public System.Action OnNew;

        /// <summary>Optional: opens the submit dialog (<c>CreationSubmitDialog</c>).</summary>
        public System.Action OnSubmit;

        /// <summary>Shows Undo / Redo; the document answers <see cref="IEditorDocument.CanUndo"/> /
        /// <see cref="IEditorDocument.CanRedo"/>.</summary>
        public bool HasUndo;

        /// <summary>Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z / Ctrl+S, never while a text field has the keyboard. An editor that
        /// adopts the shell drops its own Ctrl+Z / Ctrl+Y handling.</summary>
        public bool Shortcuts = true;

        /// <summary>The pad's own bottom hint strip (<see cref="UiNav.Enable"/>). Off by default: UiNav centres it 10 px
        /// above the bottom edge, right over the shell's status bar — an editor names its pad keys through
        /// <see cref="EditorShell.SetHint"/> instead (the ship and face editors already draw their own line).</summary>
        public bool PadHints;

        /// <summary>The drafts folder; null = <c>AppPaths.Root/drafts</c> (persistentDataPath, or the portable data
        /// folder). Tests pass a temp folder.</summary>
        public string DraftRoot;

        /// <summary>The locale lookup; null = <see cref="UiKit.L"/>.</summary>
        public System.Func<string, string> Localize;
    }
}
