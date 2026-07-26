// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text.Json;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Reads the repository's locale tables (<c>data/locales/{en,de}.json</c>) so tests can assert that a
/// feature's keys exist in BOTH languages. The game is bilingual by rule, and a missing key renders as
/// the literal "[some.key]" in game rather than failing loudly — so it needs a test to catch it.
/// </summary>
public static class TestLocales
{
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new();

    /// <summary>The locale table for a language code ("en" / "de"), keyed by locale key.</summary>
    public static Dictionary<string, string> Load(string language)
    {
        if (Cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        string path = Path.Combine(TestPaths.DataDir(), "locales", language + ".json");
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>();
        Cache[language] = table;
        return table;
    }
}
