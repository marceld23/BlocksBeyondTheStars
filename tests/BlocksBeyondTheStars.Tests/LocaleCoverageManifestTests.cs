// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1522: the content loader parses only English + the active locale at start and the other tables on first
/// use; the language picker reads <c>data/locale_coverage.json</c> (scripts/locale-coverage.py) instead of
/// parsing twelve tables for one click. These tests pin both halves: every locale still localizes exactly as
/// before (lazily), and the manifest cannot drift from the tables without failing CI.
/// </summary>
public class LocaleCoverageManifestTests
{
    private const double PickerBar = 0.45; // GameContent.SelectableLocales default

    private static string ManifestPath() => Path.Combine(TestPaths.DataDir(), "locale_coverage.json");

    private static Dictionary<string, double> ReadManifest()
        => JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(ManifestPath()))
           ?? new Dictionary<string, double>();

    [Fact]
    public void Manifest_ListsEveryLocaleFile()
    {
        Assert.True(File.Exists(ManifestPath()), "data/locale_coverage.json is missing — run scripts/locale-coverage.py");
        var manifest = ReadManifest();
        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            string code = Path.GetFileNameWithoutExtension(file);
            Assert.True(manifest.ContainsKey(code), $"locale_coverage.json has no entry for '{code}' — run scripts/locale-coverage.py");
        }
    }

    [Fact]
    public void Manifest_AgreesWithTheMeasuredCoverage()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var manifest = ReadManifest();
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            if (!manifest.TryGetValue(locale.Code(), out var listed))
            {
                continue; // Manifest_ListsEveryLocaleFile reports the gap
            }

            double measured = content.LocaleCoverage(locale);
            Assert.True(Math.Abs(measured - listed) <= 0.02,
                $"locale_coverage.json says {listed:P1} for '{locale.Code()}' but the tables measure {measured:P1} — run scripts/locale-coverage.py");
            Assert.True(measured >= PickerBar == listed >= PickerBar,
                $"'{locale.Code()}' crossed the picker bar ({measured:P1} vs manifest {listed:P1}) — run scripts/locale-coverage.py");
        }
    }

    [Fact]
    public void Loader_ParsesOnlyEnglishAndTheEagerLocale_TheRestOnFirstUse()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), eagerLocale: GameLocale.German);
        Assert.Equal(2, content.LoadedLocaleCount);

        var italian = content.CreateLocalizer(GameLocale.Italian);
        Assert.Equal(3, content.LoadedLocaleCount);
        Assert.False(string.IsNullOrWhiteSpace(italian.Get("ui.menu.play")));

        // Every locale still localizes through the lazy path — the same guarantee the eager loader gave.
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            Assert.True(content.HasLocale(locale), $"{locale} has no table");
            Assert.False(string.IsNullOrWhiteSpace(content.CreateLocalizer(locale).Get("ui.menu.play")), $"{locale} lost ui.menu.play");
        }

        Assert.Equal(Enum.GetValues<GameLocale>().Length, content.LoadedLocaleCount);
    }

    [Fact]
    public void Loader_WithoutAnEagerLocale_ParsesEnglishOnly()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        Assert.Equal(1, content.LoadedLocaleCount);
        Assert.Equal("Play", content.CreateLocalizer(GameLocale.English).Get("ui.menu.play"));
        Assert.Equal(1, content.LoadedLocaleCount);
    }

    [Fact]
    public void SelectableLocales_AnswersFromTheManifest_WithoutParsingTheOtherTables()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var offered = content.SelectableLocales();
        Assert.Contains(GameLocale.English, offered);
        Assert.Contains(GameLocale.German, offered);
        Assert.Equal(1, content.LoadedLocaleCount);

        // The manifest and the measured tables agree on who is offered (the picker's whole contract).
        var manifest = ReadManifest();
        foreach (GameLocale locale in Enum.GetValues<GameLocale>())
        {
            bool expected = locale is GameLocale.English or GameLocale.German
                || (manifest.TryGetValue(locale.Code(), out var c) && c >= PickerBar);
            Assert.Equal(expected, offered.Contains(locale));
        }
    }
}
