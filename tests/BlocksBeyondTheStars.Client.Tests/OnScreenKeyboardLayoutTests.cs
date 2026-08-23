// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The gamepad on-screen keyboard's model (#1211). The Unity side is buttons; everything that can actually
/// be WRONG — which key produces what, where the character limit bites, what backspace does to an empty
/// field — lives here and is covered without a pad, a canvas or a player loop.
/// </summary>
public sealed class OnScreenKeyboardLayoutTests
{
    [Fact]
    public void LetterPage_CoversTheAlphabet_TheDigits_AndTheGermanCharacters()
    {
        string all = string.Concat(OnScreenKeyboardLayout.Rows(KeyboardPage.Letters, shift: false));

        for (char c = 'a'; c <= 'z'; c++)
        {
            Assert.Contains(c, all);
        }

        for (char c = '0'; c <= '9'; c++)
        {
            Assert.Contains(c, all);
        }

        // A player name or a world name is typed here, and this game ships in German too.
        Assert.Contains('ä', all);
        Assert.Contains('ö', all);
        Assert.Contains('ü', all);
        Assert.Contains('ß', all);
    }

    [Fact]
    public void Shift_UppercasesTheLetters_ButLeavesTheSharpSAlone()
    {
        string shifted = string.Concat(OnScreenKeyboardLayout.Rows(KeyboardPage.Letters, shift: true));

        Assert.Contains('Q', shifted);
        Assert.Contains('Ä', shifted);
        Assert.DoesNotContain('q', shifted);

        // Culture-aware casing turns ß into "SS" — one key would then produce TWO characters and quietly
        // blow past the character limit. The invariant per-character rule leaves it as it is.
        Assert.Contains('ß', shifted);
        Assert.Equal(
            string.Concat(OnScreenKeyboardLayout.Rows(KeyboardPage.Letters, shift: false)).Length,
            shifted.Length);
    }

    [Fact]
    public void SymbolPage_HasNoAngleBrackets()
    {
        // The chat log parses uGUI rich text (chat itself is neutralised on the way in — see ChatMarkup),
        // but a beacon label or a world name is not, and neither needs "<" or ">".
        string symbols = string.Concat(OnScreenKeyboardLayout.Rows(KeyboardPage.Symbols, shift: false));

        Assert.DoesNotContain('<', symbols);
        Assert.DoesNotContain('>', symbols);
        Assert.Contains('@', symbols); // …but a join address must still be typeable
        Assert.Contains('.', symbols);
        Assert.Contains(':', symbols);
    }

    [Fact]
    public void SymbolPage_IgnoresShift()
    {
        Assert.Equal(
            OnScreenKeyboardLayout.Rows(KeyboardPage.Symbols, shift: false),
            OnScreenKeyboardLayout.Rows(KeyboardPage.Symbols, shift: true));
    }

    [Fact]
    public void Rows_AreACopy_SoACallerCannotEditTheLayout()
    {
        var rows = OnScreenKeyboardLayout.Rows(KeyboardPage.Letters, shift: false);
        rows[0] = "tampered";

        Assert.NotEqual("tampered", OnScreenKeyboardLayout.Rows(KeyboardPage.Letters, shift: false)[0]);
    }

    [Fact]
    public void Apply_AppendsLiteralKeys_AndSpace()
    {
        string text = OnScreenKeyboardLayout.Apply(string.Empty, "h", 0);
        text = OnScreenKeyboardLayout.Apply(text, "i", 0);
        text = OnScreenKeyboardLayout.Apply(text, OnScreenKeyboardLayout.Space, 0);
        text = OnScreenKeyboardLayout.Apply(text, "!", 0);

        Assert.Equal("hi !", text);
    }

    [Fact]
    public void Apply_Backspace_RemovesOneCharacter_AndIsSafeOnAnEmptyField()
    {
        Assert.Equal("ab", OnScreenKeyboardLayout.Apply("abc", OnScreenKeyboardLayout.Backspace, 0));
        Assert.Equal(string.Empty, OnScreenKeyboardLayout.Apply(string.Empty, OnScreenKeyboardLayout.Backspace, 0));
        Assert.Equal(string.Empty, OnScreenKeyboardLayout.Apply(null, OnScreenKeyboardLayout.Backspace, 0));
    }

    [Fact]
    public void Apply_RespectsTheCharacterLimit_AndBackspaceStillWorksAtIt()
    {
        string full = OnScreenKeyboardLayout.Apply("abcd", "e", 4);
        Assert.Equal("abcd", full); // dropped silently, exactly like a uGUI character limit

        Assert.Equal("abc", OnScreenKeyboardLayout.Apply(full, OnScreenKeyboardLayout.Backspace, 4));
        Assert.Equal("abcde", OnScreenKeyboardLayout.Apply("abcd", "e", 0)); // 0 = no limit
    }

    [Fact]
    public void Apply_LeavesTheTextAlone_ForTheKeysThatOnlyChangeTheKeyboard()
    {
        foreach (string command in new[]
                 {
                     OnScreenKeyboardLayout.Shift, OnScreenKeyboardLayout.Page,
                     OnScreenKeyboardLayout.Done, OnScreenKeyboardLayout.Cancel,
                 })
        {
            Assert.True(OnScreenKeyboardLayout.IsCommand(command));
            Assert.Equal("hello", OnScreenKeyboardLayout.Apply("hello", command, 0));
        }

        Assert.False(OnScreenKeyboardLayout.IsCommand("a"));
        Assert.True(OnScreenKeyboardLayout.IsCommand(OnScreenKeyboardLayout.Space));
    }
}
