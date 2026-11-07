// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Sounders' music in the router (#3345/#3346): a server <see cref="NoteSequence"/> (a rune's word, a puzzle's
    /// prompt, a struck pillar, a friend's melody-bar tone) is timed by the Unity-free <see cref="NoteSequenceScheduler"/>
    /// and each due note plays as a positional one-shot of its procedural clip (<c>note_&lt;instrument&gt;_&lt;tone&gt;</c>);
    /// "with fifth" adds the same clip at pitch 1.5. The melody bar's own tones play at once, unpositioned
    /// (<see cref="PlayMelodyTone"/>) — the player's instant feedback; the server lets everyone else hear them.
    /// </summary>
    public sealed partial class ClientAudio
    {
        private readonly NoteSequenceScheduler _noteBeat = new NoteSequenceScheduler();
        private readonly List<ScheduledNote> _dueNotes = new List<ScheduledNote>();

        /// <summary>Queues a server melody to play at <paramref name="scenePos"/> (the caller maps the message's world position
        /// into the scene, like <see cref="Fx"/>).</summary>
        public void PlayNoteSequence(NoteSequence m, Vector3 scenePos)
        {
            if (m?.Notes == null || m.Notes.Length == 0)
            {
                return;
            }

            _noteBeat.Enqueue(new NoteSequence
            {
                Notes = m.Notes,
                Instrument = m.Instrument,
                NoteSeconds = m.NoteSeconds,
                WithFifth = m.WithFifth,
                X = scenePos.x,
                Y = scenePos.y,
                Z = scenePos.z,
                Source = m.Source,
            }, Time.unscaledTimeAsDouble);
        }

        /// <summary>A tone of the melody bar for the player themself: at once, 2D, with the bar's instrument.</summary>
        public void PlayMelodyTone(int instrument, int tone)
            => PlayOwn(NoteSequenceScheduler.ClipId(instrument, tone), 0.9f);

        /// <summary>Forgets every melody still waiting (a world change, a disconnect).</summary>
        public void StopNoteSequences() => _noteBeat.Clear();

        /// <summary>Per frame: plays the notes that are due.</summary>
        private void TickNoteSequences()
        {
            if (_noteBeat.Pending == 0)
            {
                return;
            }

            _dueNotes.Clear();
            _noteBeat.Due(Time.unscaledTimeAsDouble, _dueNotes);
            foreach (var note in _dueNotes)
            {
                var pos = new Vector3(note.X, note.Y, note.Z);
                At(note.ClipId, pos, 1f, 0.85f);
                if (note.WithFifth)
                {
                    At(note.ClipId, pos, NoteSequenceScheduler.FifthPitch, 0.55f);
                }
            }
        }
    }
}
