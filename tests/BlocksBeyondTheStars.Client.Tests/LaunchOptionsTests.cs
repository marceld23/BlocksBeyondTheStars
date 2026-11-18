// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The start options (data folder + port of the bundled server) from the portable marker and the command line: the
/// marker's new <c>port=</c> line next to its directory line, <c>-dataDir</c> / <c>-hostPort</c> on the command line, the
/// command line winning, bad values reported and ignored, and what the update restart hands on.
/// </summary>
public sealed class LaunchOptionsTests
{
    private readonly string _exeDir = Path.Combine(Path.GetTempPath(), "bbs-launch-" + Guid.NewGuid().ToString("N"));

    private static readonly string[] NoArgs = { "BlocksBeyondTheStars.exe" };

    private string Userdata => Path.GetFullPath(Path.Combine(_exeDir, PortableDataDir.DefaultSubfolder));

    [Fact]
    public void NothingGiven_KeepsTheDefaults()
    {
        var o = LaunchOptions.Resolve(_exeDir, null, NoArgs);
        Assert.Null(o.DataDir);
        Assert.Null(o.HostPort);
        Assert.Empty(o.Warnings);
        Assert.Empty(o.RestartArguments());
    }

    [Fact]
    public void MarkerWithDirectoryAndPort_GivesBoth_InAnyOrder()
    {
        string dir = Path.Combine(_exeDir, "data");
        var o = LaunchOptions.Resolve(_exeDir, $"# my stick\nport = 31600\n{dir}\n", NoArgs);
        Assert.Equal(Path.GetFullPath(dir), o.DataDir);
        Assert.Equal(31600, o.HostPort);
        Assert.Equal(LaunchOptions.FromMarker, o.HostPortSource);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void MarkerWithOnlyAPort_StaysPortable()
    {
        // A marker means a portable data folder — the port line does not change that.
        var o = LaunchOptions.Resolve(_exeDir, "PORT=31600", NoArgs);
        Assert.Equal(Userdata, o.DataDir);
        Assert.Equal(31600, o.HostPort);
    }

    [Fact]
    public void MarkerPath_ThatContainsAnEqualsSign_IsStillTheDirectory()
    {
        string dir = Path.Combine(_exeDir, "games=fun");
        Assert.Equal(Path.GetFullPath(dir), PortableDataDir.Resolve(_exeDir, dir));
        Assert.Null(PortableDataDir.OptionValue(dir, PortableDataDir.PortKey));
    }

    [Theory]
    [InlineData("port=80")]
    [InlineData("port=70000")]
    [InlineData("port=abc")]
    [InlineData("port=")]
    [InlineData("port=-5")]
    public void BadPortInTheMarker_IsReportedAndIgnored(string line)
    {
        var o = LaunchOptions.Resolve(_exeDir, line, NoArgs);
        Assert.Null(o.HostPort);
        Assert.Single(o.Warnings);
        Assert.Equal(Userdata, o.DataDir); // the marker still counts
    }

    [Fact]
    public void CommandLine_SetsBoth_AndRelativePathsAnchorAtTheExecutable()
    {
        var o = LaunchOptions.Resolve(_exeDir, null, new[] { "game.exe", "-dataDir", "../BBTS Data", "-hostPort", "31700" });
        Assert.Equal(Path.GetFullPath(Path.Combine(_exeDir, "..", "BBTS Data")), o.DataDir);
        Assert.Equal(31700, o.HostPort);
        Assert.Equal(LaunchOptions.DataDirArgument, o.DataDirSource);
        Assert.Equal(LaunchOptions.HostPortArgument, o.HostPortSource);
    }

    [Theory]
    [InlineData("-hostport=31701")]
    [InlineData("--host-port=31701")]
    [InlineData("--HOSTPORT=31701")]
    public void CommandLine_AcceptsTheUsualSpellings(string arg)
    {
        Assert.Equal(31701, LaunchOptions.Resolve(_exeDir, null, new[] { arg }).HostPort);
    }

    [Fact]
    public void CommandLine_WinsOverTheMarker()
    {
        string fileDir = Path.Combine(_exeDir, "from-file");
        string argDir = Path.Combine(_exeDir, "from-args");
        var o = LaunchOptions.Resolve(_exeDir, $"{fileDir}\nport=31600", new[] { "-dataDir", argDir, "-hostPort", "31601" });
        Assert.Equal(Path.GetFullPath(argDir), o.DataDir);
        Assert.Equal(31601, o.HostPort);
    }

    [Fact]
    public void BadPortOnTheCommandLine_FallsBackToTheMarkerPort()
    {
        var o = LaunchOptions.Resolve(_exeDir, "port=31600", new[] { "-hostPort", "99999" });
        Assert.Equal(31600, o.HostPort);
        Assert.Equal(LaunchOptions.FromMarker, o.HostPortSource);
        Assert.Single(o.Warnings);
    }

    [Fact]
    public void OptionWithoutValue_DoesNotEatTheNextOption()
    {
        var o = LaunchOptions.Resolve(_exeDir, null, new[] { "-dataDir", "-hostPort", "31602" });
        Assert.Null(o.DataDir);
        Assert.Equal(31602, o.HostPort);
        Assert.Single(o.Warnings);
    }

    [Fact]
    public void UnrelatedArguments_AreIgnored()
    {
        var o = LaunchOptions.Resolve(_exeDir, null, new[] { "game.exe", "-screen-width", "1280", "-captureShots", "-logFile", "x.log" });
        Assert.Null(o.DataDir);
        Assert.Null(o.HostPort);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void RestartArguments_CarryOnlyWhatTheCommandLineGave()
    {
        string argDir = Path.Combine(_exeDir, "from-args");
        var fromArgs = LaunchOptions.Resolve(_exeDir, "port=31600", new[] { "-dataDir", argDir });
        Assert.Equal(new[] { LaunchOptions.DataDirArgument, Path.GetFullPath(argDir) }, fromArgs.RestartArguments());

        var portOnly = LaunchOptions.Resolve(_exeDir, null, new[] { "-hostPort=31603" });
        Assert.Equal(new[] { LaunchOptions.HostPortArgument, "31603" }, portOnly.RestartArguments());

        // The restart arguments read back to the same options.
        var again = LaunchOptions.Resolve(_exeDir, "port=31600", fromArgs.RestartArguments());
        Assert.Equal(fromArgs.DataDir, again.DataDir);
    }

    [Fact]
    public void EnvironmentVariables_AreExpandedOnTheCommandLineToo()
    {
        string name = "BBTS_TEST_LAUNCH_" + Guid.NewGuid().ToString("N");
        string value = Path.Combine(_exeDir, "env-data");
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            Assert.Equal(Path.GetFullPath(value), LaunchOptions.Resolve(_exeDir, null, new[] { "-dataDir", $"%{name}%" }).DataDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
