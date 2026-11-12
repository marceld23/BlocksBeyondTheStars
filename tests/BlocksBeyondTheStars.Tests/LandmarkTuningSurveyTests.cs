// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A landmark builder's measuring tools (on demand, like <see cref="SchoolClubLandmarkTests.RuinPreviews_AreWrittenWhenAsked"/>):
/// how often a span seats over a set of world boots, and what one landmark's stamp costs in time and memory. Each does nothing
/// unless its environment variable is set — they boot many worlds and are no check.
/// </summary>
public sealed class LandmarkTuningSurveyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landmark_survey_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public LandmarkTuningSurveyTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _repos.ForEach(r => r.Dispose());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private static List<string> List(string? value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private SvGameServer Start(GameContent content, string name, string planet, long seed, double chance)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            LandmarkChance = chance,
        };
        config.World.TerrainGeneration = WorldDescription.SchoolClubLandmarksGeneration;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>
    /// <c>BBTS_SPAN_SURVEY=&lt;key&gt;</c> (a span landmark), <c>BBTS_SPAN_SURVEY_PLANETS</c> (comma list) and
    /// <c>BBTS_SPAN_SURVEY_SEEDS</c> (comma list): boots a generation-39 world per planet and seed with only that landmark in
    /// the pool and reports whether it seated, per planet and in all.
    /// </summary>
    [Xunit.Fact]
    public void SpanSeats_AreSurveyedWhenAsked()
    {
        string? key = Environment.GetEnvironmentVariable("BBTS_SPAN_SURVEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(new[] { content.Landmarks.Single(l => l.Key == key) });
        var seeds = List(Environment.GetEnvironmentVariable("BBTS_SPAN_SURVEY_SEEDS")).Select(s => long.Parse(s, CultureInfo.InvariantCulture)).ToList();
        int seated = 0, boots = 0;
        foreach (string planet in List(Environment.GetEnvironmentVariable("BBTS_SPAN_SURVEY_PLANETS")))
        {
            var line = new List<string>();
            foreach (long seed in seeds)
            {
                var clock = Stopwatch.StartNew();
                var server = Start(content, $"span_{planet}_{seed}", planet, seed, 1.0);
                bool ok = server.LandmarksForTest.Count == 1;
                server.Stop();
                (seated, boots) = (seated + (ok ? 1 : 0), boots + 1);
                line.Add(FormattableString.Invariant($"{seed}:{(ok ? "seated" : "-")} ({clock.ElapsedMilliseconds} ms)"));
            }

            _output.WriteLine($"{planet}: {string.Join(", ", line)}");
        }

        _output.WriteLine(FormattableString.Invariant($"{key}: seated in {seated} of {boots} boots"));
    }

    /// <summary>
    /// <c>BBTS_RUIN_SWEEP=&lt;keys&gt;</c> (comma list) and <c>BBTS_RUIN_SWEEP_STRENGTHS</c> (comma list, empty = as shipped):
    /// the share of each landmark's blocks that falls in its ruin for seeds 1, 2 and 3 at each template strength (a ruin zone's
    /// strength scaled by the same factor, at most 1) — the numbers to tune a template's <c>ruinStrength</c> by.
    /// </summary>
    [Xunit.Fact]
    public void RuinShares_AreSweptWhenAsked()
    {
        var keys = List(Environment.GetEnvironmentVariable("BBTS_RUIN_SWEEP"));
        if (keys.Count == 0)
        {
            return;
        }

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (string key in keys)
        {
            var t = content.Landmarks.Single(l => l.Key == key);
            double shipped = t.RuinStrength > 0 ? t.RuinStrength : 0.5;
            var zones = t.RuinZones.Select(z => z.Strength).ToList();
            int intact = Blocks(LandmarkGenerator.Build(t, 1, 0, content, "grass", ruin: false));
            var strengths = List(Environment.GetEnvironmentVariable("BBTS_RUIN_SWEEP_STRENGTHS"))
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).DefaultIfEmpty(shipped);
            foreach (double s in strengths)
            {
                t.RuinStrength = s;
                for (int i = 0; i < zones.Count; i++)
                {
                    t.RuinZones[i].Strength = Math.Min(1.0, zones[i] * s / shipped);
                }

                var shares = new[] { 1L, 2L, 3L }.Select(seed => 100.0 * (intact - Blocks(LandmarkGenerator.Build(t, seed, 0, content, "grass", ruin: true))) / intact);
                _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{key} @ {s:0.00}: {string.Join(" / ", shares.Select(p => p.ToString("0.0", CultureInfo.InvariantCulture)))} % fell"));
            }
        }
    }

    private static int Blocks(LandmarkLayout l)
    {
        var s = l.Structure;
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    n += s.Get(x, y, z) != 0 ? 1 : 0;
                }
            }
        }

        return n;
    }

    /// <summary>
    /// <c>BBTS_STAMP_TIMING=&lt;key&gt;</c> (and optionally <c>BBTS_STAMP_TIMING_PLANET</c>): boots a generation-39 world without
    /// landmarks, then stamps that landmark the way the admin command does and reports the time, the bytes allocated and the
    /// peak of the managed heap and of the working set while it ran (sampled every 5 ms).
    /// </summary>
    [Xunit.Fact]
    public void AStamp_IsTimedWhenAsked()
    {
        string? key = Environment.GetEnvironmentVariable("BBTS_STAMP_TIMING");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var template = content.Landmarks.Single(l => l.Key == key);
        string planet = Environment.GetEnvironmentVariable("BBTS_STAMP_TIMING_PLANET") ?? "meadowlands";
        var boot = Stopwatch.StartNew();
        var server = Start(content, "stamp_" + key, planet, 20261112L, 0.0);
        _output.WriteLine(FormattableString.Invariant($"{planet}: the world boots without a landmark in {boot.ElapsedMilliseconds} ms"));
        var (padX, padY, padZ) = server.LandingPadForTest(0);
        for (int run = 0; run < 2; run++) // the first run pays the JIT; the second is the number
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long heap0 = GC.GetTotalMemory(true), alloc0 = GC.GetTotalAllocatedBytes(precise: true), ws0 = Environment.WorkingSet;
            long peakHeap = heap0, peakWs = ws0;
            using var done = new ManualResetEventSlim();
            var sampler = new Thread(() =>
            {
                while (!done.Wait(5))
                {
                    peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
                    peakWs = Math.Max(peakWs, Environment.WorkingSet);
                }
            });
            sampler.Start();
            var clock = Stopwatch.StartNew();
            var lm = server.ForceLandmark(template, new Vector3i(padX + (400 * (run + 1)), padY, padZ));
            clock.Stop();
            done.Set();
            sampler.Join();
            string what = lm is null ? "no layout" : lm.Layout.Ruined ? "ruin" : "intact";
            long allocated = (GC.GetTotalAllocatedBytes(precise: true) - alloc0) / 1048576;
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"run {run}: {key} stamped in {clock.ElapsedMilliseconds} ms ({what}), {allocated} MB allocated, managed heap peak +{(peakHeap - heap0) / 1048576} MB, working set peak +{(peakWs - ws0) / 1048576} MB (from {ws0 / 1048576} MB)"));
        }

        server.Stop();
    }
}
