// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4078: the optional seed field of the new-world form. The world's name used to BE its seed (the server hashed it when
/// the client sent none); now the name is a label, an empty field rolls a random world, a number is that seed and a word
/// is hashed the way the name was — so typing an old world's name recreates exactly that old world.
/// </summary>
public sealed class WorldSeedInputTests
{
    /// <summary>A <see cref="Random"/> that hands out scripted byte fills, so the seed rules can be pinned without
    /// depending on a real generator's sequence.</summary>
    private sealed class ScriptedRandom : Random
    {
        private readonly Queue<byte> _fills;

        public ScriptedRandom(params byte[] fills) => _fills = new Queue<byte>(fills);

        public override void NextBytes(byte[] buffer)
        {
            byte fill = _fills.Count > 0 ? _fills.Dequeue() : (byte)0x5A;
            Array.Fill(buffer, fill);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyField_RollsARandomSeed(string? typed)
        => Assert.Equal(0x1111111111111111L, WorldSeedInput.Resolve(typed, new ScriptedRandom(0x11)));

    [Fact]
    public void Resolve_EmptyField_GetsADifferentWorldEveryTime()
    {
        var rng = new Random(4078);
        long first = WorldSeedInput.Resolve(string.Empty, rng);
        long second = WorldSeedInput.Resolve(string.Empty, rng);

        Assert.NotEqual(0L, first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("4711", 4711L)]
    [InlineData("-4711", -4711L)]
    [InlineData("+12", 12L)]
    [InlineData("  007  ", 7L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Resolve_AWholeNumber_IsExactlyThatSeed(string typed, long seed)
        => Assert.Equal(seed, WorldSeedInput.Resolve(typed, new ScriptedRandom()));

    [Theory]
    [InlineData("Lyxette")]
    [InlineData("Mars base")]
    [InlineData("new_world")]
    public void Resolve_AWord_IsTheHashTheServerTookOfAWorldName(string typed)
    {
        // GameServer.CreateInitialMetadata derives a nameless seed as WorldGenerator.StableHash(WorldName): the same
        // number, so an old world's name typed into the seed field makes that old world again.
        Assert.Equal(WorldGenerator.StableHash(typed), WorldSeedInput.Resolve(typed, new ScriptedRandom()));
        Assert.Equal(WorldGenerator.StableHash(typed), WorldSeedInput.Resolve("  " + typed + " ", new ScriptedRandom()));
    }

    [Fact]
    public void Resolve_IsCaseSensitive_LikeTheNameHashWas()
        => Assert.NotEqual(WorldSeedInput.Resolve("Erde", new ScriptedRandom()), WorldSeedInput.Resolve("erde", new ScriptedRandom()));

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("99999999999999999999")] // past long.MaxValue
    [InlineData("12.5")]
    public void Resolve_ZeroOrNoLong_IsHashedAsText(string typed)
    {
        // 0 would tell the server "derive the seed from the name" — the very coupling the field removes.
        long seed = WorldSeedInput.Resolve(typed, new ScriptedRandom());
        Assert.NotEqual(0L, seed);
        Assert.Equal(WorldGenerator.StableHash(typed), seed);
    }

    [Fact]
    public void Resolve_WithoutARandom_Throws()
        => Assert.Throws<ArgumentNullException>(() => WorldSeedInput.Resolve("1", null!));

    [Fact]
    public void RandomSeed_IsNeverZeroOrNegative()
    {
        // All-zero bytes would mean "derive from the name"; all-0xFF would be -1 before masking.
        Assert.Equal(long.MaxValue, WorldSeedInput.RandomSeed(new ScriptedRandom(0x00, 0xFF)));
        Assert.Equal(long.MaxValue, WorldSeedInput.RandomSeed(new ScriptedRandom(0xFF)));
    }

    [Fact]
    public void RandomSeed_AStuckGeneratorStillYieldsAUsableSeed()
        => Assert.Equal(1L, WorldSeedInput.RandomSeed(new ScriptedRandom(new byte[18])));

    [Fact]
    public void Format_WritesAnAsciiMinusInEveryLanguage()
    {
        // Swedish writes the minus as U+2212 — the bundled server's --seed and the copied seed must not.
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("-4711", WorldSeedInput.Format(-4711L));
            Assert.Equal(-4711L, WorldSeedInput.Resolve(WorldSeedInput.Format(-4711L), new ScriptedRandom()));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
