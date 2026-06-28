// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Minigames;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Verifies the native minigame registry is self-consistent and, when the repo is present, stays in sync
/// with <c>data/minigames/catalog.json</c> — whose order is authoritative for the data-cube → game mapping.</summary>
public sealed class MinigameRegistryTests
{
    [Fact]
    public void EveryKeyCreatesItsGame_AndLookupsAreConsistent()
    {
        Assert.Equal(20, MinigameRegistry.Count);
        var seen = new HashSet<string>();
        for (int i = 0; i < MinigameRegistry.Keys.Count; i++)
        {
            string key = MinigameRegistry.Keys[i];
            Assert.True(seen.Add(key), $"duplicate key {key}");
            Assert.Equal(i, MinigameRegistry.IndexOf(key));
            Assert.True(MinigameRegistry.Has(key));

            var game = MinigameRegistry.Create(key);
            Assert.NotNull(game);
            Assert.Equal(key, game!.Key);
            Assert.False(game.Title.IsEmpty);
        }

        Assert.Null(MinigameRegistry.Create("nope"));
        Assert.Equal(-1, MinigameRegistry.IndexOf("nope"));
    }

    [Fact]
    public void ForSeed_WrapsAndMatchesIndex()
    {
        Assert.Equal(MinigameRegistry.Keys[0], MinigameRegistry.ForSeed(0).Key);
        Assert.Equal(MinigameRegistry.Keys[3], MinigameRegistry.ForSeed(3).Key);
        Assert.Equal(MinigameRegistry.Keys[3], MinigameRegistry.ForSeed(3 + MinigameRegistry.Count).Key);
        Assert.Equal(MinigameRegistry.Keys[MinigameRegistry.Count - 1], MinigameRegistry.ForSeed(-1).Key);
    }

    [Fact]
    public void RegistryOrderMatchesCatalogJson_WhenRepoPresent()
    {
        string? catalog = FindCatalog();
        if (catalog == null)
        {
            return; // running outside the repo tree (e.g. packaged) — the consistency test above still applies
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(catalog));
        var jsonKeys = new List<string>();
        foreach (var g in doc.RootElement.GetProperty("games").EnumerateArray())
        {
            jsonKeys.Add(g.GetProperty("key").GetString()!);
        }

        Assert.Equal(jsonKeys, MinigameRegistry.Keys);
    }

    private static string? FindCatalog()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        for (int up = 0; up < 12 && dir != null; up++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data", "minigames", "catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
