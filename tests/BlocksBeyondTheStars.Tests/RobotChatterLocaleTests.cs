// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The talking robot's pools (#3394-#3396, <see cref="RobotChatter"/>): every line exists in English and German and fits
/// one page of VEGA's panel, the shuffle plays each entry once per round and never the same twice in a row, about one talk
/// in three is an exchange, and the ambient gaps stay in their bounds.
/// </summary>
public sealed class RobotChatterLocaleTests
{
    /// <summary>The robot speaks in short lines (English); German may run a quarter longer.</summary>
    private const int EnglishMax = 160;
    private const int GermanMax = 200;

    private static Dictionary<string, string> Table(string code)
    {
        string path = Path.Combine(TestPaths.DataDir(), "locales", code + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            table[entry.Name] = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() ?? string.Empty : string.Empty;
        }

        return table;
    }

    [Theory]
    [InlineData("en", EnglishMax)]
    [InlineData("de", GermanMax)]
    public void EveryChatterLine_IsTranslated_AndShort(string code, int max)
    {
        var table = Table(code);
        foreach (string key in RobotChatter.AllKeys())
        {
            Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{code}.json lacks {key}");
            Assert.True(text!.Length <= max, $"{code}: {key} has {text.Length} characters (max {max})");
            Assert.DoesNotContain("{key:", text, StringComparison.Ordinal);
            Assert.DoesNotContain("{1}", text, StringComparison.Ordinal); // the one slot is the robot's name
        }

        for (int n = 1; n <= RobotChatter.ExchangeCount; n++)
        {
            Assert.Contains("{0}", table[RobotChatter.QuestionKey(n)], StringComparison.Ordinal); // VEGA asks it by name
        }
    }

    [Fact]
    public void ThePools_HaveNoMoreLinesInTheTablesThanTheCountsSay()
    {
        var english = Table("en").Keys.Where(RobotChatter.IsChatterKey).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(RobotChatter.AllKeys().ToHashSet(StringComparer.Ordinal), english); // a new line raises its count
    }

    [Theory]
    [InlineData("Ada")]
    [InlineData("Justus")]
    [InlineData("")]
    public void TheShuffle_PlaysEachEntryOncePerRound_AndNeverTheSameTwiceInARow(string player)
    {
        int count = RobotChatter.LineCount;
        int previous = -1;
        for (int round = 0; round < 6; round++)
        {
            var seen = new HashSet<int>();
            for (int slot = 0; slot < count; slot++)
            {
                int entry = RobotChatter.ShuffledEntry(player, "line", (round * count) + slot, count);
                Assert.InRange(entry, 0, count - 1);
                Assert.True(seen.Add(entry), $"round {round} repeats entry {entry}");
                Assert.NotEqual(previous, entry);
                previous = entry;
            }
        }
    }

    [Fact]
    public void AboutOneTalkInThree_IsAnExchange()
    {
        int exchanges = Enumerable.Range(0, 3000).Count(t => RobotChatter.IsExchange("Ada", t));
        Assert.InRange(exchanges / 3000.0, 0.25, 0.42);
        Assert.Equal(RobotChatter.IsExchange("Ada", 17), RobotChatter.IsExchange("Ada", 17)); // fixed per player and talk
    }

    [Fact]
    public void TheNextTalk_NamesAnEntryOfThePoolItPicked()
    {
        for (int t = 0; t < 60; t++)
        {
            var (exchange, number) = RobotChatter.Next("Ada", t, 0);
            Assert.Equal(RobotChatter.IsExchange("Ada", t), exchange);
            Assert.InRange(number, 1, exchange ? RobotChatter.ExchangeCount : RobotChatter.LineCount);
        }
    }

    [Fact]
    public void TheAmbientGaps_StayInTheirBounds()
    {
        foreach (string player in new[] { "Ada", "Ben", "Justus", string.Empty })
        {
            Assert.InRange(RobotChatter.FirstAmbientDelay(player), RobotChatter.FirstAmbientMinSeconds, RobotChatter.FirstAmbientMaxSeconds);
            for (int n = 0; n < 20; n++)
            {
                Assert.InRange(RobotChatter.AmbientInterval(player, n), RobotChatter.AmbientMinSeconds, RobotChatter.AmbientMaxSeconds);
            }
        }
    }
}
