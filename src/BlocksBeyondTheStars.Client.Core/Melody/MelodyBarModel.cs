// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the melody bar (#3346) shows, Unity-free: open or closed, the puzzle's mode and progress, the tone the prompt
    /// lights right now, the tone that flashes after a key press or a judged note, and the result line. The server's
    /// <see cref="MelodyPuzzleState"/> drives it (<see cref="Apply"/>); the bar itself only reads it and sends the keys.
    /// The bar can also open for free play (<see cref="OpenFreePlay"/> — the Resonance Blaster in hand), with no puzzle id.
    /// Every tone has its own colour AND its own symbol (<see cref="Symbols"/>), so it never relies on colour alone.
    /// </summary>
    public sealed class MelodyBarModel
    {
        /// <summary>One symbol per tone, C … C' — distinct shapes the UI font draws.</summary>
        public static readonly string[] Symbols = { "●", "▲", "■", "★", "♥", "♪", "+", "×" };

        /// <summary>One colour per tone (0xRRGGBB), C … C': red, orange, yellow, green, teal, blue, violet, pink.</summary>
        public static readonly int[] Colors = { 0xE8505B, 0xF39C3D, 0xF2D14B, 0x5CC86A, 0x3FC6D8, 0x4A7FE0, 0x9B6BE0, 0xE86FB8 };

        /// <summary>How long a key press or a judged note flashes its tone (seconds).</summary>
        public const double FlashSeconds = 0.35;

        /// <summary>How long a result line ("Again!", "Well done!") stays (seconds) — and how long a closed bar stays on screen
        /// to show it.</summary>
        public const double ResultSeconds = 1.6;

        private int[] _prompt = Array.Empty<int>();
        private double _promptStart;
        private double _resultAt = double.NegativeInfinity;
        private double _closedAt = double.NegativeInfinity;
        private int _flashTone = -1;
        private double _flashAt = double.NegativeInfinity;

        /// <summary>The bar is open and takes keys.</summary>
        public bool Open { get; private set; }

        /// <summary>The running puzzle's id; empty in free play.</summary>
        public string PuzzleId { get; private set; } = string.Empty;

        public bool FreePlay => Open && PuzzleId.Length == 0;
        public MelodyPuzzleMode Mode { get; private set; }
        public int Round { get; private set; }
        public int RoundCount { get; private set; }
        public int Entered { get; private set; }
        public int Expected { get; private set; }

        /// <summary>The instrument the bar sounds its tones with.</summary>
        public int Instrument { get; private set; } = SoundersIds.InstrumentVoice;

        public float NoteSeconds { get; private set; } = 0.45f;
        public MelodyPuzzleResult Result { get; private set; }

        /// <summary>#3426: the word the puzzle's melody spells (the Song of Return's ORNEMIS), or empty.</summary>
        public string Word { get; private set; } = string.Empty;

        /// <summary>#3426: the two tones the player plays for each letter of <see cref="Word"/>.</summary>
        public int[] WordTones { get; private set; } = Array.Empty<int>();

        /// <summary>Bumped on every change, so the HUD can rebuild its texts only when needed.</summary>
        public int Version { get; private set; }

        /// <summary>A server state arrived at <paramref name="now"/> (seconds on the client's clock).</summary>
        public void Apply(MelodyPuzzleState m, double now)
        {
            if (m is null)
            {
                return;
            }

            bool wasOpen = Open;
            Open = m.Open;
            PuzzleId = m.PuzzleId ?? string.Empty;
            Mode = (MelodyPuzzleMode)m.Mode;
            Round = m.Round;
            RoundCount = m.RoundCount;
            Entered = m.Entered;
            Expected = m.Expected;
            Instrument = m.Instrument;
            NoteSeconds = m.NoteSeconds > 0 ? m.NoteSeconds : 0.45f;
            Result = (MelodyPuzzleResult)m.Result;
            Word = m.Open ? m.Word ?? string.Empty : string.Empty;
            WordTones = m.Open && m.WordTones is { } tones ? tones : Array.Empty<int>();
            _resultAt = now;
            if (m.Highlight is { Length: > 0 })
            {
                _prompt = m.Highlight;
                _promptStart = now;
            }

            if (m.LastTone >= 0)
            {
                Flash(m.LastTone, now);
            }

            if (wasOpen && !Open)
            {
                _closedAt = now;
                _prompt = Array.Empty<int>();
            }

            Version++;
        }

        /// <summary>Opens the bar for free play — no puzzle, the tones are only music (the Resonance Blaster in hand).</summary>
        public void OpenFreePlay(int instrument)
        {
            if (Open && PuzzleId.Length > 0)
            {
                return; // a puzzle has the bar
            }

            Open = true;
            PuzzleId = string.Empty;
            Instrument = Math.Max(0, Math.Min(NoteSequenceScheduler.MaxInstrument, instrument));
            Result = MelodyPuzzleResult.Playing;
            Round = RoundCount = Entered = Expected = 0;
            Word = string.Empty;
            WordTones = Array.Empty<int>();
            Version++;
        }

        /// <summary>Closes a free-play bar (a puzzle's bar is closed by the server only).</summary>
        public void CloseFreePlay()
        {
            if (FreePlay)
            {
                Open = false;
                Version++;
            }
        }

        /// <summary>Forgets everything (a world change, a disconnect).</summary>
        public void Reset()
        {
            Open = false;
            PuzzleId = string.Empty;
            _prompt = Array.Empty<int>();
            _closedAt = _resultAt = _flashAt = double.NegativeInfinity;
            _flashTone = -1;
            Result = MelodyPuzzleResult.Playing;
            Word = string.Empty;
            WordTones = Array.Empty<int>();
            Version++;
        }

        /// <summary>The player pressed a tone key: it flashes at once (the server's verdict follows).</summary>
        public void Flash(int tone, double now)
        {
            _flashTone = tone;
            _flashAt = now;
        }

        /// <summary>Whether the bar is on screen: open, or just closed and still showing its result.</summary>
        public bool Visible(double now) => Open || now - _closedAt < ResultSeconds;

        /// <summary>The tone the prompt lights at <paramref name="now"/> (one after the other, <see cref="NoteSeconds"/> apart;
        /// rests light nothing), or -1.</summary>
        public int LitTone(double now)
        {
            if (_prompt.Length == 0 || now < _promptStart)
            {
                return -1;
            }

            int index = (int)Math.Floor((now - _promptStart) / NoteSeconds);
            return index < _prompt.Length ? _prompt[index] : -1;
        }

        /// <summary>Whether the prompt is still playing at <paramref name="now"/>.</summary>
        public bool PromptPlaying(double now) => _prompt.Length > 0 && now >= _promptStart && now - _promptStart < _prompt.Length * (double)NoteSeconds;

        /// <summary>Whether <paramref name="tone"/> flashes at <paramref name="now"/> (a key press or a judged note).</summary>
        public bool Flashing(int tone, double now) => tone == _flashTone && now - _flashAt < FlashSeconds;

        /// <summary>The locale key of the bar's title for the mode (free play: just "Melody").</summary>
        public string TitleKey => FreePlay ? "ui.melody.title" : "ui.melody.mode." + ModeKey(Mode);

        /// <summary>#3426: the letter of <see cref="Word"/> the player is at (two right tones per letter), or -1 without a word.</summary>
        public int CurrentLetter => MelodySpelling.CurrentLetter(Entered, MelodySpelling.Letters(Word).Length);

        /// <summary>
        /// #3426: the spelling line of a puzzle whose melody spells a word — every letter with its two notes,
        /// <c>O = F E · R = G D · …</c>, named from <paramref name="noteNames"/> (the locale's <c>ui.note.names</c>), the letter the
        /// player is at wrapped in <paramref name="markOpen"/> / <paramref name="markClose"/>. Empty when the bar spells no word.
        /// </summary>
        public string SpellingLine(IReadOnlyList<string>? noteNames, string markOpen = "", string markClose = "")
            => Open && Word.Length > 0 ? MelodySpelling.Line(Word, WordTones, noteNames, CurrentLetter, markOpen, markClose) : string.Empty;

        /// <summary>The locale key of the result line shown at <paramref name="now"/>, or null (nothing to say).</summary>
        public string? ResultKey(double now)
        {
            if (now - _resultAt >= ResultSeconds)
            {
                return null;
            }

            return Result switch
            {
                MelodyPuzzleResult.RoundDone => "ui.melody.result.round",
                MelodyPuzzleResult.TryAgain => "ui.melody.result.again",
                MelodyPuzzleResult.Solved => "ui.melody.result.solved",
                MelodyPuzzleResult.Cancelled => "ui.melody.result.stopped",
                MelodyPuzzleResult.TimedOut => "ui.melody.result.stopped",
                MelodyPuzzleResult.Reminder => "ui.melody.result.listen",
                _ => null,
            };
        }

        /// <summary>The locale key fragment of a mode: <c>repeat</c>, <c>compose</c>, <c>harmony</c>, <c>call</c>.</summary>
        public static string ModeKey(MelodyPuzzleMode mode) => mode switch
        {
            MelodyPuzzleMode.Compose => "compose",
            MelodyPuzzleMode.Harmony => "harmony",
            MelodyPuzzleMode.CallAndResponse => "call",
            _ => "repeat",
        };

        /// <summary>The colour of a tone as 0..1 RGB.</summary>
        public static (float R, float G, float B) ColorOf(int tone)
        {
            int c = Colors[Math.Max(0, Math.Min(Colors.Length - 1, tone))];
            return (((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);
        }
    }
}
