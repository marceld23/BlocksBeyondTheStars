// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The tick schedule (#2904, GameServerTickSchedule.cs): the step names are the tick-timing (#1504) and
/// tick-fault bucket names, so each phase must name its systems uniquely, and the held (paused) phase runs exactly the
/// control-plane systems. Only anchors are pinned here, not the full order — a new system is one new entry.</summary>
public sealed class TickScheduleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_tickschedule_" + Guid.NewGuid().ToString("N"));
    private readonly SqliteWorldRepository _repo;
    private readonly SvGameServer _server;

    /// <summary>Start() is deliberately NOT called: the schedule is built in the constructor, so no world is loaded
    /// and the test stays sub-second.</summary>
    public TickScheduleTests()
    {
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sched"));
        var config = new ServerConfig
        {
            WorldName = "sched",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
        };
        _server = new SvGameServer(
            config,
            ContentLoader.LoadFromDirectory(TestPaths.DataDir()),
            new LoopbackServerTransport(new LoopbackLink()),
            _repo);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    [Theory]
    [InlineData("Held")]
    [InlineData("PreWorld")]
    [InlineData("PerWorld")]
    [InlineData("PostWorld")]
    public void Phase_NamesAreNonEmptyAndUnique(string phase)
    {
        string[] names = _server.TickScheduleNamesForTest(Enum.Parse<SvGameServer.TickPhase>(phase));

        Assert.NotEmpty(names);
        // A duplicate name would silently merge two systems' timing and fault buckets.
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void HeldPhase_RunsOnlyTheControlPlaneSystems_AndSharesTheirBucketsWithThePostWorldPhase()
    {
        string[] held = _server.TickScheduleNamesForTest(SvGameServer.TickPhase.Held);
        string[] post = _server.TickScheduleNamesForTest(SvGameServer.TickPhase.PostWorld);

        Assert.Equal(new[] { "Moderation", "Maintenance", "HostedLifecycle", "SpectatorChunks" }, held);
        Assert.Contains("Moderation", post);
        Assert.Contains("Maintenance", post);
        Assert.Contains("HostedLifecycle", post);
        Assert.DoesNotContain("SpectatorChunks", _server.TickScheduleNamesForTest(SvGameServer.TickPhase.PreWorld));
        Assert.DoesNotContain("SpectatorChunks", _server.TickScheduleNamesForTest(SvGameServer.TickPhase.PerWorld));
        Assert.DoesNotContain("SpectatorChunks", post);
    }

    [Fact]
    public void PerWorldPhase_BeginsWithTheEnvironment_AndEndsWithTheThrottledFarTerrainSweeps()
    {
        string[] names = _server.TickScheduleNamesForTest(SvGameServer.TickPhase.PerWorld);

        Assert.Equal("TickEnvironment", names[0]);
        Assert.Equal("SweepFarChunks", names[^2]);
        Assert.Equal("RefreshFarTiles", names[^1]);
        Assert.Contains("StreamChunks", names); // the bucket name TickTimingTests finds in the timing report
    }
}
