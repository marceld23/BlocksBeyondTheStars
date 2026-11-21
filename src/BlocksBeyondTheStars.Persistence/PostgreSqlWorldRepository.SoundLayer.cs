// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>The sound layer of the other side (#3907) in the PostgreSQL save: table <c>sound_edit</c>, one row per sound
/// block, keyed by (planet, x, y, z) — the SQLite twin's rules.</summary>
public sealed partial class PostgreSqlWorldRepository
{
    /// <summary>Creates the table on a save that has none (every save written before the other side).</summary>
    private void EnsureSoundLayerSchema()
        => Execute(@"
            CREATE TABLE IF NOT EXISTS sound_edit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                glow INTEGER NOT NULL DEFAULT 0, owner TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (planet, x, y, z));");

    public void SaveSoundCell(string planet, Vector3i worldPosition, int glow, string owner)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO sound_edit (planet, x, y, z, glow, owner) VALUES (@p, @x, @y, @z, @g, @o) " +
                              "ON CONFLICT (planet, x, y, z) DO UPDATE SET glow = excluded.glow, owner = excluded.owner;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@g", glow);
            cmd.Parameters.AddWithValue("@o", owner ?? string.Empty);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredSoundCell> ListSoundCells(string planet)
    {
        var result = new List<StoredSoundCell>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, glow, owner FROM sound_edit WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredSoundCell
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Glow = reader.GetInt32(3),
                    Owner = reader.GetString(4),
                });
            }
        }

        return result;
    }

    public void DeleteSoundCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM sound_edit WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.ExecuteNonQuery();
        }
    }

    public int CountSoundCells(string planet, string owner)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sound_edit WHERE planet = @p AND owner = @o;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@o", owner ?? string.Empty);
            return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
