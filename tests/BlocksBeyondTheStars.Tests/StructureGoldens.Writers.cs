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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Goldens of the server's direct block writers (#3511) that no ordinary world boot reaches: the Guardian-core chamber of the
/// finale body (<c>guardian_finale-core</c>) and a hunter shuttle's camp (#3097). The other direct writers — the vaults, the
/// unique sites (Singing Shrine, Sealed Observatory) and the Soaniri buildings — are pinned with the worlds they stand on by
/// <see cref="StructurePlacementGoldenTests"/> (its "edits" part). Hashes: every block edit the writer left in the save,
/// by cell, block (by key), tint, glow and shape.
/// </summary>
public sealed class StructureWriterGoldenTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_writergolden_" + Guid.NewGuid().ToString("N"));

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

    private static readonly Dictionary<string, string> Pins = new(StringComparer.Ordinal)
    {
        ["hunter_camp:edits"] = "c6d1356103eec359",
        ["guardian_core:edits"] = "9f1c68244f435db0",
    };

    private SvGameServer Start(string name, Action<ServerConfig>? configure, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.World.TerrainGeneration = WorldDescription.LandmarkGeneration;
        configure?.Invoke(config);
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The finale body's chamber: stamped on the body's load (today on EVERY load — #3513 makes it once; the blocks
    /// are the same either way, so the second boot of the save must show the same edits).</summary>
    [Fact]
    public void TheGuardianCoreChamber_WritesThePinnedBlocks_OnEveryLoadAlike()
    {
        var hashes = new List<ulong>();
        for (int boot = 0; boot < 2; boot++)
        {
            var server = Start("guardian_core", null, out var repo);
            using (repo)
            {
                server.LoadGuardianCoreForTest();
                Assert.True(server.HasCoreChamberForTest);
                server.Stop();
            }

            hashes.Add(EditsOf(new SaveGamePaths(_root, "guardian_core").DatabaseFile, SvGameServer.GuardianCoreBodyId, out _));
        }

        StructureGoldens.AssertPinned("guardian core", Pins, new[] { ("guardian_core:edits", hashes[0]) }, scope: "guardian_core:");
        Assert.Equal(hashes[0], hashes[1]);
    }

    /// <summary>A hunter shuttle's camp (#3097). The camp is a runtime event whose seed comes from an unseeded
    /// <see cref="Random"/>, so the test plans it at a fixed spot and pins the record's seed before the camp lands; what is
    /// pinned is the stamp itself (spot search from the plan, hull, ground fill, robot docks).</summary>
    [Fact]
    public void AHunterCamp_WithAFixedSeed_WritesThePinnedBlocks()
    {
        var server = Start("hunter_camp", c =>
        {
            c.PlaceSettlements = false;
            c.PlaceWrecks = false;
            c.PlaceBanditCamps = false;
            c.Rules.PlanetEnemies = AlienActivity.Normal;
            c.Rules.PlanetDrones = false;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
        }, out var repo);
        ulong hash;
        string spotLine;
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunted");
            string body = server.Metadata.ActiveLocationId;
            string db = new SaveGamePaths(_root, "hunter_camp").DatabaseFile;
            var before = Edits(db, body);
            var spot = FlatSpot(server, (int)p.State.Position.X, (int)p.State.Position.Z);
            Assert.True(server.PlanHunterSiteForTest(spot.X, spot.Z));
            server.HunterSiteForTest(body)!.Seed = 3097L;
            server.TickHunterSitesForTest(1.0); // someone is on the body: the camp lands
            var site = server.HunterSiteForTest(body)!;
            Assert.Equal(HunterShuttleSiteRecord.Active, site.State);
            spotLine = $"camp at {site.X},{site.Y},{site.Z} with {site.Spawners.Count} docks (planned {spot.X},{spot.Z})";
            var after = Edits(db, body);
            var written = after.Where(kv => !before.TryGetValue(kv.Key, out var old) || old != kv.Value)
                .Select(kv => (kv.Key, kv.Value.Block, kv.Value.Tint, kv.Value.Glow, kv.Value.Shape));
            var h = new StructureGoldenHash(Content).Add(site.X).Add(site.Y).Add(site.Z);
            foreach (var d in site.Spawners.OrderBy(s => s.X).ThenBy(s => s.Y).ThenBy(s => s.Z))
            {
                h.Add(d.X).Add(d.Y).Add(d.Z);
            }

            hash = h.Add(StructureGoldens.Cells(Content, written)).Value;
            server.Stop();
        }

        StructureGoldens.AssertPinned("hunter camp", Pins, new[] { ("hunter_camp:edits", hash) }, scope: "hunter_camp:", note: spotLine);
    }

    /// <summary>The first dry, fairly flat spot on rings around the player (the search of <c>HunterShuttleTests</c>, in integers).</summary>
    private static (int X, int Z) FlatSpot(SvGameServer server, int px, int pz)
    {
        int[] dx = { 16, 11, 0, -11, -16, -11, 0, 11 };
        int[] dz = { 0, 11, 16, 11, 0, -11, -16, -11 };
        for (int r = 3; r <= 10; r++)
        {
            for (int a = 0; a < dx.Length; a++)
            {
                int x = px + dx[a] * r, z = pz + dz[a] * r;
                if (server.HunterSpotSpreadForTest(x, z) is >= 0 and <= 2)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("no flat spot for the hunter camp");
    }

    private static Dictionary<Vector3i, (ushort Block, int Tint, int Glow, int Shape)> Edits(string databaseFile, string location)
    {
        var map = new Dictionary<Vector3i, (ushort, int, int, int)>();
        using var connection = new SqliteConnection($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT x, y, z, block, tint, glow, shape FROM block_edit WHERE planet = $p;";
        cmd.Parameters.AddWithValue("$p", location);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))] =
                ((ushort)reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));
        }

        return map;
    }

    private static ulong EditsOf(string databaseFile, string location, out int count)
    {
        var map = Edits(databaseFile, location);
        count = map.Count;
        return StructureGoldens.Cells(Content, map.Select(kv => (kv.Key, kv.Value.Block, kv.Value.Tint, kv.Value.Glow, kv.Value.Shape)));
    }
}
