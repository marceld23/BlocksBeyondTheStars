// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship editor as an <see cref="IEditorDocument"/> (#3689, #3690, #3695): undo / redo, "unsaved", the draft and the
    /// Esc / pad B rule.
    /// <list type="bullet">
    /// <item><b>Undo</b>: every placed or removed block (a bed's two halves together) is one step of an
    /// <see cref="UndoHistory{T}"/> whose snapshots are copies of the cell map; taking a step back applies only the cells
    /// that differ, so a big ship does not rebuild every chunk. The card's fields (name, values, cost) are not undo steps —
    /// an undo never changes a name typed after the block went in — but they count as unsaved work.</item>
    /// <item><b>Draft</b>: <see cref="DraftPrefix"/> + the export bundle as JSON (<see cref="ShipDraftJson"/>: the ship, its
    /// layout, the raw key, the shipped ship it started from), written by the shell while the work is unsaved and offered
    /// on the next opening. Never written during the <c>-captureEditors</c> run.</item>
    /// <item><b>Esc / pad B</b> (<see cref="CloseTopLayer"/>): the form picker, the load picker, the pad's build view — one
    /// per press; never the editor.</item>
    /// </list>
    /// </summary>
    public sealed partial class ShipEditor
    {
        /// <summary>The draft's first line: format and version. A later format bumps the number and reads the old one too.</summary>
        internal const string DraftPrefix = "bbs-ship-draft/1\n";

        private readonly UndoHistory<Dictionary<Vector3i, CellData>> _history = new();

        /// <summary>The card's fields (name, values, interior, cost) changed since the last save / load / new.</summary>
        private bool _metaDirty;

        /// <summary>The draft: the export bundle plus what the bundle does not carry.</summary>
        [Serializable]
        private sealed class ShipDraftJson
        {
            public string key, basedOn;
            public bool keyEdited;
            public ExportShipJson ship;
            public ExportLayoutJson layout;
        }

        public string DocumentName => _shipName;

        /// <summary>Unsaved cells (across undo: back at the saved state is clean again) or unsaved card fields. Always false
        /// during the <c>-captureEditors</c> run, so the shell never writes a draft of a screenshot sample.</summary>
        public bool IsDirty => !ScreenshotDirector.CapturingEditors && (_history.IsDirty || _metaDirty);

        public bool CanUndo => _history.CanUndo;

        public bool CanRedo => _history.CanRedo;

        public void Undo()
        {
            if (_history.TryUndo(CellSnapshot(), out var restore))
            {
                RestoreCells(restore);
            }
        }

        public void Redo()
        {
            if (_history.TryRedo(CellSnapshot(), out var restore))
            {
                RestoreCells(restore);
            }
        }

        /// <summary>A copy of the cell map — one undo snapshot (never changed after it was taken).</summary>
        private Dictionary<Vector3i, CellData> CellSnapshot() => new(_design);

        /// <summary>A change of the cells is done: one undo step from <paramref name="before"/>, a draft becomes due.</summary>
        private void Edited(Dictionary<Vector3i, CellData> before)
        {
            _history.Record(before);
            _shell?.NotifyChanged();
        }

        /// <summary>A card field changed (name, values, interior, cost): unsaved, a draft becomes due — no undo step.</summary>
        private void MetaChanged()
        {
            _metaDirty = true;
            _shell?.NotifyChanged();
        }

        /// <summary>The work was saved: the current cells are the saved state (undoing past it is unsaved again).</summary>
        private void MarkSaved()
        {
            _history.MarkSaved();
            _metaDirty = false;
        }

        /// <summary>Another design was opened or a new one started: no undo steps, nothing unsaved.</summary>
        private void MarkFresh()
        {
            _history.Reset();
            _metaDirty = false;
        }

        /// <summary>Brings the cells to <paramref name="target"/> by changing only the cells that differ — an undo of one
        /// block touches one chunk, not the whole ship.</summary>
        private void RestoreCells(Dictionary<Vector3i, CellData> target)
        {
            var gone = new List<Vector3i>();
            foreach (var cell in _design.Keys)
            {
                if (!target.ContainsKey(cell))
                {
                    gone.Add(cell);
                }
            }

            foreach (var cell in gone)
            {
                RemoveCell(cell);
            }

            foreach (var kv in target)
            {
                if (_design.TryGetValue(kv.Key, out var now) && now.SameAs(kv.Value))
                {
                    continue;
                }

                var pal = FindPalette(kv.Value.Id, kv.Value.Kind);
                if (pal.Id != null)
                {
                    PlaceCellData(kv.Key, pal, kv.Value);
                }
            }

            _view.Flush();
        }

        /// <summary>New (header): an empty room and the defaults. The shell asks first over unsaved work.</summary>
        private void NewDesign()
        {
            _view.Clear();
            _overlay?.Clear();
            _design.Clear();
            ResetMeta();
            RebuildFrame();
            FrameCamera();
            MarkFresh();
            RebuildForm();
            _shell?.ClearStatus();
        }

        /// <summary>The open work as a draft: <see cref="DraftPrefix"/> + JSON; null for an empty room (nothing to keep).</summary>
        public string SaveDraft()
        {
            if (_design.Count == 0)
            {
                return null;
            }

            var (ship, layout) = BuildExport(Slug(_key));
            var draft = new ShipDraftJson { key = _key, basedOn = _basedOn, keyEdited = _keyEdited, ship = ship, layout = layout };
            return DraftPrefix + JsonUtility.ToJson(draft);
        }

        /// <summary>Brings a draft back (see <see cref="SaveDraft"/>); false for anything else — never throws. The restored
        /// work is unsaved.</summary>
        public bool LoadDraft(string draft)
        {
            if (string.IsNullOrEmpty(draft) || !draft.StartsWith(DraftPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            ShipDraftJson d;
            try
            {
                d = JsonUtility.FromJson<ShipDraftJson>(draft.Substring(DraftPrefix.Length));
            }
            catch (Exception)
            {
                return false;
            }

            if (d?.layout?.cells == null || d.ship == null)
            {
                return false;
            }

            try
            {
                ApplyDraft(d);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ShipEditor] The draft could not be restored: {e}");
                return false;
            }
        }

        private void ApplyDraft(ShipDraftJson d)
        {
            var skippedIds = new List<string>();
            int skipped = ApplyCells(d.layout.cells, skippedIds);
            ApplyInterior(d.layout);
            ApplyShipMeta(d.ship, d.key ?? string.Empty, keepCostWhenEmpty: false);
            _key = d.key ?? string.Empty;
            _keyEdited = d.keyEdited;
            _basedOn = d.basedOn ?? string.Empty;
            FinishLoad(_shipName, skipped, skippedIds);
            _metaDirty = true; // a draft is unsaved work
        }

        /// <summary>
        /// Esc / pad B (#3690), one layer per press: the form picker, the load picker, then the pad's build view (back to the
        /// cards — B in the build view used to close the whole editor, because AppShell read the same press). False when
        /// nothing of the editor's own is open: the editor stays, it is left only through Back.
        /// </summary>
        public bool CloseTopLayer()
        {
            if (_formPicker != null)
            {
                _formPicker.Close();
                _formPicker = null;
                return true;
            }

            if (_loadPicker != null)
            {
                _loadPicker.Close();
                _loadPicker = null;
                return true;
            }

            return LeavePadViewport();
        }

        /// <summary>A status-bar message (the shell colours it; no file paths). Ignored before the UI exists.</summary>
        private void Status(string text, EditorStatusSeverity severity = EditorStatusSeverity.Info) => _shell?.SetStatus(text, severity);

        /// <summary>How the status bar shows <paramref name="text"/> (paths cut, one line) — to recognise our own message.</summary>
        private static string ShownAs(string text)
            => EditorStatusText.WithoutPaths(text).Replace("\r", string.Empty).Replace('\n', ' ');

        /// <summary>The severity of a submit dialog's outcome, read from the colour it reports (<see cref="CreationSubmitDialog.OnOutcome"/>).</summary>
        private static EditorStatusSeverity SeverityOf(Color color)
            => color == UiKit.Ok ? EditorStatusSeverity.Success
                : color == UiKit.DangerCol ? EditorStatusSeverity.Error
                : color == UiKit.Warn ? EditorStatusSeverity.Warning
                : EditorStatusSeverity.Info;
    }
}
