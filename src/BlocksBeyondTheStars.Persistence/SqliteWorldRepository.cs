using System.Text.Json;
using Microsoft.Data.Sqlite;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// SQLite-backed savegame repository. Stores world metadata, per-block player edits and
/// player/ship snapshots. Uses WAL mode for durable, low-overhead writes suitable for
/// small self-hosted servers (including a Raspberry Pi 5).
/// </summary>
public sealed class SqliteWorldRepository : IWorldRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly SaveGamePaths _paths;
    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public string WorldDirectory => _paths.WorldDirectory;

    public SqliteWorldRepository(SaveGamePaths paths)
    {
        _paths = paths;
    }

    private SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("Repository is not initialized. Call Initialize() first.");

    public void Initialize()
    {
        _paths.EnsureDirectories();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Execute("PRAGMA foreign_keys=ON;");

        Execute(@"
            CREATE TABLE IF NOT EXISTS world_meta (id INTEGER PRIMARY KEY CHECK (id = 0), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS block_edit (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, tint INTEGER NOT NULL DEFAULT 0, glow INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS player (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ship (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS container (
                id TEXT PRIMARY KEY, planet TEXT NOT NULL, kind TEXT NOT NULL,
                x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS door (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                kind TEXT NOT NULL, axisx INTEGER NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS beacon (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                label TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS base_claim (
                planet TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                name TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (planet, x, y, z));
            CREATE TABLE IF NOT EXISTS location_status (id TEXT PRIMARY KEY, status TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mission (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS space_structure (
                id TEXT PRIMARY KEY, owner TEXT NOT NULL, name TEXT NOT NULL, location TEXT NOT NULL,
                px REAL NOT NULL, py REAL NOT NULL, pz REAL NOT NULL, boardable INTEGER NOT NULL, blocks TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS structure_edit (
                structure TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, z INTEGER NOT NULL,
                block INTEGER NOT NULL, PRIMARY KEY (structure, x, y, z));");
        // (Landing pads are deterministic + live-occupancy now — no per-player landing_zone table; item 38.)

        // Migrate older saves to carry per-voxel colour modifiers (dyed blocks / coloured lights). The
        // columns are added if absent; on a fresh DB they already exist from the CREATE above, so the
        // ALTERs throw "duplicate column" and are harmlessly ignored.
        TryExecute("ALTER TABLE block_edit ADD COLUMN tint INTEGER NOT NULL DEFAULT 0;");
        TryExecute("ALTER TABLE block_edit ADD COLUMN glow INTEGER NOT NULL DEFAULT 0;");
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
            cmd.CommandText = "INSERT INTO world_meta (id, json) VALUES (0, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(metadata, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    // --- Block edits ---

    public void SetBlock(string planet, Vector3i worldPosition, ushort block, int tint = 0, int glow = 0)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO block_edit (planet, x, y, z, block, tint, glow) VALUES ($p, $x, $y, $z, $b, $t, $g) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET block = excluded.block, tint = excluded.tint, glow = excluded.glow;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", worldPosition.X);
            cmd.Parameters.AddWithValue("$y", worldPosition.Y);
            cmd.Parameters.AddWithValue("$z", worldPosition.Z);
            cmd.Parameters.AddWithValue("$b", block);
            cmd.Parameters.AddWithValue("$t", tint);
            cmd.Parameters.AddWithValue("$g", glow);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteBlockEdits(string planet, Vector3i min, Vector3i max)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM block_edit WHERE planet = $p " +
                              "AND x BETWEEN $minx AND $maxx AND y BETWEEN $miny AND $maxy AND z BETWEEN $minz AND $maxz;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$minx", min.X);
            cmd.Parameters.AddWithValue("$maxx", max.X);
            cmd.Parameters.AddWithValue("$miny", min.Y);
            cmd.Parameters.AddWithValue("$maxy", max.Y);
            cmd.Parameters.AddWithValue("$minz", min.Z);
            cmd.Parameters.AddWithValue("$maxz", max.Z);
            cmd.ExecuteNonQuery();
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
            cmd.CommandText = "SELECT x, y, z, block, tint, glow FROM block_edit WHERE planet = $p " +
                              "AND x BETWEEN $minx AND $maxx AND y BETWEEN $miny AND $maxy AND z BETWEEN $minz AND $maxz;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$minx", origin.X);
            cmd.Parameters.AddWithValue("$maxx", maxX);
            cmd.Parameters.AddWithValue("$miny", origin.Y);
            cmd.Parameters.AddWithValue("$maxy", maxY);
            cmd.Parameters.AddWithValue("$minz", origin.Z);
            cmd.Parameters.AddWithValue("$maxz", maxZ);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pos = new Vector3i(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                result.Add(new BlockEdit(pos, (ushort)reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
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
            cmd.CommandText = "SELECT json FROM player WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", playerId);
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
            cmd.CommandText = "INSERT INTO player (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", player.PlayerId);
            cmd.Parameters.AddWithValue("$json", json);
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
            cmd.CommandText = "SELECT json FROM ship WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", shipId);
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
            cmd.CommandText = "INSERT INTO ship (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", shipId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Containers ---

    public void SaveContainer(StoredContainer container)
    {
        var json = JsonSerializer.Serialize(container.Items, JsonOptions);
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO container (id, planet, kind, x, y, z, json) " +
                              "VALUES ($id, $p, $k, $x, $y, $z, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET planet=excluded.planet, kind=excluded.kind, " +
                              "x=excluded.x, y=excluded.y, z=excluded.z, json=excluded.json;";
            cmd.Parameters.AddWithValue("$id", container.Id);
            cmd.Parameters.AddWithValue("$p", container.Planet);
            cmd.Parameters.AddWithValue("$k", container.Kind);
            cmd.Parameters.AddWithValue("$x", container.Position.X);
            cmd.Parameters.AddWithValue("$y", container.Position.Y);
            cmd.Parameters.AddWithValue("$z", container.Position.Z);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredContainer> ListContainers(string planet)
    {
        var result = new List<StoredContainer>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT id, kind, x, y, z, json FROM container WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new StoredContainer
                {
                    Id = reader.GetString(0),
                    Planet = planet,
                    Kind = reader.GetString(1),
                    Position = new Vector3i(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
                    Items = JsonSerializer.Deserialize<List<ItemStack>>(reader.GetString(5), JsonOptions) ?? new List<ItemStack>(),
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
            cmd.CommandText = "DELETE FROM container WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
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
                              "VALUES ($p, $x, $y, $z, $k, $a) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET kind=excluded.kind, axisx=excluded.axisx;";
            cmd.Parameters.AddWithValue("$p", door.Planet);
            cmd.Parameters.AddWithValue("$x", door.X);
            cmd.Parameters.AddWithValue("$y", door.Y);
            cmd.Parameters.AddWithValue("$z", door.Z);
            cmd.Parameters.AddWithValue("$k", door.Kind);
            cmd.Parameters.AddWithValue("$a", door.AxisX ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredDoor> ListDoors(string planet)
    {
        var result = new List<StoredDoor>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, kind, axisx FROM door WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
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
            cmd.CommandText = "DELETE FROM door WHERE planet = $p AND x = $x AND y = $y AND z = $z;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
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
                              "VALUES ($id, $o, $n, $loc, $px, $py, $pz, $b, $blk) " +
                              "ON CONFLICT(id) DO UPDATE SET owner=excluded.owner, name=excluded.name, location=excluded.location, " +
                              "px=excluded.px, py=excluded.py, pz=excluded.pz, boardable=excluded.boardable, blocks=excluded.blocks;";
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$o", s.OwnerId);
            cmd.Parameters.AddWithValue("$n", s.Name);
            cmd.Parameters.AddWithValue("$loc", s.Location);
            cmd.Parameters.AddWithValue("$px", s.PosX);
            cmd.Parameters.AddWithValue("$py", s.PosY);
            cmd.Parameters.AddWithValue("$pz", s.PosZ);
            cmd.Parameters.AddWithValue("$b", s.Boardable ? 1 : 0);
            cmd.Parameters.AddWithValue("$blk", s.Blocks);
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
            cmd.CommandText = "DELETE FROM space_structure WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- In-space voxel structure edits (own-ship hull deltas, item 20) ---

    public void SetStructureBlock(string structureId, Vector3i position, ushort block)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO structure_edit (structure, x, y, z, block) VALUES ($s, $x, $y, $z, $b) " +
                              "ON CONFLICT(structure, x, y, z) DO UPDATE SET block = excluded.block;";
            cmd.Parameters.AddWithValue("$s", structureId);
            cmd.Parameters.AddWithValue("$x", position.X);
            cmd.Parameters.AddWithValue("$y", position.Y);
            cmd.Parameters.AddWithValue("$z", position.Z);
            cmd.Parameters.AddWithValue("$b", block);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<BlockEdit> LoadStructureEdits(string structureId)
    {
        var result = new List<BlockEdit>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, block FROM structure_edit WHERE structure = $s;";
            cmd.Parameters.AddWithValue("$s", structureId);
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
            cmd.CommandText = "DELETE FROM structure_edit WHERE structure = $s;";
            cmd.Parameters.AddWithValue("$s", structureId);
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
                              "VALUES ($p, $x, $y, $z, $l, $o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET label=excluded.label, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("$p", beacon.Planet);
            cmd.Parameters.AddWithValue("$x", beacon.X);
            cmd.Parameters.AddWithValue("$y", beacon.Y);
            cmd.Parameters.AddWithValue("$z", beacon.Z);
            cmd.Parameters.AddWithValue("$l", beacon.Label);
            cmd.Parameters.AddWithValue("$o", beacon.OwnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredBeacon> ListBeacons(string planet)
    {
        var result = new List<StoredBeacon>();
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, label, owner FROM beacon WHERE planet = $p;";
            cmd.Parameters.AddWithValue("$p", planet);
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
            cmd.CommandText = "DELETE FROM beacon WHERE planet = $p AND x = $x AND y = $y AND z = $z;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
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
                              "VALUES ($p, $x, $y, $z, $n, $o) " +
                              "ON CONFLICT(planet, x, y, z) DO UPDATE SET name=excluded.name, owner=excluded.owner;";
            cmd.Parameters.AddWithValue("$p", basePoint.Planet);
            cmd.Parameters.AddWithValue("$x", basePoint.X);
            cmd.Parameters.AddWithValue("$y", basePoint.Y);
            cmd.Parameters.AddWithValue("$z", basePoint.Z);
            cmd.Parameters.AddWithValue("$n", basePoint.Name);
            cmd.Parameters.AddWithValue("$o", basePoint.OwnerId);
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
            cmd.CommandText = "DELETE FROM base_claim WHERE planet = $p AND x = $x AND y = $y AND z = $z;";
            cmd.Parameters.AddWithValue("$p", planet);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$z", z);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Location status ---

    public void SetLocationStatus(string locationId, string status)
    {
        lock (_gate)
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO location_status (id, status) VALUES ($id, $s) " +
                              "ON CONFLICT(id) DO UPDATE SET status = excluded.status;";
            cmd.Parameters.AddWithValue("$id", locationId);
            cmd.Parameters.AddWithValue("$s", status);
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
            cmd.CommandText = "INSERT INTO mission (id, json) VALUES ($id, $json) " +
                              "ON CONFLICT(id) DO UPDATE SET json = excluded.json;";
            cmd.Parameters.AddWithValue("$id", mission.Id);
            cmd.Parameters.AddWithValue("$json", json);
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
            cmd.CommandText = "DELETE FROM mission WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // --- Maintenance ---

    public void Flush()
    {
        lock (_gate)
        {
            // Checkpoint the WAL into the main database file so a copy is fully consistent.
            Execute("PRAGMA wal_checkpoint(TRUNCATE);");
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

            var target = Path.Combine(_paths.BackupsDirectory, label + ".db");
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            // VACUUM INTO produces a transactionally consistent standalone copy.
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "VACUUM INTO $target;";
            cmd.Parameters.AddWithValue("$target", target);
            cmd.ExecuteNonQuery();
            return target;
        }
    }

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
        catch (SqliteException)
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
                try
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // best-effort checkpoint on close
                }

                _connection.Dispose();
                _connection = null;
            }

            // Release the pooled native connection handles so the file can be deleted (tests).
            SqliteConnection.ClearAllPools();
        }
    }
}
