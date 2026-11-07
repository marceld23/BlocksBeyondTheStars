// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One note of a <see cref="NoteSequence"/> that is due now: what to play and where.</summary>
    public readonly struct ScheduledNote
    {
        public ScheduledNote(int tone, int instrument, bool withFifth, float x, float y, float z, string source)
        {
            Tone = tone;
            Instrument = instrument;
            WithFifth = withFifth;
            X = x;
            Y = y;
            Z = z;
            Source = source;
        }

        /// <summary>The tone 0–7 (C4 … C5).</summary>
        public int Tone { get; }

        /// <summary>The procedural instrument 0–5.</summary>
        public int Instrument { get; }

        /// <summary>Play its fifth above with it (the same clip at pitch <see cref="NoteSequenceScheduler.FifthPitch"/>).</summary>
        public bool WithFifth { get; }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public string Source { get; }

        /// <summary>The procedural clip id of this note (<c>note_&lt;instrument&gt;_&lt;tone&gt;</c>).</summary>
        public string ClipId => NoteSequenceScheduler.ClipId(Instrument, Tone);
    }

    /// <summary>
    /// The client's beat for server melodies (#3345): a <see cref="NoteSequence"/> arrives as ONE message and the client
    /// times its notes itself (no network jitter between them) — note <c>i</c> sounds <c>i × NoteSeconds</c> after the
    /// message, a rest (-1) is silence of the same length. Unity-free: the audio router feeds the clock and plays what
    /// <see cref="Due"/> hands back. Several sequences may overlap (a rune and a prompt); a flood is capped at
    /// <see cref="MaxPending"/> notes.
    /// </summary>
    public sealed class NoteSequenceScheduler
    {
        /// <summary>The most notes waiting at once — more are dropped (a broken server cannot pile up minutes of sound).</summary>
        public const int MaxPending = 256;

        /// <summary>The pitch factor of a perfect fifth (the "with fifth" voice).</summary>
        public const float FifthPitch = 1.5f;

        /// <summary>The note length the scheduler accepts (seconds): anything outside is clamped.</summary>
        public const float MinNoteSeconds = 0.05f;
        public const float MaxNoteSeconds = 2f;

        /// <summary>The highest procedural instrument (5 = the Sounder choir).</summary>
        public const int MaxInstrument = 5;

        private readonly List<(double At, long Order, ScheduledNote Note)> _pending = new();
        private long _order;

        /// <summary>Notes still waiting.</summary>
        public int Pending => _pending.Count;

        /// <summary>The procedural clip id of a note: <c>note_&lt;instrument&gt;_&lt;tone&gt;</c>, both clamped to the library.</summary>
        public static string ClipId(int instrument, int tone)
            => "note_" + Math.Max(0, Math.Min(MaxInstrument, instrument)) + "_" + Math.Max(0, Math.Min(7, tone));

        /// <summary>How long a sequence plays (seconds).</summary>
        public static double Duration(NoteSequence m) => m?.Notes is null ? 0 : m.Notes.Length * (double)NoteSeconds(m);

        /// <summary>Queues <paramref name="m"/> to start at <paramref name="now"/>. Returns how many notes (not rests) it queued.</summary>
        public int Enqueue(NoteSequence m, double now)
        {
            if (m?.Notes is null)
            {
                return 0;
            }

            float step = NoteSeconds(m);
            int instrument = Math.Max(0, Math.Min(MaxInstrument, m.Instrument));
            int queued = 0;
            for (int i = 0; i < m.Notes.Length && _pending.Count < MaxPending; i++)
            {
                int tone = m.Notes[i];
                if (tone < 0 || tone > 7)
                {
                    continue; // a rest — or something that is no tone: silence either way
                }

                var note = new ScheduledNote(tone, instrument, m.WithFifth, m.X, m.Y, m.Z, m.Source ?? string.Empty);
                _pending.Add((now + (i * (double)step), _order++, note));
                queued++;
            }

            return queued;
        }

        /// <summary>Moves every note due by <paramref name="now"/> into <paramref name="into"/>, earliest first. Returns how many.</summary>
        public int Due(double now, List<ScheduledNote> into)
        {
            if (_pending.Count == 0)
            {
                return 0;
            }

            _pending.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Order.CompareTo(b.Order));
            int due = 0;
            while (due < _pending.Count && _pending[due].At <= now)
            {
                into.Add(_pending[due].Note);
                due++;
            }

            _pending.RemoveRange(0, due);
            return due;
        }

        /// <summary>Forgets everything waiting (a world change, a disconnect).</summary>
        public void Clear() => _pending.Clear();

        private static float NoteSeconds(NoteSequence m)
            => float.IsNaN(m.NoteSeconds) ? 0.32f : Math.Max(MinNoteSeconds, Math.Min(MaxNoteSeconds, m.NoteSeconds));
    }
}
