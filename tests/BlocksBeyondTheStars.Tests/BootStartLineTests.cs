// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3260: the server announces every boot pass and step BEFORE it runs it (<c>[boot] &gt; 7/12 landing_pads</c>,
/// <c>[boot]   &gt; settlements</c>), so the loading screen can name what the player is waiting for. The finish lines
/// keep their shape — the bar, the measurements and older clients read those.
/// </summary>
public sealed class BootStartLineTests : IDisposable
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
    private static readonly Regex FinishLine = new(@"^\[boot\] (?<n>\d+)/\d+ (?<name>.+) \(\d+ ms( total)?\)$", Options, TimeSpan.FromSeconds(1));
    private static readonly Regex StartLine = new(@"^\[boot\] > (?<n>\d+)/\d+ (?<id>[a-z_]+)$", Options, TimeSpan.FromSeconds(1));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_bootlines_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void Run_AnnouncesThePassBeforeItsWork_AndReportsItAfter()
    {
        var log = new CapturingLogger();
        var boot = new BootProgress(log, 3);

        boot.Run("landing pads", () => log.Info("the work"));

        Assert.Equal("[boot] > 1/3 landing_pads", log.Infos[0]);
        Assert.Equal("the work", log.Infos[1]);
        Assert.Matches(@"^\[boot\] 1/3 landing pads \(\d+ ms\)$", log.Infos[2]); // the finish line is unchanged
        Assert.Equal(3, log.Infos.Count);
    }

    [Fact]
    public void Run_CountsTheNextPassInItsStartLine()
    {
        var log = new CapturingLogger();
        var boot = new BootProgress(log, 12);

        boot.Run("persistence", () => { });
        boot.Run("galaxy", () => { });

        Assert.Equal("[boot] > 2/12 galaxy", log.Infos[2]);
    }

    [Fact]
    public void Detail_AnnouncesTheStepById_EvenWhenItIsTooQuickToReport()
    {
        var log = new CapturingLogger();
        var boot = new BootProgress(log, 12);

        boot.Detail("  npcs", () => log.Info("the work"));

        Assert.Equal("[boot]   > npcs", log.Infos[0]);
        Assert.Equal("the work", log.Infos[1]);
        Assert.InRange(log.Infos.Count, 2, 3); // a sub-millisecond step keeps its finish line quiet (#1990)
    }

    [Fact]
    public void BeginFinish_AnnouncesReady_BeforeTheTotal()
    {
        var log = new CapturingLogger();
        var boot = new BootProgress(log, 7);

        boot.BeginFinish();
        boot.Finish();

        Assert.Equal("[boot] > 7/7 ready", log.Infos[0]);
        Assert.Matches(@"^\[boot\] 7/7 ready \(\d+ ms total\)$", log.Infos[1]);
    }

    [Theory]
    [InlineData("landing pads", "landing_pads")]
    [InlineData("  npcs", "npcs")]
    [InlineData("intercity rail", "intercity_rail")]
    [InlineData("persistence", "persistence")]
    public void StageId_IsTheTrimmedLowerCasedNameWithUnderscores(string name, string id)
        => Assert.Equal(id, BootProgress.StageId(name));

    [Fact]
    [Trait("Category", "Slow")] // a full rocky world boot (~1.5 min) — the fast PR tier must not pay for it
    public void ServerStart_AnnouncesEveryPassRightBeforeItsWork()
    {
        var log = new LockedLogger();
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var config = new ServerConfig
        {
            WorldName = "bootlines",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        using var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "bootlines"));
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo, log);
        server.Start();
        try
        {
            var lines = log.Snapshot().Where(l => l.StartsWith("[boot] ", StringComparison.Ordinal) && !l.StartsWith("[boot]   ", StringComparison.Ordinal)).ToList();
            Assert.Equal("[boot] > 1/12 persistence", lines[0]);

            // Pass lines alternate: the announcement, then (after the pass's work, with its steps nested in between and
            // filtered out above) the finish line of the SAME pass number.
            for (int i = 0; i < lines.Count; i += 2)
            {
                var start = StartLine.Match(lines[i]);
                Assert.True(start.Success, $"line {i} is not a start line: {lines[i]}");
                var finish = FinishLine.Match(lines[i + 1]);
                Assert.True(finish.Success, $"line {i + 1} is not a finish line: {lines[i + 1]}");
                Assert.Equal(start.Groups["n"].Value, finish.Groups["n"].Value);
                Assert.Equal(start.Groups["id"].Value, BootProgress.StageId(finish.Groups["name"].Value));
            }

            Assert.Equal("[boot] > 12/12 ready", lines[^2]);
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>The server logs from its formatter warm-up thread too, so the boot test's logger takes a lock.</summary>
    private sealed class LockedLogger : IGameLogger
    {
        private readonly List<string> _lines = new();

        public void Info(string message) => Add(message);

        public void Warn(string message) => Add(message);

        public void Error(string message) => Add(message);

        public List<string> Snapshot()
        {
            lock (_lines)
            {
                return new List<string>(_lines);
            }
        }

        private void Add(string message)
        {
            lock (_lines)
            {
                _lines.Add(message);
            }
        }
    }
}
