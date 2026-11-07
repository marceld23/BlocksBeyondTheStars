// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>What one pick in the note-key minigame did.</summary>
    public enum NoteKeyPick
    {
        /// <summary>Nothing happened (the run is over, or not a letter).</summary>
        Ignored,

        /// <summary>The right letter; the word goes on.</summary>
        Correct,

        /// <summary>Not this letter — try again (the letter's tones play once more).</summary>
        Wrong,

        /// <summary>Wrong again: the letter is shown (no dead end) and the word goes on.</summary>
        Revealed,

        /// <summary>The right letter finished the word; the next round starts.</summary>
        WordDone,

        /// <summary>The last word is spelled — the run is complete.</summary>
        AllDone,

        /// <summary>Too many wrong picks in this run — it is failed (the player may try again).</summary>
        Failed,
    }

    /// <summary>
    /// The rules of the note-key minigame (#3352), engine-free so they are tested headless: a Song Disc's phrase, one word per
    /// round, each letter sung as its two tones in the Sounders' note alphabet (<see cref="SounderAlphabet"/>: the first tone
    /// picks the row of the table, the second the column). The player picks the letter in the table; a right pick writes it
    /// into the word and the table (it is learned), a wrong one costs a mistake and sings the letter again. Wrong
    /// <see cref="RevealAfter"/> times on one letter and the letter is shown — nobody gets stuck on one sound. A run fails
    /// only after <see cref="MaxMistakes"/> wrong picks in all; the window then offers another try (and, after two failed tries,
    /// the server accepts a skip).
    /// </summary>
    public sealed class NoteKeyPuzzle
    {
        /// <summary>Wrong picks on one letter after which it is shown.</summary>
        public const int RevealAfter = 3;

        /// <summary>Wrong picks in one run after which it is failed.</summary>
        public const int MaxMistakes = 8;

        private readonly string[] _words;
        private readonly bool[] _known = new bool[26];
        private readonly List<string> _answers = new List<string>();
        private readonly StringBuilder _spelled = new StringBuilder();

        /// <summary>A run over <paramref name="words"/> (A–Z, any case; empty words are dropped) with the letters the player
        /// already knows filled into the table.</summary>
        public NoteKeyPuzzle(IReadOnlyList<string> words, string? knownLetters = null)
        {
            var clean = new List<string>();
            foreach (string w in words ?? Array.Empty<string>())
            {
                string letters = OnlyLetters(w);
                if (letters.Length > 0)
                {
                    clean.Add(letters);
                }
            }

            _words = clean.ToArray();
            foreach (char c in knownLetters ?? string.Empty)
            {
                Learn(c);
            }
        }

        /// <summary>The words of the run, upper case.</summary>
        public IReadOnlyList<string> Words => _words;

        /// <summary>The current round (0-based); equals <see cref="RoundCount"/> once every word is spelled.</summary>
        public int Round { get; private set; }

        public int RoundCount => _words.Length;

        /// <summary>The word of the current round ("" when done).</summary>
        public string Word => Round < _words.Length ? _words[Round] : string.Empty;

        /// <summary>The letters of the current word found so far.</summary>
        public string Spelled => _spelled.ToString();

        /// <summary>The position of the letter to find in the current word.</summary>
        public int LetterIndex => _spelled.Length;

        /// <summary>The letter to find now, or '\0' when the run is over.</summary>
        public char CurrentLetter => Done || Failed ? '\0' : Word[LetterIndex];

        /// <summary>Wrong picks in this run, and on the current letter.</summary>
        public int Mistakes { get; private set; }

        public int MistakesThisLetter { get; private set; }

        /// <summary>Letters that were shown after too many wrong picks.</summary>
        public int Revealed { get; private set; }

        /// <summary>Every word is spelled.</summary>
        public bool Done => Round >= _words.Length;

        /// <summary>Too many wrong picks.</summary>
        public bool Failed { get; private set; }

        /// <summary>The spelled words of the finished rounds — what the client reports to the server.</summary>
        public IReadOnlyList<string> Answers => _answers;

        /// <summary>Whether the player knows <paramref name="letter"/> (the table shows it).</summary>
        public bool Knows(char letter)
        {
            int i = char.ToUpperInvariant(letter) - 'A';
            return i >= 0 && i < 26 && _known[i];
        }

        /// <summary>How many letters of the table the player knows.</summary>
        public int KnownCount
        {
            get
            {
                int n = 0;
                foreach (bool k in _known)
                {
                    n += k ? 1 : 0;
                }

                return n;
            }
        }

        /// <summary>The two tones of the letter to find (row, column), or an empty array when the run is over.</summary>
        public int[] CurrentLetterNotes
            => SounderAlphabet.TonesOf(CurrentLetter) is { } t ? new[] { t.Row, t.Column } : Array.Empty<int>();

        /// <summary>The current word in the note alphabet (rests between the letters).</summary>
        public int[] CurrentWordNotes => SounderAlphabet.Encode(Word);

        /// <summary>The rating of a finished run: 3 without a mistake, 2 with a few, 1 otherwise.</summary>
        public int Rating => Mistakes == 0 && Revealed == 0 ? 3 : Mistakes <= 3 && Revealed == 0 ? 2 : 1;

        /// <summary>The score of the run: 100 per letter, minus 15 per mistake, never below 0.</summary>
        public int Score
        {
            get
            {
                int letters = 0;
                foreach (string w in _answers)
                {
                    letters += w.Length;
                }

                return Math.Max(0, (letters * 100) - (Mistakes * 15));
            }
        }

        /// <summary>The player picks <paramref name="letter"/> in the table.</summary>
        public NoteKeyPick Pick(char letter)
        {
            char up = char.ToUpperInvariant(letter);
            if (Done || Failed || up < 'A' || up > 'Z')
            {
                return NoteKeyPick.Ignored;
            }

            if (up == CurrentLetter)
            {
                return Advance();
            }

            Mistakes++;
            MistakesThisLetter++;
            if (Mistakes >= MaxMistakes)
            {
                Failed = true;
                return NoteKeyPick.Failed;
            }

            if (MistakesThisLetter >= RevealAfter)
            {
                Revealed++;
                var after = Advance();
                return after == NoteKeyPick.Correct ? NoteKeyPick.Revealed : after;
            }

            return NoteKeyPick.Wrong;
        }

        /// <summary>The current letter is found (or shown): learned, written down; the word or the run may be done.</summary>
        private NoteKeyPick Advance()
        {
            char found = CurrentLetter;
            Learn(found);
            _spelled.Append(found);
            MistakesThisLetter = 0;
            if (_spelled.Length < Word.Length)
            {
                return NoteKeyPick.Correct;
            }

            _answers.Add(_spelled.ToString());
            _spelled.Clear();
            Round++;
            return Done ? NoteKeyPick.AllDone : NoteKeyPick.WordDone;
        }

        private void Learn(char c)
        {
            int i = char.ToUpperInvariant(c) - 'A';
            if (i >= 0 && i < 26)
            {
                _known[i] = true;
            }
        }

        private static string OnlyLetters(string? word)
        {
            var sb = new StringBuilder();
            foreach (char c in word ?? string.Empty)
            {
                char up = char.ToUpperInvariant(c);
                if (up >= 'A' && up <= 'Z')
                {
                    sb.Append(up);
                }
            }

            return sb.ToString();
        }
    }
}
