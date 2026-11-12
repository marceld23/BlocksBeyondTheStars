// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using NpgsqlTypes;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>The structure overlays (terrain generation 39) of the PostgreSQL save: table <c>structure_overlay</c>, one
/// BYTEA row per placed structure, keyed by (planet, key) — the SQLite twin's rules.</summary>
public sealed partial class PostgreSqlWorldRepository
{
    public void SaveStructureOverlay(StoredStructureOverlay overlay)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO structure_overlay (planet, key, data) VALUES (@p, @k, @d) " +
                              "ON CONFLICT (planet, key) DO UPDATE SET data = excluded.data;";
            cmd.Parameters.AddWithValue("@p", overlay.Planet);
            cmd.Parameters.AddWithValue("@k", overlay.Key);
            cmd.Parameters.AddWithValue("@d", NpgsqlDbType.Bytea, overlay.Data);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredStructureOverlay> ListStructureOverlays(string planet)
    {
        var result = new List<StoredStructureOverlay>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT key, data FROM structure_overlay WHERE planet = @p ORDER BY key;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredStructureOverlay { Planet = planet, Key = reader.GetString(0), Data = reader.GetFieldValue<byte[]>(1) });
            }
        }

        return result;
    }

    public void DeleteStructureOverlay(string planet, string key)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM structure_overlay WHERE planet = @p AND key = @k;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@k", key);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>The palette migration of the overlays' cells (inside the migration's transaction).</summary>
    private void RemapStructureOverlays(IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (remap.Count == 0)
        {
            return;
        }

        var rows = new List<(string Planet, string Key, byte[] Data)>();
        using (var read = Connection.CreateCommand())
        {
            read.CommandText = "SELECT planet, key, data FROM structure_overlay;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetFieldValue<byte[]>(2)));
            }
        }

        foreach (var (planet, key, data) in rows)
        {
            byte[] remapped = StructureOverlay.RemapBlocks(data, remap);
            if (ReferenceEquals(remapped, data))
            {
                continue;
            }

            using var upd = Connection.CreateCommand();
            upd.CommandText = "UPDATE structure_overlay SET data = @d WHERE planet = @p AND key = @k;";
            upd.Parameters.AddWithValue("@d", NpgsqlDbType.Bytea, remapped);
            upd.Parameters.AddWithValue("@p", planet);
            upd.Parameters.AddWithValue("@k", key);
            upd.ExecuteNonQuery();
        }
    }
}
