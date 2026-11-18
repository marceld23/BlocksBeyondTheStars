// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3795: the musical accent for "another world reached" plays once per session — on the first landing on a second
/// body — and not again on the third, the fourth … The sunrise track still opens the first visit of every body. The rule
/// (<see cref="SessionArrivals"/>) and its use by the music (as source text — the scripts cannot load headless).
/// </summary>
public sealed class SessionArrivalsTests
{
    [Fact]
    public void TheAccent_PlaysOnTheSecondWorld_AndNotAgainInThisSession()
    {
        var s = new SessionArrivals();

        Assert.True(s.Arrive("home", out bool accent));
        Assert.False(accent, "the world the session starts on is not 'another world'");

        Assert.True(s.Arrive("moon", out accent));
        Assert.True(accent, "the first other world");

        Assert.True(s.Arrive("lava", out accent)); // a first visit: the sunrise track still opens it
        Assert.False(accent, "once per session");
        Assert.True(s.Arrive("ice", out accent));
        Assert.False(accent);
        Assert.Equal(4, s.Count);
    }

    [Fact]
    public void AWorldAlreadyVisited_IsNoArrival()
    {
        var s = new SessionArrivals();
        s.Arrive("home", out _);
        s.Arrive("moon", out _);

        Assert.False(s.Arrive("home", out bool accent));
        Assert.False(accent);
        Assert.False(s.Arrive("moon", out accent));
        Assert.False(accent);
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void NoBody_IsNoArrival()
    {
        var s = new SessionArrivals();
        Assert.False(s.Arrive(string.Empty, out bool accent));
        Assert.False(accent);
        Assert.False(s.Arrive(null!, out accent));
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void ANewSession_StartsAfresh()
    {
        var s = new SessionArrivals();
        s.Arrive("home", out _);
        s.Arrive("moon", out _);
        s.Reset();

        Assert.Equal(0, s.Count);
        Assert.True(s.Arrive("moon", out bool accent));
        Assert.False(accent, "the first world of the new session");
        Assert.True(s.Arrive("home", out accent));
        Assert.True(accent, "and its first other world gets the accent again");
    }

    [Fact]
    public void TheMusic_AsksTheRule()
    {
        string music = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "ClientMusic.cs"));
        Assert.Contains("private readonly SessionArrivals _arrivals = new SessionArrivals();", music);
        Assert.Contains("if (_arrivals.Arrive(body, out bool accent))", music);
        Assert.Contains("_arrivals.Reset();", music);
        Assert.DoesNotContain("_visitedBodies", music);

        // Exactly one place plays the accent, and only on the rule's word.
        int at = music.IndexOf("Sting(\"sting_new_planet\")", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.Equal(at, music.LastIndexOf("Sting(\"sting_new_planet\")", StringComparison.Ordinal));
        Assert.Contains("if (accent)", music.Substring(at - 120, 120));
    }
}
