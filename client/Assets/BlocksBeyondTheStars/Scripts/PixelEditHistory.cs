// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The undo/redo stack behind <see cref="FaceEditor"/> — the pixel editor the appearance screen, the
    /// main-menu Avatar Designer and the block paint tool all share. It used to be a single snapshot the
    /// undo button swapped in and out, which takes back the last brush stroke and nothing before it.
    /// <para>
    /// Two things make this more than a list of snapshots. A step is either a <b>grid</b> change (a stroke,
    /// a fill, a clear) or a <b>base-colour</b> change, because a slip of the colour wheel repaints the
    /// whole figure and has to be as takeable-back as a stroke. And every step names the <b>subject</b> it
    /// happened on (face, torso, arms, legs, helmet), so the history survives a tab switch and the editor
    /// can carry the tab back with the step — a change the player cannot see happening does not read as an
    /// undo at all.
    /// </para>
    /// Plain C# on purpose: this is where the fiddly rules live (a new step forgets the redo tail, the
    /// oldest step falls off a full stack, a colour DRAG is one step and not sixty), so it is testable
    /// without standing up a canvas.
    /// </summary>
    public sealed class PixelEditHistory
    {
        /// <summary>One undoable step. <see cref="GridBefore"/> is null on a colour step.</summary>
        public sealed class Edit
        {
            public int Subject;
            public byte[] GridBefore, GridAfter;
            public Color ColorBefore, ColorAfter;

            /// <summary>True when this step changed a base colour rather than the canvas.</summary>
            public bool IsColor => GridBefore == null;
        }

        private readonly List<Edit> _edits = new List<Edit>();
        private readonly int _max;
        private int _at;            // steps [0.._at) are applied, [_at..) are redoable
        private int _colorRun = -1; // slot the colour drag in progress keeps growing, -1 = no drag

        /// <summary>A grid is one byte per cell, so the worst case — a full leg canvas of 128×64 — is 8 KB
        /// before + 8 KB after per step: 32 steps is about half a megabyte, which buys a real undo.</summary>
        public PixelEditHistory(int maxSteps = 32) => _max = Mathf.Max(1, maxSteps);

        public bool CanUndo => _at > 0;

        public bool CanRedo => _at < _edits.Count;

        /// <summary>Steps currently on the stack (applied plus redoable) — for tests and diagnostics.</summary>
        public int Count => _edits.Count;

        /// <summary>Throws the whole history away: used when the host swaps the entire look under the editor
        /// (wearing an outfit), where undoing into the previous look would mix two outfits together.</summary>
        public void Clear()
        {
            _edits.Clear();
            _at = 0;
            _colorRun = -1;
        }

        /// <summary>Ends the colour drag in progress, so the next colour change starts its own step. The
        /// editor calls this the moment the mouse button comes up.</summary>
        public void EndColorRun() => _colorRun = -1;

        /// <summary>Records a canvas change. Identical before/after is not a step: a stroke that only
        /// repainted pixels already in that colour must not eat an undo.</summary>
        public void PushGrid(int subject, byte[] before, byte[] after)
        {
            if (before == null || after == null || before.Length != after.Length || SameBytes(before, after))
            {
                return;
            }

            Push(new Edit { Subject = subject, GridBefore = before, GridAfter = after });
            _colorRun = -1;
        }

        /// <summary>Records a base-colour change. While <paramref name="fromDrag"/> keeps coming from the
        /// same gesture the running step only grows its "after", so one sweep around the colour wheel is one
        /// undo rather than a history full of near-identical shades.</summary>
        public void PushColor(int subject, Color before, Color after, bool fromDrag)
        {
            if (SameColor(before, after))
            {
                return;
            }

            if (fromDrag && _colorRun >= 0 && _colorRun == _edits.Count - 1 && _at == _edits.Count
                && _edits[_colorRun].IsColor && _edits[_colorRun].Subject == subject)
            {
                _edits[_colorRun].ColorAfter = after;
                return;
            }

            Push(new Edit { Subject = subject, ColorBefore = before, ColorAfter = after });
            _colorRun = fromDrag ? _edits.Count - 1 : -1;
        }

        /// <summary>The step to take back, or null at the bottom of the stack.</summary>
        public Edit Undo() => CanUndo ? _edits[--_at] : null;

        /// <summary>The step to put back, or null at the top.</summary>
        public Edit Redo() => CanRedo ? _edits[_at++] : null;

        private void Push(Edit edit)
        {
            if (_at < _edits.Count)
            {
                _edits.RemoveRange(_at, _edits.Count - _at); // a new step forgets what was redoable
            }

            _edits.Add(edit);
            if (_edits.Count > _max)
            {
                _edits.RemoveAt(0);
                _colorRun--;
            }

            _at = _edits.Count;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameColor(Color a, Color b)
            => Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);
    }
}
