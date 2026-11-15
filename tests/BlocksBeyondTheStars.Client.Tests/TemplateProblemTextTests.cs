// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3692: the structure editor shows a template check's problem in the player's language (<see cref="TemplateProblemText"/>)
/// — the problem's locale key with its values, else the check's English sentence, so a message is never lost. And every code
/// a check can raise has its sentence in English and German (the community languages fall back to English).
/// </summary>
public sealed class TemplateProblemTextTests
{
    private static readonly Regex CodeLiteral = new("new TemplateProblem\\(\\s*\"(?<code>[a-z0-9_.]+)\"", RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Format_FillsTheLocalizedSentence_WithTheProblemsValues()
    {
        var p = new TemplateProblem("port.on_marker", "port 'door' at (2,1,1): a port sits on a wall block, not on a marker", "door", "2, 1, 1");
        string text = TemplateProblemText.Format(p, key => key == "ui.problem.port.on_marker" ? "Der Anschluss {0} bei {1} sitzt auf einem Marker" : key);
        Assert.Equal("Der Anschluss door bei 2, 1, 1 sitzt auf einem Marker", text);
    }

    [Fact]
    public void Format_FallsBackToEnglish_WithoutAKey_OrWithABrokenPlaceholder()
    {
        var p = new TemplateProblem("size.none", "it has no size");
        Assert.Equal("it has no size", TemplateProblemText.Format(p, key => key));
        Assert.Equal("it has no size", TemplateProblemText.Format(p, _ => string.Empty));
        Assert.Equal("it has no size", TemplateProblemText.Format(p, null));
        Assert.Equal("it has no size", TemplateProblemText.Format(p, _ => "Kaputt {3"));
        Assert.Equal(string.Empty, TemplateProblemText.Format(null, key => key));
    }

    [Fact]
    public void First_IsTheFirstProblem_OrNothing()
    {
        Assert.Equal(string.Empty, TemplateProblemText.First(new List<TemplateProblem>(), key => key));
        var list = new List<TemplateProblem> { new("a", "first"), new("b", "second") };
        Assert.Equal("first", TemplateProblemText.First(list, key => key));
    }

    [Fact]
    public void EveryProblemCode_HasItsSentence_InEnglishAndGerman()
    {
        string src = Path.Combine(ClientTestPaths.RepoRoot(), "src");
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in CodeLiteral.Matches(File.ReadAllText(file)))
            {
                codes.Add(m.Groups["code"].Value);
            }
        }

        Assert.True(codes.Count >= 40, $"the checks raise their problems as codes (found {codes.Count})");
        foreach (string locale in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", locale + ".json"), Encoding.UTF8));
            var missing = codes.Where(c => !table.ContainsKey(TemplateProblem.KeyPrefix + c)).ToList();
            Assert.True(missing.Count == 0, $"{locale}.json lacks the sentence of: " + string.Join(", ", missing.Select(c => TemplateProblem.KeyPrefix + c)));
        }
    }
}
