// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client.Minigames.Games
{
    /// <summary>
    /// The note-key minigame (#3352, the Sounders story pack): hear a word of the Sounders sung in their note alphabet — two
    /// tones a letter, the first picks the row of the table, the second the column — and pick its letters in the alphabet
    /// table; three words, three rounds. The table fills in as letters are learned. The rules live in
    /// <see cref="NoteKeyPuzzle"/>; the drawing and the input in <see cref="NoteKeyRun"/>.
    /// <para>Two ways in: a Song Disc at a Song Spindle opens it with the disc's three words and the letters the player
    /// already knows (the story window reads <see cref="Puzzle"/>'s answers and reports them to the server); in the DataQubes
    /// arcade it picks three rune words of its own.</para>
    /// </summary>
    public sealed class NoteKeyGame : IMinigame
    {
        private readonly IReadOnlyList<string>? _words;
        private readonly string _known;
        private readonly int _instrument;

        /// <summary>The arcade game: three random rune words, an empty table.</summary>
        public NoteKeyGame()
        {
            _known = string.Empty;
            _instrument = SoundersIds.InstrumentVoice;
        }

        /// <summary>The story game: these words (one per round) and the letters the player already knows.</summary>
        public NoteKeyGame(IReadOnlyList<string> words, string? knownLetters, int instrument = SoundersIds.InstrumentVoice)
        {
            _words = words;
            _known = knownLetters ?? string.Empty;
            _instrument = instrument;
        }

        public string Key => SongDiscs.NoteKeyGame;
        public LocText Title => new LocText("minigame.note_key.title");
        public LocText Desc => new LocText("minigame.note_key.desc");
        public LocText Hint => new LocText("minigame.note_key.hint");
        public IReadOnlyList<LocText> Help { get; } = new[]
        {
            new LocText("minigame.note_key.help1"),
            new LocText("minigame.note_key.help2"),
            new LocText("minigame.note_key.help3"),
        };
        public int Difficulty => 2;

        /// <summary>The run started last (null before the first start) — its <see cref="NoteKeyPuzzle.Answers"/> are what the
        /// story window reports.</summary>
        public NoteKeyPuzzle? Puzzle { get; private set; }

        public MinigameController Create(MinigameApi api)
        {
            var puzzle = new NoteKeyPuzzle(_words ?? RandomWords(api), _known);
            Puzzle = puzzle;
            var run = new NoteKeyRun(api, puzzle, _instrument);
            return new MinigameController { Start = run.Start };
        }

        /// <summary>Three different rune words for the arcade.</summary>
        private static List<string> RandomWords(MinigameApi api)
        {
            var pool = new List<string>(SounderLexicon.Words);
            api.Shuffle(pool);
            return pool.GetRange(0, System.Math.Min(SongDiscs.RoundsPerDisc, pool.Count));
        }
    }
}
