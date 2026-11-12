// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Placement goldens (#3511, before the structure-pipeline refactor of #3510): fresh worlds of a handful of planet types are
/// booted through the real <see cref="SvGameServer"/> (terrain generation 37 pinned), and what the "structures" boot stage
/// left behind is fingerprinted in five separately pinned parts, so a failure names what moved:
/// <list type="number">
/// <item><b>records</b> — the body's <see cref="StructurePlacementRecord"/>s of the kinds that exist today (<see cref="RecordKinds"/>),
/// sorted, with the fields that exist today: a later NEW kind or field never moves this part;</item>
/// <item><b>features</b> — the body's <see cref="WorldMetadata.StampedFeatures"/> keys of today's families (<see cref="FeatureFamilies"/>);</item>
/// <item><b>stations</b> — the save's pinned station layouts (<see cref="WorldMetadata.StationTemplates"/> / <c>StationKits</c>):
/// empty in every case today — a station is pinned when it first shows up in flight, never by a planet's boot (the station
/// builders themselves are pinned by <see cref="StructureGeneratorGoldenTests"/>); a boot that starts pinning one shows here;</item>
/// <item><b>loot</b> — the save's <see cref="WorldMetadata.GeneratedLoot"/> keys, sorted;</item>
/// <item><b>edits</b> — every block edit the boot wrote on the body (cell, block by key, tint, glow, shape), read from the save.</item>
/// </list>
/// The same save is then booted a second time and must show the same five parts (the replay every later load performs) —
/// except where the code is known not to replay (<see cref="KnownReplayGaps"/>, empty since #3513). The kinds #3513 added
/// records for (ruins, monuments, text tablets, chests, data cubes, the Guardian core) and the new fields of the old kinds
/// are not hashed, so the pins taken before it still hold.
/// <para>The refactor may move code, never output: for terrain generation ≤ 37 these pins must never be re-taken. The terrain
/// itself runs through <c>Math.Sin/Cos</c>; like <c>WorldGenerationGoldenTests</c> the Windows pins are expected to hold on
/// Linux (Linux-only pins can go into <see cref="LinuxPins"/> if CI ever disagrees). Pins: <c>StructureGoldens.PlacementPins.cs</c>.</para>
/// </summary>
public sealed partial class StructurePlacementGoldenTests : IDisposable
{
    private const int Generation = WorldDescription.LandmarkGeneration; // 37: the last generation before the refactor

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_placegolden_" + Guid.NewGuid().ToString("N"));

    private readonly ITestOutputHelper _output;

    /// <summary>Every case writes what its world stamped (record kinds, feature families, settlements, loot, edits, the boot
    /// time) to the test output, so a passing run shows what the pins cover.</summary>
    public StructurePlacementGoldenTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    /// <summary>What one boot left on one body, fingerprinted part by part.</summary>
    private sealed record Snapshot(ulong Records, ulong Features, ulong Stations, ulong Loot, ulong Edits, string Summary,
        IReadOnlyList<string> LootKeys)
    {
        public IEnumerable<(string Part, ulong Hash)> Parts()
        {
            yield return ("records", Records);
            yield return ("features", Features);
            yield return ("stations", Stations);
            yield return ("loot", Loot);
            yield return ("edits", Edits);
        }
    }

    // ---------------------------------------------------------------- the cases

    /// <summary>A fresh world started on a planet type: the start body is stamped by the boot itself. These cases, the landmark
    /// world and the two cheap visits run on every PR — about 45 s together (the class's first case also pays for the content
    /// load and the JIT, some 10 s).</summary>
    [Theory]
    [InlineData("meadowlands", 20261112L)]
    [InlineData("gas_giant", 20261112L)]
    [InlineData("gds_desert", 20261107L)]
    [InlineData("titas", 20261112L)]
    [InlineData("sounder_world", 20261112L)]
    [InlineData("asteroid", 14L)]
    [InlineData("asteroid_kuba_w", 20261112L)]
    [InlineData("scrapyard", 424242L)]
    public void AFreshStartWorld_StampsThePinnedStructures_AndReplaysThem(string planet, long seed)
        => StartWorldCase(planet, seed);

    /// <summary>The worlds with the slowest first boot (the landing-pad search over dunes and sea ice: 8–20 s each, depending on
    /// the machine's load). Slow keeps the PR tier of this class under a minute; they run on every push to main, and locally with
    /// <c>--filter FullyQualifiedName~StructurePlacementGoldenTests</c> (every case of the class).</summary>
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData("desert", 20261112L)]
    [InlineData("frozen_ocean", 20260903L)]
    public void ALargeFreshStartWorld_StampsThePinnedStructures_AndReplaysThem(string planet, long seed)
        => StartWorldCase(planet, seed);

    private void StartWorldCase(string planet, long seed)
    {
        string name = $"{planet}_{seed}";
        Func<SvGameServer, string> start = s => s.Metadata.ActiveLocationId;
        var first = BootAndCapture(name, planet, seed, Content, null, start, out string location);
        var second = BootAndCapture(name, planet, seed, Content, null, start, out string again);
        Assert.Equal(location, again);
        AssertCase(name, first, second);
    }

    /// <summary>The landmark stamper (#3486): the three shipped samples made rollable (they ship pin-only), chance 1.</summary>
    [Fact]
    public void AFreshWorldWithLandmarks_StampsThePinnedLandmark_AndReplaysIt()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var samples = content.Landmarks.ToList();
        Assert.Equal(3, samples.Count);
        foreach (var t in samples)
        {
            t.PinOnly = false;
        }

        content.SetLandmarks(samples);
        const string name = "landmarks_meadowlands_20261111";
        Func<SvGameServer, string> start = s => s.Metadata.ActiveLocationId;
        var first = BootAndCapture(name, "meadowlands", 20261111L, content, c => c.LandmarkChance = 1.0, start, out _);
        var second = BootAndCapture(name, "meadowlands", 20261111L, content, c => c.LandmarkChance = 1.0, start, out _);
        AssertCase(name, first, second);
    }

    /// <summary>A body a pilot flies to: the Singing Shrine's (#1129) — the unique-site writer stamps there; on seed 11 the body
    /// also carries the intercity rail, a rail ruin and a crystal vault — and the galaxy's first airless moon (cratered regolith,
    /// monuments and vaults only).</summary>
    [Theory]
    [InlineData("shrine")]
    [InlineData("moon")]
    public void AVisitedBody_StampsThePinnedStructures_AndReplaysThem(string target) => VisitCase(target);

    /// <summary>The Sealed Observatory's body (#1129; on seed 11 also a settlement, a bandit camp and a factory). Its first boot
    /// takes 8–20 s: Slow (see <see cref="ALargeFreshStartWorld_StampsThePinnedStructures_AndReplaysThem"/>).</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void TheObservatoryBody_StampsThePinnedStructures_AndReplaysThem() => VisitCase("observatory");

    private void VisitCase(string target)
    {
        string name = "visit_" + target;
        Func<SvGameServer, string> visit = s => Visit(s, target);
        var first = BootAndCapture(name, "meadowlands", 11L, Content, c => c.Rules.FreeSpaceFlight = true, visit, out string location);
        var second = BootAndCapture(name, "meadowlands", 11L, Content, c => c.Rules.FreeSpaceFlight = true, visit, out string again);
        Assert.Equal(location, again);
        AssertCase(name, first, second);
    }

    /// <summary>Flies a pilot to the target body (a temporary jump generator for a hop between stars) and returns its id.</summary>
    private static string Visit(SvGameServer server, string target)
    {
        string body = target switch
        {
            "shrine" => server.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey)!,
            "observatory" => server.UniqueSiteBodyForTest(SvGameServer.ObservatorySiteKey)!,
            _ => AirlessMoon(server),
        };
        var pilot = server.AddLocalPlayer("Pilot");
        if (pilot.CurrentLocationId != body)
        {
            if (!server.Ship.Modules.Contains("jump_generator"))
            {
                server.Ship.Modules.Add("jump_generator");
            }

            server.Travel(pilot.State.PlayerId, body);
        }

        Assert.Equal(body, pilot.CurrentLocationId);
        return body;
    }

    /// <summary>The first airless moon of the galaxy in generation order (a pure function of the seed).</summary>
    private static string AirlessMoon(SvGameServer server)
        => server.Galaxy.Systems.SelectMany(s => s.Bodies)
            .First(b => b.Kind == CelestialKind.Moon && b.PlanetType is { } t && Content.GetPlanet(t)?.IsAirless == true).Id;

    /// <summary>The first boot against the pins, then the second boot against the first (minus the known gaps).</summary>
    private void AssertCase(string name, Snapshot first, Snapshot second)
    {
        var gaps = KnownReplayGaps.TryGetValue(name, out var g) ? g : Array.Empty<string>();
        var moved = first.Parts().Zip(second.Parts()).Where(p => p.First.Hash != p.Second.Hash).Select(p => p.First.Part).ToList();
        string replay = $"  replay: {(moved.Count == 0 ? "identical" : "differs in " + string.Join(", ", moved))}"
                        + $" (known gaps: {(gaps.Length == 0 ? "none" : string.Join(", ", gaps))})\n"
                        + $"  loot only after the first boot: {string.Join(" ", first.LootKeys.Except(second.LootKeys))}\n"
                        + $"  loot only after the second boot: {string.Join(" ", second.LootKeys.Except(first.LootKeys))}\n";
        _output.WriteLine($"{name}:\n{first.Summary}\n{replay}");
        var computed = first.Parts().Select(p => ($"{name}:{p.Part}", p.Hash)).ToList();
        StructureGoldens.AssertPinned("placement " + name, PlacementPins, computed, LinuxPins, scope: name + ":",
            note: first.Summary + "\n" + replay);

        var unexpected = moved.Where(part => Array.IndexOf(gaps, part) < 0).ToList();
        Assert.True(unexpected.Count == 0, $"{name}: the second boot of the same save changed the {string.Join(", ", unexpected)}.\n"
            + $"{replay}first boot:\n{first.Summary}\nsecond boot:\n{second.Summary}");

        // A known gap that closed: the bookkeeping fix (#3513) removes its entry from KnownReplayGaps.
        var healed = gaps.Where(part => !moved.Contains(part)).ToList();
        Assert.True(healed.Count == 0, $"{name}: the {string.Join(", ", healed)} now replay(s) identically — drop the entry from KnownReplayGaps (#3513).");
    }

    // ---------------------------------------------------------------- booting

    /// <summary>Boots the save (fresh the first time, a reload afterwards), runs <paramref name="arrive"/> to get the body
    /// under test, captures the metadata parts, stops the server and reads the body's block edits back from the save.</summary>
    private Snapshot BootAndCapture(string name, string planet, long seed, GameContent content, Action<ServerConfig>? configure,
        Func<SvGameServer, string> arrive, out string location)
    {
        var paths = new SaveGamePaths(_root, name);
        var clock = Stopwatch.StartNew();
        SnapshotMeta meta;
        using (var repo = new SqliteWorldRepository(paths))
        {
            var config = new ServerConfig
            {
                WorldName = name,
                Seed = seed,
                StartPlanet = planet,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
            };
            config.World.TerrainGeneration = Generation;
            configure?.Invoke(config);
            var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            location = arrive(server);
            meta = CaptureMeta(server.Metadata, location, content);
            meta = meta with
            {
                Summary = meta.Summary + $"  settlements: {string.Join(",", server.SettlementTiersForTest)}"
                                       + $" ({server.SettlementsForTest.Count(s => s.Ruined)} ruined)\n",
            };
            server.Stop();
        }

        var edits = ReadEdits(paths.DatabaseFile, location, content, out int editCount);
        string summary = meta.Summary + $"  edits: {editCount}\n  boot: {clock.ElapsedMilliseconds} ms on {location}";
        return new Snapshot(meta.Records, meta.Features, meta.Stations, meta.Loot, edits, summary, meta.LootKeys);
    }

    private sealed record SnapshotMeta(ulong Records, ulong Features, ulong Stations, ulong Loot, string Summary, IReadOnlyList<string> LootKeys);

    private static SnapshotMeta CaptureMeta(WorldMetadata meta, string location, GameContent content)
    {
        var records = meta.Placements.Where(r => r.LocationId == location && RecordKinds.Contains(r.Kind)).ToList();
        var unknownKinds = meta.Placements.Where(r => r.LocationId == location && !RecordKinds.Contains(r.Kind)).Select(r => r.Kind).Distinct();
        string prefix = location + "|";
        var features = meta.StampedFeatures.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)).Select(f => f.Substring(prefix.Length)).ToList();
        var known = features.Where(f => FeatureFamilies.Contains(FamilyOf(f))).ToList();
        var stations = meta.StationTemplates.Select(kv => kv.Key + "=" + kv.Value)
            .Concat(meta.StationKits.Select(kv => kv.Key + "=" + KitLine(kv.Value)));

        var summary = new System.Text.StringBuilder();
        summary.Append("  records: ").AppendLine(string.Join(", ", records.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}×{g.Count()}({g.Count(r => r.Placed)} placed)")));
        summary.Append("  other record kinds (not hashed): ").AppendLine(string.Join(", ", unknownKinds));
        summary.Append("  feature families: ").AppendLine(string.Join(", ", features.Select(FamilyOf).Distinct().OrderBy(f => f, StringComparer.Ordinal)));
        summary.Append("  unknown feature families (not hashed): ").AppendLine(string.Join(", ", features.Select(FamilyOf).Where(f => !FeatureFamilies.Contains(f)).Distinct()));
        summary.Append("  stations: ").Append(meta.StationTemplates.Count).Append("  loot: ").AppendLine(string.Join(", ", meta.GeneratedLoot
            .GroupBy(FamilyOf).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}×{g.Count()}"))); // "ruin" = standalone ruins
        return new SnapshotMeta(StructureGoldens.Strings(content, records.Select(RecordLine)), StructureGoldens.Strings(content, known),
            StructureGoldens.Strings(content, stations), StructureGoldens.Strings(content, meta.GeneratedLoot), summary.ToString(),
            meta.GeneratedLoot.ToList());
    }

    /// <summary>A record as one line, only the fields that exist today (a later field never moves it).</summary>
    private static string RecordLine(StructurePlacementRecord r)
        => string.Join("|", r.Kind, I(r.Index), r.Placed ? "1" : "0", I(r.X), I(r.GroundY), I(r.Z), r.OnIsland ? "1" : "0", r.Seat, r.Name,
            r.Template, I(r.Modules), r.Kit, r.KitLayout, r.Composition is null ? "-" : string.Join(",", r.Composition),
            r.Roster is null ? "-" : string.Join(",", r.Roster), I(r.HashRevision));

    private static string KitLine(StationKitRecord k)
        => string.Join("|", k.Kit, k.Seed.ToString(CultureInfo.InvariantCulture), I(k.Revision),
            k.Exterior is null ? "-" : $"{k.Exterior.SolarWings},{k.Exterior.Antennas},{k.Exterior.Domes}",
            string.Join(";", k.Modules.Select(m => $"{m.Key}@{m.X},{m.Y},{m.Z}r{m.Turns}")));

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>A stamped feature's family: its name up to the first ':' or '@' (the rest is an index or a cell).</summary>
    private static string FamilyOf(string feature)
    {
        int cut = feature.IndexOfAny(new[] { ':', '@' });
        return cut < 0 ? feature : feature.Substring(0, cut);
    }

    /// <summary>Every block edit of the body in the save, by cell; the block by key.</summary>
    private static ulong ReadEdits(string databaseFile, string location, GameContent content, out int count)
    {
        var cells = new List<(Vector3i, ushort, int, int, int)>();
        using (var connection = new SqliteConnection($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block, tint, glow, shape FROM block_edit WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", location);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cells.Add((new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)), (ushort)reader.GetInt32(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
            }
        }

        count = cells.Count;
        return StructureGoldens.Cells(content, cells);
    }
}
