// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Diagnostics;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The PII scrubber redacts the OS account name from home-folder paths and any e-mail address, while
/// leaving the rest of the diagnostic text intact — applied to crash text before it is written or uploaded.</summary>
public sealed class CrashPiiScrubberTests
{
    [Fact]
    public void RedactsWindowsHome_KeepsRestOfPath()
    {
        string input = @"at Game.Load() in C:\Users\Alice Smith\AppData\Local\BBS\save.cs:line 12";
        string output = CrashPiiScrubber.Scrub(input);

        Assert.DoesNotContain("Alice Smith", output);
        Assert.Contains(@"C:\Users\<user>\AppData\Local\BBS\save.cs", output); // path structure preserved
    }

    [Theory]
    [InlineData("/home/bob/.config/bbs/x.log", "/home/<user>/.config/bbs/x.log")]
    [InlineData("/Users/charlie/Library/bbs/y", "/Users/<user>/Library/bbs/y")]
    public void RedactsUnixAndMacHome(string input, string expected)
    {
        Assert.Equal(expected, CrashPiiScrubber.Scrub(input));
    }

    [Fact]
    public void RedactsEmail()
    {
        string output = CrashPiiScrubber.Scrub("contact pilot.one+tag@example.co.uk for details");
        Assert.DoesNotContain("pilot.one", output);
        Assert.Contains("<email>", output);
    }

    [Fact]
    public void LeavesNonPiiTextUnchanged()
    {
        string input = "NullReferenceException at Player.Update() line 42 (block 12,64,-8)";
        Assert.Equal(input, CrashPiiScrubber.Scrub(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_ReturnedUnchanged(string input)
    {
        Assert.Equal(input, CrashPiiScrubber.Scrub(input));
    }
}
