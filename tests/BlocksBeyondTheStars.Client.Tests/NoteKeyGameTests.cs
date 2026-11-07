// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Minigames;
using BlocksBeyondTheStars.Client.Minigames.Games;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3352 — the note-key minigame, headless: the puzzle's rules (one word per round, the right letter goes on, a wrong one
/// costs a mistake, a letter is shown after three, a run fails only after eight), the table learning letters, the game on
/// the host — it sings through the host's new note sink (two tones a letter, the Sounder voice), a pick in the table is
/// a pick of the cell's letter, and a finished run reports the spelled words — and the minigame audio API itself.
/// </summary>
public sealed class NoteKeyGameTests
{
    private static readonly string[] Builders = { "OLU", "TAR", "SOA" };

    [Fact]
    public void RightPicks_SpellTheWords_RoundByRound()
    {
        var puzzle = new NoteKeyPuzzle(Builders);
        Assert.Equal(3, puzzle.RoundCount);
        Assert.Equal('O', puzzle.CurrentLetter);
        Assert.Equal(new[] { 3, 2 }, puzzle.CurrentLetterNotes); // O: row F (3), column E (2)
        Assert.Equal(SounderAlphabet.Encode("OLU"), puzzle.CurrentWordNotes);

        Assert.Equal(NoteKeyPick.Correct, puzzle.Pick('o'));
        Assert.Equal(NoteKeyPick.Correct, puzzle.Pick('L'));
        Assert.Equal(NoteKeyPick.WordDone, puzzle.Pick('U'));
        Assert.Equal(1, puzzle.Round);
        foreach (char c in "TA")
        {
            puzzle.Pick(c);
        }

        Assert.Equal(NoteKeyPick.WordDone, puzzle.Pick('R'));
        puzzle.Pick('S');
        puzzle.Pick('O');
        Assert.Equal(NoteKeyPick.AllDone, puzzle.Pick('A'));
        Assert.True(puzzle.Done);
        Assert.Equal(Builders, puzzle.Answers);
        Assert.True(SongDiscs.AnswersMatch(SoundersIds.DiscBuilders, puzzle.Answers.ToArray()));
        Assert.Equal(3, puzzle.Rating);
        Assert.Equal(900, puzzle.Score);
        Assert.Equal(NoteKeyPick.Ignored, puzzle.Pick('A')); // over
    }

    [Fact]
    public void AWrongPick_CostsAMistake_AndTheThirdShowsTheLetter()
    {
        var puzzle = new NoteKeyPuzzle(new[] { "LUM" });
        Assert.Equal(NoteKeyPick.Wrong, puzzle.Pick('A'));
        Assert.Equal(NoteKeyPick.Wrong, puzzle.Pick('B'));
        Assert.Equal(NoteKeyPick.Revealed, puzzle.Pick('C'));
        Assert.Equal("L", puzzle.Spelled);
        Assert.Equal(3, puzzle.Mistakes);
        Assert.Equal(0, puzzle.MistakesThisLetter);
        Assert.True(puzzle.Knows('L'));
        Assert.Equal(1, puzzle.Rating); // a shown letter is still a help
    }

    [Fact]
    public void EightMistakes_FailTheRun_AndNothingMoreHappens()
    {
        var puzzle = new NoteKeyPuzzle(new[] { "SOA", "NIRI", "ORN" });
        var results = new List<NoteKeyPick>();
        for (int i = 0; i < NoteKeyPuzzle.MaxMistakes; i++)
        {
            results.Add(puzzle.Pick('Z'));
        }

        Assert.Equal(NoteKeyPick.Failed, results.Last());
        Assert.True(puzzle.Failed);
        Assert.False(puzzle.Done);
        Assert.Equal(NoteKeyPick.Ignored, puzzle.Pick('S'));
        Assert.Equal('\0', puzzle.CurrentLetter);
    }

    [Fact]
    public void TheTable_StartsWithTheKnownLetters_AndLearnsTheFoundOnes()
    {
        var puzzle = new NoteKeyPuzzle(new[] { "ARU" }, knownLetters: "sz");
        Assert.True(puzzle.Knows('S'));
        Assert.True(puzzle.Knows('z'));
        Assert.False(puzzle.Knows('A'));
        Assert.Equal(2, puzzle.KnownCount);
        puzzle.Pick('A');
        Assert.True(puzzle.Knows('A'));
        Assert.Equal(3, puzzle.KnownCount);
    }

    [Fact]
    public void WordsAreCleaned_AndAnEmptyRunIsDoneAtOnce()
    {
        var puzzle = new NoteKeyPuzzle(new[] { " o-lu ", "", "123" });
        Assert.Equal(new[] { "OLU" }, puzzle.Words);
        Assert.True(new NoteKeyPuzzle(System.Array.Empty<string>()).Done);
    }

    private static (MinigameHost Host, NoteKeyGame Game, List<(int Instrument, int Tone)> Notes) Story(params string[] words)
    {
        var game = new NoteKeyGame(words, "OL", SoundersIds.InstrumentVoice);
        var notes = new List<(int, int)>();
        var host = new MinigameHost(game, seed: 7) { NoteSink = (i, t) => notes.Add((i, t)) };
        host.StartGame();
        return (host, game, notes);
    }

    private static void Run(MinigameHost host, float seconds)
    {
        for (int i = 0; i < seconds * 60 && host.State == MinigameState.Playing; i++)
        {
            host.Tick(1f / 60f);
        }
    }

    private static void Click(MinigameHost host, char letter)
    {
        var tones = SounderAlphabet.TonesOf(letter)!.Value;
        var (x, y, w, h) = NoteKeyLayout.Cell(tones.Row, tones.Column);
        host.Pointer(PointerPhase.Down, x + (w / 2f), y + (h / 2f));
    }

    [Fact]
    public void TheGame_SingsTheWord_ThroughTheHostsNoteSink_WithTheSounderVoice()
    {
        var (host, game, notes) = Story("OLU", "TAR", "SOA");
        Assert.NotNull(host.Api.Surface);
        Assert.Equal(NoteKeyRun.W, host.Api.Surface!.Width);

        Run(host, 6f);

        int[] expected = SounderAlphabet.TonesOnly(SounderAlphabet.Encode("OLU"));
        Assert.True(notes.Count >= expected.Length + 2, "the word, then the first letter again");
        Assert.All(notes, n => Assert.Equal(SoundersIds.InstrumentVoice, n.Instrument));
        Assert.Equal(expected, notes.Take(expected.Length).Select(n => n.Tone));
        Assert.Equal(new[] { 3, 2 }, notes.Skip(expected.Length).Take(2).Select(n => n.Tone)); // O again
        Assert.True(game.Puzzle!.Knows('O')); // the known letters fill the table from the start
    }

    [Fact]
    public void ClickingTheCells_SpellsTheWords_AndTheRunCompletesWithTheAnswers()
    {
        var (host, game, _) = Story("OLU", "TAR", "SOA");
        MinigameResult? result = null;
        host.OnResult += r => result = r;
        foreach (char c in "OLUTARSOA")
        {
            Click(host, c);
            Run(host, 0.2f);
        }

        Run(host, 3f);

        Assert.Equal(MinigameState.Result, host.State);
        Assert.True(result!.Value.Completed);
        Assert.Equal(3, result.Value.Rating);
        Assert.Equal(new[] { "OLU", "TAR", "SOA" }, game.Puzzle!.Answers);
    }

    [Fact]
    public void TooManyWrongClicks_FailTheRun()
    {
        var (host, _, _) = Story("ESA");
        MinigameResult? result = null;
        host.OnResult += r => result = r;
        for (int i = 0; i < NoteKeyPuzzle.MaxMistakes; i++)
        {
            Click(host, i % 2 == 0 ? 'Z' : 'Y');
            Run(host, 0.1f);
        }

        Run(host, 3f);
        Assert.False(result!.Value.Completed);
    }

    [Fact]
    public void TheArcadeGame_PicksThreeDifferentRuneWords()
    {
        var game = new NoteKeyGame();
        var host = new MinigameHost(game, seed: 3);
        host.StartGame();
        var words = game.Puzzle!.Words;
        Assert.Equal(3, words.Count);
        Assert.Equal(3, words.Distinct().Count());
        Assert.All(words, w => Assert.True(SounderLexicon.IsWord(w)));
        Assert.Equal(SongDiscs.NoteKeyGame, game.Key);
        Assert.Equal(MinigameRegistry.Keys.Count - 1, MinigameRegistry.IndexOf(SongDiscs.NoteKeyGame)); // appended last
    }

    [Fact]
    public void TheHeaders_AreHitTested_AndTheLastRowHasOnlyYAndZ()
    {
        var (x, y, w, h) = NoteKeyLayout.RowHeader(4);
        Assert.True(NoteKeyLayout.HitHeader(x + 2, y + 2, out int tone, out bool isRow));
        Assert.Equal((4, true), (tone, isRow));
        var col = NoteKeyLayout.ColumnHeader(3);
        Assert.True(NoteKeyLayout.HitHeader(col.X + 2, col.Y + 2, out tone, out isRow));
        Assert.Equal((3, false), (tone, isRow));

        var noCell = NoteKeyLayout.Cell(6, 2);
        Assert.False(NoteKeyLayout.HitCell(noCell.X + 2, noCell.Y + 2, out _, out _)); // after Z there is no letter
        var z = NoteKeyLayout.Cell(6, 1);
        Assert.True(NoteKeyLayout.HitCell(z.X + 2, z.Y + 2, out int row, out int column));
        Assert.Equal('Z', SounderAlphabet.LetterOf(row, column));
        Assert.True(z.Y + z.H <= NoteKeyRun.H && z.X + z.W <= NoteKeyRun.W, "the table fits the canvas");
    }

    [Fact]
    public void PlayPhrase_SoundsEveryToneOnTheGameClock_AndRestsStaySilent()
    {
        var notes = new List<(int, int)>();
        var game = new SilentGame();
        var host = new MinigameHost(game) { NoteSink = (i, t) => notes.Add((i, t)) };
        host.StartGame();
        float length = host.Api.PlayPhrase(5, new[] { 0, SounderAlphabet.Rest, 7, 9 }, 0.25f);
        Assert.Equal(1f, length, 3);
        Assert.Empty(notes); // nothing before the clock runs
        for (int i = 0; i < 70; i++)
        {
            host.Tick(1f / 60f);
        }

        Assert.Equal(new[] { (5, 0), (5, 7) }, notes); // the rest is a silent beat, a tone past 7 is no tone
        host.Api.PlayNote(1, 3);
        Assert.Equal((1, 3), notes.Last());
        new MinigameHost(new SilentGame()).Api.PlayNote(0, 1); // no sink: dropped, no exception
    }

    private sealed class SilentGame : IMinigame
    {
        public string Key => "silent";
        public LocText Title => new("t");
        public LocText Desc => new("d");
        public LocText Hint => new("h");
        public IReadOnlyList<LocText> Help { get; } = System.Array.Empty<LocText>();
        public int Difficulty => 1;

        public MinigameController Create(MinigameApi api)
        {
            api.Canvas(8, 8);
            return new MinigameController();
        }
    }
}
