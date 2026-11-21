// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>The sound layer of the other side (#3907) in the SQLite save: table <c>sound_edit</c>, one row per sound block,
/// keyed by (planet, x, y, z). A table of its own on purpose — <c>block_edit</c> is the real world, and nothing that reads
/// it may ever meet a sound block. <c>WITHOUT ROWID</c>: the key is the row, so the table is its own index (about half the
/// pages of a rowid table plus its key index).</summary>
public sealed partial class SqliteWorldRepository
{
    private SqliteCommand? _saveSoundCellCmd;
    private SqliteCommand? _deleteSoundCellCmd;

    /// <summary>Creates the table on a save that has none (every save written before the other side). Additive: an older
    /// build simply never reads it.</summary>
    private void EnsureSoundLayerSchema()
        => Execute(@"
            CREATE TABLE IF NOT EXISTS sound_edit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                glow INTEGER NOT NULL DEFAULT 0, owner TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (planet, x, y, z)) WITHOUT ROWID;");

    private void DisposeSoundLayerCommands()
    {
        _saveSoundCellCmd?.Dispose();
        _deleteSoundCellCmd?.Dispose();
        _saveSoundCellCmd = _deleteSoundCellCmd = null;
    }

    public void SaveSoundCell(string planet, Vector3i worldPosition, int glow, string owner)
    {
        lock (_gate)
        {
            var cmd = Prepared(ref _saveSoundCellCmd,
                "INSERT INTO sound_edit (planet, x, y, z, glow, owner) VALUES ($p, $x, $y, $z, $g, $o) " +
                "ON CONFLICT(planet, x, y, z) DO UPDATE SET glow = excluded.glow, owner = excluded.owner;",
                ("$p", SqliteType.Text), ("$x", SqliteType.Integer), ("$y", SqliteType.Integer), ("$z", SqliteType.Integer),
                ("$g", SqliteType.Integer), ("$o", SqliteType.Text));
            var ps = cmd.Parameters;
            ps[0].Value = planet;
            ps[1].Value = worldPosition.X;
            ps[2].Value = worldPosition.Y;
            ps[3].Value = worldPosition.Z;
            ps[4].Value = glow;
            ps[5].Value = owner ?? string.Empty;
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredSoundCell> ListSoundCells(string planet)
    {
        var result = new List<StoredSoundCell>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, glow, owner FROM sound_edit WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
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
            var cmd = Prepared(ref _deleteSoundCellCmd,
                "DELETE FROM sound_edit WHERE planet = $p AND x = $x AND y = $y AND z = $z;",
                ("$p", SqliteType.Text), ("$x", SqliteType.Integer), ("$y", SqliteType.Integer), ("$z", SqliteType.Integer));
            var ps = cmd.Parameters;
            ps[0].Value = planet;
            ps[1].Value = worldPosition.X;
            ps[2].Value = worldPosition.Y;
            ps[3].Value = worldPosition.Z;
            cmd.ExecuteNonQuery();
        }
    }

    public int CountSoundCells(string planet, string owner)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sound_edit WHERE planet = $p AND owner = $o;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$o", owner ?? string.Empty);
            return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
