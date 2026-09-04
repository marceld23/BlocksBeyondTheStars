// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Opt-in tick profiling (#1504): with <see cref="ServerConfig.TickTimingLogSeconds"/> set, the server
/// logs one summary line per window naming the systems the tick spent its time in; with the default (0) it
/// never logs a timing line.</summary>
public sealed class TickTimingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_ticktiming_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private sealed class CapturingLogger : IGameLogger
    {
        public List<string> Info { get; } = new();
        void IGameLogger.Info(string message) => Info.Add(message);
        public void Warn(string message) { }
        public void Error(string message) { }
    }

    private SvGameServer Start(string tag, double timingSeconds, CapturingLogger log, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
            TickTimingLogSeconds = timingSeconds,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo, log);
        server.Start();
        return server;
    }

    [Fact]
    public void TimingEnabled_LogsOneReportPerWindow_NamingTheSystems()
    {
        var log = new CapturingLogger();
        var server = Start("timing_on", timingSeconds: 1.0, log, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Profiler");
            for (int i = 0; i < 5; i++)
            {
                server.TickForTest(0.5); // 2.5 s of sim time → windows close at 1.0 s and 2.0 s
            }

            var reports = log.Info.FindAll(m => m.StartsWith("Tick timing", StringComparison.Ordinal));
            Assert.Equal(2, reports.Count);
            var report = reports[0];
            Assert.Contains("ticks)", report);
            Assert.Contains("p95", report);
            Assert.Contains("budget", report);
            Assert.Contains("StreamChunks", report); // a Guard-wrapped system that always runs with a player joined
            Assert.Contains("ms/s", report);
        }
    }

    [Fact]
    public void TimingOff_ByDefault_NeverLogsAReport()
    {
        var log = new CapturingLogger();
        var server = Start("timing_off", timingSeconds: 0, log, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Quiet");
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.5); // 20 s of sim time
            }

            Assert.DoesNotContain(log.Info, m => m.StartsWith("Tick timing", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Report_RanksSystemsByTime_AndCountsOverBudgetTicks()
    {
        var log = new CapturingLogger();
        var server = Start("timing_report", timingSeconds: 60, log, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Ranker");
            server.TickForTest(0.1);
            string report = server.BuildTickTimingReport(0.1);
            Assert.StartsWith("Tick timing (last 0.1 s, 1 ticks)", report);
            Assert.Contains("over 66.7 ms budget", report); // TickRate 15 → 66.7 ms
            int bar = report.IndexOf(" | ", StringComparison.Ordinal);
            Assert.True(bar > 0, "the report lists the top systems after the tick summary");
            // The first listed system is the most expensive one; every entry carries ms and ms/s.
            var systems = report[(bar + 3)..].Split(", ");
            Assert.True(systems.Length >= 2 && systems.Length <= 6, $"top-6 list expected, got {systems.Length}: {report}");
            foreach (var s in systems)
            {
                Assert.Matches(@"^\S+ \d+ ms \(\d+(\.\d)? ms/s\)$", s);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // temp dir cleanup is best-effort
        }
    }
}
