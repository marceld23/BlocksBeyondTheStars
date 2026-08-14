// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Npgsql;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// PostgreSQL-backed savegame repository. Stores world metadata, per-block player edits and
/// player/ship snapshots in a hosted database for long-running dedicated/MMO-style servers.
/// Each world is isolated in its own schema, while sidecar logs/backups still live on disk.
/// </summary>
public sealed class PostgreSqlWorldRepository : IWorldRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly SaveGamePaths _paths;
    private readonly Lock _gate = new();
    private NpgsqlConnection? _connection;
    private readonly string _connectionString;
    private readonly string _schemaName;
    // True while a RunInTransaction batch is open (manual BEGIN/COMMIT via raw SQL — all the per-row write
    // commands then run inside it at the PostgreSQL level without each needing an explicit transaction object).
    // Guards against an illegal nested BEGIN so RunInTransaction can be called reentrantly.
    private bool _inTransaction;

    /// <summary>player id → <c>player_ref</c> surrogate; see the SQLite twin. Guarded by <see cref="_gate"/>.</summary>
    private readonly Dictionary<string, int> _playerRefCache = new(StringComparer.Ordinal);

    public string WorldDirectory => _paths.WorldDirectory;

    public PostgreSqlWorldRepository(SaveGamePaths paths, string connectionString)
    {
        _paths = paths;
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString))
            : connectionString;
        _schemaName = "bbs_" + NormalizeSchemaSegment(Path.GetFileName(paths.WorldDirectory));
    }

    private NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException("Repository is not initialized. Call Initialize() first.");

    public void Initialize()
    {
        _paths.EnsureDirectories();

        _connection = new NpgsqlConnection(_connectionString);
        _connection.Open();

        Execute($"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(_schemaName)};");
        Execute($"SET search_path TO {QuoteIdentifier(_schemaName)};");

        Execute(@"
            CREATE TABLE IF NOT EXISTS world_meta (id INTEGER PRIMARY KEY CHECK (id = 0), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS block_edit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, tint INTEGER NOT NULL DEFAULT 0, glow INTEGER NOT NULL DEFAULT 0,
                shape INTEGER NOT NULL DEFAULT 0,
                owner_id INTEGER NOT NULL DEFAULT 0, edited_unix BIGINT NOT NULL DEFAULT 0,
                PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS player_ref (id SERIAL PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS player (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ship (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS container (
                id TEXT PRIMARY KEY, planet TEXT NOT NULL, kind TEXT NOT NULL,
                x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL, json TEXT NOT NULL,
                filter TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS door (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                kind TEXT NOT NULL, axisx INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS beacon (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                label TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS beam (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                name TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS base_claim (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                name TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS alliance (
                a TEXT NOT NULL, b TEXT NOT NULL, formed TEXT NOT NULL, PRIMARY KEY (a, b));
            CREATE TABLE IF NOT EXISTS story_state (story_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS location_status (id TEXT PRIMARY KEY, status TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mission (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS space_structure (
                id TEXT PRIMARY KEY, owner TEXT NOT NULL, name TEXT NOT NULL, location TEXT NOT NULL,
                px DOUBLE PRECISION NOT NULL, py DOUBLE PRECISION NOT NULL, pz DOUBLE PRECISION NOT NULL, boardable INTEGER NOT NULL, blocks TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS structure_edit (
                structure TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, PRIMARY KEY (structure, x, y, z));
            CREATE TABLE IF NOT EXISTS flora_regrow (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, timer DOUBLE PRECISION NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS weather_deposit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, timer DOUBLE PRECISION NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS fluid_cell (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                level INTEGER NOT NULL, falling INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS fire_cell (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                remaining DOUBLE PRECISION NOT NULL, gen INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS block_palette (numeric_id INTEGER PRIMARY KEY, key TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS paint_design (
                id INTEGER PRIMARY KEY, owner TEXT NOT NULL, pixels TEXT NOT NULL,
                owner_name TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS paint_report (
                reporter TEXT NOT NULL, owner TEXT NOT NULL, design_id INTEGER NOT NULL,
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                created_unix BIGINT NOT NULL, kind TEXT NOT NULL DEFAULT 'paint');
            CREATE TABLE IF NOT EXISTS custom_shape (
                id INTEGER PRIMARY KEY, owner TEXT NOT NULL, owner_name TEXT NOT NULL,
                name TEXT NOT NULL, voxels TEXT NOT NULL);");
        // (Landing pads are deterministic + live-occupancy now — no per-player landing_zone table; item 38.)

        // Migrate older saves to carry per-voxel colour modifiers (dyed blocks / coloured lights). The
        // columns are added if absent; on a fresh DB they already exist from the CREATE above, so the
        // ALTERs throw "duplicate column" and are harmlessly ignored.
        TryExecute("ALTER TABLE block_edit ADD COLUMN IF NOT EXISTS tint INTEGER NOT NULL DEFAULT 0;");
        TryExecute("ALTER TABLE block_edit ADD COLUMN IF NOT EXISTS glow INTEGER NOT NULL DEFAULT 0;");
        // Migrate older saves to carry the per-voxel shape descriptor (non-cube building forms). Same pattern:
        // harmlessly ignored on a fresh DB where the CREATE already added the column.
        TryExecute("ALTER TABLE block_edit ADD COLUMN IF NOT EXISTS shape INTEGER NOT NULL DEFAULT 0;");
        // Block attribution (issue #490) — mirrors the SQLite side: interned owner + edit time, 0 = unknown for
        // every row written before this shipped (no back-fill is possible).
        TryExecute("ALTER TABLE block_edit ADD COLUMN IF NOT EXISTS owner_id INTEGER NOT NULL DEFAULT 0;");
        TryExecute("ALTER TABLE block_edit ADD COLUMN IF NOT EXISTS edited_unix BIGINT NOT NULL DEFAULT 0;");
        // Player-designed forms (#843) share the report table with paint — every pre-existing row is a paint
        // report, which is exactly what the column default says.
        TryExecute("ALTER TABLE paint_report ADD COLUMN IF NOT EXISTS kind TEXT NOT NULL DEFAULT 'paint';");
        // Attribution for copied designs (#846): pre-existing designs simply have no designer name on record.
        TryExecute("ALTER TABLE paint_design ADD COLUMN IF NOT EXISTS owner_name TEXT NOT NULL DEFAULT '';");
        // Per-container stash filter (#1032): '' = no filter, which is what every pre-existing crate keeps.
        TryExecute("ALTER TABLE container ADD COLUMN IF NOT EXISTS filter TEXT NOT NULL DEFAULT '';");
    }

    // --- Block-id palette (content-shift migration) ---

    public void EnsureBlockPalette(IReadOnlyDictionary<ushort, string> currentPalette)
    {
        Dictionary<ushort, string> stored;
        lock (_gate)
        {
            Execute("CREATE TABLE IF NOT EXISTS block_palette (numeric_id INTEGER PRIMARY KEY, key TEXT NOT NULL);");
            stored = ReadBlockPalette();
        }

        if (stored.Count == 0)
        {
            // Fresh save, or the first load after this feature shipped: no recorded mapping to remap FROM, so
            // adopt the current assignment as the baseline. From here on, any content change that shifts ids is
            // detected and remapped on the next load.
            WriteBlockPalette(currentPalette);
            return;
        }

        var remap = BlockPaletteMigration.BuildRemap(stored, currentPalette);
        if (remap.Count == 0)
        {
            WriteBlockPalette(currentPalette);
            return;
        }

        // Atomic: remap every persisted id AND rewrite the palette in one transaction.
        RunInTransaction(() =>
        {
            RemapBlockColumn("block_edit", remap);
            RemapBlockColumn("structure_edit", remap);
            RemapBlockColumn("flora_regrow", remap);
            RemapBlockColumn("weather_deposit", remap);
            RemapSpaceStructureBlocks(remap);
            WriteBlockPaletteLocked(currentPalette);
        });
    }

    private Dictionary<ushort, string> ReadBlockPalette()
    {
        var result = new Dictionary<ushort, string>();
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT numeric_id, key FROM block_palette;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[(ushort)reader.GetInt32(0)] = reader.GetString(1);
        }

        return result;
    }

    private void WriteBlockPalette(IReadOnlyDictionary<ushort, string> palette)
        => RunInTransaction(() => WriteBlockPaletteLocked(palette));

    private void WriteBlockPaletteLocked(IReadOnlyDictionary<ushort, string> palette)
    {
        Execute("DELETE FROM block_palette;");
        foreach (var kv in palette)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO block_palette (numeric_id, key) VALUES (@id, @k);";
            cmd.Parameters.AddWithValue("@id", (int)kv.Key);
            cmd.Parameters.AddWithValue("@k", kv.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Remaps the <c>block</c> column of a table via a single CASE over the ORIGINAL value, so every
    /// row is translated atomically. Only changed ids appear; all values are server-side ushorts, so inlining
    /// them is injection-safe.</summary>
    private void RemapBlockColumn(string table, IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (remap.Count == 0)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("UPDATE ").Append(table).Append(" SET block = CASE block");
        foreach (var kv in remap)
        {
            sb.Append(" WHEN ").Append(kv.Key).Append(" THEN ").Append(kv.Value);
        }

        sb.Append(" ELSE block END WHERE block IN (");
        bool first = true;
        foreach (var kv in remap)
        {
            if (!first)
            {
                sb.Append(',');
            }

            sb.Append(kv.Key);
            first = false;
        }

        sb.Append(");");
        Execute(sb.ToString());
    }

    private void RemapSpaceStructureBlocks(IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (remap.Count == 0)
        {
            return;
        }

        var rows = new List<(string Id, string Blocks)>();
        using (var read = Connection.CreateCommand())
        {
            read.CommandText = "SELECT id, blocks FROM space_structure;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (id, blocks) in rows)
        {
            string remapped = BlockPaletteMigration.RemapCellString(blocks, remap);
            if (remapped == blocks)
            {
                continue;
            }

            using var upd = Connection.CreateCommand();
            upd.CommandText = "UPDATE space_structure SET blocks = @b WHERE id = @id;";
            upd.Parameters.AddWithValue("@b", remapped);
            upd.Parameters.AddWithValue("@id", id);
            upd.ExecuteNonQuery();
        }
    }

    // --- Metadata ---

    public WorldMetadata? LoadMetadata()
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM world_meta WHERE id = 0;";
            var json = cmd.ExecuteScalar() as string;
            return json is null ? null : JsonSerializer.Deserialize<WorldMetadata>(json, JsonOptions);
        }
    }

    public void SaveMetadata(WorldMetadata metadata)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO world_meta (id, json) VALUES (0, @json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(metadata, JsonOptions));
            cmd.ExecuteNonQuery();
        }

        WriteMetaSidecar(metadata);
    }

    /// <summary>Mirrors a few headline stats into <c>world.meta.json</c> so the client world-picker can show
    /// them without opening this PostgreSQL DB. Best-effort: a sidecar write failure never blocks the real (DB)
    /// save — the picker simply falls back to showing the bare world name.</summary>
    private void WriteMetaSidecar(WorldMetadata metadata)
    {
        try
        {
            var summary = new WorldSaveSummary
            {
                WorldName = metadata.WorldName,
                PlaytimeSeconds = metadata.CumulativePlaytimeSeconds,
                LastPlayedUtc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(_paths.MetaSidecarFile, JsonSerializer.Serialize(summary, JsonOptions));
        }
        catch
        {
            // Non-fatal: the DB is the source of truth; the sidecar is a convenience for the menu.
        }
    }

    // --- Block edits ---

    public void SetBlock(string planet, Vector3i worldPosition, ushort block, int tint = 0, int glow = 0, int shape = 0, string owner = "")
    {
        lock (_gate)
        {
            // A server-internal write (worldgen stamp, flora regrowth) carries no owner and must not erase an
            // existing attribution — see the SQLite twin for the reasoning.
            int ownerId = string.IsNullOrEmpty(owner) ? 0 : InternPlayerLocked(owner);
            long now = ownerId == 0 ? 0 : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var cmd = Connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO block_edit (planet, x, y, z, block, tint, glow, shape, owner_id, edited_unix) " +
                "VALUES (@p, @x, @y, @z, @b, @t, @g, @s, @o, @u) " +
                "ON CONFLICT(planet, x, y, z) DO UPDATE SET block = excluded.block, tint = excluded.tint, " +
                "glow = excluded.glow, shape = excluded.shape, " +
                "owner_id = CASE WHEN excluded.owner_id = 0 THEN block_edit.owner_id ELSE excluded.owner_id END, " +
                "edited_unix = CASE WHEN excluded.owner_id = 0 THEN block_edit.edited_unix ELSE excluded.edited_unix END;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@b", (int)block);
            cmd.Parameters.AddWithValue("@t", tint);
            cmd.Parameters.AddWithValue("@g", glow);
            cmd.Parameters.AddWithValue("@s", shape);
            cmd.Parameters.AddWithValue("@o", ownerId);
            cmd.Parameters.AddWithValue("@u", now);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>player id → interned surrogate, inserted on first use. Caller holds <c>_gate</c>; cached because
    /// block writes are hot.</summary>
    private int InternPlayerLocked(string playerId)
    {
        if (_playerRefCache.TryGetValue(playerId, out int cached))
        {
            return cached;
        }

        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO player_ref (name) VALUES (@n) ON CONFLICT (name) DO NOTHING; " +
                          "SELECT id FROM player_ref WHERE name = @n;";
        cmd.Parameters.AddWithValue("@n", playerId);
        int id = Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        _playerRefCache[playerId] = id;
        return id;
    }

    public (string Owner, DateTime? EditedUtc)? GetBlockAttribution(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(r.name, ''), e.edited_unix FROM block_edit e " +
                "LEFT JOIN player_ref r ON r.id = e.owner_id " +
                "WHERE e.planet = @p AND e.x = @x AND e.y = @y AND e.z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            string owner = reader.GetString(0);
            long unix = reader.GetInt64(1);
            return (owner, unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null);
        }
    }

    public void DeleteBlockEdits(string planet, Vector3i min, Vector3i max)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM block_edit WHERE planet = @p " +
                              "AND x BETWEEN @minx AND @maxx AND y BETWEEN @miny AND @maxy AND z BETWEEN @minz AND @maxz;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@minx", min.X);
            cmd.Parameters.AddWithValue("@maxx", max.X);
            cmd.Parameters.AddWithValue("@miny", min.Y);
            cmd.Parameters.AddWithValue("@maxy", max.Y);
            cmd.Parameters.AddWithValue("@minz", min.Z);
            cmd.Parameters.AddWithValue("@maxz", max.Z);
            cmd.ExecuteNonQuery();
        }
    }

    public bool HasPlayerBlockEdits(string planet, Vector3i min, Vector3i max)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM block_edit WHERE planet = @p AND owner_id <> 0 " +
                              "AND x BETWEEN @minx AND @maxx AND y BETWEEN @miny AND @maxy AND z BETWEEN @minz AND @maxz " +
                              "LIMIT 1;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@minx", min.X);
            cmd.Parameters.AddWithValue("@maxx", max.X);
            cmd.Parameters.AddWithValue("@miny", min.Y);
            cmd.Parameters.AddWithValue("@maxy", max.Y);
            cmd.Parameters.AddWithValue("@minz", min.Z);
            cmd.Parameters.AddWithValue("@maxz", max.Z);
            return cmd.ExecuteScalar() is not null;
        }
    }

    public bool HasAnyBlockEdits(string planet)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM block_edit WHERE planet = @p LIMIT 1;";
            cmd.Parameters.AddWithValue("@p", planet);
            return cmd.ExecuteScalar() is not null;
        }
    }

    public IReadOnlyList<BlockEdit> LoadChunkEdits(string planet, ChunkCoord chunk)
    {
        var origin = WorldConstants.ChunkOrigin(chunk);
        int maxX = origin.X + WorldConstants.ChunkSize - 1;
        int maxY = origin.Y + WorldConstants.ChunkSize - 1;
        int maxZ = origin.Z + WorldConstants.ChunkSize - 1;

        var result = new List<BlockEdit>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block, tint, glow, shape FROM block_edit WHERE planet = @p " +
                              "AND x BETWEEN @minx AND @maxx AND y BETWEEN @miny AND @maxy AND z BETWEEN @minz AND @maxz;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@minx", origin.X);
            cmd.Parameters.AddWithValue("@maxx", maxX);
            cmd.Parameters.AddWithValue("@miny", origin.Y);
            cmd.Parameters.AddWithValue("@maxy", maxY);
            cmd.Parameters.AddWithValue("@minz", origin.Z);
            cmd.Parameters.AddWithValue("@maxz", maxZ);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pos = new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                result.Add(new BlockEdit(pos, (ushort)reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
            }
        }

        return result;
    }

    // --- Players ---

    public PlayerState? LoadPlayer(string playerId)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM player WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", playerId);
            var json = cmd.ExecuteScalar() as string;
            if (json is null)
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOptions)!;
            return StateMapper.FromSnapshot(snapshot);
        }
    }

    public void SavePlayer(PlayerState player)
    {
        var json = JsonSerializer.Serialize(StateMapper.ToSnapshot(player), JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO player (id, json) VALUES (@id, @json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("@id", player.PlayerId);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> ListPlayerIds()
    {
        var ids = new List<string>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM player;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        return ids;
    }

    // --- Ship ---

    public ShipState? LoadShip(string shipId)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM ship WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", shipId);
            var json = cmd.ExecuteScalar() as string;
            if (json is null)
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<ShipSnapshot>(json, JsonOptions)!;
            return StateMapper.FromSnapshot(snapshot);
        }
    }

    public void SaveShip(string shipId, ShipState ship)
    {
        var json = JsonSerializer.Serialize(StateMapper.ToSnapshot(ship), JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO ship (id, json) VALUES (@id, @json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("@id", shipId);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Containers ---

    public void SaveContainer(StoredContainer container)
    {
        var json = JsonSerializer.Serialize(container.Items, JsonOptions);
        var filter = container.Filter.Count == 0 ? string.Empty : JsonSerializer.Serialize(container.Filter, JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO container (id, planet, kind, x, y, z, json, filter) " +
                              "VALUES (@id, @p, @k, @x, @y, @z, @json, @filter) " +
                              "ON CONFLICT(id) DO UPDATE SET planet=excluded.planet, kind=excluded.kind, " +
                              "x=excluded.x, y=excluded.y, z=excluded.z, json=excluded.json, filter=excluded.filter;";
            cmd.Parameters.AddWithValue("@id", container.Id);
            cmd.Parameters.AddWithValue("@p", container.Planet);
            cmd.Parameters.AddWithValue("@k", container.Kind);
            cmd.Parameters.AddWithValue("@x", container.Position.X);
            cmd.Parameters.AddWithValue("@y", container.Position.Y);
            cmd.Parameters.AddWithValue("@z", container.Position.Z);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@filter", filter);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredContainer> ListContainers(string planet)
    {
        var result = new List<StoredContainer>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, kind, x, y, z, json, filter FROM container WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var filterJson = reader.GetString(6);
                result.Add(new StoredContainer
                {
                    Id = reader.GetString(0),
                    Planet = planet,
                    Kind = reader.GetString(1),
                    Position = new Vector3i(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
                    Items = JsonSerializer.Deserialize<List<ItemStack>>(reader.GetString(5), JsonOptions) ?? new List<ItemStack>(),
                    Filter = filterJson.Length == 0
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(filterJson, JsonOptions) ?? new List<string>(),
                });
            }
        }

        return result;
    }

    public void DeleteContainer(string id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM container WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Doors (player-built) ---

    public void SaveDoor(StoredDoor door)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO door (planet, x, y, z, kind, axisx) " +
                              "VALUES (@p, @x, @y, @z, @k, @a) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET kind=excluded.kind, axisx=excluded.axisx;";
            cmd.Parameters.AddWithValue("@p", door.Planet);
            cmd.Parameters.AddWithValue("@x", door.X);
            cmd.Parameters.AddWithValue("@y", door.Y);
            cmd.Parameters.AddWithValue("@z", door.Z);
            cmd.Parameters.AddWithValue("@k", door.Kind);
            cmd.Parameters.AddWithValue("@a", door.AxisX ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredDoor> ListDoors(string planet)
    {
        var result = new List<StoredDoor>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, kind, axisx FROM door WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredDoor
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Kind = reader.GetString(3),
                    AxisX = reader.GetInt32(4) != 0,
                });
            }
        }

        return result;
    }

    public void DeleteDoor(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM door WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Flora regrowth (harvested plants returning on their cell) ---

    public void SaveFloraRegrow(string planet, Vector3i worldPosition, ushort block, double timer)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO flora_regrow (planet, x, y, z, block, timer) " +
                              "VALUES (@p, @x, @y, @z, @b, @t) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET block=excluded.block, timer=excluded.timer;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@b", (int)block);
            cmd.Parameters.AddWithValue("@t", timer);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredFloraRegrow> ListFloraRegrow(string planet)
    {
        var result = new List<StoredFloraRegrow>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block, timer FROM flora_regrow WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredFloraRegrow(
                    new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                    (ushort)reader.GetInt32(3),
                    reader.GetDouble(4)));
            }
        }

        return result;
    }

    public void DeleteFloraRegrow(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM flora_regrow WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Weather deposits (snow the sky laid down, so the melt pass can tell it from player snow — #900) ---

    public void SaveWeatherDeposit(string planet, Vector3i worldPosition, ushort block, double timer)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO weather_deposit (planet, x, y, z, block, timer) " +
                              "VALUES (@p, @x, @y, @z, @b, @t) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET block=excluded.block, timer=excluded.timer;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@b", (int)block);
            cmd.Parameters.AddWithValue("@t", timer);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredWeatherDeposit> ListWeatherDeposits(string planet)
    {
        var result = new List<StoredWeatherDeposit>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block, timer FROM weather_deposit WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredWeatherDeposit(
                    new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                    (ushort)reader.GetInt32(3),
                    reader.GetDouble(4)));
            }
        }

        return result;
    }

    public void DeleteWeatherDeposit(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM weather_deposit WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Flowing fluid cells (level state, so a restart doesn't promote them to sources — #657) ---

    public void SaveFluidCell(string planet, Vector3i worldPosition, byte level, bool falling)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO fluid_cell (planet, x, y, z, level, falling) " +
                              "VALUES (@p, @x, @y, @z, @l, @f) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET level=excluded.level, falling=excluded.falling;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@l", (int)level);
            cmd.Parameters.AddWithValue("@f", falling ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredFluidCell> ListFluidCells(string planet)
    {
        var result = new List<StoredFluidCell>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, level, falling FROM fluid_cell WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredFluidCell(
                    new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                    (byte)reader.GetInt32(3),
                    reader.GetInt32(4) != 0));
            }
        }

        return result;
    }

    public void DeleteFluidCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM fluid_cell WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Burning cells (burn timers, so a restart doesn't strand permanent flames — #784) ---

    public void SaveFireCell(string planet, Vector3i worldPosition, double remaining, int generation)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO fire_cell (planet, x, y, z, remaining, gen) " +
                              "VALUES (@p, @x, @y, @z, @r, @g) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET remaining=excluded.remaining, gen=excluded.gen;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.Parameters.AddWithValue("@r", remaining);
            cmd.Parameters.AddWithValue("@g", generation);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredFireCell> ListFireCells(string planet)
    {
        var result = new List<StoredFireCell>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, remaining, gen FROM fire_cell WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredFireCell(
                    new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                    reader.GetDouble(3),
                    reader.GetInt32(4)));
            }
        }

        return result;
    }

    public void DeleteFireCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM fire_cell WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", worldPosition.X);
            cmd.Parameters.AddWithValue("@y", worldPosition.Y);
            cmd.Parameters.AddWithValue("@z", worldPosition.Z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Player-built space stations (item 20 S4) ---

    public void SaveSpaceStructure(StoredSpaceStructure s)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO space_structure (id, owner, name, location, px, py, pz, boardable, blocks) " +
                              "VALUES (@id, @o, @n, @loc, @px, @py, @pz, @b, @blk) " +
                              "ON CONFLICT(id) DO UPDATE SET owner=excluded.owner, name=excluded.name, location=excluded.location, " +
                              "px=excluded.px, py=excluded.py, pz=excluded.pz, boardable=excluded.boardable, blocks=excluded.blocks;";
            cmd.Parameters.AddWithValue("@id", s.Id);
            cmd.Parameters.AddWithValue("@o", s.OwnerId);
            cmd.Parameters.AddWithValue("@n", s.Name);
            cmd.Parameters.AddWithValue("@loc", s.Location);
            cmd.Parameters.AddWithValue("@px", s.PosX);
            cmd.Parameters.AddWithValue("@py", s.PosY);
            cmd.Parameters.AddWithValue("@pz", s.PosZ);
            cmd.Parameters.AddWithValue("@b", s.Boardable ? 1 : 0);
            cmd.Parameters.AddWithValue("@blk", s.Blocks);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredSpaceStructure> ListSpaceStructures()
    {
        var result = new List<StoredSpaceStructure>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, owner, name, location, px, py, pz, boardable, blocks FROM space_structure;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredSpaceStructure
                {
                    Id = reader.GetString(0),
                    OwnerId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Location = reader.GetString(3),
                    PosX = (float)reader.GetDouble(4),
                    PosY = (float)reader.GetDouble(5),
                    PosZ = (float)reader.GetDouble(6),
                    Boardable = reader.GetInt32(7) != 0,
                    Blocks = reader.GetString(8),
                });
            }
        }

        return result;
    }

    public void DeleteSpaceStructure(string id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM space_structure WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- In-space voxel structure edits (own-ship hull deltas, item 20) ---

    public void SetStructureBlock(string structureId, Vector3i position, ushort block)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO structure_edit (structure, x, y, z, block) VALUES (@s, @x, @y, @z, @b) " +
                              "ON CONFLICT(structure, x, y, z) DO UPDATE SET block = excluded.block;";
            cmd.Parameters.AddWithValue("@s", structureId);
            cmd.Parameters.AddWithValue("@x", position.X);
            cmd.Parameters.AddWithValue("@y", position.Y);
            cmd.Parameters.AddWithValue("@z", position.Z);
            cmd.Parameters.AddWithValue("@b", (int)block);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<BlockEdit> LoadStructureEdits(string structureId)
    {
        var result = new List<BlockEdit>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block FROM structure_edit WHERE structure = @s;";
            cmd.Parameters.AddWithValue("@s", structureId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pos = new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                result.Add(new BlockEdit(pos, (ushort)reader.GetInt32(3)));
            }
        }

        return result;
    }

    public void DeleteStructureEdits(string structureId)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM structure_edit WHERE structure = @s;";
            cmd.Parameters.AddWithValue("@s", structureId);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Beacons (placed radio beacons, item 37) ---

    public void SaveBeacon(StoredBeacon beacon)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO beacon (planet, x, y, z, label, owner) " +
                              "VALUES (@p, @x, @y, @z, @l, @o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET label=excluded.label, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("@p", beacon.Planet);
            cmd.Parameters.AddWithValue("@x", beacon.X);
            cmd.Parameters.AddWithValue("@y", beacon.Y);
            cmd.Parameters.AddWithValue("@z", beacon.Z);
            cmd.Parameters.AddWithValue("@l", beacon.Label);
            cmd.Parameters.AddWithValue("@o", beacon.OwnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredBeacon> ListAllBeacons()
    {
        var result = new List<StoredBeacon>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT planet, x, y, z, label, owner FROM beacon;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBeacon
                {
                    Planet = reader.GetString(0),
                    X = reader.GetInt32(1),
                    Y = reader.GetInt32(2),
                    Z = reader.GetInt32(3),
                    Label = reader.GetString(4),
                    OwnerId = reader.GetString(5),
                });
            }
        }

        return result;
    }

    public IReadOnlyList<StoredBeam> ListAllBeams()
    {
        var result = new List<StoredBeam>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT planet, x, y, z, name, owner FROM beam;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBeam
                {
                    Planet = reader.GetString(0),
                    X = reader.GetInt32(1),
                    Y = reader.GetInt32(2),
                    Z = reader.GetInt32(3),
                    Name = reader.GetString(4),
                    OwnerId = reader.GetString(5),
                });
            }
        }

        return result;
    }

    public IReadOnlyList<StoredBeacon> ListBeacons(string planet)
    {
        var result = new List<StoredBeacon>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, label, owner FROM beacon WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBeacon
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Label = reader.GetString(3),
                    OwnerId = reader.GetString(4),
                });
            }
        }

        return result;
    }

    public void DeleteBeacon(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM beacon WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Paint designs (player-painted block bitmaps, referenced by shape-descriptor design bits) ---

    public void SavePaintDesign(StoredPaintDesign design)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO paint_design (id, owner, pixels, owner_name) VALUES (@i, @o, @x, @n) " +
                              "ON CONFLICT(id) DO UPDATE SET owner=excluded.owner, pixels=excluded.pixels, " +
                              "owner_name=excluded.owner_name;";
            cmd.Parameters.AddWithValue("@i", design.Id);
            cmd.Parameters.AddWithValue("@o", design.OwnerId);
            cmd.Parameters.AddWithValue("@x", design.Pixels);
            cmd.Parameters.AddWithValue("@n", design.OwnerName);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredPaintDesign> ListPaintDesigns()
    {
        var result = new List<StoredPaintDesign>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, owner, pixels, owner_name FROM paint_design;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredPaintDesign
                {
                    Id = reader.GetInt32(0),
                    OwnerId = reader.GetString(1),
                    Pixels = reader.GetString(2),
                    OwnerName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                });
            }
        }

        return result;
    }

    public void DeletePaintDesign(int id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM paint_design WHERE id = @i;";
            cmd.Parameters.AddWithValue("@i", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void SavePaintReport(StoredPaintReport report)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO paint_report (reporter, owner, design_id, planet, x, y, z, created_unix, kind) " +
                              "VALUES (@r, @o, @d, @p, @x, @y, @z, @c, @k);";
            cmd.Parameters.AddWithValue("@k", string.IsNullOrEmpty(report.Kind) ? "paint" : report.Kind);
            cmd.Parameters.AddWithValue("@r", report.ReporterId);
            cmd.Parameters.AddWithValue("@o", report.OwnerId);
            cmd.Parameters.AddWithValue("@d", report.DesignId);
            cmd.Parameters.AddWithValue("@p", report.Planet);
            cmd.Parameters.AddWithValue("@x", report.X);
            cmd.Parameters.AddWithValue("@y", report.Y);
            cmd.Parameters.AddWithValue("@z", report.Z);
            cmd.Parameters.AddWithValue("@c", report.CreatedUnix);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredPaintReport> ListPaintReports()
    {
        var result = new List<StoredPaintReport>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT reporter, owner, design_id, planet, x, y, z, created_unix, kind FROM paint_report;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredPaintReport
                {
                    ReporterId = reader.GetString(0),
                    OwnerId = reader.GetString(1),
                    DesignId = reader.GetInt32(2),
                    Planet = reader.GetString(3),
                    X = reader.GetInt32(4),
                    Y = reader.GetInt32(5),
                    Z = reader.GetInt32(6),
                    CreatedUnix = reader.GetInt64(7),
                    Kind = reader.IsDBNull(8) ? "paint" : reader.GetString(8),
                });
            }
        }

        return result;
    }

    // --- Player-designed block forms (#843) ---

    public void SaveCustomShape(StoredCustomShape shape)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO custom_shape (id, owner, owner_name, name, voxels) VALUES (@i, @o, @n, @m, @v) " +
                              "ON CONFLICT(id) DO UPDATE SET owner=excluded.owner, owner_name=excluded.owner_name, " +
                              "name=excluded.name, voxels=excluded.voxels;";
            cmd.Parameters.AddWithValue("@i", shape.Id);
            cmd.Parameters.AddWithValue("@o", shape.OwnerId);
            cmd.Parameters.AddWithValue("@n", shape.OwnerName);
            cmd.Parameters.AddWithValue("@m", shape.Name);
            cmd.Parameters.AddWithValue("@v", shape.Voxels);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredCustomShape> ListCustomShapes()
    {
        var result = new List<StoredCustomShape>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, owner, owner_name, name, voxels FROM custom_shape;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredCustomShape
                {
                    Id = reader.GetInt32(0),
                    OwnerId = reader.GetString(1),
                    OwnerName = reader.GetString(2),
                    Name = reader.GetString(3),
                    Voxels = reader.GetString(4),
                });
            }
        }

        return result;
    }

    public void DeleteCustomShape(int id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM custom_shape WHERE id = @i;";
            cmd.Parameters.AddWithValue("@i", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Beam blocks (placed teleporter pads) ---

    public void SaveBeam(StoredBeam beam)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO beam (planet, x, y, z, name, owner) " +
                              "VALUES (@p, @x, @y, @z, @n, @o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET name=excluded.name, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("@p", beam.Planet);
            cmd.Parameters.AddWithValue("@x", beam.X);
            cmd.Parameters.AddWithValue("@y", beam.Y);
            cmd.Parameters.AddWithValue("@z", beam.Z);
            cmd.Parameters.AddWithValue("@n", beam.Name);
            cmd.Parameters.AddWithValue("@o", beam.OwnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredBeam> ListBeams(string planet)
    {
        var result = new List<StoredBeam>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, name, owner FROM beam WHERE planet = @p;";
            cmd.Parameters.AddWithValue("@p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBeam
                {
                    Planet = planet,
                    X = reader.GetInt32(0),
                    Y = reader.GetInt32(1),
                    Z = reader.GetInt32(2),
                    Name = reader.GetString(3),
                    OwnerId = reader.GetString(4),
                });
            }
        }

        return result;
    }

    public void DeleteBeam(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM beam WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Planet bases (player-founded "Grundstein" claims) ---

    public void SaveBase(StoredBase basePoint)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO base_claim (planet, x, y, z, name, owner) " +
                              "VALUES (@p, @x, @y, @z, @n, @o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET name=excluded.name, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("@p", basePoint.Planet);
            cmd.Parameters.AddWithValue("@x", basePoint.X);
            cmd.Parameters.AddWithValue("@y", basePoint.Y);
            cmd.Parameters.AddWithValue("@z", basePoint.Z);
            cmd.Parameters.AddWithValue("@n", basePoint.Name);
            cmd.Parameters.AddWithValue("@o", basePoint.OwnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredBase> ListAllBases()
    {
        var result = new List<StoredBase>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT planet, x, y, z, name, owner FROM base_claim;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredBase
                {
                    Planet = reader.GetString(0),
                    X = reader.GetInt32(1),
                    Y = reader.GetInt32(2),
                    Z = reader.GetInt32(3),
                    Name = reader.GetString(4),
                    OwnerId = reader.GetString(5),
                });
            }
        }

        return result;
    }

    public void DeleteBase(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM base_claim WHERE planet = @p AND x = @x AND y = @y AND z = @z;";
            cmd.Parameters.AddWithValue("@p", planet);
            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Alliances (player-to-player, server-wide) ---

    public void SaveAlliance(StoredAlliance alliance)
    {
        var (a, b) = NormalizePair(alliance.PlayerA, alliance.PlayerB);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO alliance (a, b, formed) VALUES (@a, @b, @f) " +
                              "ON CONFLICT(a, b) DO UPDATE SET formed = excluded.formed;";
            cmd.Parameters.AddWithValue("@a", a);
            cmd.Parameters.AddWithValue("@b", b);
            cmd.Parameters.AddWithValue("@f", alliance.FormedUtc);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredAlliance> ListAlliances()
    {
        var result = new List<StoredAlliance>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT a, b, formed FROM alliance;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredAlliance
                {
                    PlayerA = reader.GetString(0),
                    PlayerB = reader.GetString(1),
                    FormedUtc = reader.GetString(2),
                });
            }
        }

        return result;
    }

    public void DeleteAlliance(string playerA, string playerB)
    {
        var (a, b) = NormalizePair(playerA, playerB);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM alliance WHERE a = @a AND b = @b;";
            cmd.Parameters.AddWithValue("@a", a);
            cmd.Parameters.AddWithValue("@b", b);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Orders a player-id pair so each alliance is stored under exactly one (a, b) key.</summary>
    private static (string A, string B) NormalizePair(string x, string y)
        => string.CompareOrdinal(x, y) <= 0 ? (x, y) : (y, x);

    // --- Story state (per active story pack, server-wide) ---

    public void SaveStoryState(StoredStoryState state)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO story_state (story_id, json) VALUES (@id, @json) " +
                              "ON CONFLICT(story_id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("@id", state.StoryId);
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(state, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredStoryState> ListStoryStates()
    {
        var result = new List<StoredStoryState>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM story_state;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (JsonSerializer.Deserialize<StoredStoryState>(reader.GetString(0), JsonOptions) is { } s)
                {
                    result.Add(s);
                }
            }
        }

        return result;
    }

    // --- Location status ---

    public void SetLocationStatus(string locationId, string status)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO location_status (id, status) VALUES (@id, @s) " +
                              "ON CONFLICT(id) DO UPDATE SET status = excluded.status;";
            cmd.Parameters.AddWithValue("@id", locationId);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<string, string> LoadLocationStatuses()
    {
        var map = new Dictionary<string, string>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, status FROM location_status;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                map[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return map;
    }

    // --- Missions (player/admin-created) ---

    public void SaveMission(MissionDefinition mission)
    {
        var json = JsonSerializer.Serialize(mission, JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mission (id, json) VALUES (@id, @json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("@id", mission.Id);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<MissionDefinition> ListMissions()
    {
        var result = new List<MissionDefinition>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT json FROM mission;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var m = JsonSerializer.Deserialize<MissionDefinition>(reader.GetString(0), JsonOptions);
                if (m is not null)
                {
                    result.Add(m);
                }
            }
        }

        return result;
    }

    public void DeleteMission(string id)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM mission WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Maintenance ---

    public void RunInTransaction(Action body)
    {
        // The _gate is a Monitor (reentrant on the same thread), so the per-row write methods called inside
        // body() can re-acquire it freely. Holding it for the whole batch also keeps the transaction atomic
        // against any other thread that might write through the same connection.
        lock (_gate)
        {
            if (_inTransaction)
            {
                body(); // already inside a batch — PostgreSQL forbids a nested BEGIN, so just join it
                return;
            }

            _inTransaction = true;
            Execute("BEGIN;");
            try
            {
                body();
                Execute("COMMIT;");
            }
            catch
            {
                Execute("ROLLBACK;");
                throw;
            }
            finally
            {
                _inTransaction = false;
            }
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            // PostgreSQL commits each statement/transaction durably; no client-side WAL checkpoint is required.
        }
    }

    public string CreateBackup(string label)
    {
        lock (_gate)
        {
            Flush();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                label = label.Replace(c, '_');
            }

            var target = Path.Combine(_paths.BackupsDirectory, label + ".postgresql.json");
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            var dump = new Dictionary<string, List<Dictionary<string, object?>>>();
            foreach (string table in BackupTables)
            {
                dump[table] = ReadTableForBackup(table);
            }

            File.WriteAllText(target, JsonSerializer.Serialize(dump, JsonOptions));
            return target;
        }
    }

    private static readonly string[] BackupTables =
    {
        "world_meta", "block_edit", "player", "player_ref", "ship", "container", "door", "beacon", "beam",
        "base_claim", "alliance", "story_state", "location_status", "mission",
        "space_structure", "structure_edit", "flora_regrow", "fluid_cell", "fire_cell",
    };

    private List<Dictionary<string, object?>> ReadTableForBackup(string table)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {QuoteIdentifier(table)};";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string NormalizeSchemaSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "world";
        }

        var chars = value.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            bool ok = (chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9');
            chars[i] = ok ? chars[i] : '_';
        }

        var normalized = new string(chars).Trim('_');
        return string.IsNullOrEmpty(normalized) ? "world" : normalized;
    }

    private static string QuoteIdentifier(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private void Execute(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Runs DDL that may legitimately fail on an up-to-date schema (e.g. an idempotent
    /// ADD COLUMN migration that the CREATE already satisfied); swallows the error.</summary>
    private void TryExecute(string sql)
    {
        try
        {
            Execute(sql);
        }
        catch (PostgresException)
        {
            // Column already exists / nothing to migrate.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_connection is not null)
            {
                _connection.Dispose();
                _connection = null;
            }

            // Release the pooled native connection handles so tests and short-lived tools do not keep sockets open.
            NpgsqlConnection.ClearAllPools();
        }
    }
}
