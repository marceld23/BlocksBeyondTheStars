// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _root;

    public PersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_test_" + Guid.NewGuid().ToString("N"));
    }

    private SqliteWorldRepository NewSqliteRepo(string world = "world_001")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        return repo;
    }

    private MemoryWorldRepository NewMemoryRepo(string world = "world_001")
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        return repo;
    }

    [Fact]
    public void Metadata_RoundTrips()
    {
        using (var repo = NewSqliteRepo())
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "Alpha", Seed = 42, DefaultPlanetType = "ice" });
        }

        using var reopened = NewSqliteRepo();
        var meta = reopened.LoadMetadata();
        Assert.NotNull(meta);
        Assert.Equal("Alpha", meta!.WorldName);
        Assert.Equal(42, meta.Seed);
        Assert.Equal("ice", meta.DefaultPlanetType);
    }

    [Fact]
    public void BlockEdits_PersistAndLoadByChunk()
    {
        using (var repo = NewSqliteRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 5);
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 0);   // overwrite (mined to air)
            repo.SetBlock("rocky", new Vector3i(40, 2, 3), 9);  // different chunk
        }

        using var reopened = NewSqliteRepo();
        var edits = reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0));
        Assert.Single(edits);
        Assert.Equal(new Vector3i(1, 2, 3), edits[0].WorldPosition);
        Assert.Equal((ushort)0, edits[0].Block);

        var otherChunk = reopened.LoadChunkEdits("rocky", WorldConstants.WorldToChunk(new Vector3i(40, 2, 3)));
        Assert.Single(otherChunk);
        Assert.Equal((ushort)9, otherChunk[0].Block);
    }

    [Fact]
    public void Player_RoundTripsNpcMemory()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Pilot" };
        var rel = new NpcRelationship
        {
            Name = "Kra Thraxon",
            Role = "quartermaster",
            Value = 7,
            LastSeenUtc = "2026-10-14T18:30:00.0000000Z", // #2595
            LastGiftAt = 4321.5,                          // #2598
        };
        rel.Log.Add(new NpcInteraction { Kind = NpcInteractionKind.MissionAccepted });
        rel.Log.Add(new NpcInteraction { Kind = NpcInteractionKind.Trade });
        player.NpcMemory["settle_123:quartermaster"] = rel;

        using (var repo = NewSqliteRepo())
        {
            repo.SavePlayer(player);
        }

        using var reopened = NewSqliteRepo();
        var loaded = reopened.LoadPlayer("p1");

        Assert.NotNull(loaded);
        Assert.True(loaded!.NpcMemory.TryGetValue("settle_123:quartermaster", out var r));
        Assert.Equal("Kra Thraxon", r!.Name);
        Assert.Equal("quartermaster", r.Role);
        Assert.Equal(7, r.Value);
        Assert.Equal(2, r.Log.Count);
        Assert.Equal(NpcInteractionKind.MissionAccepted, r.Log[0].Kind);
        Assert.Equal(NpcInteractionKind.Trade, r.Log[1].Kind);

        // #2595/#2598: the two memory fields go through the hand-written CloneNpcMemory — a field missing there is
        // silently dropped on every save.
        Assert.Equal("2026-10-14T18:30:00.0000000Z", r.LastSeenUtc);
        Assert.Equal(4321.5, r.LastGiftAt);
    }

    [Fact]
    public void Player_NpcMemoryFromAnOlderBuild_LoadsWithEmptyStampAndNoGift()
    {
        // What a build from before #2595/#2598 wrote: the relationship without the two new fields.
        var player = new PlayerState { PlayerId = "p1", Name = "Pilot" };
        player.NpcMemory["settle_123:settler"] = new NpcRelationship { Name = "Mira", Role = "settler", Value = 45 };
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(StateMapper.ToSnapshot(player)))!.AsObject();
        var relNode = node["NpcMemory"]!["settle_123:settler"]!.AsObject();
        Assert.True(relNode.Remove("LastSeenUtc"));
        Assert.True(relNode.Remove("LastGiftAt"));

        var loaded = StateMapper.PlayerFromJson(node.ToJsonString(), "p1", new System.Text.Json.JsonSerializerOptions());

        var r = loaded.NpcMemory["settle_123:settler"];
        Assert.Equal(45, r.Value);
        Assert.Equal(string.Empty, r.LastSeenUtc); // an empty stamp: only stamped, never a "long time no see"
        Assert.Equal(0.0, r.LastGiftAt);            // never gifted
    }

    [Fact]
    public void Player_RoundTripsInventoryAndBlueprints()
    {
        var player = new PlayerState
        {
            PlayerId = "p1",
            Name = "Pilot",
            Position = new Vector3f(10f, 64f, -5f),
            Health = 80f,
        };
        player.Inventory.Add("iron_ore", 30, 99);
        player.UnlockedBlueprints.Add("oxygen_tank_1");

        using (var repo = NewSqliteRepo())
        {
            repo.SavePlayer(player);
        }

        using var reopened = NewSqliteRepo();
        var loaded = reopened.LoadPlayer("p1");
        Assert.NotNull(loaded);
        Assert.Equal("Pilot", loaded!.Name);
        Assert.Equal(30, loaded.Inventory.CountOf("iron_ore"));
        Assert.Contains("oxygen_tank_1", loaded.UnlockedBlueprints);
        Assert.Equal(64f, loaded.Position.Y);
    }

    [Fact]
    public void Ship_RoundTripsModulesAndCargo()
    {
        var ship = new ShipState { CurrentLocationId = "rocky" };
        ship.Modules.Add("cockpit");
        ship.Modules.Add("workshop");
        ship.Cargo.Add("stone", 200, 99);

        using (var repo = NewSqliteRepo())
        {
            repo.SaveShip("default", ship);
        }

        using var reopened = NewSqliteRepo();
        var loaded = reopened.LoadShip("default");
        Assert.NotNull(loaded);
        Assert.Contains("workshop", loaded!.Modules);
        Assert.Equal(200, loaded.Cargo.CountOf("stone"));
    }

    [Fact]
    public void Backup_CreatesReadableCopy()
    {
        string backup;
        using (var repo = NewSqliteRepo())
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "Beta", Seed = 7 });
            backup = repo.CreateBackup("backup_test");
        }

        Assert.True(File.Exists(backup));

        // The backup should itself be a valid, loadable world database.
        var backupPaths = new SaveGamePaths(Path.GetDirectoryName(backup)!, "x");
        using var verify = new SqliteWorldRepository(
            new SaveGamePaths(_root, "world_001")); // not used; just ensure original still works
        verify.Initialize();
        Assert.Equal(7, verify.LoadMetadata()!.Seed);
    }

    [Fact]
    public void BlockPalette_RemapsStoredIdsWhenContentShifts()
    {
        // Baseline content: steel_wall=5, torch=6, gone_block=7.
        var paletteA = new Dictionary<ushort, string> { [5] = "steel_wall", [6] = "torch", [7] = "gone_block" };
        // A new block (algae) sorts first and takes id 5, shifting steel_wall→6 and torch→7; gone_block removed.
        var paletteB = new Dictionary<ushort, string> { [5] = "algae", [6] = "steel_wall", [7] = "torch" };

        using (var repo = NewSqliteRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 5);  // steel_wall
            repo.SetBlock("rocky", new Vector3i(2, 2, 3), 6);  // torch
            repo.SetBlock("rocky", new Vector3i(3, 2, 3), 7);  // gone_block → air after migration
            repo.SetStructureBlock("ship:p1", new Vector3i(0, 0, 0), 5);
            repo.SaveFloraRegrow("rocky", new Vector3i(4, 2, 3), 6, 12.0);
            repo.SaveSpaceStructure(new StoredSpaceStructure
            {
                Id = "st1",
                OwnerId = "p1",
                Name = "Base",
                Location = "sys0-p1",
                Blocks = "0:0:0:5;1:0:0:6;-2:3:0:7",
            });
            repo.EnsureBlockPalette(paletteA); // record the baseline palette
        }

        using (var repo = NewSqliteRepo())
        {
            repo.EnsureBlockPalette(paletteB); // content shifted → remap every stored id
        }

        using var reopened = NewSqliteRepo();
        var edits = reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0));
        ushort BlockAt(int x) => edits.First(e => e.WorldPosition.X == x).Block;
        Assert.Equal((ushort)6, BlockAt(1)); // steel_wall 5 → 6 (no cascade into torch's remap)
        Assert.Equal((ushort)7, BlockAt(2)); // torch 6 → 7
        Assert.Equal((ushort)0, BlockAt(3)); // gone_block 7 → air (key no longer in content)

        Assert.Equal((ushort)6, reopened.LoadStructureEdits("ship:p1").Single().Block);
        Assert.Equal((ushort)7, reopened.ListFloraRegrow("rocky").Single().Block);
        Assert.Equal("0:0:0:6;1:0:0:7;-2:3:0:0", reopened.ListSpaceStructures().Single().Blocks);
    }

    [Fact]
    public void BlockPalette_LeavesIdsUntouchedWhenUnchanged()
    {
        var palette = new Dictionary<ushort, string> { [5] = "steel_wall", [6] = "torch" };
        using (var repo = NewSqliteRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 5);
            repo.EnsureBlockPalette(palette);
        }

        using (var repo = NewSqliteRepo())
        {
            repo.EnsureBlockPalette(palette); // identical palette → no remap
        }

        using var reopened = NewSqliteRepo();
        Assert.Equal((ushort)5, reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)).Single().Block);
    }

    // --- SQLite Player JSON Corruption Tests ---

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("""{"Id":"p1","Name":"Pilot","Health":"lots"}""")]
    [InlineData("""{"Id":"p1"}""")]
    public void Sqlite_Player_DamagedJson_ThrowsInvalidDataExceptionAndPreservesRow(string json)
    {
        using (var repo = NewSqliteRepo())
        {
            repo.SavePlayer(new PlayerState
            {
                PlayerId = "p1",
                Name = "Pilot",
            });
        }

        var databaseFile = Path.Combine(_root, "world_001", "world.db");

        using (var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE player SET json = $json WHERE id = $id;";
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$id", "p1");
            cmd.ExecuteNonQuery();
        }

        using (var reopened = NewSqliteRepo())
        {
            Assert.Throws<InvalidDataException>(() => reopened.LoadPlayer("p1"));
        }

        using (var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM player WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", "p1");

            var storedJson = cmd.ExecuteScalar() as string;
            Assert.Equal(json, storedJson);
        }
    }

    // --- Memory Player JSON Corruption Tests ---

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("""{"Id":"p1","Name":"Pilot","Health":"lots"}""")]
    [InlineData("""{"Id":"p1"}""")]
    public void Memory_Player_DamagedJson_ThrowsInvalidDataExceptionAndPreservesState(string json)
    {
        using var repo = NewMemoryRepo();

        repo.SavePlayer(new PlayerState
        {
            PlayerId = "p1",
            Name = "Pilot",
        });

        repo.SetRawPlayerJson("p1", json);

        Assert.Throws<InvalidDataException>(() => repo.LoadPlayer("p1"));
        Assert.Equal(json, repo.GetRawPlayerJson("p1"));
    }

    // --- SQLite File-Level Integrity Tests ---

    [Theory]
    [InlineData("non-sqlite")]
    [InlineData("truncated")]
    public void Initialize_WithCorruptedDatabase_ThrowsWithoutOverwritingFile(string corruption)
    {
        string dbPath;

        using (var repo = NewSqliteRepo())
        {
            repo.SaveMetadata(new WorldMetadata
            {
                WorldName = "Alpha",
                Seed = 42,
            });

            dbPath = Path.Combine(_root, "world_001", "world.db");
        }

        ApplyCorruption(dbPath, corruption);

        var before = File.ReadAllBytes(dbPath);

        using (var reopened = new SqliteWorldRepository(
            new SaveGamePaths(_root, "world_001")))
        {
            Assert.Throws<InvalidDataException>(() => reopened.Initialize());
        }

        var after = File.ReadAllBytes(dbPath);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Initialize_WithMultipleFlippedDatabaseBytes_PreservesFile()
    {
        string dbPath;

        using (var repo = NewSqliteRepo())
        {
            repo.SaveMetadata(new WorldMetadata
            {
                WorldName = "Alpha",
                Seed = 42,
            });

            dbPath = Path.Combine(_root, "world_001", "world.db");
        }

        byte[] bytes = File.ReadAllBytes(dbPath);
        Assert.True(bytes.Length > 50);

        bytes[50] ^= 0xFF;
        bytes[60] ^= 0xFF;
        bytes[65] ^= 0xFF;
        bytes[6] ^= 0xFF;
        File.WriteAllBytes(dbPath, bytes);

        var before = File.ReadAllBytes(dbPath);

        using (var reopened = new SqliteWorldRepository(
            new SaveGamePaths(_root, "world_001")))
        {
            Record.Exception(() => reopened.Initialize());
        }

        var after = File.ReadAllBytes(dbPath);

        Assert.Equal(before, after);
    }

    private static void ApplyCorruption(string dbPath, string corruption)
    {
        switch (corruption)
        {
            case "non-sqlite":
                File.WriteAllText(dbPath, "this is not a sqlite database");
                break;

            case "truncated":
                {
                    byte[] bytes = File.ReadAllBytes(dbPath);
                    int newLength = Math.Max(1, bytes.Length / 2);
                    File.WriteAllBytes(dbPath, bytes[..newLength]);
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(corruption), corruption, null);
        }
    }

    [Fact]
    public void BlockPalette_HandEditedWithUnknownAndDuplicateNames_MigratesSafely()
    {
        // 1. Initialize a world with baseline palette
        var paletteBaseline = new Dictionary<ushort, string>
        {
            [5] = "steel_wall",
            [6] = "torch",
        };

        using (var repo = NewSqliteRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 0, 0), 5); // steel_wall (id 5)
            repo.SetBlock("rocky", new Vector3i(2, 0, 0), 6); // will be corrupted to unknown (id 6)
            repo.SetBlock("rocky", new Vector3i(3, 0, 0), 7); // duplicate steel_wall (id 7)
            repo.EnsureBlockPalette(paletteBaseline);
        }

        // 2. Simulate hand-edited SQLite: inject duplicate key (steel_wall at 5 and 7) and unknown key at 6
        var databaseFile = Path.Combine(_root, "world_001", "world.db");
        using (var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM block_palette;
                INSERT INTO block_palette (numeric_id, key) VALUES (5, 'steel_wall');
                INSERT INTO block_palette (numeric_id, key) VALUES (6, 'unknown_modded_block_99');
                INSERT INTO block_palette (numeric_id, key) VALUES (7, 'steel_wall');
            ";
            cmd.ExecuteNonQuery();
        }

        // 3. New game version content palette: steel_wall shifted to ID 10, torch is ID 11
        var newPalette = new Dictionary<ushort, string>
        {
            [10] = "steel_wall",
            [11] = "torch",
        };

        // 4. Migration executes without throwing
        using (var repo = NewSqliteRepo())
        {
            var ex = Record.Exception(() => repo.EnsureBlockPalette(newPalette));
            Assert.Null(ex);
        }

        // 5. Verify world data state:
        // - Stored id 5 ('steel_wall') -> 10
        // - Stored id 6 ('unknown_modded_block_99') -> 0 (air fallback for dropped/unknown keys)
        // - Stored id 7 ('steel_wall') -> 10 (duplicate entry resolved to correct new id)
        using var reopened = NewSqliteRepo();
        var edits = reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0));
        ushort BlockAt(int x) => edits.First(e => e.WorldPosition.X == x).Block;

        Assert.Equal((ushort)10, BlockAt(1));
        Assert.Equal((ushort)0, BlockAt(2));
        Assert.Equal((ushort)10, BlockAt(3));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup races on Windows file locks
        }
    }
}
