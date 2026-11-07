// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A sung dialog reply (#3347, the Sounders' Echoes): the reply's melody is letters of the note alphabet
    /// (<see cref="SounderAlphabet"/>); the dialog panel shows it as note symbols and plays it locally in the Sounder voice
    /// (procedural instrument <see cref="Instrument"/>, sound ids <c>note_4_&lt;tone&gt;</c>). Unity-free, unit-tested.
    /// </summary>
    public static class DialogMelody
    {
        /// <summary>The procedural instrument a reply is sung with (the Sounder voice).</summary>
        public const int Instrument = SoundersIds.InstrumentVoice;

        /// <summary>Seconds one note (or a rest) lasts when the panel plays a melody.</summary>
        public const float NoteSeconds = 0.3f;

        /// <summary>The note sequence of a melody (two tones per letter, rests between letters and words).</summary>
        public static int[] Notes(string? letters) => SounderAlphabet.Encode(letters ?? string.Empty);

        /// <summary>The client sound id of a tone (<c>note_4_3</c>), or null for a rest.</summary>
        public static string? NoteId(int tone) => tone < 0 ? null : "note_" + Instrument + "_" + tone;

        /// <summary>The eight tone names from the locale's <c>ui.note.names</c> ("C D E F G A B C'" — German writes H), or the
        /// alphabet's own names when the text is missing or not eight words.</summary>
        public static IReadOnlyList<string> ToneNames(string? localized)
        {
            var words = (localized ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == SounderAlphabet.ToneCount ? words : SounderAlphabet.ToneNames;
        }

        /// <summary>The melody as note symbols: <c>♪ F E · G D ♪</c> — one pair of tone names per letter, a dot between letters,
        /// a wider gap between words. Empty for no melody.</summary>
        public static string Symbols(string? letters, IReadOnlyList<string> toneNames)
        {
            int[] notes = Notes(letters);
            if (notes.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder("♪ ");
            int rests = 0;
            bool any = false;
            foreach (int n in notes)
            {
                if (n == SounderAlphabet.Rest)
                {
                    rests++;
                    continue;
                }

                if (any)
                {
                    sb.Append(rests >= 2 ? "  ·  ·  " : rests == 1 ? " · " : " ");
                }

                sb.Append(n < toneNames.Count ? toneNames[n] : "?");
                rests = 0;
                any = true;
            }

            return sb.Append(" ♪").ToString();
        }

        /// <summary>The reply button's text: the spoken words (if any), then the melody's symbols.</summary>
        public static string ChoiceLabel(string? text, string? melody, IReadOnlyList<string> toneNames)
        {
            string symbols = Symbols(melody, toneNames);
            if (symbols.Length == 0)
            {
                return text ?? string.Empty;
            }

            return string.IsNullOrEmpty(text) ? symbols : text + "  " + symbols;
        }

        /// <summary>The melody of reply <paramref name="index"/> of a dialog step (empty when the step sings nothing).</summary>
        public static string MelodyOf(string[]? melodies, int index)
            => melodies is not null && index >= 0 && index < melodies.Length ? melodies[index] ?? string.Empty : string.Empty;
    }
}
