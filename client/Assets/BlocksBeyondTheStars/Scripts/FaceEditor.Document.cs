// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pixel editor as an <see cref="IEditorDocument"/> of the <see cref="EditorShell"/> (#3693, #3689, #3690). ONE
    /// frame in every host: the appearance screen over the in-game console, the Avatar Designer in the main menu (its
    /// host fields below make the difference) and the block paint tool.
    /// <list type="bullet">
    /// <item><b>Save</b> commits the canvas to its host and then lets the host persist (<see cref="OnSaveAll"/>): the
    /// designer writes the scratch look into the settings; in a world a commit already reached the server; the paint tool
    /// paints the block.</item>
    /// <item><b>Unsaved</b> follows the history's saved mark (<see cref="PixelEditHistory.IsDirty"/>): in a LIVE host a
    /// commit is the save, so every commit marks the history saved; the designer (<see cref="KeepsScratch"/>) marks it on
    /// Save only, and a look put on from the outfit column is unsaved too.</item>
    /// <item><b>Drafts</b> (<see cref="CanvasDraftFormat"/>, kinds <c>appearance</c> / <c>avatar</c> / <c>paint</c>): a live
    /// host keeps only the canvas not yet committed; the designer keeps its whole scratch look (every part, base colours,
    /// its own host fields).</item>
    /// <item><b>Esc / pad B</b> (<see cref="CloseTopLayer"/>) disarms an armed fill or eyedropper; otherwise the shell
    /// points at Back. The submit dialog and the pad's canvas mode read their own cancel.</item>
    /// </list>
    /// </summary>
    public sealed partial class FaceEditor : IEditorDocument
    {
        /// <summary>The draft layout this editor writes and reads.</summary>
        internal const int DraftVersion = 1;

        // Host fields of the shell frame. Defaults make the classic hosts work unchanged: several subjects = the in-game
        // appearance screen, one subject = the block paint tool.
        public string EditorKey;           // the draft slot (null: "appearance" / "paint")
        public string ShellTitleKey;       // the header title (null: ui.appearance.title / the subject's TitleKey)
        public string SaveSublineKey;      // the line under Save (null: ui.editor.save_sub.appearance / .paint)
        public Action OnBack;              // leaving through Back (null: the editor goes away)
        public Func<bool> OnSaveAll;       // after the canvas was committed: the host persists (null: the commit is the save)
        public bool KeepsScratch;          // commits only reach a scratch look that Save persists (the menu designer)
        public string FigureTabKey;        // the "Figure" tab of the appearance screen (null: none) — FaceEditor.Figure.cs
        public Action<Transform, float> BuildFigureExtras; // the host adds to the Figure tab's left column (card, y)
        public Func<string> HostDraft;     // the host's own part of a draft (the designer's name field)
        public Action<string> RestoreHostDraft;
        public string DraftRoot;           // the drafts folder (null: the player's; tests pass a temp folder)

        private bool _hostChanged;         // the host's scratch look changed outside the history (an outfit was put on)
        private string _aboutText;

        private string DraftKind => !string.IsNullOrEmpty(EditorKey) ? EditorKey : _multi ? "appearance" : "paint";

        /// <summary>A commit reaches the player's real look at once: in a world, in the paint tool and for the HUD frame
        /// (a client setting in both hosts) — only the designer's character mode keeps a scratch look.</summary>
        private bool LiveCommit => !KeepsScratch || _hudMode;

        private EditorShellOptions ShellOptions() => new EditorShellOptions
        {
            EditorKey = DraftKind,
            TitleKey = !string.IsNullOrEmpty(ShellTitleKey) ? ShellTitleKey : _multi ? "ui.appearance.title" : TitleKey,
            SaveSublineKey = !string.IsNullOrEmpty(SaveSublineKey) ? SaveSublineKey
                : _multi ? "ui.editor.save_sub.appearance" : "ui.editor.save_sub.paint",
            OnBack = OnBack ?? Close,
            OnSave = SaveWork,
            OnSubmit = HasSubmit ? SubmitPressed : null,
            HasUndo = true,
            DraftRoot = !string.IsNullOrEmpty(DraftRoot) ? DraftRoot : CanvasEditorDrafts.DraftRoot,
            Localize = Localizer != null ? L : null,
        };

        // ---- IEditorDocument ---------------------------------------------------------------------------------------

        /// <summary>The look's name (the designer's name field), the paint tool's design name, nothing in a world.</summary>
        public string DocumentName => LookName != null ? LookName() : HasLibrary ? _name : null;

        public bool IsDirty => _stroking || _hostChanged || _history.IsDirty || (_parked?.History != null && _parked.History.IsDirty);

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        void IEditorDocument.Undo() => Undo();

        void IEditorDocument.Redo() => Redo();

        /// <summary>Esc / pad B: an armed fill or eyedropper goes back to the pen — the one layer of this editor's own.</summary>
        public bool CloseTopLayer()
        {
            if (_tool == DrawTool.Fill || _tool == DrawTool.Pick)
            {
                SelectTool(DrawTool.Pen);
                return true;
            }

            return false;
        }

        /// <summary>Save: the canvas to its host, then the host persists. True when it was saved.</summary>
        private bool SaveWork()
        {
            EndStroke();
            CommitActive(force: true);
            if (OnSaveAll != null && !OnSaveAll())
            {
                return false;
            }

            _history.MarkSaved();
            _parked?.History?.MarkSaved();
            _hostChanged = false;
            return true;
        }

        // ---- status, hint, change notes ------------------------------------------------------------------------------

        /// <summary>A message in the shell's status bar (hosts use it: the paint tool's share code, the designer's export).</summary>
        internal void ShowStatus(string text, EditorStatusSeverity severity) => _shell?.SetStatus(text ?? string.Empty, severity);

        /// <summary>The active tab's note (the helmet's frame, the HUD frame's tiles, "mining loses the paint") in the status
        /// bar; a note of the tab before goes when the new tab has none.</summary>
        private void ShowAbout()
        {
            var subject = Current;
            string key = _figureShown || subject == null ? null : subject.AboutKey;
            if (!string.IsNullOrEmpty(key))
            {
                _aboutText = L(key);
                ShowStatus(_aboutText, EditorStatusSeverity.Info);
            }
            else if (_shell != null && _aboutText != null && _shell.StatusText.text == _aboutText)
            {
                _shell.ClearStatus();
                _aboutText = null;
            }
        }

        /// <summary>The work changed: the shell's draft becomes due.</summary>
        private void Changed()
        {
            if (_shell != null)
            {
                _shell.NotifyChanged();
            }
        }

        /// <summary>Something just reached the host: in a live host that is the saved state, unless the canvas still holds
        /// paint that was not committed.</summary>
        private void AfterHostApplied()
        {
            if (LiveCommit && !_dirty)
            {
                _history.MarkSaved();
            }
        }

        /// <summary>A part's base colour changed outside the paint card (the Figure tab's ← / →): one
        /// undoable colour step on that part.</summary>
        internal void SetBaseColorOf(int subject, Color color)
        {
            if (_hudMode || subject < 0 || subject >= _subjects.Count || _subjects[subject].SetBaseColor == null)
            {
                return;
            }

            var s = _subjects[subject];
            Color before = s.GetBaseColor?.Invoke() ?? color;
            s.SetBaseColor(color);
            _history.EndColorRun();
            _history.PushColor(subject, before, color, fromDrag: false);
            AfterHostApplied();
            Changed();
            UpdateBaseSwatch();
            RenderAll();
            RefreshPreview();
            RefreshFigureSwatches();
            OnChanged?.Invoke();
        }

        // ---- drafts ------------------------------------------------------------------------------------------------

        /// <summary>A draft's content (JSON after the <see cref="CanvasDraftFormat"/> header).</summary>
        [Serializable]
        private sealed class DraftData
        {
            public string name = string.Empty;
            public string host = string.Empty;
            public int tab;
            public bool hud;
            public string hudPixels = string.Empty;
            public List<DraftSubject> subjects = new List<DraftSubject>();
        }

        [Serializable]
        private sealed class DraftSubject
        {
            public int index;
            public string pixels = string.Empty;
            public bool hasColor;
            public Color color;
        }

        public string SaveDraft()
        {
            var data = new DraftData { name = _name ?? string.Empty, host = HostDraft?.Invoke() ?? string.Empty, hud = _hudMode };
            var chars = _hudMode && _parked != null ? _parked.Subjects : _subjects.ToArray();
            data.tab = _hudMode && _parked != null ? _parked.Active : _active;
            for (int i = 0; i < chars.Length; i++)
            {
                bool onCanvas = !_hudMode && i == _active;
                if (!KeepsScratch && !(onCanvas && _dirty))
                {
                    continue; // a live host already wears every committed part
                }

                var s = chars[i];
                var entry = new DraftSubject { index = i, pixels = onCanvas ? Encode() : s.Pixels ?? string.Empty };
                if (KeepsScratch && s.SetBaseColor != null && s.GetBaseColor != null)
                {
                    entry.hasColor = true;
                    entry.color = s.GetBaseColor();
                }

                data.subjects.Add(entry);
            }

            if (_hudMode && _dirty)
            {
                data.hudPixels = Encode();
            }

            bool any = data.subjects.Count > 0 || data.hudPixels.Length > 0 || (KeepsScratch && data.host.Length > 0);
            return any ? CanvasDraftFormat.Wrap(DraftKind, DraftVersion, JsonUtility.ToJson(data)) : null;
        }

        public bool LoadDraft(string draft)
        {
            if (!CanvasDraftFormat.TryUnwrap(draft, DraftKind, DraftVersion, out _, out string payload))
            {
                return false;
            }

            try
            {
                var data = JsonUtility.FromJson<DraftData>(payload);
                if (data == null)
                {
                    return false;
                }

                RestoreDraft(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FaceEditor] the draft could not be restored: {e.Message}");
                return false;
            }
        }

        private void RestoreDraft(DraftData data)
        {
            HideFigure();
            if (_hudMode)
            {
                EnterCharacterMode();
            }

            RestoreDraftName(data);
            DraftSubject onCanvas = null;
            foreach (var entry in data.subjects ?? new List<DraftSubject>())
            {
                if (entry == null || entry.index < 0 || entry.index >= _subjects.Count)
                {
                    continue;
                }

                var s = _subjects[entry.index];
                if (KeepsScratch && entry.hasColor)
                {
                    s.SetBaseColor?.Invoke(entry.color);
                }

                if (entry.index == data.tab)
                {
                    onCanvas = entry;
                }
                else if (KeepsScratch)
                {
                    s.Pixels = entry.pixels ?? string.Empty; // the designer's scratch: not the player's look until Save
                    s.OnApply?.Invoke(s.Pixels);
                }
            }

            SwitchSubject(data.tab);
            if (onCanvas != null)
            {
                PutOnCanvas(onCanvas.pixels);
            }

            if (data.hud && !string.IsNullOrEmpty(data.hudPixels) && _hudLayer != null)
            {
                EnterHudMode();
                PutOnCanvas(data.hudPixels);
            }

            _hostChanged |= KeepsScratch;
            UpdateBaseSwatch();
            RefreshFigureSwatches();
            RenderAll();
            RefreshPreview();
            HighlightTabs();
            OnChanged?.Invoke();
            Changed();
        }

        private void RestoreDraftName(DraftData data)
        {
            if (!string.IsNullOrEmpty(data.name))
            {
                _name = data.name;
                if (_nameField != null)
                {
                    _nameField.text = _name;
                }
            }

            if (!string.IsNullOrEmpty(data.host))
            {
                RestoreHostDraft?.Invoke(data.host);
            }
        }
    }
}
