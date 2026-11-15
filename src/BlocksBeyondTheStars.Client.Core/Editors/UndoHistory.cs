// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One snapshot-based undo / redo history for every editor (#3695): the shared core of what the texture, form and
    /// tool-look models each wrote for themselves (<c>PixelCanvasModel</c>, <c>FormCanvasModel</c>,
    /// <c>ToolLookCanvasModel</c>: a capped list of "before" snapshots, a stroke as one step, a change that changed
    /// nothing is no step, a new step forgets the redo tail) plus the colour-drag merging of <c>PixelEditHistory</c> — and
    /// the ship and structure editors, which had no undo at all, can snapshot their build (for example the same text they
    /// write as a draft) with it.
    /// <para>
    /// The history holds states, not commands: <typeparamref name="T"/> is whatever brings the editor back — a voxel
    /// array, a serialized design, a record that also names the tab the change happened on so the editor can switch to it
    /// when the step is taken back. Snapshots must be immutable or copies: the history keeps the instance it is given.
    /// </para>
    /// <para>
    /// It also answers "unsaved?" correctly across undo: every state gets an id, <see cref="MarkSaved"/> remembers the
    /// current one, and undoing back to the saved state makes <see cref="IsDirty"/> false again.
    /// </para>
    /// </summary>
    /// <typeparam name="T">One snapshot of the edited work.</typeparam>
    public sealed class UndoHistory<T>
    {
        /// <summary>Steps kept by default — what the three existing models chose (48).</summary>
        public const int DefaultCapacity = 48;

        private readonly List<Entry> _undo = new List<Entry>();
        private readonly List<Entry> _redo = new List<Entry>();
        private readonly IEqualityComparer<T> _same;
        private long _nextId;
        private long _currentId;
        private long _savedId;
        private bool _inGesture;
        private T _gestureBefore = default!;
        private string? _mergeKey; // the merge run the newest undo step belongs to, null = none

        /// <param name="capacity">Undo steps kept; the oldest falls off a full history (at least 1).</param>
        /// <param name="sameState">Says when two snapshots are the same state — a change between equal states is no step.
        /// Defaults to <see cref="EqualityComparer{T}.Default"/>, which compares arrays by reference: pass
        /// <see cref="UndoSnapshots.Bytes"/> for byte-array snapshots.</param>
        public UndoHistory(int capacity = DefaultCapacity, IEqualityComparer<T>? sameState = null)
        {
            Capacity = Math.Max(1, capacity);
            _same = sameState ?? EqualityComparer<T>.Default;
        }

        /// <summary>Raised after every change of the history (a step, an undo, a redo, a save mark, a reset) — the editor
        /// shell refreshes its undo / redo buttons from it, or polls <see cref="CanUndo"/>.</summary>
        public event Action? Changed;

        /// <summary>Undo steps kept at most.</summary>
        public int Capacity { get; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>Steps that can be taken back.</summary>
        public int UndoCount => _undo.Count;

        /// <summary>Steps that can be put back.</summary>
        public int RedoCount => _redo.Count;

        /// <summary>True between <see cref="Begin"/> and <see cref="Commit"/> (a stroke or a drag in progress).</summary>
        public bool InGesture => _inGesture;

        /// <summary>True when the current state is not the one last marked with <see cref="MarkSaved"/> (or the one
        /// <see cref="Reset"/> started from).</summary>
        public bool IsDirty => _currentId != _savedId;

        /// <summary>The current state is the saved one now (the editor just saved it).</summary>
        public void MarkSaved()
        {
            _savedId = _currentId;
            Changed?.Invoke();
        }

        /// <summary>Another piece of work was opened (or a new one started): no history, nothing unsaved.</summary>
        public void Reset()
        {
            DropSteps();
            _currentId = ++_nextId;
            _savedId = _currentId;
            Changed?.Invoke();
        }

        /// <summary>Forgets every step but keeps the unsaved state — for a change under the editor that must not be
        /// undone into (the face editor's outfit swap mixed two outfits when undone).</summary>
        public void Clear()
        {
            DropSteps();
            Changed?.Invoke();
        }

        /// <summary>
        /// Records a finished change: <paramref name="before"/> is the state before it, the editor already shows the state
        /// after it. A change recorded during a gesture (<see cref="Begin"/>) belongs to the gesture and is not recorded on
        /// its own.
        /// </summary>
        public void Record(T before) => Add(before, null);

        /// <summary>Records a change from <paramref name="before"/> to <paramref name="after"/>; nothing when the two are
        /// the same state (a click that repainted pixels in the colour they had). Returns whether a step was recorded.</summary>
        public bool Record(T before, T after)
        {
            if (_inGesture || _same.Equals(before, after))
            {
                return false;
            }

            Add(before, null);
            return true;
        }

        /// <summary>
        /// Records a change that merges with the previous one when both carry the same <paramref name="mergeKey"/>: a sweep
        /// across the colour wheel is ONE undo, not sixty. The run's first "before" stays the step. <see cref="EndMerge"/>
        /// ends the run (as releasing the mouse does); an undo or redo ends it too.
        /// </summary>
        public void Merge(T before, string mergeKey) => Add(before, mergeKey);

        /// <summary><see cref="Merge(T, string)"/> with the "nothing changed, no step" check of
        /// <see cref="Record(T, T)"/>.</summary>
        public bool Merge(T before, T after, string mergeKey)
        {
            if (_inGesture || _same.Equals(before, after))
            {
                return false;
            }

            Add(before, mergeKey);
            return true;
        }

        /// <summary>Ends a merge run (<see cref="Merge(T, string)"/>): the next change is a step of its own.</summary>
        public void EndMerge() => _mergeKey = null;

        private void Add(T before, string? mergeKey)
        {
            if (_inGesture)
            {
                return;
            }

            if (mergeKey != null && mergeKey == _mergeKey && _undo.Count > 0 && _redo.Count == 0)
            {
                _currentId = ++_nextId; // the run's first "before" stays the step; the state moved on
                Changed?.Invoke();
                return;
            }

            Push(before);
            _mergeKey = mergeKey;
            Changed?.Invoke();
        }

        /// <summary>Starts a gesture (a brush stroke, a drag of many cells): everything until <see cref="Commit"/> is ONE
        /// step. A second Begin while one is open is ignored — the first "before" stays.</summary>
        public void Begin(T before)
        {
            if (_inGesture)
            {
                return;
            }

            _inGesture = true;
            _gestureBefore = before;
        }

        /// <summary>Ends the gesture with the state it produced. A gesture that changed nothing is no step. Returns whether
        /// a step was recorded.</summary>
        public bool Commit(T after)
        {
            if (!_inGesture)
            {
                return false;
            }

            _inGesture = false;
            var before = _gestureBefore;
            _gestureBefore = default!;
            _mergeKey = null;
            return Record(before, after);
        }

        /// <summary>The state the open gesture started from (default when none is open) — what an editor rolls back to when
        /// a stroke is aborted.</summary>
        public T GestureStart => _gestureBefore;

        /// <summary>Drops the open gesture without a step (the editor rolled the change back itself).</summary>
        public void Cancel()
        {
            _inGesture = false;
            _gestureBefore = default!;
        }

        /// <summary>
        /// Takes the newest step back: <paramref name="current"/> is what the editor shows now (it becomes the redo step),
        /// <paramref name="restore"/> the state to show instead. An open gesture is committed first, so undo during a
        /// stroke takes the stroke back. False at the bottom of the history.
        /// </summary>
        public bool TryUndo(T current, out T restore) => Step(_undo, _redo, current, out restore);

        /// <summary>Puts the newest undone step back; the mirror of <see cref="TryUndo"/>. False when nothing was undone (or
        /// a new change since forgot the redo steps).</summary>
        public bool TryRedo(T current, out T restore) => Step(_redo, _undo, current, out restore);

        private bool Step(List<Entry> from, List<Entry> to, T current, out T restore)
        {
            if (_inGesture)
            {
                Commit(current);
            }

            _mergeKey = null;
            if (from.Count == 0)
            {
                restore = default!;
                return false;
            }

            var entry = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(new Entry(current, _currentId));
            _currentId = entry.StateId;
            restore = entry.Snapshot;
            Changed?.Invoke();
            return true;
        }

        private void Push(T before)
        {
            _undo.Add(new Entry(before, _currentId));
            if (_undo.Count > Capacity)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
            _currentId = ++_nextId;
        }

        private void DropSteps()
        {
            _undo.Clear();
            _redo.Clear();
            _inGesture = false;
            _gestureBefore = default!;
            _mergeKey = null;
        }

        /// <summary>A state the history can return to, with the id that tells saved from unsaved.</summary>
        private readonly struct Entry
        {
            public Entry(T snapshot, long stateId)
            {
                Snapshot = snapshot;
                StateId = stateId;
            }

            public T Snapshot { get; }

            public long StateId { get; }
        }
    }

    /// <summary>Equality helpers for <see cref="UndoHistory{T}"/> snapshots.</summary>
    public static class UndoSnapshots
    {
        /// <summary>Compares byte arrays by content (voxel volumes, pixel frames) — the "a change that changed nothing is no
        /// step" rule of the canvas models.</summary>
        public static IEqualityComparer<byte[]> Bytes { get; } = new ByteArrayComparer();

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public bool Equals(byte[]? x, byte[]? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                return x != null && y != null && x.AsSpan().SequenceEqual(y);
            }

            public int GetHashCode(byte[] obj)
            {
                unchecked
                {
                    int hash = 17;
                    int step = Math.Max(1, obj.Length / 64); // a sample is enough for a hash; Equals decides
                    for (int i = 0; i < obj.Length; i += step)
                    {
                        hash = (hash * 31) + obj[i];
                    }

                    return hash ^ obj.Length;
                }
            }
        }
    }
}
