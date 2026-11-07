// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Sounders' note alphabet (#3352) and the rune words (#3345): the table is canon (printed on the school club's
/// worksheet), encoding round-trips, and a rune's word is fixed by seed and position.
/// </summary>
public sealed class SounderAlphabetTests
{
    [Theory]
    [InlineData('A', 0, 0)]
    [InlineData('D', 0, 3)]
    [InlineData('E', 1, 0)]
    [InlineData('O', 3, 2)]
    [InlineData('S', 4, 2)]
    [InlineData('Z', 6, 1)]
    public void TheTable_IsCanon(char letter, int row, int column)
    {
        Assert.Equal((row, column), SounderAlphabet.TonesOf(letter));
        Assert.Equal(letter, SounderAlphabet.LetterOf(row, column));
    }

    [Fact]
    public void EveryLetter_UsesTheFourLowestTonesForTheColumn_AndNeverTheTopC()
    {
        for (char c = 'A'; c <= 'Z'; c++)
        {
            (int row, int column) = SounderAlphabet.TonesOf(c)!.Value;
            Assert.InRange(column, 0, SounderAlphabet.Columns - 1);
            Assert.InRange(row, 0, SounderAlphabet.ToneCount - 2);
        }

        Assert.Null(SounderAlphabet.LetterOf(6, 2)); // past Z
        Assert.Null(SounderAlphabet.LetterOf(0, 4)); // a column tone above F
    }

    [Fact]
    public void Encode_PutsOneRestBetweenLetters_AndTwoBetweenWords()
    {
        int r = SounderAlphabet.Rest;
        Assert.Equal(new[] { 3, 2, r, 4, 1 }, SounderAlphabet.Encode("OR"));
        Assert.Equal(new[] { 0, 0, r, r, 0, 1 }, SounderAlphabet.Encode("a b"));
        Assert.Empty(SounderAlphabet.Encode("!?"));
    }

    [Theory]
    [InlineData("ORNEMIS")]
    [InlineData("SOA NIRI")]
    [InlineData("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG")]
    public void Decode_IsTheInverseOfEncode(string text)
    {
        Assert.Equal(text, SounderAlphabet.Decode(SounderAlphabet.Encode(text)));
    }

    [Fact]
    public void TheSongOfReturn_IsTheSystemName_SplitIntoTheEchoesParts()
    {
        Assert.Equal(SoundersIds.SongOfReturnWord, string.Concat(SoundersIds.SongOfReturnParts));
        Assert.Equal(SoundersIds.Echoes.Length, SoundersIds.SongOfReturnParts.Length);
        Assert.Equal(14, SounderAlphabet.TonesOnly(SounderAlphabet.Encode(SoundersIds.SongOfReturnWord)).Length);
    }

    [Fact]
    public void ARune_SingsTheSameWord_ForTheSameSeedAndPosition()
    {
        string first = SounderLexicon.WordAt(12345, 10, 64, -7);
        Assert.Equal(first, SounderLexicon.WordAt(12345, 10, 64, -7));
        Assert.True(SounderLexicon.IsWord(first));

        // Neighbouring stones vary: a monument does not sing one word on every stone.
        var words = Enumerable.Range(0, 40).Select(i => SounderLexicon.WordAt(12345, i, 64, 0)).Distinct().Count();
        Assert.True(words > 4, $"only {words} different words on 40 stones");
    }

    [Fact]
    public void EveryRuneWord_IsShortAndPlain()
    {
        foreach (string word in SounderLexicon.Words)
        {
            Assert.InRange(word.Length, 3, 4);
            Assert.All(word, c => Assert.InRange(c, 'A', 'Z'));
        }

        Assert.Equal(SounderLexicon.Words.Length, SounderLexicon.Words.Distinct().Count());
    }
}
