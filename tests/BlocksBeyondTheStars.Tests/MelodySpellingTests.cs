// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3426 — "You cannot play the name Ornemis, only the E is among the notes": a kid read "play ORNEMIS" as the note names
/// O-R-N-E-M-I-S. The melody bar now spells a word letter by letter with each letter's TWO notes (<see cref="MelodySpelling"/>):
/// the groups are exactly the puzzle's expected tones (the Song of Return's own, the Bridge Song's counter-voice), named in
/// the player's language, the letter the player is at marked; the word and its tones travel on <see cref="MelodyPuzzleState"/>.
/// </summary>
public sealed class MelodySpellingTests
{
    private static readonly string[] English = "C D E F G A B C'".Split(' ');
    private static readonly string[] German = "C D E F G A H C'".Split(' ');

    [Fact]
    public void Ornemis_IsSevenLetters_OfTwoNotesEach_WhoseNotesAreTheEncodedName()
    {
        int[] tones = MelodySpelling.PlayedTones(SoundersIds.SongOfReturnWord, MelodyPuzzleMode.Compose, 4);

        Assert.Equal("ORNEMIS", MelodySpelling.Letters(SoundersIds.SongOfReturnWord));
        Assert.Equal(14, tones.Length);
        Assert.Equal(SounderAlphabet.TonesOnly(SounderAlphabet.Encode("ORNEMIS")), tones);
        string line = MelodySpelling.Line("ORNEMIS", tones, English);
        Assert.Equal("O = F E · R = G D · N = F D · E = D C · M = F C · I = E C · S = G E", line);
        Assert.Equal(7, line.Split(MelodySpelling.Separator).Length);
    }

    [Fact]
    public void TheSpelledTones_AreWhatThePuzzleExpects_ForTheSongOfReturnAndTheBridgeSong()
    {
        foreach (var mode in new[] { MelodyPuzzleMode.Compose, MelodyPuzzleMode.Harmony })
        {
            var spec = new MelodyPuzzleSpec
            {
                Id = "spelled",
                Mode = mode,
                Target = SounderAlphabet.Encode(SoundersIds.SongOfReturnWord),
                Word = SoundersIds.SongOfReturnWord,
            };
            Assert.Equal(new MelodyPuzzle(spec).Expected, MelodySpelling.PlayedTones(spec));
            Assert.Equal(spec.Word, spec.Clone().Word);
        }

        // The Bridge Song's second voice, a fifth up: O is F E in the table, played as C' and B (German H).
        var bridge = MelodySpelling.PlayedTones("O", MelodyPuzzleMode.Harmony, 4);
        Assert.Equal("O = C' H", MelodySpelling.Line("O", bridge, German));
        Assert.Empty(MelodySpelling.PlayedTones("ORNEMIS", MelodyPuzzleMode.CallAndResponse, 4));
        Assert.Empty(MelodySpelling.PlayedTones(string.Empty, MelodyPuzzleMode.Compose, 4));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 2)]
    [InlineData(13, 6)]
    [InlineData(14, 6)]
    public void TheCurrentLetter_IsTheEnteredNotesHalved_NeverPastTheLast(int entered, int letter)
        => Assert.Equal(letter, MelodySpelling.CurrentLetter(entered, 7));

    [Fact]
    public void TheLetterThePlayerIsAt_IsMarked_AndNothingSpellsNothing()
    {
        int[] tones = MelodySpelling.PlayedTones("ORNEMIS", MelodyPuzzleMode.Compose, 4);
        string line = MelodySpelling.Line("ORNEMIS", tones, German, MelodySpelling.CurrentLetter(3, 7), "[", "]");
        Assert.StartsWith("O = F E · [R = G D] · N = F D", line, System.StringComparison.Ordinal);
        Assert.Single(line.Where(c => c == '['));

        Assert.Equal(-1, MelodySpelling.CurrentLetter(4, 0));
        Assert.Equal(string.Empty, MelodySpelling.Line(string.Empty, tones, English));
        Assert.Equal(string.Empty, MelodySpelling.Line("ORNEMIS", null, English));
        Assert.Equal("O = F E", MelodySpelling.Line("O", new[] { 3, 2 }, new[] { "too", "few" })); // a broken name list falls back
    }

    [Fact]
    public void TheWordAndItsTones_TravelOnTheMelodyBarState()
    {
        var back = Assert.IsType<MelodyPuzzleState>(NetCodec.Decode(NetCodec.Encode(new MelodyPuzzleState
        {
            PuzzleId = SoundersIds.PuzzleSongOfReturn,
            Open = true,
            Word = "ORNEMIS",
            WordTones = new[] { 3, 2, 4, 1 },
        })));

        Assert.Equal("ORNEMIS", back.Word);
        Assert.Equal(new[] { 3, 2, 4, 1 }, back.WordTones);
        Assert.Equal(string.Empty, new MelodyPuzzleState().Word); // an older server sends none: no spelling line
        Assert.Empty(new MelodyPuzzleState().WordTones);
    }
}
