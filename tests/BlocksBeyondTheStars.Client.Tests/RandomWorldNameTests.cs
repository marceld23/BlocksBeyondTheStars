// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// "Random world" in the save picker (#2522): a tester typed "Lyxette_2", pressed the button and found a
/// <c>world_&lt;number&gt;</c> in her list. A typed name must win; only an empty, untouched or taken name keeps
/// the old <c>world_&lt;n&gt;</c> behaviour.
/// </summary>
public sealed class RandomWorldNameTests
{
    private const string Default = RandomWorldName.DefaultName;

    /// <summary>A <see cref="Random"/> that hands out scripted numbers and byte fills, so the reroll and
    /// seed rules can be pinned without depending on a real generator's sequence.</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly Queue<int> _numbers;
        private readonly Queue<byte> _fills;

        public ScriptedRandom(IEnumerable<int>? numbers = null, IEnumerable<byte>? fills = null)
        {
            _numbers = new Queue<int>(numbers ?? Array.Empty<int>());
            _fills = new Queue<byte>(fills ?? Array.Empty<byte>());
        }

        public override int Next(int minValue, int maxValue) => _numbers.Count > 0 ? _numbers.Dequeue() : minValue;

        public override void NextBytes(byte[] buffer)
        {
            byte fill = _fills.Count > 0 ? _fills.Dequeue() : (byte)0x5A;
            Array.Fill(buffer, fill);
        }
    }

    [Fact]
    public void Resolve_TypedName_IsUsedWithARandomSeed()
    {
        var pick = RandomWorldName.Resolve("Lyxette_2", Default, new[] { "Lyxette" }, new ScriptedRandom(fills: new byte[] { 0x11 }));

        Assert.True(pick.UsesTypedName);
        Assert.Equal("Lyxette_2", pick.Name);
        Assert.Equal(0x1111111111111111L, pick.Seed);
    }

    [Fact]
    public void Resolve_TypedName_GetsADifferentSeedOnEveryClick()
    {
        var rng = new Random(2522);
        var first = RandomWorldName.Resolve("Mars base", Default, null, rng);
        var second = RandomWorldName.Resolve("Mars base", Default, null, rng);

        Assert.Equal("Mars base", first.Name);
        Assert.NotEqual(0L, first.Seed);
        Assert.NotEqual(first.Seed, second.Seed);
    }

    [Theory]
    [InlineData(Default)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Resolve_EmptyOrUntouchedName_KeepsTheWorldNumberFallback(string? typed)
    {
        var pick = RandomWorldName.Resolve(typed, Default, Array.Empty<string>(), new ScriptedRandom(new[] { 4711 }));

        Assert.False(pick.UsesTypedName);
        Assert.Equal("world_4711", pick.Name);
        Assert.Equal(0L, pick.Seed); // the server derives it from the (already random) name, as before
    }

    [Fact]
    public void Resolve_TakenName_FallsBackInsteadOfResumingThatSave()
    {
        // Case-insensitive like the save folders: "lyxette" IS the existing "Lyxette".
        var pick = RandomWorldName.Resolve("lyxette", Default, new[] { "Lyxette" }, new ScriptedRandom(new[] { 1234 }));

        Assert.False(pick.UsesTypedName);
        Assert.Equal("world_1234", pick.Name);
    }

    [Fact]
    public void Resolve_Fallback_RerollsANumberThatNamesAnExistingSave()
    {
        var pick = RandomWorldName.Resolve(Default, Default, new[] { "world_1000", "WORLD_2000" },
            new ScriptedRandom(new[] { 1000, 2000, 3000 }));

        Assert.Equal("world_3000", pick.Name);
    }

    [Fact]
    public void Resolve_EditedButSimilarToDefault_CountsAsTyped()
    {
        // Only the untouched default falls back — "new_world 2" is a name the player chose.
        var pick = RandomWorldName.Resolve("new_world 2", Default, null, new ScriptedRandom(fills: new byte[] { 0x22 }));

        Assert.True(pick.UsesTypedName);
        Assert.Equal("new_world 2", pick.Name);
    }

    [Fact]
    public void RandomSeed_IsNeverZeroOrNegative()
    {
        // All-zero bytes would mean "derive from the name"; all-0xFF would be -1 before masking.
        long fromZero = RandomWorldName.RandomSeed(new ScriptedRandom(fills: new byte[] { 0x00, 0xFF }));
        long fromNegative = RandomWorldName.RandomSeed(new ScriptedRandom(fills: new byte[] { 0xFF }));

        Assert.Equal(long.MaxValue, fromZero);
        Assert.Equal(long.MaxValue, fromNegative);
    }

    [Fact]
    public void RandomSeed_AStuckGeneratorStillYieldsAUsableSeed()
    {
        var alwaysZero = new ScriptedRandom(fills: new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Equal(1L, RandomWorldName.RandomSeed(alwaysZero));
    }
}
