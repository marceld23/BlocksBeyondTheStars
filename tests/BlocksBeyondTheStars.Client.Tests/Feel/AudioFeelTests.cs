// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Client.Music;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>
/// The audio rules of the feel pass: takes of the most repeated sounds rotate without a repeat, a placed block
/// sounds like its material, and the on-foot tension music neither flickers nor overstays.
/// </summary>
public sealed class AudioFeelTests
{
    private const float Frame = 1f / 60f;

    private static string AudioDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "client", "Assets", "Resources", "audio")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir != null, "client/Assets/Resources/audio not found above the test output folder");
        return Path.Combine(dir!.FullName, "client", "Assets", "Resources", "audio");
    }

    // ---------------- ClipTakes ----------------

    [Fact]
    public void Takes_AreFoundByFileName_UpToTheFirstGap()
    {
        var files = new HashSet<string> { "mine_stone", "mine_stone_2", "mine_stone_3", "mine_stone_5", "jump" };

        Assert.Equal(new[] { "mine_stone", "mine_stone_2", "mine_stone_3" }, ClipTakes.Find("mine_stone", files.Contains));
        Assert.Equal(new[] { "jump" }, ClipTakes.Find("jump", files.Contains));
        Assert.Empty(ClipTakes.Find("place_wood", files.Contains)); // no plain clip, no family
    }

    [Fact]
    public void TheNextTake_IsNeverTheOneJustPlayed()
    {
        var rng = new Random(7);
        int previous = -1;
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            int pick = ClipTakes.Next(3, previous, rng.NextDouble());
            Assert.InRange(pick, 0, 2);
            Assert.NotEqual(previous, pick);
            seen.Add(pick);
            previous = pick;
        }

        Assert.Equal(3, seen.Count); // every take gets its turn
        Assert.Equal(0, ClipTakes.Next(1, 0, 0.9));
        Assert.Equal(-1, ClipTakes.Next(0, -1, 0.5));
    }

    [Fact]
    public void EveryRotatingFamily_ShipsAtLeastTwoTakes()
    {
        string dir = AudioDir();
        bool Has(string id) => File.Exists(Path.Combine(dir, id + ".mp3"));

        var thin = ClipTakes.Families.Where(f => ClipTakes.Find(f, Has).Length < 2).ToList();
        Assert.True(thin.Count == 0, "rotating families with fewer than two bundled takes: " + string.Join(", ", thin));
    }

    // ---------------- BlockSoundRules ----------------

    [Theory]
    [InlineData("stone", "place_stone")]
    [InlineData("basalt", "place_stone")]
    [InlineData("iron_ore", "place_stone")]      // an ore is the stone it sits in
    [InlineData("ship_hull", "place_metal")]
    [InlineData("metal_grate", "place_metal")]
    [InlineData("oak_planks", "place_wood")]
    [InlineData("birch_log", "place_wood")]
    [InlineData("glass", "place_glass")]
    [InlineData("crystal_block", "place_crystal")]
    [InlineData("dirt", "place_dirt")]
    [InlineData("sand", "place_dirt")]
    [InlineData("grass", "place_dirt")]
    [InlineData("", "place_block")]
    [InlineData(null, "place_block")]
    public void APlacedBlock_SoundsLikeItsMaterial(string? key, string cue)
        => Assert.Equal(cue, BlockSoundRules.PlaceCue(key));

    [Fact]
    public void AMaterialWithoutAClip_FallsBackToTheOldKnock()
    {
        Assert.Equal("place_block", BlockSoundRules.PlaceClip("oak_planks", _ => false));
        Assert.Equal("place_wood", BlockSoundRules.PlaceClip("oak_planks", id => id == "place_wood"));
    }

    [Fact]
    public void EveryPlaceMaterial_ShipsItsClip()
    {
        string dir = AudioDir();
        foreach (string cue in new[] { "place_stone", "place_metal", "place_crystal", "place_dirt", "place_wood", "place_glass", BlockSoundRules.Fallback })
        {
            Assert.True(File.Exists(Path.Combine(dir, cue + ".mp3")), $"missing place clip {cue}.mp3");
        }
    }

    // ---------------- TensionMusic ----------------

    private static bool Run(TensionMusic rule, float seconds, bool near, bool allowed = true)
    {
        bool active = rule.Active;
        for (float t = 0f; t < seconds; t += Frame)
        {
            active = rule.Step(Frame, near, allowed);
        }

        return active;
    }

    [Fact]
    public void WalkingPastAHostile_ChangesNothing_StayingCloseStartsThePool()
    {
        var rule = new TensionMusic();
        Assert.False(Run(rule, TensionMusic.ArmSeconds * 0.5f, near: true));
        Assert.False(Run(rule, 1f, near: false)); // gone again: the count starts over
        Assert.False(Run(rule, TensionMusic.ArmSeconds * 0.5f, near: true));
        Assert.True(Run(rule, TensionMusic.ArmSeconds, near: true));
    }

    [Fact]
    public void ThePool_PlaysItsMinimum_AndEndsOnlyAWhileAfterTheLastHostileLeft()
    {
        var rule = new TensionMusic();
        Assert.True(Run(rule, TensionMusic.ArmSeconds + 0.5f, near: true));

        // The hostile leaves at once: the pool still plays its minimum.
        Assert.True(Run(rule, TensionMusic.MinimumSeconds - 3f, near: false));
        Assert.False(Run(rule, 4f, near: false));

        // A long fight: after the minimum the pool ends a release time after the last hostile left — not earlier.
        Assert.True(Run(rule, TensionMusic.MinimumSeconds + 5f, near: true));
        Assert.True(Run(rule, TensionMusic.ReleaseSeconds - 1f, near: false));
        Assert.False(Run(rule, 2f, near: false));
    }

    [Fact]
    public void GoingAboard_EndsThePoolAtOnce()
    {
        var rule = new TensionMusic();
        Assert.True(Run(rule, TensionMusic.ArmSeconds + 0.5f, near: true));
        Assert.False(rule.Step(Frame, hostileNear: true, allowed: false));
        Assert.False(rule.Active);
    }
}
