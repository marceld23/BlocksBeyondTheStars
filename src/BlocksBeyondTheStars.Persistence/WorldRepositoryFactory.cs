// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Central persistence selector used by the dedicated server and admin API. SQLite stays the zero-config
/// local default; PostgreSQL is opt-in through config/environment so hosted deployments never require
/// code changes or committed credentials.
/// </summary>
public static class WorldRepositoryFactory
{
    public const string ProviderSqlite = "sqlite";
    public const string ProviderPostgreSql = "postgresql";

    public static IWorldRepository Create(ServerConfig config, SaveGamePaths paths)
    {
        if (IsPostgreSql(config.DatabaseProvider))
        {
            return new PostgreSqlWorldRepository(paths, config.PostgresConnectionString);
        }

        return new SqliteWorldRepository(paths);
    }

    public static bool IsPostgreSql(ServerConfig config)
        => IsPostgreSql(config.DatabaseProvider);

    public static string DisplayName(ServerConfig config)
        => IsPostgreSql(config) ? "PostgreSQL" : "SQLite";

    public static string BackupSearchPattern(ServerConfig config)
        => IsPostgreSql(config) ? "*.postgresql.json" : "*.db";

    private static bool IsPostgreSql(string? provider)
    {
        string value = (provider ?? string.Empty).Trim().ToLowerInvariant();
        return value is "postgres" or "postgresql" or "pg";
    }
}
