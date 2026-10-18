// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Where a hosted world's save lives on the host, plus the validation an UPLOADED save must pass before
/// it is allowed anywhere near an instance. The instance's /app/saves is a bind mount of
/// <c>&lt;WorldsDir&gt;/&lt;worldId&gt;/saves</c>, and the server stores a world named X at
/// <c>saves/X/world.db</c> (SaveGamePaths) — hosted instances run with BBS_WORLD=&lt;worldId&gt;.
/// </summary>
public static class SavePaths
{
    public static string HostSavesDir(WorldHostConfig config, string worldId)
        => Path.GetFullPath(Path.Combine(config.WorldsDir, worldId, "saves"));

    public static string WorldDbPath(WorldHostConfig config, string worldId)
        => Path.Combine(HostSavesDir(config, worldId), worldId, "world.db");

    /// <summary>Where an archived world's saves rest (<c>_archive</c> can't collide with world ids —
    /// they are pure hex). Same volume as the live dir, so archiving is a rename, not a copy.</summary>
    public static string ArchivedSavesDir(WorldHostConfig config, string worldId)
        => Path.GetFullPath(Path.Combine(config.WorldsDir, "_archive", worldId, "saves"));

    /// <summary>Moves a world's saves into the archive. True when something was moved; false when there
    /// was nothing to move (a world that was never started has no saves — archiving it is just the
    /// status flip).</summary>
    public static bool MoveToArchive(WorldHostConfig config, string worldId)
        => MoveDir(HostSavesDir(config, worldId), ArchivedSavesDir(config, worldId));

    /// <summary>Restores an archived world's saves so the instance can be started again.</summary>
    public static bool RestoreFromArchive(WorldHostConfig config, string worldId)
        => MoveDir(ArchivedSavesDir(config, worldId), HostSavesDir(config, worldId));

    /// <summary>Erases ALL on-disk data of a world — live saves and archive alike. Used by account
    /// self-deletion (DSGVO): unlike a plain world delete, nothing is retained.</summary>
    public static void DeleteWorldData(WorldHostConfig config, string worldId)
    {
        foreach (var dir in new[]
        {
            Path.GetFullPath(Path.Combine(config.WorldsDir, worldId)),
            Path.GetFullPath(Path.Combine(config.WorldsDir, "_archive", worldId)),
        })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort — a straggling file handle must not block the account deletion itself
            }
        }
    }

    private static bool MoveDir(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(to))
        {
            Directory.Delete(to, recursive: true); // stale leftover from an interrupted earlier move
        }

        Directory.Move(from, to);
        return true;
    }

    /// <summary>The upload refusal for a save a newer build wrote (#2464) — mapped to the <c>save_too_new</c> code.</summary>
    public const string SaveTooNewError =
        "This save comes from a newer version of the game than this server runs — upload it again once the server is updated.";

    /// <summary>
    /// Validates an uploaded candidate file as a usable world save: real SQLite (magic header), passes
    /// <c>PRAGMA quick_check</c>, carries the game's schema (the <c>world_meta</c> table — the anchor
    /// every SqliteWorldRepository save has) and was not written by a newer build (#2464): a save whose stored
    /// <see cref="WorldMetadata.SaveVersion"/> is higher than <paramref name="supportedSaveVersion"/> (default:
    /// this build's <see cref="WorldMetadata.CurrentSaveVersion"/>) would be accepted only to make the world's
    /// next wake fail — the game server refuses to open it (exit code 3). Older saves pass: the game server copies
    /// and stamps them on their first open. Returns a player-safe error; internals stay in the logs.
    /// </summary>
    public static (bool Ok, string Error) ValidateUploadedSave(string path, int supportedSaveVersion = WorldMetadata.CurrentSaveVersion)
    {
        try
        {
            // SQLite magic: the 16 header bytes "SQLite format 3\0" — rejects arbitrary uploads cheaply
            // before SQLite parses anything.
            using (var fs = File.OpenRead(path))
            {
                var header = new byte[16];
                if (fs.Read(header, 0, 16) != 16
                    || !System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0").AsSpan().SequenceEqual(header))
                {
                    return (false, "This file is not a Blocks Beyond the Stars save (world.db).");
                }
            }

            // Unpooled (#2768): leaving this scope closes the read-only handle, so the caller can move or delete the temp
            // file — without clearing every SQLite pool of the process, which could dispose a connection being opened elsewhere.
            using var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            db.Open();

            using (var check = db.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                if (check.ExecuteScalar() as string != "ok")
                {
                    return (false, "The save file is damaged (integrity check failed).");
                }
            }

            using (var schema = db.CreateCommand())
            {
                schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'world_meta'";
                if (Convert.ToInt32(schema.ExecuteScalar()) != 1)
                {
                    return (false, "This database is not a Blocks Beyond the Stars world save.");
                }
            }

            // The save version lives in the metadata row's JSON (SqliteWorldRepository: world_meta(id = 0, json)).
            // No row yet = a world that never got that far — the server creates its metadata; it is not newer.
            string? metaJson;
            using (var meta = db.CreateCommand())
            {
                meta.CommandText = "SELECT json FROM world_meta WHERE id = 0;";
                try
                {
                    metaJson = meta.ExecuteScalar() as string;
                }
                catch (SqliteException)
                {
                    return (false, "This database is not a Blocks Beyond the Stars world save."); // a world_meta of another shape
                }
            }

            if (metaJson is not null && StoredSaveVersion(metaJson) > supportedSaveVersion)
            {
                return (false, SaveTooNewError);
            }

            return (true, string.Empty);
        }
        catch (Exception)
        {
            return (false, "The save file could not be read.");
        }
    }

    /// <summary>The <c>SaveVersion</c> in a save's metadata JSON — 1 when it is absent, exactly as the game reads
    /// it (<see cref="WorldMetadata.SaveVersion"/>: every save from before the check). Property names are the
    /// repository's default (case-sensitive PascalCase). Malformed JSON throws — the caller reports the save as
    /// unreadable, because the game server could not load it either.</summary>
    public static int StoredSaveVersion(string metaJson)
    {
        using var doc = JsonDocument.Parse(metaJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
               && doc.RootElement.TryGetProperty(nameof(WorldMetadata.SaveVersion), out var version)
               && version.ValueKind == JsonValueKind.Number
               && version.TryGetInt32(out int value)
            ? value
            : 1;
    }
}
