// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2513: German texts must spell umlauts and ß as such. Nine strings once shipped as "Loeschen und neu
/// starten", "Wie heisst du?" or "beschaedigt" — players saw them in the browser build's dialogs. A plain
/// "no ae/oe/ue" rule would flag real German (Feuer, neue, Sauerstoff), so this guard looks only for a short
/// list of word stems that exist in German exclusively with an umlaut or ß.
/// </summary>
public sealed class GermanLocaleSpellingTests
{
    /// <summary>Stems whose ASCII spelling never occurs in correct German: löschen, später, prüfen, möglich,
    /// beschädigt, zurück, Gerät, heißt, können, wählen, wäre, nächste, endgültig, anfängt, für, über, größer,
    /// während, müssen, möchte, öffnen, schließen, grün, plötzlich, natürlich, wünschen, schön, häufig.</summary>
    private static readonly Regex AsciiUmlautStem = new(
        @"\b\w*(loesch|spaet|pruef|moeglich|beschaedig|zurueck|geraet|heisst|koenn|waehl|waere|naechst|endgueltig|anfaeng|fuer|ueber|groess|waehrend|muess|moecht|oeffn|schliess|gruen|ploetz|natuerl|wuensch|schoen|haeuf)\w*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public static IEnumerable<object[]> GermanTables()
    {
        string root = TestPaths.RepoRoot();
        yield return new object[] { Path.Combine("data", "locales", "de.json") };
        yield return new object[] { Path.Combine("data", "wiki", "locales", "de.json") };
        yield return new object[] { Path.Combine("src", "BlocksBeyondTheStars.WorldHost", "Locales", "de.json") };
        foreach (string pack in Directory.GetDirectories(Path.Combine(root, "data", "stories")))
        {
            string table = Path.Combine(pack, "locales", "de.json");
            if (File.Exists(table))
            {
                yield return new object[] { Path.GetRelativePath(root, table) };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GermanTables))]
    public void GermanTable_SpellsUmlautsAsUmlauts(string relativePath)
    {
        string path = Path.Combine(TestPaths.RepoRoot(), relativePath);
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>();

        var offenders = new List<string>();
        foreach (var (key, value) in table)
        {
            foreach (Match match in AsciiUmlautStem.Matches(value))
            {
                offenders.Add($"{key}: \"{match.Value}\"");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{relativePath} spells umlauts/ß as ASCII (write ä/ö/ü/ß):\n" + string.Join("\n", offenders));
    }
}
