// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The shuffle-bag track picker (#1172): every track of a pool plays once before anything repeats, the
/// current track is never picked again immediately, the neutral fillers rotate in one shared bag across
/// contexts, and the filler share lands where the library asks for it.
/// </summary>
public sealed class MusicPickerTests
{
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void Next_EmptyPools_ReturnsNull()
    {
        var picker = new MusicPicker(new Random(1));
        Assert.Null(picker.Next("x", None, None, 0.5, null));
    }

    [Fact]
    public void Next_SingleTrackPool_LoopsInPlace()
    {
        var picker = new MusicPicker(new Random(1));
        var pool = new[] { "only" };
        Assert.Equal("only", picker.Next("x", pool, None, 0.0, null));
        Assert.Equal("only", picker.Next("x", pool, None, 0.0, "only")); // nothing else to play
    }

    [Fact]
    public void Next_ShuffleBag_PlaysEveryTrackOnceBeforeRepeating()
    {
        var pool = new[] { "a", "b", "c", "d", "e" };
        for (int seed = 0; seed < 50; seed++)
        {
            var picker = new MusicPicker(new Random(seed));
            string? last = null;
            for (int round = 0; round < 4; round++)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < pool.Length; i++)
                {
                    string pick = picker.Next("p", pool, None, 0.0, last)!;
                    Assert.True(seen.Add(pick), $"seed {seed}: '{pick}' repeated inside one bag");
                    Assert.NotEqual(last, pick);
                    last = pick;
                }
            }
        }
    }

    [Fact]
    public void Next_TwoTrackPool_NeverRepeatsTheCurrentTrack()
    {
        var pool = new[] { "a", "b" };
        var picker = new MusicPicker(new Random(7));
        string? last = null;
        for (int i = 0; i < 40; i++)
        {
            string pick = picker.Next("p", pool, None, 0.0, last)!;
            Assert.NotEqual(last, pick);
            last = pick;
        }
    }

    [Fact]
    public void Next_FillerShare_LandsNearTheRequestedRate()
    {
        var primary = new[] { "ice", "ice_2" };
        var fillers = new[] { "idle", "idle_2", "explore", "explore_2" };
        var picker = new MusicPicker(new Random(3));
        int fillerPicks = 0;
        const int N = 4000;
        string? last = null;
        for (int i = 0; i < N; i++)
        {
            string pick = picker.Next("ice", primary, fillers, 0.35, last)!;
            if (Array.IndexOf(fillers, pick) >= 0)
            {
                fillerPicks++;
            }

            last = pick;
        }

        Assert.InRange(fillerPicks / (double)N, 0.30, 0.40);
    }

    [Fact]
    public void Next_ZeroFillerShare_NeverPicksFillers()
    {
        var primary = new[] { "a", "b", "c" };
        var fillers = new[] { "f1", "f2" };
        var picker = new MusicPicker(new Random(5));
        for (int i = 0; i < 200; i++)
        {
            Assert.DoesNotContain(picker.Next("p", primary, fillers, 0.0, null)!, fillers);
        }
    }

    [Fact]
    public void Next_EmptyPrimary_FallsBackToFillers()
    {
        var fillers = new[] { "f1", "f2" };
        var picker = new MusicPicker(new Random(5));
        Assert.Contains(picker.Next("p", None, fillers, 0.0, null)!, fillers);
    }

    [Fact]
    public void Next_FillerBag_IsSharedAcrossContexts()
    {
        // A filler that just played in context A is not the next filler in context B (the shared bag keeps
        // rotating, and the history skips it while alternatives exist).
        var fillers = new[] { "f1", "f2", "f3", "f4" };
        for (int seed = 0; seed < 30; seed++)
        {
            var picker = new MusicPicker(new Random(seed));
            string a = picker.Next("ctxA", None, fillers, 1.0, null)!;
            string b = picker.Next("ctxB", None, fillers, 1.0, null)!;
            string c = picker.Next("ctxC", None, fillers, 1.0, null)!;
            string d = picker.Next("ctxA", None, fillers, 1.0, null)!;
            Assert.Equal(4, new HashSet<string> { a, b, c, d }.Count);
        }
    }

    [Fact]
    public void Next_History_SkipsRecentTracksWhileAlternativesExist()
    {
        // Pool P1 = {x, y, z}; pool P2 = {x, q, r}. After x played in P1, P2 should not open with x.
        var p1 = new[] { "x", "y", "z" };
        var p2 = new[] { "x", "q", "r" };
        for (int seed = 0; seed < 30; seed++)
        {
            var picker = new MusicPicker(new Random(seed), historySize: 4);
            string first;
            do
            {
                first = picker.Next("p1", p1, None, 0.0, null)!;
            }
            while (first != "x");

            Assert.NotEqual("x", picker.Next("p2", p2, None, 0.0, null));
        }
    }

    [Fact]
    public void Next_PoolShrinks_DroppedTrackIsNeverReturned()
    {
        var picker = new MusicPicker(new Random(11));
        var full = new[] { "a", "b", "c", "d" };
        picker.Next("p", full, None, 0.0, null);
        var smaller = new[] { "a", "c" };
        for (int i = 0; i < 50; i++)
        {
            Assert.Contains(picker.Next("p", smaller, None, 0.0, null)!, smaller);
        }
    }

    [Fact]
    public void Next_PoolGrows_NewTrackJoinsTheRotation()
    {
        var picker = new MusicPicker(new Random(13));
        var small = new[] { "a", "b" };
        picker.Next("p", small, None, 0.0, null);
        var bigger = new[] { "a", "b", "new" };
        var seen = new HashSet<string>();
        for (int i = 0; i < 12; i++)
        {
            seen.Add(picker.Next("p", bigger, None, 0.0, null)!);
        }

        Assert.Contains("new", seen);
    }

    [Fact]
    public void History_IsBoundedAndReset()
    {
        var picker = new MusicPicker(new Random(1), historySize: 3);
        var pool = new[] { "a", "b", "c", "d", "e", "f" };
        for (int i = 0; i < 10; i++)
        {
            picker.Next("p", pool, None, 0.0, null);
        }

        Assert.Equal(3, picker.History.Count());
        picker.Reset();
        Assert.Empty(picker.History);
    }

    [Fact]
    public void Constructor_NullRng_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MusicPicker(null!));
    }
}
