// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Npgsql;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Real PostgreSQL smoke tests. They are opt-in because ordinary local/CI runs should not require Docker or a
/// hosted database; set BBS_POSTGRES_TEST_CONNECTION_STRING to run them against an actual PostgreSQL server.
/// </summary>
public sealed class PostgreSqlRepositoryTests
{
    private const string ConnectionStringEnv = "BBS_POSTGRES_TEST_CONNECTION_STRING";

    [Fact]
    public void PostgreSqlRepository_RoundTripsAgainstRealDatabase()
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        string world = "pg_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "bbts_pg_" + Guid.NewGuid().ToString("N"));
        string schema = SchemaNameFor(world);
        try
        {
            AssertPostgreSqlServerResponds(connectionString);

            var paths = new SaveGamePaths(root, world);
            using var repo = new PostgreSqlWorldRepository(paths, connectionString);
            repo.Initialize();
            repo.SaveMetadata(new WorldMetadata { WorldName = world, Seed = 987654321, DefaultPlanetType = "ice" });
            repo.SavePlayer(new PlayerState { PlayerId = "pilot", Name = "Pilot" });
            repo.SaveShip("ship_pilot", new ShipState { CurrentLocationId = "ice" });
            repo.SetBlock("ice", new Vector3i(1, 2, 3), 42, tint: 7, glow: 3, shape: 5);
            repo.SaveMission(new MissionDefinition { Id = "admin_pg", Title = "PostgreSQL smoke mission" });

            repo.RunInTransaction(() =>
            {
                repo.SetLocationStatus("sys0-p1", "visited");
                repo.SaveAlliance(new StoredAlliance { PlayerA = "pilot", PlayerB = "wing", FormedUtc = DateTime.UtcNow.ToString("o") });
            });

            var loaded = repo.LoadMetadata();
            Assert.NotNull(loaded);
            Assert.Equal(987654321, loaded!.Seed);
            Assert.Equal("Pilot", repo.LoadPlayer("pilot")!.Name);
            Assert.Single(repo.LoadChunkEdits("ice", new ChunkCoord(0, 0, 0)));
            Assert.Equal("visited", repo.LoadLocationStatuses()["sys0-p1"]);
            Assert.Single(repo.ListAlliances());
            Assert.Single(repo.ListMissions());

            string backup = repo.CreateBackup("real_pg_backup");
            Assert.EndsWith(".postgresql.json", backup);
            Assert.Contains("\"world_meta\"", File.ReadAllText(backup), StringComparison.Ordinal);
        }
        finally
        {
            DropSchema(connectionString, schema);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a failed temp-folder delete must not hide database failures.
            }
        }
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("null")]
    [InlineData("""{"Id":"p1","Name":"Pilot","Health":"lots"}""")]
    [InlineData("""{"Id":"p1"}""")]
    public void Postgres_Player_DamagedJson_ThrowsInvalidDataExceptionAndPreservesRow(string json)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        string world = "pg_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "bbts_pg_" + Guid.NewGuid().ToString("N"));
        string schema = SchemaNameFor(world);
        string quotedTable = QuoteIdentifier(schema) + ".player";

        try
        {
            AssertPostgreSqlServerResponds(connectionString);

            var paths = new SaveGamePaths(root, world);
            using (var repo = new PostgreSqlWorldRepository(paths, connectionString))
            {
                repo.Initialize();
                repo.SavePlayer(new PlayerState
                {
                    PlayerId = "p1",
                    Name = "Pilot",
                });
            }

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"UPDATE {quotedTable} SET json = @json WHERE id = @id;";
                cmd.Parameters.AddWithValue("@json", json);
                cmd.Parameters.AddWithValue("@id", "p1");
                cmd.ExecuteNonQuery();
            }

            using (var reopened = new PostgreSqlWorldRepository(paths, connectionString))
            {
                reopened.Initialize();
                Assert.Throws<InvalidDataException>(() => reopened.LoadPlayer("p1"));
            }

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT json FROM {quotedTable} WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", "p1");

                var storedJson = cmd.ExecuteScalar() as string;
                Assert.Equal(json, storedJson);
            }
        }
        finally
        {
            DropSchema(connectionString, schema);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a failed temp-folder delete must not hide database failures.
            }
        }
    }

    // ---------------- #2221 / #2223: the palette remap and the backups, against the real database ----------------

    /// <summary>Runs <paramref name="body"/> against a throwaway world schema, or not at all without a database.</summary>
    private static void WithRealDatabase(Action<SaveGamePaths, string, string> body)
    {
        string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnv);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        string world = "pg_" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(Path.GetTempPath(), "bbts_pg_" + Guid.NewGuid().ToString("N"));
        string schema = SchemaNameFor(world);
        try
        {
            AssertPostgreSqlServerResponds(connectionString);
            body(new SaveGamePaths(root, world), connectionString, schema);
        }
        finally
        {
            DropSchema(connectionString, schema);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a failed temp-folder delete must not hide database failures.
            }
        }
    }

    [Fact]
    public void Postgres_PaletteShift_RemapsTheHullOfASelfBuiltShip_AndKeepsTheRestOfItsRow()
        => WithRealDatabase((paths, connectionString, schema) =>
        {
            var paletteA = new Dictionary<ushort, string> { [5] = "steel_wall", [6] = "torch", [7] = "gone_block" };

            // A block now sorts in at 5: steel_wall and torch move up by one, gone_block is no longer in the game.
            var paletteB = new Dictionary<ushort, string> { [5] = "new_block", [6] = "steel_wall", [7] = "torch" };

            using (var repo = new PostgreSqlWorldRepository(paths, connectionString))
            {
                repo.Initialize();
                repo.SaveShip("ship_Pilot#built", new ShipState
                {
                    ShipType = ShipState.CustomShipType,
                    BuiltCells = "0:0:0:5;1:0:0:6;-2:3:0:7",
                    Hull = 77f,
                    Modules = { "cockpit" },
                });
                repo.SaveShip("ship_Pilot#starter", new ShipState());
                repo.SetBlock("ice", new Vector3i(1, 2, 3), 5);
                repo.EnsureBlockPalette(paletteA);
                Assert.False(repo.BlockPaletteNeedsRemap(paletteA));
                Assert.Equal(3, repo.LoadBlockPalette().Count);
            }

            // A field a later build added to the row: the remap rewrites one property, not the row.
            string shipTable = QuoteIdentifier(schema) + ".ship";
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"UPDATE {shipTable} SET json = '{{\"FutureField\":\"kept\",' || substring(json from 2) WHERE id = 'ship_Pilot#built';";
                Assert.Equal(1, cmd.ExecuteNonQuery());
            }

            using (var repo = new PostgreSqlWorldRepository(paths, connectionString))
            {
                repo.Initialize();
                Assert.True(repo.BlockPaletteNeedsRemap(paletteB));
                repo.EnsureBlockPalette(paletteB);
                Assert.False(repo.BlockPaletteNeedsRemap(paletteB)); // done once: the next load finds nothing to do
                repo.EnsureBlockPalette(paletteB);

                var built = repo.LoadShip("ship_Pilot#built");
                Assert.NotNull(built);
                Assert.Equal("0:0:0:6;1:0:0:7;-2:3:0:0", built!.BuiltCells); // 5→6, 6→7, gone_block→air; negative coordinates intact
                Assert.True(built.IsCustom);
                Assert.Equal(77f, built.Hull);
                Assert.Equal(new[] { "cockpit" }, built.Modules);
                Assert.Equal(string.Empty, repo.LoadShip("ship_Pilot#starter")!.BuiltCells); // a content ship has no hull string
                Assert.Equal(6, Assert.Single(repo.LoadChunkEdits("ice", new ChunkCoord(0, 0, 0))).Block); // the same transaction moved the edit
            }

            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT json FROM {shipTable} WHERE id = 'ship_Pilot#built';";
                Assert.Contains("\"FutureField\":\"kept\"", (string)cmd.ExecuteScalar()!);
            }
        });

    [Fact]
    public void Postgres_Backup_HoldsEveryTableOfTheWorld_AsJsonThatParses()
        => WithRealDatabase((paths, connectionString, schema) =>
        {
            using var repo = new PostgreSqlWorldRepository(paths, connectionString);
            repo.Initialize();
            repo.SaveMetadata(new WorldMetadata { WorldName = "pg", Seed = 11, DefaultPlanetType = "ice" });
            repo.SavePlayer(new PlayerState { PlayerId = "pilot", Name = "Pilot" });
            repo.SaveShip("ship_pilot", new ShipState { CurrentLocationId = "ice" });
            repo.SetBlock("ice", new Vector3i(1, 2, 3), 42, tint: 7, glow: 3, shape: 5, owner: "pilot");
            repo.SaveFloraRegrow("ice", new Vector3i(4, 5, 6), 9, 12.5);
            repo.SaveFluidCell("ice", new Vector3i(7, 8, 9), 3, falling: true);
            repo.SaveCrystalCell(new StoredCrystalCell { Planet = "ice", X = 1, Y = 1, Z = 1, Kind = "Conduit", OwnerId = "pilot" });
            repo.SaveNamedBlob("bio/register", "{\"species\":[]}");
            repo.EnsureBlockPalette(new Dictionary<ushort, string> { [0] = "air", [42] = "steel_wall" });

            // One row in every other table too, so the dump meets every column type the schema has.
            repo.SaveWeatherDeposit("ice", new Vector3i(2, 3, 4), 9, 30.0);
            repo.SaveFireCell("ice", new Vector3i(3, 4, 5), 4.5, 2);
            repo.SaveContainer(new StoredContainer { Id = "crate1", Planet = "ice", Position = new Vector3i(1, 1, 2), LifetimeLeft = 1.5 });
            repo.SaveDoor(new StoredDoor { Planet = "ice", X = 1, Y = 1, Z = 3, AxisX = true });
            repo.SaveBeacon(new StoredBeacon { Planet = "ice", X = 1, Y = 1, Z = 4, Label = "Home", OwnerId = "pilot" });
            repo.SaveBeam(new StoredBeam { Planet = "ice", X = 1, Y = 1, Z = 5, Name = "Pad", OwnerId = "pilot" });
            repo.SaveBase(new StoredBase { Planet = "ice", X = 1, Y = 1, Z = 6, Name = "Base", OwnerId = "pilot" });
            repo.SaveAlliance(new StoredAlliance { PlayerA = "pilot", PlayerB = "wing", FormedUtc = DateTime.UtcNow.ToString("o") });
            repo.SaveCrew(new StoredCrew { CrewId = "crew1", Name = "Crew", OwnerId = "pilot", CreatedUtc = DateTime.UtcNow.ToString("o") });
            repo.SaveCrewMember(new StoredCrewMember { CrewId = "crew1", PlayerId = "pilot", JoinedUtc = DateTime.UtcNow.ToString("o") });
            repo.SaveStoryState(new StoredStoryState { StoryId = "main", FragmentsFound = 2 });
            repo.SetLocationStatus("sys0-p1", "visited");
            repo.SaveMission(new MissionDefinition { Id = "admin_pg", Title = "PostgreSQL smoke mission" });
            repo.SaveSpaceStructure(new StoredSpaceStructure { Id = "pstation:1", OwnerId = "pilot", Name = "Station", Location = "sys0-p1", PosX = 1.5f, PosY = -2.25f, PosZ = 3f, Boardable = true, Blocks = "0:0:0:42" });
            repo.SetStructureBlock("ship:pilot", new Vector3i(0, 1, 0), 42, shape: 5);
            repo.SavePaintDesign(new StoredPaintDesign { Id = 1, OwnerId = "pilot", OwnerName = "Pilot", Pixels = "00ff" });
            repo.SavePaintReport(new StoredPaintReport { ReporterId = "wing", OwnerId = "pilot", DesignId = 1, Planet = "ice", CreatedUnix = 1_790_000_000 });
            repo.SaveCustomShape(new StoredCustomShape { Id = 70, OwnerId = "pilot", OwnerName = "Pilot", Name = "Arch", Voxels = "ff00" });
            repo.SaveWorldTexture(new StoredWorldTexture { Key = "stone", Frames = 1, Data = "AAAA", OwnerId = "pilot", OwnerName = "Pilot", CreatedUnix = 1_790_000_000 });

            string backup = repo.CreateBackup("backup_pg");

            Assert.Equal("backup_pg.postgresql.json", Path.GetFileName(backup));
            Assert.Empty(Directory.GetFiles(paths.BackupsDirectory, "*" + BackupRotation.TempSuffix)); // swapped in when complete
            using var dump = System.Text.Json.JsonDocument.Parse(File.ReadAllText(backup));

            // Every table the schema has is in the dump — a table added later must not be forgotten in it.
            var tables = new List<string>();
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema;";
                cmd.Parameters.AddWithValue("@schema", schema);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            Assert.True(tables.Count >= 29, $"only {tables.Count} tables found in schema {schema}");
            foreach (string table in tables)
            {
                Assert.True(
                    dump.RootElement.TryGetProperty(table, out var rows) && rows.ValueKind == System.Text.Json.JsonValueKind.Array,
                    $"table '{table}' is missing from the backup");
                Assert.True(rows.GetArrayLength() > 0, $"table '{table}' has no row in the backup although the world has one");
            }

            var station = Assert.Single(dump.RootElement.GetProperty("space_structure").EnumerateArray().ToList());
            Assert.Equal(-2.25, station.GetProperty("py").GetDouble());
            Assert.Equal("0:0:0:42", station.GetProperty("blocks").GetString());

            // The rows are there with their values, in types a restore can read back.
            var palette = dump.RootElement.GetProperty("block_palette").EnumerateArray().ToDictionary(r => r.GetProperty("numeric_id").GetInt32(), r => r.GetProperty("key").GetString());
            Assert.Equal("steel_wall", palette[42]);
            Assert.Equal(2, palette.Count);
            var edit = Assert.Single(dump.RootElement.GetProperty("block_edit").EnumerateArray().ToList());
            Assert.Equal(42, edit.GetProperty("block").GetInt32());
            Assert.Equal(1, edit.GetProperty("x").GetInt32());
            Assert.Equal(7, edit.GetProperty("tint").GetInt32());
            Assert.Contains("\"Seed\":11", Assert.Single(dump.RootElement.GetProperty("world_meta").EnumerateArray().ToList()).GetProperty("json").GetString());
            Assert.Equal(12.5, Assert.Single(dump.RootElement.GetProperty("flora_regrow").EnumerateArray().ToList()).GetProperty("timer").GetDouble());
            Assert.Single(dump.RootElement.GetProperty("fluid_cell").EnumerateArray().ToList());
            Assert.Single(dump.RootElement.GetProperty("crystal_cell").EnumerateArray().ToList());
            Assert.Equal("bio/register", Assert.Single(dump.RootElement.GetProperty("named_blob").EnumerateArray().ToList()).GetProperty("key").GetString());
            Assert.Single(dump.RootElement.GetProperty("player").EnumerateArray().ToList());
            Assert.Single(dump.RootElement.GetProperty("ship").EnumerateArray().ToList());
        });

    [Fact]
    public void Postgres_BackgroundBackup_HoldsTheLastCommit_WhileTheMainConnectionIsInsideATransaction()
        => WithRealDatabase((paths, connectionString, _) =>
        {
            using var repo = new PostgreSqlWorldRepository(paths, connectionString);
            repo.Initialize();
            Assert.True(repo.SupportsAutomaticBackups);
            repo.SaveMetadata(new WorldMetadata { WorldName = "pg", Seed = 12, DefaultPlanetType = "ice" });
            repo.SetBlock("ice", new Vector3i(9, 9, 9), 42);

            // Inside an open write transaction of the tick thread: the copy must neither wait for it nor see it.
            string label = BackupRotation.Label(BackupRotation.AutoPrefix, new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
            string? path = null;
            repo.RunInTransaction(() =>
            {
                repo.SetBlock("ice", new Vector3i(8, 8, 8), 42);
                path = repo.CreateBackgroundBackup(label);
            });

            Assert.NotNull(path);
            Assert.Equal("auto_20261003_120000.postgresql.json", Path.GetFileName(path));
            using var dump = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path!));
            var edits = dump.RootElement.GetProperty("block_edit").EnumerateArray().Select(r => r.GetProperty("x").GetInt32()).ToList();
            Assert.Equal(new[] { 9 }, edits); // what was committed, not what was still in flight
            Assert.Single(dump.RootElement.GetProperty("world_meta").EnumerateArray().ToList());

            // …and the transaction of the main connection went through untouched.
            Assert.Equal(2, repo.LoadChunkEdits("ice", new ChunkCoord(0, 0, 0)).Count);

            // The rotation finds the copies by their prefix and keeps the newest.
            repo.CreateBackgroundBackup(BackupRotation.Label(BackupRotation.AutoPrefix, new DateTime(2026, 10, 3, 13, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(1, BackupRotation.Prune(paths.BackupsDirectory, BackupRotation.AutoPrefix, keep: 1));
            Assert.Equal("auto_20261003_130000.postgresql.json", Path.GetFileName(Assert.Single(Directory.GetFiles(paths.BackupsDirectory))));
        });

    private static void AssertPostgreSqlServerResponds(string connectionString)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SHOW server_version;";
        string? version = cmd.ExecuteScalar() as string;
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    private static void DropSchema(string connectionString, string schema)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP SCHEMA IF EXISTS " + QuoteIdentifier(schema) + " CASCADE;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort cleanup; the test world schema is unique and harmless if left behind.
        }
    }

    private static string SchemaNameFor(string worldName)
    {
        char[] chars = worldName.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            bool ok = (chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9');
            chars[i] = ok ? chars[i] : '_';
        }

        return "bbs_" + new string(chars).Trim('_');
    }

    private static string QuoteIdentifier(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}
