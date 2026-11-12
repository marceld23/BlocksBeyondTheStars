// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The structure bookkeeping of #3513: one placement record per stamped structure, and what used to be recomputed on every
/// load (counts, a settlement's tier and ruin verdict, the wreck's design) written down once and replayed. One test per bug the
/// issue names — each boots a save, changes what used to move the structure (a restart, a rule, the content), boots it again
/// and checks that what stands stayed. The seed-dependent cases pin terrain generation 37 so the later generation-38 placement
/// rules (#3514) cannot move their premises.
/// </summary>
public sealed partial class StructureBookkeepingTests : IDisposable
{
    private const int Gen37 = WorldDescription.LandmarkGeneration;

    /// <summary>A meadow world whose cities are composed from kits and one of whose ruin verdicts the new city template flips.</summary>
    private const string FlipPlanet = "meadowlands";

    private const long FlipSeed = 1;

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_bookkeeping_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
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

    // ---------------------------------------------------------------- the Guardian core

    /// <summary>The chamber was guarded only by an in-memory flag, so every server start wrote it again over whatever a player
    /// had mined or built in it. Now it is written once (a stamped feature) and has a record.</summary>
    [Fact]
    public void TheGuardianCoreChamber_IsWrittenOnce_AMinedCoreAndABuiltBlockSurviveARestart()
    {
        var stone = Content.GetBlock("stone")!.NumericId;
        Vector3i core = default, inside = default;
        Boot("core", "rocky", 9, Gen37, s =>
        {
            core = s.LoadGuardianCoreForTest(); // the lowest cell of the glowing core column
            inside = new Vector3i(core.X + 3, core.Y, core.Z); // open floor inside the chamber
            Assert.False(s.World.GetBlock(core).IsAir);
            Assert.True(s.World.GetBlock(inside).IsAir);
            Assert.True(s.FeatureStampedForTest("guardiancore"));
            var rec = Assert.Single(s.PlacementRecordsForTest, r => r.Kind == SvGameServer.GuardianCoreRecordKind);
            Assert.True(rec.Placed);
            Assert.Equal((core.X, core.Y - 1, core.Z), (rec.X, rec.GroundY, rec.Z));

            s.World.SetBlock(core, BlockId.Air, owner: "tester"); // a player mines the core cell …
            s.World.SetBlock(inside, stone, owner: "tester");     // … and builds inside
        });

        Boot("core", "rocky", 9, Gen37, s =>
        {
            Assert.Equal(core, s.LoadGuardianCoreForTest());
            Assert.True(s.HasCoreChamberForTest); // the runtime state is re-derived as before
            Assert.True(s.World.GetBlock(core).IsAir);
            Assert.Equal(stone, s.World.GetBlock(inside));
        });
    }

    // ---------------------------------------------------------------- chests

    /// <summary>The meadow world of the placement golden rolls two chests. Their spots and their loot share the body's stream:
    /// on a reload the first chest (whose cache exists) skipped its loot draws, so the second chest landed elsewhere and a third
    /// cache appeared. Now a reload makes the same draws, and both chests are recorded.</summary>
    [Fact]
    public void TwoChests_SurviveReloads_WithoutAThirdAppearing()
    {
        const string name = "chests";
        static void ChestsOnly(ServerConfig c)
        {
            c.PlaceSettlements = false;
            c.PlaceRuins = false;
            c.PlaceRailRuins = false;
            c.PlaceWrecks = false;
            c.PlaceVaults = false;
            c.PlaceDataCubes = false;
            c.PlaceMonuments = false;
            c.PlaceFactories = false;
            c.PlaceBanditCamps = false;
            c.PlaceLodges = false;
            c.PlacePioneers = false;
            c.PlaceLandmarks = false;
        }

        (List<string> Keys, List<(int, int, int)> Records) Look(SvGameServer s)
        {
            Assert.Equal(2, s.StructureCountForTest(SvGameServer.ChestRecordKind)); // the premise: this body rolls two chests
            var keys = s.Metadata.GeneratedLoot.Where(k => k.StartsWith("chest:", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var records = s.PlacementRecordsForTest.Where(r => r.Kind == SvGameServer.ChestRecordKind && r.Placed)
                .OrderBy(r => r.Index).Select(r => (r.X, r.GroundY, r.Z)).ToList();
            return (keys, records);
        }

        var first = Boot(name, "meadowlands", 20261112L, Gen37, Look, ChestsOnly);
        Assert.Equal(2, first.Keys.Count);
        Assert.Equal(first.Keys, first.Records.Select(c => $"chest:chest:{c.Item1}:{c.Item2}:{c.Item3}").OrderBy(k => k, StringComparer.Ordinal));

        for (int reload = 0; reload < 2; reload++)
        {
            var again = Boot(name, "meadowlands", 20261112L, Gen37, Look, ChestsOnly);
            Assert.Equal(first.Keys, again.Keys);
            Assert.Equal(first.Records, again.Records);
        }
    }

    // ---------------------------------------------------------------- the Ruined verdict and the tier

    /// <summary>A whole template of a tier that had none (here: the first city template) adds a draw to the template pick, which
    /// runs ahead of the ruin roll on the instance's stream — so a city composed from a kit read the next number and could flip
    /// between ruin and town on the next load. The recorded verdict stays. Shown against the same save with the verdicts struck
    /// out of its records (as a record from before #3513 reads), which does flip.</summary>
    [Fact]
    public void ANewCityTemplate_AfterTheFirstLoad_DoesNotFlipASettlementsRuinVerdict()
    {
        const string name = "ruined";
        var withCityTemplate = ContentWithACityTemplate();
        var first = Boot(name, FlipPlanet, FlipSeed, Gen37, SettlementVerdicts, Frequent);
        Assert.Contains(first, v => v.Tier == "city" && v.Kit.Length > 0); // the premise: a city composed from a kit

        var withRecords = Boot(name, FlipPlanet, FlipSeed, Gen37, SettlementVerdicts, Frequent, withCityTemplate);
        Assert.Equal(first, withRecords);

        EditMetadata(name, meta =>
        {
            foreach (var r in meta.Placements.Where(r => r.Kind == "settlement"))
            {
                r.Ruined = null; // a record written before #3513
            }
        });
        var withoutVerdicts = Boot(name, FlipPlanet, FlipSeed, Gen37, SettlementVerdicts, Frequent, withCityTemplate);
        Assert.NotEqual(first, withoutVerdicts); // the premise: without the record the new template flips a verdict
    }

    /// <summary>A settlement record from before #3513 gains its tier and verdict on the next load — the values computed then.</summary>
    [Fact]
    public void ARecordFromBeforeTheFields_GainsTierAndVerdict_OnTheNextLoad()
    {
        const string name = "migrate";
        var first = Boot(name, FlipPlanet, FlipSeed, Gen37, SettlementVerdicts, Frequent);
        Assert.NotEmpty(first);
        Assert.All(first, v => Assert.True(v.Tier.Length > 0 && v.Ruined is not null));

        EditMetadata(name, meta =>
        {
            meta.StructureCounts.Clear();
            foreach (var r in meta.Placements)
            {
                (r.Tier, r.Ruined) = (null, null);
            }
        });
        var again = Boot(name, FlipPlanet, FlipSeed, Gen37, SettlementVerdicts, Frequent);
        Assert.Equal(first, again);
        Assert.Equal(first.Count, Boot(name, FlipPlanet, FlipSeed, Gen37, s => s.StructureCountForTest("settlement") ?? -1, Frequent));
    }

    private static void Frequent(ServerConfig c)
    {
        c.World.SettlementTemplateUse = Frequency.Frequent;
        c.PlaceRuins = false;
        c.PlaceWrecks = false;
        c.PlaceVaults = false;
        c.PlaceDataCubes = false;
        c.PlaceChests = false;
        c.PlaceMonuments = false;
        c.PlaceFactories = false;
        c.PlaceBanditCamps = false;
        c.PlaceLodges = false;
        c.PlacePioneers = false;
        c.PlaceLandmarks = false;
        c.PlaceRailRuins = false;
    }

    /// <summary>Every placed settlement as (index, recorded tier, kit, recorded verdict, verdict in play, bounds).</summary>
    private static List<(int Index, string Tier, string Kit, bool? Ruined, bool InPlay, string Bounds)> SettlementVerdicts(SvGameServer s)
    {
        var records = s.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).OrderBy(r => r.Index).ToList();
        var live = s.SettlementsForTest;
        return records.Select(r =>
        {
            var inst = live.First(l => l.MinX == r.X && l.MinZ == r.Z);
            return (r.Index, r.Tier ?? string.Empty, r.Kit, r.Ruined, inst.Ruined, $"{inst.MinX},{inst.MinZ},{inst.MaxX},{inst.MaxZ}");
        }).ToList();
    }

    /// <summary>The shipped content plus one whole template of the city tier (the shipped pool has none).</summary>
    private static GameContent ContentWithACityTemplate()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var whole = content.SettlementTemplates.ToList();
        Assert.DoesNotContain(whole, t => !t.IsModule && t.Tier == "city");
        // A town template of a second load (its own object) made into a city — the town tier keeps its own.
        var city = ContentLoader.LoadFromDirectory(TestPaths.DataDir()).SettlementTemplates.First(t => !t.IsModule && t.Tier == "town");
        city.Key = "test_city_whole";
        city.Tier = "city";
        city.LegacyPool = false; // a new template, as a user's would be
        city.PinOnly = false;
        whole.Add(city);
        content.SetStructureTemplates(content.StationTemplates, whole);
        return content;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Boots the save <paramref name="name"/> (fresh the first time, a reload afterwards), hands the running server to
    /// <paramref name="look"/>, stops it and returns what it saw.</summary>
    private T Boot<T>(string name, string planet, long seed, int generation, Func<SvGameServer, T> look,
        Action<ServerConfig>? configure = null, GameContent? content = null)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.World.TerrainGeneration = generation;
        configure?.Invoke(config);
        var server = new SvGameServer(config, content ?? Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        try
        {
            return look(server);
        }
        finally
        {
            server.Stop();
        }
    }

    private void Boot(string name, string planet, long seed, int generation, Action<SvGameServer> look,
        Action<ServerConfig>? configure = null, GameContent? content = null)
        => Boot(name, planet, seed, generation, s =>
        {
            look(s);
            return 0;
        }, configure, content);

    /// <summary>Edits the save's metadata between two boots.</summary>
    private void EditMetadata(string name, Action<WorldMetadata> edit)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        repo.Initialize();
        var meta = repo.LoadMetadata()!;
        edit(meta);
        repo.SaveMetadata(meta);
    }
}
