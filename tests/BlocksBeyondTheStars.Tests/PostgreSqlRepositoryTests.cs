// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Npgsql;

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
