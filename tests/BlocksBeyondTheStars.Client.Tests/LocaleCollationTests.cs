// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2566: the crafting list, the Blueprints tab and the Sandbox catalog sort A–Z in the ACTIVE GAME LANGUAGE (not the
/// PC's culture) — "Äpfel" between "Apfel" and "Birne" in German, plain Latin order in English, an unknown code never
/// throws, and the platform-independent folding fallback (for a player without culture collation, e.g. IL2CPP/WebGL)
/// sorts the same way.
/// </summary>
public sealed class LocaleCollationTests
{
    private static List<string> Sorted(StringComparer comparer, params string[] words)
        => words.OrderBy(w => w, comparer).ToList();

    [Fact]
    public void German_PutsUmlautsBesideTheirBaseLetter()
    {
        var sorted = Sorted(LocaleCollation.Comparer("de"), "Birne", "Zange", "Äpfel", "Apfel", "Öl", "Ofen", "Übung", "Uhr");

        Assert.Equal(new[] { "Apfel", "Äpfel", "Birne", "Ofen", "Öl", "Übung", "Uhr", "Zange" }, sorted);
    }

    [Fact]
    public void English_SortsLatinWordsAlphabetically_IgnoringCase()
    {
        var sorted = Sorted(LocaleCollation.Comparer("en"), "drill", "Bed", "anvil", "Cable", "zinc ingot", "Campfire");

        Assert.Equal(new[] { "anvil", "Bed", "Cable", "Campfire", "drill", "zinc ingot" }, sorted);
    }

    [Fact]
    public void GameLocaleOverload_MatchesTheCode()
    {
        Assert.Same(LocaleCollation.Comparer("de"), LocaleCollation.Comparer(GameLocale.German));
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../de")]
    [InlineData("de-\u0000")]
    public void UnknownCode_FallsBackWithoutThrowing(string? code)
    {
        var comparer = LocaleCollation.Comparer(code);

        Assert.NotNull(comparer);
        Assert.Equal(new[] { "Apfel", "Äpfel", "Birne", "Zange" }, Sorted(comparer, "Zange", "Birne", "Äpfel", "Apfel"));
    }

    [Fact]
    public void EveryGameLanguage_GetsAComparerThatSortsLatinWords()
    {
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            var comparer = LocaleCollation.Comparer(locale);
            Assert.True(comparer.Compare("apple", "Banana") < 0, $"{locale.Code()}: apple < Banana");
            Assert.Equal(0, comparer.Compare("Cable", "cable"));
        }
    }

    [Fact]
    public void Folding_SortsLikeAGermanCollation()
    {
        var sorted = Sorted(LocaleCollation.Folding, "Birne", "Zange", "Äpfel", "Apfel", "Öl", "Ofen", "Straße", "Strand", "Übung", "Uhr");

        Assert.Equal(new[] { "Apfel", "Äpfel", "Birne", "Ofen", "Öl", "Strand", "Straße", "Übung", "Uhr", "Zange" }, sorted);
    }

    [Fact]
    public void Folding_KeepsAccentedLettersOfOtherLanguagesBesideTheirBase()
    {
        // Polish, French, Spanish, Turkish — a code-point order would put every one of these behind "Z".
        Assert.Equal(new[] { "Cegła", "Ćma", "Dom" }, Sorted(LocaleCollation.Folding, "Dom", "Ćma", "Cegła"));
        Assert.Equal(new[] { "école", "étoile", "fer" }, Sorted(LocaleCollation.Folding, "fer", "étoile", "école"));
        Assert.Equal(new[] { "niño", "nube", "oro" }, Sorted(LocaleCollation.Folding, "oro", "nube", "niño"));
        Assert.Equal(new[] { "şeker", "taş" }, Sorted(LocaleCollation.Folding, "taş", "şeker"));
    }

    [Fact]
    public void Folding_KeepsCyrillicYoBesideYe()
    {
        // ё (U+0451) comes after я (U+044F) in code points; the fallback reads it as е, like a Russian dictionary.
        Assert.Equal(new[] { "ёж", "ель", "жук", "яблоко" }, Sorted(LocaleCollation.Folding, "яблоко", "ель", "жук", "ёж"));
    }

    [Fact]
    public void Folding_IgnoresCase_AndIsATotalOrder()
    {
        Assert.Equal(0, LocaleCollation.Folding.Compare("Kabel", "kabel"));
        Assert.True(LocaleCollation.Folding.Equals("Kabel", "KABEL"));
        Assert.Equal(LocaleCollation.Folding.GetHashCode("Kabel"), LocaleCollation.Folding.GetHashCode("KABEL"));
        Assert.True(LocaleCollation.Folding.Compare("Apfel", "Äpfel") < 0, "the plain letter goes first on a tie");
        Assert.True(LocaleCollation.Folding.Compare("Äpfel", "Apfel") > 0);
        Assert.True(LocaleCollation.Folding.Compare(null, "a") < 0);
    }
}
