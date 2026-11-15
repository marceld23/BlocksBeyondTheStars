// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Undo / Redo for the Station/Village editor's block and marker edits (#3692, V17) — the build editors had none — and its
    /// "unsaved" answer for the shell.
    /// <list type="bullet">
    /// <item>A click is one step: every cell it touches (a bed's two halves, a removed block with its partner, a painted port, a
    /// kept cell, a device's or a marker's settings) is collected in one <see cref="CellDelta"/> — the cells as they were. A town
    /// can hold a quarter of a million cells, so the history keeps these deltas, never whole builds (<see cref="UndoHistory{T}"/>
    /// with the delta as its snapshot: undoing hands the history the inverse delta, filled with the cells as they are now, before
    /// the old ones are put back).</item>
    /// <item>Loading, New, Generate and Assemble start a new piece of work (<see cref="NewDocument"/>): the history is emptied
    /// and nothing is unsaved — they ask first over unsaved work (<see cref="EditorShell.ConfirmDiscard"/>).</item>
    /// <item>The form's fields (name, size, use as, the developer rows) are not undo steps; whether they changed since the work
    /// was opened or saved is told by a signature of them (<see cref="FormSignature"/>), checked once a frame.</item>
    /// <item>While a preview shows, nothing can be undone: the design is set aside.</item>
    /// </list>
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The cells of one edit as they were (null: the cell was empty). Filled once per cell — the first state wins.</summary>
        private sealed class CellDelta
        {
            public readonly List<KeyValuePair<Vector3i, CellData?>> Cells = new List<KeyValuePair<Vector3i, CellData?>>();
            private readonly HashSet<Vector3i> _seen = new HashSet<Vector3i>();

            public void Track(Vector3i cell, CellData? before)
            {
                if (_seen.Add(cell))
                {
                    Cells.Add(new KeyValuePair<Vector3i, CellData?>(cell, before));
                }
            }
        }

        private readonly UndoHistory<CellDelta> _history = new UndoHistory<CellDelta>();
        private CellDelta _edit;
        private string _cleanSignature = string.Empty, _lastSignature = string.Empty;
        private bool _cleanPending, _draftDirty, _savedNow;
        private readonly StringBuilder _signature = new StringBuilder(256);

        /// <inheritdoc/>
        public bool IsDirty => !ScreenshotDirector.CapturingEditors && (_history.IsDirty || _draftDirty || FormChanged());

        /// <inheritdoc/>
        public bool CanUndo => !_previewOn && _history.CanUndo;

        /// <inheritdoc/>
        public bool CanRedo => !_previewOn && _history.CanRedo;

        /// <summary>Steps that can be taken back — for tests.</summary>
        internal int UndoSteps => _history.UndoCount;

        /// <inheritdoc/>
        public void Undo()
        {
            var now = new CellDelta();
            if (CanUndo && _history.TryUndo(now, out var restore))
            {
                ApplyDelta(restore, now);
            }
        }

        /// <inheritdoc/>
        public void Redo()
        {
            var now = new CellDelta();
            if (CanRedo && _history.TryRedo(now, out var restore))
            {
                ApplyDelta(restore, now);
            }
        }

        /// <summary>Runs one edit as one undo step: every cell <paramref name="body"/> places or removes is recorded as it was.</summary>
        private void Edit(Action body)
        {
            if (_edit != null)
            {
                body();
                return;
            }

            _edit = new CellDelta();
            try
            {
                body();
            }
            finally
            {
                var delta = _edit;
                _edit = null;
                CommitEdit(delta);
            }
        }

        /// <summary>Records a cell's state before an edit changes it (only inside <see cref="Edit"/>).</summary>
        private void TrackCell(Vector3i cell)
        {
            if (_edit != null)
            {
                _edit.Track(cell, _design.TryGetValue(cell, out var d) ? d : (CellData?)null);
            }
        }

        /// <summary>Keeps the cells the edit really changed; one of them is enough for a step.</summary>
        private void CommitEdit(CellDelta delta)
        {
            var changed = new CellDelta();
            foreach (var kv in delta.Cells)
            {
                CellData? now = _design.TryGetValue(kv.Key, out var d) ? d : (CellData?)null;
                if (!Same(kv.Value, now))
                {
                    changed.Track(kv.Key, kv.Value);
                }
            }

            if (changed.Cells.Count == 0)
            {
                return;
            }

            _history.Record(changed);
            _shell?.NotifyChanged();
        }

        /// <summary>Puts <paramref name="restore"/>'s cells back, first noting in <paramref name="inverse"/> how they are now (the
        /// step the other direction takes).</summary>
        private void ApplyDelta(CellDelta restore, CellDelta inverse)
        {
            ClearLeaks();
            foreach (var kv in restore.Cells)
            {
                inverse.Track(kv.Key, _design.TryGetValue(kv.Key, out var d) ? d : (CellData?)null);
            }

            foreach (var kv in restore.Cells)
            {
                if (kv.Value is { } data)
                {
                    PlaceCellData(kv.Key, FindPalette(data.Id, data.Kind), data);
                }
                else if (_design.ContainsKey(kv.Key))
                {
                    RemoveCell(kv.Key);
                }
            }

            _view?.Flush();
        }

        private static bool Same(CellData? a, CellData? b)
        {
            if (a.HasValue != b.HasValue)
            {
                return false;
            }

            if (!a.HasValue)
            {
                return true;
            }

            var x = a.Value;
            var y = b.Value;
            return x.Id == y.Id && x.Kind == y.Kind && x.Tint == y.Tint && x.Glow == y.Glow && x.Shape == y.Shape && x.Mode == y.Mode
                   && x.Keep == y.Keep && (x.Port ?? string.Empty) == (y.Port ?? string.Empty) && (x.Config ?? string.Empty) == (y.Config ?? string.Empty)
                   && (x.Label ?? string.Empty) == (y.Label ?? string.Empty);
        }

        // ---------------------------------------------------------------- unsaved

        /// <summary>A new piece of work was opened (a load, New, Generate, Assemble): no steps, nothing unsaved. The form's
        /// signature is taken on the next frame, when the load has set every field.</summary>
        private void NewDocument()
        {
            _edit = null;
            _history.Reset();
            _draftDirty = false;
            _cleanPending = true;
        }

        /// <summary>The work was saved: the current build and form are the saved state.</summary>
        private void MarkSaved()
        {
            _history.MarkSaved();
            _draftDirty = false;
            _cleanPending = false;
            _cleanSignature = FormSignature();
        }

        /// <summary>Once a frame (Update): takes the signature a load asked for, and tells the shell when a field changed, so a
        /// draft follows a changed name too.</summary>
        private void TrackFormChanges()
        {
            string now = FormSignature();
            if (_cleanPending)
            {
                _cleanPending = false;
                _cleanSignature = now;
            }

            if (now != _lastSignature)
            {
                bool first = _lastSignature.Length == 0;
                _lastSignature = now;
                if (!first && now != _cleanSignature)
                {
                    _shell?.NotifyChanged();
                }
            }
        }

        private bool FormChanged() => !_cleanPending && _lastSignature.Length > 0 && _lastSignature != _cleanSignature;

        /// <summary>Every form field a save writes, in one string — compared with the one taken when the work was opened or saved.</summary>
        private string FormSignature()
        {
            var s = _signature.Clear();
            s.Append(_key).Append('|').Append(_name).Append('|').Append(_tier).Append('|').Append(_pack).Append('|').Append(_weight)
                .Append('|').Append(_planetTypes).Append('|').Append(_moduleMode).Append(_landmarkMode).Append(_giantShipMode).Append(_entryMode)
                .Append(_underground).Append('|').Append(_kit).Append('|').Append(_function).Append('|').Append(_style).Append('|').Append(_protection)
                .Append('|').Append(_lmRuin).Append(_lmAtmosphere).Append(_lmInhabited).Append('|').Append(_lmProfile).Append('|')
                .Append(_lmStrength.ToString(CultureInfo.InvariantCulture)).Append('|').Append(_groundRow).Append('|').Append(_lmNameKey).Append('|')
                .Append(_lmSeat).Append('|').Append(_gsStyle).Append('|').Append(_gsStrength.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(_gsNameKey).Append('|').Append(_entryPool).Append('|').Append(_sourceKey);
            foreach (var z in _zones)
            {
                s.Append('|').Append(z.X0).Append(',').Append(z.Y0).Append(',').Append(z.Z0).Append(',').Append(z.X1).Append(',').Append(z.Y1)
                    .Append(',').Append(z.Z1).Append(',').Append(z.Profile).Append(',').Append(z.Strength.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var kv in _sourceValues)
            {
                s.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
            }

            return s.ToString();
        }

        /// <summary>For tests: the frame's bookkeeping without a frame (edit mode runs no Update).</summary>
        internal void TickForTest() => TrackFormChanges();

        /// <summary>For tests: places <paramref name="id"/> at a cell as one edit, the way a click does.</summary>
        internal bool PlaceForTest(Vector3i cell, string id, string kind = "block")
        {
            var pal = FindPalette(id, kind);
            if (pal.Id == null || !InBounds(cell) || _design.ContainsKey(cell))
            {
                return false;
            }

            Edit(() => PlaceCell(cell, pal));
            return true;
        }

        /// <summary>For tests: whether a cell is part of the design.</summary>
        internal bool HasCellForTest(Vector3i cell) => _design.ContainsKey(cell);

        /// <summary>For tests: the number of cells in the design.</summary>
        internal int CellCountForTest => _design.Count;

        /// <summary>For tests: the name field.</summary>
        internal string NameForTest
        {
            get => _name;
            set => _name = value;
        }
    }
}
