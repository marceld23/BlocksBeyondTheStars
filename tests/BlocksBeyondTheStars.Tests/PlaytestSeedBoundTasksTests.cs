// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3836 — a playtest task about something rare (a guardian giant, one of the school club's giant buildings) brings a
/// test world in which the thing stands: a world picked by its SEED, and a start command that takes the tester there
/// (<c>teleport_to_named</c>). The fixed list of start commands has no "spawn" on purpose, so the seed is the only way.
/// <para>Such a task ages. A test world is created with the terrain generation of the build that plays it, and after a
/// generation step the same seed may no longer hold the place — the teleport then moves nobody and the tester stands
/// at the ship with a task that cannot be done. This guard builds each such task's world the way the client's bundled
/// server does and checks that every named place is there. When it goes red after a generation step: search a new
/// seed (<see cref="SearchVariable"/>), count the task's <c>rev</c> up and set its <c>minVersion</c> to the release
/// that carries the generation (<c>docs/developer/PLAYTEST_TASKS.md</c> § "Seed-bound tasks").</para>
/// </summary>
public sealed class PlaytestSeedBoundTasksTests : IDisposable
{
    /// <summary>Set to <c>&lt;planet&gt;:&lt;kind&gt;:&lt;from&gt;-&lt;to&gt;</c> (<c>rocky:giant:1-400</c>) to print the seeds of that
    /// range whose start world holds such a place — the seed search for a new task.</summary>
    public const string SearchVariable = "BBS_PLAYTEST_SEED_SEARCH";

    /// <summary>The named places world generation decides. A task that teleports to one is bound to its seed; a ship,
    /// a landing pad or something the player built is not.</summary>
    private static readonly string[] GeneratedKinds =
    {
        "city", "village", "ruin", "railruin", "vault", "ancient", "landmark", "civ", "giantwreck", "giant", "tower", "wreck", "factory",
        "camp", "lodge", "outpost", "monument", "treasure",
    };

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_seedbound_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public PlaytestSeedBoundTasksTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>
    /// A world as the client's bundled server creates it for a test world of the standard preset without world
    /// options: the seed, the start planet and the switches <c>LocalServerLauncher</c> always passes.
    /// </summary>
    private (SvGameServer Server, PlayerSession Tester) BundledWorld(long seed, string startPlanet)
    {
        string name = "seed" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" + startPlanet;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, AutoSaveIntervalMinutes = 9999, ViewDistanceChunks = 1 };
        config.ApplyCommandLine(new[]
        {
            "--seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture), "--start-planet", startPlanet,
            "--free-flight", "true", "--space-combat", "PvE", "--space-npcs", "Normal", "--guarantee-start-cube", "true",
            "--admin-cheats", "true",
        });
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return (server, server.AddLocalPlayer("Tester"));
    }

    /// <summary>What a named teleport resolves in that world, as the words a start command names (<c>giant1</c>).</summary>
    private static HashSet<string> Places(SvGameServer server, PlayerSession tester)
        => server.TeleportTargetsForTest(tester.State.PlayerId)
            .Select(t => t.Kind + t.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string File, PlaytestTask Task, List<(string Kind, int Number)> Places)> SeedBoundTasks()
    {
        string folder = Path.Combine(TestPaths.RepoRoot(), "data-online", "playtests");
        foreach (string path in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (!PlaytestCatalogJson.TryReadTask(File.ReadAllText(path), out var task, PlaytestReadMode.AsWritten) || task.World is not { } world)
            {
                continue;
            }

            var places = world.Commands
                .Where(c => c.Cmd == PlaytestWorldCommands.TeleportToNamed)
                .Select(c => SvGameServer.ParseTeleportTargetForTest(c.Arg))
                .Where(p => GeneratedKinds.Contains(p.Kind))
                .Select(p => (p.Kind, Math.Max(1, p.Number)))
                .ToList();
            if (places.Count > 0)
            {
                yield return (Path.GetFileName(path), task, places);
            }
        }
    }

    [Fact]
    public void ASeedBoundTask_FindsEveryPlaceItTeleportsTo_InTheWorldOfItsSeed()
    {
        var problems = new List<string>();
        foreach (var (file, task, places) in SeedBoundTasks())
        {
            var world = task.World!;
            if (world.Preset != PlaytestWorldPresets.Standard || world.Options.Count > 0 || world.StartPlanet.Length == 0)
            {
                problems.Add($"{file}: a task that teleports to a generated place uses the standard preset, no world options and "
                             + "names its start planet — this guard rebuilds exactly that world, and a slider can move the place away");
                continue;
            }

            var (server, tester) = BundledWorld(world.Seed, world.StartPlanet);
            var found = Places(server, tester);
            foreach (var (kind, number) in places)
            {
                if (!found.Contains(kind + number))
                {
                    problems.Add($"{file}: seed {world.Seed} on '{world.StartPlanet}' has no '{kind}{number}' (it has: "
                                 + $"{string.Join(", ", found.Where(f => f.StartsWith(kind, StringComparison.Ordinal)).DefaultIfEmpty("none of that kind"))}). "
                                 + $"After a terrain generation step search a new seed ({SearchVariable}={world.StartPlanet}:{kind}:1-400), "
                                 + "count the task's rev up and set its minVersion to the release that carries the generation.");
                }
                else if (kind == "giant" && !server.StampedGuardianGiantsForTest[number - 1].CanWake)
                {
                    problems.Add($"{file}: 'giant{number}' of seed {world.Seed} is dead for good — a task about the fight needs one that can wake.");
                }
            }

            server.Stop();
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void TheGuard_NamesAPlaceThatIsNotThere()
    {
        // The rule the guard stands on: a world lists its generated places by kind and number, and one that is not
        // there is not listed. Seed 1 on a rocky world has at most two giants (the kind's cap).
        var (server, tester) = BundledWorld(1, "rocky");
        var found = Places(server, tester);

        Assert.DoesNotContain("giant3", found);
        Assert.Contains("pad1", found); // a world always has its landing pad
        server.Stop();
    }

    /// <summary>The seed search (not a test of anything): prints the seeds whose start world holds the place.</summary>
    [Fact]
    public void SeedSearch_PrintsTheSeedsThatHoldThePlace()
    {
        string? wanted = Environment.GetEnvironmentVariable(SearchVariable);
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return;
        }

        string[] parts = wanted.Split(':');
        string[] range = parts[2].Split('-');
        var lines = new List<string>();
        for (long seed = long.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture);
             seed <= long.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture);
             seed++)
        {
            var (server, tester) = BundledWorld(seed, parts[0]);
            var of = server.TeleportTargetsForTest(tester.State.PlayerId).Where(t => t.Kind == parts[1]).ToList();
            if (of.Count > 0)
            {
                string more = parts[1] == "giant"
                    ? " wake=" + string.Join("/", server.StampedGuardianGiantsForTest.Select(g => g.CanWake ? "yes" : "no"))
                    : parts[1] == "landmark" ? " is=" + string.Join("/", server.LandmarksForTest.Select(l => l.Key))
                    : string.Empty;
                lines.Add($"seed {seed}: " + string.Join("; ", of.Select(t => $"{t.Kind}{t.Number} '{t.Label}' at {t.Position}")) + more);
            }

            server.Stop();
        }

        string report = $"{parts[0]} / {parts[1]} / {parts[2]}: {lines.Count} seed(s)\n" + string.Join("\n", lines);
        _output.WriteLine(report);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "bbs_playtest_seed_search.txt"), report);
    }
}
