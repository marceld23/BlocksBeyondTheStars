// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
using System.Text.Json;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2829: every hide the server can put on the wire — the roster generator's hide arrays and the authored records'
/// <c>hide</c> in <c>data/creatures.json</c> — has a bundled tile the client can load
/// (<c>client/Assets/Resources/textures/creature_&lt;name&gt;.bytes</c>, one raw 64 × 64 RGBA32 tile). The sky giant and the
/// leviathan rolled <c>slick</c> for months while no such tile existed, so they silently fell back to a hashed pick.
/// </summary>
public sealed class CreatureHideTilesTests
{
    private const int TileBytes = 64 * 64 * 4;

    /// <summary>Every static <c>string[]</c> of the generator named <c>…Hides</c> (found by reflection, so a new array is
    /// covered without touching this test) plus the authored hides.</summary>
    private static SortedSet<string> ServerHides()
    {
        var arrays = typeof(CreatureGenerator)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(string[]) && f.Name.EndsWith("Hides", StringComparison.Ordinal))
            .ToList();
        Assert.True(arrays.Count >= 7, "the generator's hide arrays were not found");
        var hides = new SortedSet<string>(arrays.SelectMany(f => (string[])f.GetValue(null)!), StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "creatures.json")));
        foreach (var record in doc.RootElement.EnumerateArray())
        {
            if (record.TryGetProperty("hide", out var hide) && hide.GetString() is { Length: > 0 } name)
            {
                hides.Add(name);
            }
        }

        return hides;
    }

    [Fact]
    public void EveryRollableHide_HasABundledTile()
    {
        string dir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        var missing = new List<string>();
        foreach (string hide in ServerHides())
        {
            var file = new FileInfo(Path.Combine(dir, "creature_" + hide + ".bytes"));
            if (!file.Exists || file.Length != TileBytes)
            {
                missing.Add(hide);
            }
        }

        Assert.True(missing.Count == 0,
            "hides the server can roll without a bundled 64x64 RGBA tile creature_<name>.bytes: " + string.Join(", ", missing));
    }
}
