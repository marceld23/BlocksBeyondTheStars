// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2466 (synthesis §5.7): a client without <c>packs1</c> shows a line's <c>Text</c> verbatim — its text path fills neither
/// <c>{key:…}</c> tokens nor <c>{0}</c> slots. The server therefore composes such lines itself (<see cref="LineText"/>, the
/// one implementation the client's <c>VegaText</c> now delegates to) with device-neutral bracket words
/// (<c>ui.vega.act.&lt;Action&gt;</c> = "[Use]"). Content test: every VEGA and story line composes without a brace left, and
/// every action a token names has its bracket word in English and German.
/// </summary>
public sealed class LegacyLineTextTests
{
    private static readonly string[] Codes = { "en", "de" };

    /// <summary>The base table and every story pack's table of one language, merged like the content loader does.</summary>
    private static Dictionary<string, string> Table(string code)
    {
        var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")));
        foreach (string dir in Directory.GetDirectories(Path.Combine(TestPaths.DataDir(), "stories")).OrderBy(d => d, StringComparer.Ordinal))
        {
            string file = Path.Combine(dir, "locales", code + ".json");
            if (File.Exists(file))
            {
                foreach (var kv in ContentLoader.ParseLocaleTable(File.ReadAllText(file)))
                {
                    table[kv.Key] = kv.Value;
                }
            }
        }

        return table;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryNarratorLine_ComposesWithoutABraceLeft(string code)
    {
        var table = Table(code);
        string args = string.Join(LineText.ArgSeparator.ToString(), "a", "b", "c", "d");
        var bad = new List<string>();
        foreach (var (key, text) in table.Where(kv => kv.Key.StartsWith("vega.", StringComparison.Ordinal) || kv.Key.StartsWith("story.", StringComparison.Ordinal)))
        {
            foreach (string? arg in new[] { args, null })
            {
                string composed = LineText.ComposeNeutral(text, arg, k => table.TryGetValue(k, out var v) ? v : null);
                if (composed.Contains('{', StringComparison.Ordinal) || composed.Contains('}', StringComparison.Ordinal))
                {
                    bad.Add($"{code}:{key} → {composed}");
                }
            }
        }

        Assert.True(bad.Count == 0, "Lines that keep a brace when the server composes them:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void EveryTokenAction_HasItsNeutralWord_InEnglishAndGerman()
    {
        var tables = Codes.ToDictionary(c => c, Table);
        var actions = tables.Values.SelectMany(t => t.Values).SelectMany(LineText.KeyTokenActions).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(actions);
        var missing = new List<string>();
        foreach (string code in Codes)
        {
            foreach (string action in actions)
            {
                if (!tables[code].TryGetValue(LineText.NeutralActionKeyPrefix + action, out string? word))
                {
                    missing.Add($"{code}: {LineText.NeutralActionKeyPrefix}{action}");
                }
                else if (!(word.StartsWith("[", StringComparison.Ordinal) && word.EndsWith("]", StringComparison.Ordinal)))
                {
                    missing.Add($"{code}: {LineText.NeutralActionKeyPrefix}{action} = \"{word}\" is not a [bracket word]");
                }
            }
        }

        Assert.True(missing.Count == 0, "Key tokens without a device-neutral word for older clients:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void ComposeNeutral_FallsBackToTheActionName_InBrackets()
    {
        Assert.Equal("Press [Use] now", LineText.ComposeNeutral("Press {key:Interact} now", null, k => k == "ui.vega.act.Interact" ? "[Use]" : null));
        Assert.Equal("Press [Dance] now", LineText.ComposeNeutral("Press {key:Dance} now", null, _ => null));
        Assert.Equal("A wreck: Likorn. [Map]", LineText.ComposeNeutral("A wreck: {0}. {key:FlightMap}", "Likorn", k => k.EndsWith("FlightMap", StringComparison.Ordinal) ? "[Map]" : null));
        Assert.Equal("The wreck … drifts", LineText.ComposeNeutral("The wreck {0} drifts", null, _ => null));
        Assert.Equal(new[] { "Interact", "UiMenu" }, LineText.KeyTokenActions("{key:Interact} and {key:UiMenu}"));
    }
}
